import { ChangeDetectionStrategy, Component, computed, input, numberAttribute } from '@angular/core';
import { DgaCardComponent, DgaInfoFieldComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { renderCards, type RecordCard } from './record-cards';
import { RecordsTranslationKeys } from './records-translation-keys';

/** The heading level of each card when the screen sets none: the cards sit under the page's h1. */
export const RECORD_CARDS_DEFAULT_HEADING_LEVEL = 2;

/**
 * The record's details as generic key/value cards: one `dga-card` per {@link RecordCard} with one `dga-info-field`
 * per field, labels translated, an empty value shown as "Not provided". The record type supplies the cards through
 * its adapter (`<type>CardsAdapter(details) -> RecordCard[]`); this component knows no record type.
 *
 * Inputs: `cards`, `headingLevel` (default {@link RECORD_CARDS_DEFAULT_HEADING_LEVEL}).
 */
@Component({
  selector: 'app-record-detail-cards',
  imports: [DgaCardComponent, DgaInfoFieldComponent, TranslatePipe],
  templateUrl: './record-detail-cards.component.html',
  styleUrl: './record-detail-cards.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecordDetailCardsComponent {
  readonly cards = input<readonly RecordCard[]>([]);
  readonly headingLevel = input(RECORD_CARDS_DEFAULT_HEADING_LEVEL, { transform: numberAttribute });

  protected readonly keys = RecordsTranslationKeys.Cards;
  readonly rendered = computed(() => renderCards(this.cards()));
}
