import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DgaBreadcrumbItem } from '../breadcrumb/breadcrumb-items';
import { DgaPortalPageHeaderComponent } from './dga-portal-page-header.component';

const CRUMBS: readonly DgaBreadcrumbItem[] = [{ label: 'Home', link: '/' }, { label: 'Questions' }];

@Component({
  imports: [DgaPortalPageHeaderComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-portal-page-header title="Questions" [subtitle]="subtitle()" [breadcrumbs]="crumbs()" breadcrumbLabel="Breadcrumb">
        <span illustration class="art" aria-hidden="true"></span>
        <button actions type="button" class="action">Act</button>
      </dga-portal-page-header>
    </div>
  `,
})
class HostComponent {
  readonly dir = signal('rtl');
  readonly subtitle = signal<string | null>('Answers to common questions.');
  readonly crumbs = signal<readonly DgaBreadcrumbItem[]>(CRUMBS);
}

async function render(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('DgaPortalPageHeaderComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  it.each(['rtl', 'ltr'])('renders the breadcrumb landmark, one h1 and the subtitle (%s)', async (dir) => {
    const root = await render((host) => host.dir.set(dir));
    const nav = root.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Breadcrumb');
    expect(nav?.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Questions');
    const headings = root.querySelectorAll('h1');
    expect(headings).toHaveLength(1);
    expect(headings[0].textContent).toBe('Questions');
    expect(root.querySelector('.dga-portal-page-header__subtitle')?.textContent).toBe('Answers to common questions.');
  });

  it('omits the breadcrumb without items and the subtitle without text', async () => {
    const root = await render((host) => {
      host.crumbs.set([]);
      host.subtitle.set(null);
    });
    expect(root.querySelector('nav')).toBeNull();
    expect(root.querySelector('.dga-portal-page-header__subtitle')).toBeNull();
  });

  it('projects the illustration and the actions into their slots', async () => {
    const root = await render();
    expect(root.querySelector('.dga-portal-page-header__illustration .art')).not.toBeNull();
    expect(root.querySelector('.dga-portal-page-header__actions .action')).not.toBeNull();
  });
});
