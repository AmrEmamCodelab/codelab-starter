import { TestBed } from '@angular/core/testing';
import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { afterEach, describe, expect, it } from 'vitest';
import { TEST_DIRECTIONS, portalTestProviders } from '../portal.testing';
import { PortalContentStatus } from './portal-content';
import { PortalContentStateComponent, contentFeedback } from './portal-content-state.component';
import { PortalContentTranslationKeys as Keys } from './portal-content-translation-keys';

const echo = (key: string): string => key;

describe('contentFeedback', () => {
  it('shows loading and empty as information, a failure as an error, and nothing once loaded', () => {
    expect(contentFeedback(PortalContentStatus.Loading, false, echo)).toEqual({ kind: DgaPageFeedbackKind.Info, message: Keys.Loading });
    expect(contentFeedback(PortalContentStatus.Failed, false, echo)).toEqual({
      kind: DgaPageFeedbackKind.Error,
      title: Keys.FailedTitle,
      message: Keys.FailedBody,
    });
    expect(contentFeedback(PortalContentStatus.Loaded, true, echo)).toEqual({ kind: DgaPageFeedbackKind.Info, message: Keys.Empty });
    expect(contentFeedback(PortalContentStatus.Loaded, false, echo)).toBeNull();
  });
});

describe('PortalContentStateComponent', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  function render(status: PortalContentStatus, empty = false) {
    TestBed.configureTestingModule({ imports: [PortalContentStateComponent], providers: portalTestProviders() });
    const fixture = TestBed.createComponent(PortalContentStateComponent);
    fixture.componentRef.setInput('status', status);
    fixture.componentRef.setInput('empty', empty);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it.each(TEST_DIRECTIONS)('offers a public retry after a failure and emits it (%s)', (direction) => {
    document.documentElement.setAttribute('dir', direction);
    const { fixture, root } = render(PortalContentStatus.Failed);
    let retries = 0;
    fixture.componentInstance.retry.subscribe(() => retries++);

    expect(root.querySelector('.dga-page-feedback')?.getAttribute('role')).toBe('alert');
    const retry = root.querySelector<HTMLElement>('[data-testid="portal-content-retry"]');
    expect(retry?.hasAttribute('data-public-action')).toBe(true);
    retry?.click();
    expect(retries).toBe(1);
  });

  it('shows the loading text without a retry and renders nothing once loaded', () => {
    const { fixture, root } = render(PortalContentStatus.Loading);
    expect(root.querySelector('.dga-page-feedback__message')?.textContent).toBe(Keys.Loading);
    expect(root.querySelector('[data-testid="portal-content-retry"]')).toBeNull();

    fixture.componentRef.setInput('status', PortalContentStatus.Loaded);
    fixture.detectChanges();
    expect(root.querySelector('.dga-page-feedback')).toBeNull();
  });

  it('shows the empty state', () => {
    const { root } = render(PortalContentStatus.Loaded, true);
    expect(root.querySelector('.dga-page-feedback__message')?.textContent).toBe(Keys.Empty);
  });
});
