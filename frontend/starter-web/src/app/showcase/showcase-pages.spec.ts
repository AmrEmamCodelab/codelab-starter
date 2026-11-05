import type { Type } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AppLanguages, type AppLanguage } from '../core/i18n/language';
import { SHOWCASE_PAGES, ShowcasePageId } from './showcase-pages';
import { SHOWCASE_PAGE_LOADERS } from './showcase.routes';
import { ShowcaseTranslationKeys } from './showcase-translation-keys';
import { bundleText, showcaseTestLanguage, showcaseTestProviders } from './showcase.testing';
import { ShowcaseState } from './frame/showcase-state';
import { StaffDetailPageComponent } from './patterns/staff-detail-page.component';
import { WizardOutcome, WizardPageComponent } from './patterns/wizard-page.component';
import { AdminListPageComponent, NEW_ROW } from './patterns/admin-list-page.component';
import { ALLOWED_ACTIONS_FIXTURE, DecisionCode } from './patterns/pattern-fixtures';

async function render<T>(component: Type<T>, language: AppLanguage): Promise<ComponentFixture<T>> {
  const testLanguage = showcaseTestLanguage(language);
  TestBed.configureTestingModule({ providers: showcaseTestProviders(testLanguage) });
  const fixture = TestBed.createComponent(component);
  await fixture.whenStable();
  return fixture;
}

const LANGUAGES = [AppLanguages.Arabic, AppLanguages.English] as const;
const action = (code: string) => ALLOWED_ACTIONS_FIXTURE.find(candidate => candidate.code === code)!;

describe('every showcase page', () => {
  for (const page of SHOWCASE_PAGES) {
    for (const language of LANGUAGES) {
      it(`renders ${page.id} in ${language}: translated title and intro, its navigation entry marked current`, async () => {
        const component = (await SHOWCASE_PAGE_LOADERS[page.id]()) as Type<unknown>;
        const fixture = await render(component, language);
        const element = fixture.nativeElement as HTMLElement;
        expect(element.querySelector('.showcase__title')?.textContent?.trim()).toBe(bundleText(language, page.titleKey));
        expect(element.querySelector('.showcase__intro')?.textContent?.trim()).toBe(bundleText(language, page.introKey));
        expect(element.querySelector('a[aria-current="page"]')?.textContent?.trim()).toBe(bundleText(language, page.titleKey));
        expect(element.textContent).not.toMatch(/\bshowcase\.[a-z-]+\.[a-z-]+\b/);
      });
    }
  }

  it('renders a <main> on every page but the framed shells, which bring their own', async () => {
    for (const page of SHOWCASE_PAGES) {
      TestBed.resetTestingModule();
      const component = (await SHOWCASE_PAGE_LOADERS[page.id]()) as Type<unknown>;
      const fixture = await render(component, AppLanguages.English);
      const mains = (fixture.nativeElement as HTMLElement).querySelectorAll('main');
      expect(mains.length, page.id).toBe(1);
      const framed = page.id === ShowcasePageId.PortalShell || page.id === ShowcasePageId.RequesterShell;
      expect(mains[0]?.classList.contains('showcase__content'), page.id).toBe(!framed);
    }
  });
});

describe('the staff-detail demo', () => {
  it('decides an action without a reason at once', async () => {
    const fixture = await render(StaffDetailPageComponent, AppLanguages.English);
    fixture.componentInstance.onDecide(action(DecisionCode.Approve));
    expect(fixture.componentInstance.pending()).toBeNull();
    expect(fixture.componentInstance.decided()).toBe(ShowcaseTranslationKeys.Patterns.Approve);
  });

  it('opens the decision dialog for an action that needs a reason, and records it on confirm', async () => {
    const fixture = await render(StaffDetailPageComponent, AppLanguages.English);
    const reject = action(DecisionCode.Reject);
    fixture.componentInstance.onDecide(reject);
    expect(fixture.componentInstance.pending()).toBe(reject);
    fixture.componentInstance.onConfirm();
    expect(fixture.componentInstance.pending()).toBeNull();
    expect(fixture.componentInstance.decided()).toBe(reject.labelKey);
  });

  it('cancels the dialog without a decision', async () => {
    const fixture = await render(StaffDetailPageComponent, AppLanguages.English);
    fixture.componentInstance.onDecide(action(DecisionCode.Return));
    fixture.componentInstance.onCancel();
    expect(fixture.componentInstance.decided()).toBeNull();
  });

  it('offers no decision outside the ready state', async () => {
    const fixture = await render(StaffDetailPageComponent, AppLanguages.English);
    fixture.componentInstance.state.set(ShowcaseState.Error);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).not.toBeNull();
    fixture.componentInstance.retry();
    expect(fixture.componentInstance.state()).toBe(ShowcaseState.Ready);
  });
});

describe('the wizard demo', () => {
  it('records the outcome and starts over on cancel', async () => {
    const fixture = await render(WizardPageComponent, AppLanguages.English);
    const page = fixture.componentInstance;
    page.name.set('Sara');
    page.step.set(1);
    page.record(WizardOutcome.Submitted);
    expect(page.outcome()).toBe(WizardOutcome.Submitted);
    page.restart();
    expect([page.name(), page.step(), page.outcome()]).toEqual(['', 0, null]);
  });
});

describe('the admin-list demo', () => {
  it('opens the editor empty for a new row, prefilled for an existing one, and closes it', async () => {
    const fixture = await render(AdminListPageComponent, AppLanguages.English);
    const page = fixture.componentInstance;
    page.openCreate();
    expect([page.editing(), page.draftName()]).toEqual([NEW_ROW, '']);
    page.closeEditor();
    expect(page.editing()).toBeNull();
  });
});
