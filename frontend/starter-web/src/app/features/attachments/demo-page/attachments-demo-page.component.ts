import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ApiError } from '@codelab/ng-security';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { RequesterDetailAttachmentsDirective, RequesterDetailPatternComponent } from '../../../patterns';
import { AttachmentError, attachmentErrorFrom } from '../../../shared/attachments/attachment-error';
import type { AttachmentDemoRecord } from '../../../shared/attachments/attachments.models';
import { AttachmentsClient } from '../../../shared/attachments/attachments.client';
import { RecordAttachmentsComponent } from '../../../shared/record-attachments/record-attachments.component';
import { ERROR_TEXT_PREFIX } from '../../../shared/record-attachments/record-attachments-keys';
import { AttachmentsDemoTranslationKeys as Keys } from '../attachments-translation-keys';

/** The demo record's load failure in the shape the page pattern renders (`errors.<CODE>` + reference code). */
export function demoRecordApiError(error: AttachmentError): ApiError {
  return {
    status: error.status,
    code: error.code,
    referenceCode: error.referenceCode ?? undefined,
    fieldErrors: [],
  };
}

/**
 * `/attachments/demo` (development builds only, `attachments.view`, inside the staff shell): the sample page of the
 * attachment flow. It asks the API for the Development demo record (`GET api/attachments/demo-record`) and hosts
 * `app-record-attachments` on it inside the requester-detail pattern, so the whole chain (ticket → attachment service
 * → receipt → link, list, download, remove, refusals with reference codes) can be tried and checked with Playwright.
 */
@Component({
  selector: 'app-attachments-demo-page',
  imports: [RecordAttachmentsComponent, RequesterDetailAttachmentsDirective, RequesterDetailPatternComponent, TranslatePipe],
  templateUrl: './attachments-demo-page.component.html',
  styleUrl: './attachments-demo-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttachmentsDemoPageComponent {
  private readonly client = inject(AttachmentsClient);
  private readonly translations = inject(TranslationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly keys = Keys;
  readonly record = signal<AttachmentDemoRecord | null>(null);
  readonly loading = signal(true);
  readonly error = signal<ApiError | null>(null);
  /** The pattern shows `errors.<CODE>` for a code the bundle translates, the generic text otherwise. */
  readonly translatedCodes = computed(() => {
    const code = this.error()?.code;
    return new Set(code !== undefined && this.translations.has(`${ERROR_TEXT_PREFIX}${code}`) ? [code] : []);
  });

  constructor() {
    this.load();
  }

  /** Asks the API for the demo record (again, after a failure). */
  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.client
      .demoRecord()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (record) => {
          this.record.set(record);
          this.loading.set(false);
        },
        error: (failure: unknown) => void this.failed(failure),
      });
  }

  private async failed(failure: unknown): Promise<void> {
    this.error.set(demoRecordApiError(await attachmentErrorFrom(failure)));
    this.loading.set(false);
  }
}
