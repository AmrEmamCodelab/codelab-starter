import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OpsApiClient } from '../../core/api/ops-api.client';
import { CertificateStatus } from '../../core/api/ops-api.models';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { AppLanguages } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { RoutePaths } from '../../core/routing/route-paths';
import { FakeOpsApiClient, echoTranslate, opsTestProviders, textOf } from '../../core/testing/ops.testing';
import { SHELL_NAV_ENTRIES, shellNavItems } from './shell-navigation';
import { ShellComponent } from './shell.component';

describe('shell navigation', () => {
  it('lists the four screens with translated labels and absolute links', () => {
    const items = shellNavItems(echoTranslate);
    expect(items.map((item) => item.link)).toEqual([
      `/${RoutePaths.Overview}`,
      `/${RoutePaths.Reference}`,
      `/${RoutePaths.Jobs}`,
      `/${RoutePaths.Certificates}`,
    ]);
    expect(items.map((item) => item.label)).toEqual(SHELL_NAV_ENTRIES.map((entry) => entry.labelKey));
  });
});

describe('ShellComponent', () => {
  let client: FakeOpsApiClient;
  const toggle = vi.fn(async () => undefined);

  beforeEach(() => {
    client = new FakeOpsApiClient();
    toggle.mockClear();
    TestBed.configureTestingModule({
      providers: [
        ...opsTestProviders(),
        { provide: OpsApiClient, useValue: client },
        { provide: LanguageService, useValue: { toggle, alternate: signal(AppLanguages.English) } },
      ],
    });
  });

  it('asks who the operator is, loads certificates and shows the name', () => {
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    expect(client.calls).toEqual(['me', 'certificates']);
    client.answer('me', { name: 'CORP\\operator1', groups: [] });
    client.answer('certificates', []);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(textOf(root, '[data-testid="operator-name"] bdi')).toBe('CORP\\operator1');
    expect(root.querySelector('[data-testid="certificate-alert"]')).toBeNull();
  });

  it('shows the certificate banner when a certificate needs attention', () => {
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    client.answer('certificates', [
      { name: 'idp-signing', subject: 'CN=idp', notAfter: '2026-11-20T00:00:00Z', daysLeft: 13, status: CertificateStatus.Warning },
    ]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="certificate-alert"]')).not.toBeNull();
  });

  it('switches the language from the header', () => {
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(textOf(root, '[data-testid="language-switch"]')).toContain(CommonKeys.Language.Switch);
    expect(root.querySelector('[data-testid="language-switch"]')?.getAttribute('lang')).toBe('en');
    root.querySelector<HTMLButtonElement>('[data-testid="language-switch"] button')?.click();
    expect(toggle).toHaveBeenCalled();
  });
});
