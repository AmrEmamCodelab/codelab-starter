import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { DgaGeoSelectComponent } from './dga-geo-select.component';
import {
  DGA_GEO_EMPTY_SELECTION,
  type DgaGeoDataSource,
  DgaGeoDepth,
  type DgaGeoId,
  DgaGeoLanguage,
  DgaGeoLevel,
  type DgaGeoPlace,
  type DgaGeoSelection,
} from './geo-data-source';

const place = (id: string, en: string, ar: string): DgaGeoPlace => ({ id, name: { ar, en } });

/** An in-memory source whose answers the test releases by hand, to observe loading and late answers. */
class ManualSource implements DgaGeoDataSource {
  readonly pending: { key: string; resolve: (places: readonly DgaGeoPlace[]) => void; reject: (failure: unknown) => void }[] = [];

  regions() {
    return this.ask('regions');
  }

  cities(regionId: DgaGeoId) {
    return this.ask(`cities:${regionId}`);
  }

  districts(cityId: DgaGeoId) {
    return this.ask(`districts:${cityId}`);
  }

  async answer(key: string, places: readonly DgaGeoPlace[]): Promise<void> {
    const request = this.pending.find((entry) => entry.key === key);
    if (request === undefined) throw new Error(`no request for ${key}`);
    request.resolve(places);
    await Promise.resolve();
  }

  async fail(key: string): Promise<void> {
    this.pending.find((entry) => entry.key === key)?.reject(new Error('offline'));
    await Promise.resolve();
  }

  private ask(key: string): Promise<readonly DgaGeoPlace[]> {
    return new Promise((resolve, reject) => this.pending.push({ key, resolve, reject }));
  }
}

@Component({
  imports: [DgaGeoSelectComponent],
  template: `
    <div [attr.dir]="language() === 'ar' ? 'rtl' : 'ltr'">
      <dga-geo-select label="Address" regionLabel="Region" cityLabel="City" districtLabel="District" placeholder="Choose"
                      loadErrorText="Could not load" [error]="error()" [depth]="depth()" [language]="language()"
                      [dataSource]="source" [(value)]="place" [(touched)]="touched" (loadFailed)="failures.push($event)" />
    </div>
  `,
})
class HostComponent {
  readonly source = new ManualSource();
  readonly language = signal<DgaGeoLanguage>(DgaGeoLanguage.Arabic);
  readonly depth = signal<DgaGeoDepth>(DgaGeoDepth.District);
  readonly error = signal<string | null>(null);
  readonly place = signal<DgaGeoSelection>(DGA_GEO_EMPTY_SELECTION);
  readonly touched = signal(false);
  readonly failures: DgaGeoLevel[] = [];
}

async function renderHost() {
  const fixture = TestBed.createComponent(HostComponent);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  const selects = () => Array.from(element.querySelectorAll('select'));
  const settle = () => fixture.whenStable();
  return { fixture, host: fixture.componentInstance, element, selects, settle };
}

function choose(select: HTMLSelectElement, optionText: string): void {
  const option = Array.from(select.options).find((entry) => entry.textContent === optionText);
  if (option === undefined) throw new Error(`no option ${optionText}`);
  select.value = option.value;
  select.dispatchEvent(new Event('change'));
}

function optionTexts(select: HTMLSelectElement): string[] {
  return Array.from(select.options).map((option) => option.textContent ?? '').filter((text) => text !== 'Choose');
}

describe('DgaGeoSelectComponent', () => {
  it('loads the regions, busy until they arrive, and names them in the language', async () => {
    const { host, element, selects, settle } = await renderHost();
    expect(element.querySelector('fieldset')?.getAttribute('aria-busy')).toBe('true');
    expect(selects()[0].disabled).toBe(true);
    await host.source.answer('regions', [place('r1', 'Riyadh', 'الرياض'), place('r2', 'Makkah', 'مكة المكرمة')]);
    await settle();
    expect(element.querySelector('fieldset')?.hasAttribute('aria-busy')).toBe(false);
    expect(optionTexts(selects()[0])).toEqual(['الرياض', 'مكة المكرمة']);
    host.language.set(DgaGeoLanguage.English);
    await settle();
    expect(optionTexts(selects()[0])).toEqual(['Riyadh', 'Makkah']);
  });

  it('keeps city and district disabled until their parent is chosen', async () => {
    const { host, selects, settle } = await renderHost();
    await host.source.answer('regions', [place('r1', 'Riyadh', 'الرياض')]);
    await settle();
    expect(selects()[1].disabled).toBe(true);
    expect(selects()[2].disabled).toBe(true);
  });

  it('cascades: a region loads its cities, a city its districts, and the value follows', async () => {
    const { host, selects, settle } = await renderHost();
    host.language.set(DgaGeoLanguage.English);
    await host.source.answer('regions', [place('r1', 'Riyadh', 'الرياض')]);
    await settle();
    choose(selects()[0], 'Riyadh');
    await settle();
    expect(host.place()).toEqual({ regionId: 'r1', cityId: null, districtId: null });
    await host.source.answer('cities:r1', [place('c1', 'Riyadh City', 'مدينة الرياض')]);
    await settle();
    expect(selects()[1].disabled).toBe(false);
    choose(selects()[1], 'Riyadh City');
    await settle();
    await host.source.answer('districts:c1', [place('d1', 'Al Olaya', 'العليا')]);
    await settle();
    choose(selects()[2], 'Al Olaya');
    await settle();
    expect(host.place()).toEqual({ regionId: 'r1', cityId: 'c1', districtId: 'd1' });
  });

  it('clears city and district when the region changes, and drops a late answer for the old region', async () => {
    const { host, selects, settle } = await renderHost();
    host.language.set(DgaGeoLanguage.English);
    host.place.set({ regionId: 'r1', cityId: 'c1', districtId: 'd1' });
    await settle();
    await host.source.answer('regions', [place('r1', 'Riyadh', 'الرياض'), place('r2', 'Makkah', 'مكة المكرمة')]);
    await settle();
    choose(selects()[0], 'Makkah');
    await settle();
    expect(host.place()).toEqual({ regionId: 'r2', cityId: null, districtId: null });
    await host.source.answer('cities:r1', [place('c1', 'Riyadh City', 'مدينة الرياض')]);
    await host.source.answer('cities:r2', [place('c2', 'Jeddah', 'جدة')]);
    await settle();
    expect(optionTexts(selects()[1])).toEqual(['Jeddah']);
  });

  it('shows the load error on the failed level and emits it', async () => {
    const { host, selects, element, settle } = await renderHost();
    await host.source.fail('regions');
    await settle();
    expect(host.failures).toEqual([DgaGeoLevel.Region]);
    expect(selects()[0].getAttribute('aria-invalid')).toBe('true');
    expect(element.textContent).toContain('Could not load');
  });

  it('puts the field error on the first level without a choice', async () => {
    const { host, selects, settle } = await renderHost();
    host.place.set({ regionId: 'r1', cityId: null, districtId: null });
    host.error.set('Choose the full address');
    await settle();
    expect(selects()[0].hasAttribute('aria-invalid')).toBe(false);
    expect(selects()[1].getAttribute('aria-invalid')).toBe('true');
    expect(selects()[2].hasAttribute('aria-invalid')).toBe(false);
  });

  it('shows only the levels of its depth', async () => {
    const { host, selects, settle } = await renderHost();
    host.depth.set(DgaGeoDepth.City);
    await settle();
    expect(selects()).toHaveLength(2);
    host.depth.set(DgaGeoDepth.Region);
    await settle();
    expect(selects()).toHaveLength(1);
  });

  it('marks touched when a level is left', async () => {
    const { host, selects, settle } = await renderHost();
    selects()[0].dispatchEvent(new Event('blur'));
    await settle();
    expect(host.touched()).toBe(true);
  });

  it('labels the group and each level, in RTL and LTR alike', async () => {
    const { host, element, settle } = await renderHost();
    const labels = () => Array.from(element.querySelectorAll('label')).map((label) => label.textContent?.trim());
    expect(element.querySelector('legend')?.textContent).toBe('Address');
    expect(labels()).toEqual(['Region', 'City', 'District']);
    expect(element.querySelector('[dir]')?.getAttribute('dir')).toBe('rtl');
    host.language.set(DgaGeoLanguage.English);
    await settle();
    expect(element.querySelector('[dir]')?.getAttribute('dir')).toBe('ltr');
    expect(labels()).toEqual(['Region', 'City', 'District']);
  });
});
