import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { DgaBreadcrumbComponent } from './dga-breadcrumb.component';
import type { DgaBreadcrumbItem } from './breadcrumb-items';

const ITEMS: readonly DgaBreadcrumbItem[] = [
  { label: 'Home', link: '/', icon: 'home' },
  { label: 'Admin', link: ['/admin'] },
  { label: 'Users' },
];

async function render(items: readonly DgaBreadcrumbItem[] = ITEMS) {
  const fixture = TestBed.createComponent(DgaBreadcrumbComponent);
  fixture.componentRef.setInput('items', items);
  fixture.componentRef.setInput('label', 'Breadcrumb');
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('DgaBreadcrumbComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  it('is a named navigation landmark holding an ordered list', async () => {
    const host = await render();
    const nav = host.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Breadcrumb');
    expect(nav?.querySelectorAll('ol > li')).toHaveLength(ITEMS.length);
  });

  it('renders earlier crumbs as router links and the current page as text with aria-current', async () => {
    const host = await render();
    const links = Array.from(host.querySelectorAll('a'));
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/', '/admin']);
    const current = host.querySelector('[aria-current="page"]');
    expect(current?.textContent?.trim()).toBe('Users');
    expect(current?.tagName).toBe('SPAN');
  });

  it('puts a decorative, direction-following separator between crumbs only', async () => {
    const host = await render();
    const separators = Array.from(host.querySelectorAll<HTMLElement>('.dga-breadcrumb__separator'));
    expect(separators).toHaveLength(ITEMS.length - 1);
    expect(separators.every((separator) => separator.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(separators[0].getAttribute('data-name')).toBe('chevron-end');
  });

  it('shows a crumb icon', async () => {
    const host = await render();
    expect(host.querySelector('a .dga-breadcrumb__icon')?.getAttribute('data-name')).toBe('home');
  });
});
