import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppLanguages } from '../../../../core/i18n/language';
import { fakeLanguage } from '../../../../layouts/layout.testing';
import { adminTestProviders, byTestId, press, settle } from '../../organizations/admin.testing';
import { ActivationLinkComponent } from './activation-link.component';
import { ClipboardService } from './clipboard.service';
import { formatInstant } from './format-instant';

const LINK = 'http://localhost:4280/activate#ticket=sealed';
const EXPIRES = '2026-11-06T10:00:00Z';

@Component({
  imports: [ActivationLinkComponent],
  template: `<app-activation-link [link]="link()" [expiresAt]="expiresAt()" />`,
})
class LinkHost {
  readonly link = signal(LINK);
  readonly expiresAt = signal(EXPIRES);
}

describe('ActivationLinkComponent', () => {
  let fixture: ComponentFixture<LinkHost>;
  let host: HTMLElement;
  const copy = vi.fn<(text: string) => Promise<boolean>>();

  beforeEach(() => {
    copy.mockReset().mockResolvedValue(true);
    TestBed.configureTestingModule({
      imports: [LinkHost],
      providers: [...adminTestProviders(fakeLanguage(AppLanguages.English)), { provide: ClipboardService, useValue: { copy } }],
    });
    fixture = TestBed.createComponent(LinkHost);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  it('shows the link read-only, left to right, with its expiry', () => {
    const field = host.querySelector('input') as HTMLInputElement;
    expect(field.value).toBe(LINK);
    expect(field.readOnly).toBe(true);
    expect(field.getAttribute('dir')).toBe('ltr');
    expect(host.textContent).toContain(`admin.activation.expires|at=${formatInstant(EXPIRES, AppLanguages.English)}`);
  });

  it('copies the link and says so', async () => {
    press(byTestId(fixture, 'copy-link'));
    await settle(fixture);
    expect(copy).toHaveBeenCalledWith(LINK);
    expect(host.textContent).toContain('admin.activation.copied');
  });

  it('asks for a manual copy when the clipboard refuses', async () => {
    copy.mockResolvedValue(false);
    press(byTestId(fixture, 'copy-link'));
    await settle(fixture);
    expect(host.textContent).toContain('admin.activation.copy-failed');
  });

  it('selects the whole link on focus', () => {
    const field = host.querySelector('input') as HTMLInputElement;
    const select = vi.spyOn(field, 'select');
    field.dispatchEvent(new Event('focus'));
    expect(select).toHaveBeenCalled();
  });
});

describe('formatInstant', () => {
  it('formats a valid instant in the language and keeps an unparsable value as is', () => {
    expect(formatInstant(EXPIRES, AppLanguages.English)).toMatch(/2026/);
    expect(formatInstant('not-a-date', AppLanguages.Arabic)).toBe('not-a-date');
  });
});

describe('ClipboardService', () => {
  const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');

  afterEach(() => {
    if (original) {
      Object.defineProperty(navigator, 'clipboard', original);
    } else {
      Reflect.deleteProperty(navigator, 'clipboard');
    }
  });

  function useClipboard(value: unknown): void {
    Object.defineProperty(navigator, 'clipboard', { value, configurable: true });
  }

  it('writes the text', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    useClipboard({ writeText });
    await expect(TestBed.inject(ClipboardService).copy('x')).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith('x');
  });

  it('answers false without the API or when the browser refuses', async () => {
    useClipboard(undefined);
    await expect(TestBed.inject(ClipboardService).copy('x')).resolves.toBe(false);
    useClipboard({ writeText: vi.fn().mockRejectedValue(new DOMException('denied', 'NotAllowedError')) });
    await expect(TestBed.inject(ClipboardService).copy('x')).resolves.toBe(false);
  });

  it('lets an unexpected failure through', async () => {
    const bug = new TypeError('bug');
    useClipboard({ writeText: vi.fn().mockRejectedValue(bug) });
    await expect(TestBed.inject(ClipboardService).copy('x')).rejects.toBe(bug);
  });
});
