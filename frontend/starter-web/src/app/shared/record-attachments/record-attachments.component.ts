import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, booleanAttribute, computed, effect, inject, input, signal, untracked } from '@angular/core';
import {
  DgaButtonComponent,
  DgaFileUploadComponent,
  DgaPageFeedbackComponent,
  type DgaFileRejection,
  type DgaFileSelection,
  type DgaFileUploadItem,
} from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService, type TranslationParams } from '../../core/i18n/translation.service';
import { PermissionStore } from '../../core/permissions/permission.store';
import type { AttachableRecordId } from '../attachments/attachments.models';
import { HasPermissionDirective } from '../permissions/has-permission.directive';
import { AttachmentsPermissionKeys } from './attachments-permission-keys';
import { RecordAttachmentsTestIds, RecordAttachmentsTranslationKeys } from './record-attachments-keys';
import { RecordAttachmentsStore } from './record-attachments.store';
import {
  RecordAttachmentsLoadState,
  attachmentSections,
  feedbackState,
  rejectionText,
  statusLabels,
  type AttachmentTypeSection,
} from './record-attachments-view';

/** The refusals of one pick, untranslated, with the type's file limit the count text names. */
interface PickRejections {
  readonly rejected: readonly DgaFileRejection[];
  readonly maxFiles: number;
}

/**
 * The attachments of ANY record (`app-record-attachments`): one `dga-file-upload` per attachment type of
 * `recordType`, holding the record's linked files and the files on their way. It loads the types and the list, uploads
 * picked files through the shared adapter (ticket → attachment service → receipt → link), downloads and removes, and
 * reports every outcome as page feedback with the bilingual `errors.<CODE>` text and the reference code.
 *
 * - Gates: everything needs `attachments.view` (`*hasPermission`); picking files needs `attachments.upload`
 *   (`*hasPermission`, else the read-only control); the remove button needs `attachments.remove`.
 * - `readonly` shows the list and the downloads only (a record that can no longer change).
 * - Required types show the server's missing verdict (`missingRequiredTypeKeys`), never a client guess.
 * - No client size cap: an oversized file is refused by the attachment service, with its reference code.
 *
 * @example
 * <app-record-attachments [recordType]="recordType" [recordId]="record.id" [readonly]="!record.canEdit" />
 */
@Component({
  selector: 'app-record-attachments',
  imports: [
    DgaButtonComponent,
    DgaFileUploadComponent,
    DgaPageFeedbackComponent,
    HasPermissionDirective,
    NgTemplateOutlet,
    TranslatePipe,
  ],
  providers: [RecordAttachmentsStore],
  templateUrl: './record-attachments.component.html',
  styleUrl: './record-attachments.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecordAttachmentsComponent {
  /** The record type key (`AttachableRecordTypes`), such as `attachments-demo`. */
  readonly recordType = input.required<string>();
  /** The record's PublicId, sent back exactly as received. */
  readonly recordId = input.required<AttachableRecordId>();
  /** Shows the list and the downloads only: no picking and no removing. */
  readonly readonly = input(false, { transform: booleanAttribute });

  protected readonly store = inject(RecordAttachmentsStore);
  private readonly translations = inject(TranslationService);
  private readonly language = inject(LanguageService);
  private readonly permissions = inject(PermissionStore);
  private readonly translate = (key: string, params?: TranslationParams): string => this.translations.translate(key, params);

  protected readonly keys = RecordAttachmentsTranslationKeys;
  protected readonly testIds = RecordAttachmentsTestIds;
  protected readonly permissionKeys = AttachmentsPermissionKeys;
  protected readonly loadStates = RecordAttachmentsLoadState;

  /** Remove sits inside each row of the ng-dga control, so it follows the store (live grants and revokes) here. */
  readonly canRemove = computed(() => this.permissions.has(AttachmentsPermissionKeys.Remove));
  readonly sections = computed(() =>
    attachmentSections({
      types: this.store.types(),
      attachments: this.store.attachments(),
      uploads: this.store.uploads(),
      missingTypeKeys: this.store.missingTypeKeys(),
      language: this.language.current(),
      translate: this.translate,
      has: (key) => this.translations.has(key),
    }),
  );
  /** Translation reads are signal reads, so these follow a language switch. */
  readonly statusLabels = computed(() => statusLabels(this.translate));
  readonly feedback = computed(() => feedbackState(this.store.feedback(), this.translate));
  /**
   * The client pre-check refusals of the last pick, per type key, kept raw (file and reason): the text is built in
   * {@link rejectionTexts}, so it follows a language switch.
   */
  private readonly rejections = signal<ReadonlyMap<string, PickRejections>>(new Map());
  /** The refusal line per type key in the current language (translation reads are signal reads). */
  private readonly rejectionTexts = computed(() => {
    const texts = new Map<string, string | null>();
    for (const [typeKey, pick] of this.rejections()) {
      texts.set(typeKey, rejectionText(pick.rejected, pick.maxFiles, this.translate));
    }
    return texts;
  });

  constructor() {
    effect(() => {
      const recordType = this.recordType();
      const recordId = this.recordId();
      untracked(() => {
        this.rejections.set(new Map());
        this.store.load(recordType, recordId);
      });
    });
  }

  rejectionFor(section: AttachmentTypeSection): string | null {
    return this.rejectionTexts().get(section.key) ?? null;
  }

  onFilesSelected(section: AttachmentTypeSection, selection: DgaFileSelection): void {
    const pick: PickRejections = { rejected: selection.rejected, maxFiles: section.maxFiles };
    this.rejections.update((current) => new Map(current).set(section.key, pick));
    this.store.upload(section.key, selection.accepted);
  }

  onDownload(item: DgaFileUploadItem): void {
    this.store.download(item.id);
  }

  onRemove(item: DgaFileUploadItem): void {
    this.store.remove(item.id);
  }
}
