import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import type { RecordCard } from './record-cards';
import { RecordDetailCardsComponent } from './record-detail-cards.component';
import { RecordsTranslationKeys } from './records-translation-keys';
import { recordsUiTestProviders } from './records.testing';

@Component({
  selector: 'app-record-cards-host',
  imports: [RecordDetailCardsComponent],
  template: '<app-record-detail-cards [cards]="cards()" [headingLevel]="3" />',
})
class HostComponent {
  readonly cards = signal<readonly RecordCard[]>([]);
}

async function render(cards: readonly RecordCard[]): Promise<HTMLElement> {
  TestBed.configureTestingModule({ imports: [HostComponent], providers: recordsUiTestProviders() });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.cards.set(cards);
  fixture.detectChanges();
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('RecordDetailCardsComponent', () => {
  it('renders one card per adapter card with its translated heading and labelled values', async () => {
    const root = await render([
      { titleKey: 'testRecords.cards.request', fields: [{ labelKey: 'testRecords.fields.title', value: 'Broken door' }] },
      { titleKey: 'testRecords.cards.contact', fields: [{ labelKey: 'testRecords.fields.phone', value: '+966 11 000 0000', valueDir: 'ltr' }] },
    ]);
    const cards = root.querySelectorAll('dga-card');
    expect(cards).toHaveLength(2);
    expect(cards[0]?.textContent).toContain('testRecords.cards.request');
    expect(cards[0]?.textContent).toContain('testRecords.fields.title');
    expect(cards[0]?.textContent).toContain('Broken door');
    expect(cards[1]?.textContent).toContain('+966 11 000 0000');
    expect(root.querySelector('[role="heading"]')?.getAttribute('aria-level')).toBe('3');
  });

  it('shows "not provided" for an empty value and skips a card without fields', async () => {
    const root = await render([
      { titleKey: 'a', fields: [{ labelKey: 'a.date', value: null }, { labelKey: 'a.note', value: '  ' }] },
      { titleKey: 'b', fields: [] },
    ]);
    expect(root.querySelectorAll('dga-card')).toHaveLength(1);
    const empty = root.querySelectorAll('[data-empty]');
    expect(empty).toHaveLength(2);
    expect(empty[0]?.textContent).toContain(RecordsTranslationKeys.Cards.EmptyValue);
  });

  it('renders nothing for no cards', async () => {
    const root = await render([]);
    expect(root.querySelectorAll('dga-card')).toHaveLength(0);
  });
});
