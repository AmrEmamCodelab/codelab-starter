import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AppLanguages } from '../../core/i18n/language';
import { fakeAuth, fakeLanguage, layoutTestProviders, type FakeLanguage } from '../layout.testing';
import { LanguageNameKeys, LanguageSwitcherComponent, otherLanguage } from './language-switcher.component';

describe('otherLanguage', () => {
  it('offers English from Arabic and Arabic from English', () => {
    expect(otherLanguage(AppLanguages.Arabic)).toBe(AppLanguages.English);
    expect(otherLanguage(AppLanguages.English)).toBe(AppLanguages.Arabic);
  });
});

describe('LanguageSwitcherComponent', () => {
  let language: FakeLanguage;

  beforeEach(() => {
    language = fakeLanguage(AppLanguages.Arabic);
    TestBed.configureTestingModule({
      imports: [LanguageSwitcherComponent],
      providers: layoutTestProviders(fakeAuth(), language),
    });
  });

  function render() {
    const fixture = TestBed.createComponent(LanguageSwitcherComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('names the target language in its own language', () => {
    const label = (render().nativeElement as HTMLElement).querySelector('span[lang]');
    expect(label?.getAttribute('lang')).toBe(AppLanguages.English);
    expect(label?.textContent).toBe(LanguageNameKeys[AppLanguages.English]);
  });

  it('switches to English and then back to Arabic', async () => {
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;

    button.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(language.set).toHaveBeenLastCalledWith(AppLanguages.English);
    expect((fixture.nativeElement as HTMLElement).querySelector('span[lang]')?.getAttribute('lang')).toBe(
      AppLanguages.Arabic,
    );

    button.click();
    expect(language.set).toHaveBeenLastCalledWith(AppLanguages.Arabic);
  });
});
