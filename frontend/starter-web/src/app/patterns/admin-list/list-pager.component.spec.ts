import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { patternTestProviders, textOf } from '../patterns.testing';
import { ListPagerComponent } from './list-pager.component';

@Component({
  imports: [ListPagerComponent],
  template: '<app-list-pager [(page)]="page" [total]="total()" [pageSize]="pageSize" />',
})
class PagerHost {
  readonly page = signal(1);
  readonly total = signal(95);
  readonly pageSize = 20;
}

describe('ListPagerComponent', () => {
  let fixture: ComponentFixture<PagerHost>;
  let host: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [PagerHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(PagerHost);
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
  });

  const pageButtons = () => [...host.querySelectorAll<HTMLButtonElement>('.list-pager__page')];
  const navButtons = () => [...host.querySelectorAll<HTMLButtonElement>('dga-button button')];

  it('is a labelled navigation with the row summary', () => {
    expect(host.querySelector('nav')?.getAttribute('aria-label')).toBe('patterns.pager.label');
    expect(textOf(host, '.list-pager__summary')).toBe('patterns.pager.summary|from=1,to=20,total=95');
  });

  it('marks the current page and disables previous on the first page', () => {
    expect(pageButtons().map((button) => button.textContent?.trim())).toEqual(['1', '2', '3', '4', '5']);
    expect(pageButtons()[0].getAttribute('aria-current')).toBe('page');
    expect(pageButtons()[1].getAttribute('aria-current')).toBeNull();
    expect(pageButtons()[1].getAttribute('aria-label')).toBe('patterns.pager.page|page=2');
    expect(navButtons()[0].disabled).toBe(true);
    expect(navButtons()[1].disabled).toBe(false);
  });

  it('moves with the page buttons and next/previous, two-way', () => {
    pageButtons()[3].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.page()).toBe(4);
    expect(textOf(host, '.list-pager__summary')).toBe('patterns.pager.summary|from=61,to=80,total=95');

    navButtons()[1].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.page()).toBe(5);
    expect(navButtons()[1].disabled).toBe(true);

    navButtons()[0].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.page()).toBe(4);
  });

  it('hides the controls when everything fits on one page', () => {
    fixture.componentInstance.total.set(7);
    fixture.detectChanges();
    expect(pageButtons()).toHaveLength(0);
    expect(textOf(host, '.list-pager__summary')).toBe('patterns.pager.summary|from=1,to=7,total=7');
  });
});
