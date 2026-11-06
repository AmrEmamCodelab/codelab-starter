import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input } from '@angular/core';
import { DgaStatusTagComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { formatRecordInstant } from './record-instant';
import { RecordsTranslationKeys } from './records-translation-keys';
import type { RecordSla } from './records.models';
import { slaDisplay } from './sla';

/**
 * The record's deadline as a status tag (on track / due soon / overdue) with the due date, from the server's
 * {@link RecordSla}. Renders nothing for a record without a deadline.
 *
 * Inputs: `sla`, `compact` (the tag only, without the due date: for queue rows).
 */
@Component({
  selector: 'app-sla-badge',
  imports: [DgaStatusTagComponent, TranslatePipe],
  templateUrl: './sla-badge.component.html',
  styleUrl: './sla-badge.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SlaBadgeComponent {
  readonly sla = input<RecordSla | null>(null);
  readonly compact = input(false, { transform: booleanAttribute });

  private readonly language = inject(LanguageService);

  protected readonly keys = RecordsTranslationKeys.Sla;
  readonly display = computed(() => slaDisplay(this.sla()));
  readonly dueText = computed(() => {
    const sla = this.sla();
    return sla ? formatRecordInstant(sla.dueAt, this.language.current()) : '';
  });
}
