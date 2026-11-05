import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { reflectComponentType, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import * as ngDga from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../core/i18n/language';
import { showcaseTestLanguage, showcaseTestProviders } from '../showcase.testing';
import { COMPONENT_SECTIONS, ShowcaseCategory, componentCategories, missingSections } from './component-sections';
import { ComponentsPageComponent } from './components-page.component';

/** The generated catalog (docs/design/component-catalog.md), from the repository root. */
const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..', '..', '..');
const CATALOG_DOC = join(REPO_ROOT, 'docs', 'design', 'component-catalog.md');
const CATALOG_LINK = /\]\(#(dga-[a-z-]+)\)/g;

/** Every component selector the package exports (from its public API, not a hand-kept list). */
function exportedSelectors(): string[] {
  return Object.values(ngDga)
    .map(value => (typeof value === 'function' ? reflectComponentType(value as Type<unknown>)?.selector : undefined))
    .filter((selector): selector is string => selector !== undefined);
}

/** Every selector the generated catalog lists in its category table. */
function catalogSelectors(): string[] {
  const text = readFileSync(CATALOG_DOC, 'utf8');
  return [...text.matchAll(CATALOG_LINK)].map(match => match[1] ?? '');
}

describe('the component section registry', () => {
  it('has a section for every component @codelab/ng-dga exports', () => {
    const exported = exportedSelectors();
    expect(exported.length).toBeGreaterThan(0);
    expect(missingSections(exported)).toEqual([]);
  });

  it('has a section for every component the generated catalog lists (its live-example links point here)', () => {
    const listed = catalogSelectors();
    expect(listed.length).toBeGreaterThan(0);
    expect(missingSections(listed)).toEqual([]);
  });

  it('lists only components the package exports, each once', () => {
    const exported = new Set(exportedSelectors());
    const selectors = COMPONENT_SECTIONS.map(section => section.selector);
    expect(new Set(selectors).size).toBe(selectors.length);
    expect(selectors.filter(selector => !exported.has(selector))).toEqual([]);
  });

  it('groups the sections in catalog category order', () => {
    expect(componentCategories().map(group => group.category)).toEqual(Object.values(ShowcaseCategory));
    expect(componentCategories().flatMap(group => group.selectors).sort()).toEqual(COMPONENT_SECTIONS.map(s => s.selector).sort());
  });

  it('names what is missing', () => {
    expect(missingSections(['dga-card', 'dga-new-thing', 'dga-new-thing'])).toEqual(['dga-new-thing']);
  });

  it('renders an anchored section for every registry entry on /showcase', async () => {
    const language = showcaseTestLanguage(AppLanguages.English);
    TestBed.configureTestingModule({ providers: showcaseTestProviders(language) });
    const fixture = TestBed.createComponent(ComponentsPageComponent);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    for (const { selector } of COMPONENT_SECTIONS) {
      const section = element.querySelector(`section[id="${selector}"]`);
      expect(section, selector).not.toBeNull();
      expect(section?.querySelector(selector), `${selector} renders inside its section`).not.toBeNull();
    }
  });
});
