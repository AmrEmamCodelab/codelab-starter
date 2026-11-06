import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslationService } from '../../../core/i18n/translation.service';
import { echoTranslate } from '../../../layouts/layout.testing';
import { adminTestProviders, allByTestId, byTestId, choose, grant, press, problem, settle } from '../organizations/admin.testing';
import { AdminSettingsClient } from './admin-settings.client';
import { SettingsPageComponent } from './settings-page.component';
import { SettingsPermissionKeys } from './settings-permissions';
import { SettingsTranslationKeys } from './settings-translation-keys';
import { runtimeSetting, settingsOfEveryType } from './settings.testing';
import { SettingValueType } from './setting.models';

const Keys = SettingsTranslationKeys;

describe('SettingsPageComponent', () => {
  let fixture: ComponentFixture<SettingsPageComponent>;
  let host: HTMLElement;
  let client: { list: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> };

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(SettingsPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  function dialog(): HTMLElement {
    return host.querySelector('dialog[open]') as HTMLElement;
  }

  async function submit(): Promise<void> {
    (dialog().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
  }

  async function typeValue(text: string): Promise<void> {
    const input = dialog().querySelector('[data-testid="editor-value"] input') as HTMLInputElement;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  beforeEach(() => {
    client = { list: vi.fn().mockReturnValue(of(settingsOfEveryType())), update: vi.fn() };
    TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [
        ...adminTestProviders(),
        // Only the portal group has a name text here, so the security group shows as the server sent it.
        { provide: TranslationService, useValue: { translate: echoTranslate, has: (key: string) => key === Keys.Groups.Portal } },
        { provide: AdminSettingsClient, useValue: client },
      ],
    });
  });

  it('lists the settings grouped, with typed values and the description in the language', async () => {
    grant(SettingsPermissionKeys.View);
    await render();
    const rows = [...host.querySelectorAll('tbody tr')];
    expect(rows.map((row) => row.querySelector('code')?.textContent)).toEqual([
      'portal.contact.daily-limit',
      'portal.welcome-text',
      'security.lockout-enabled',
      'security.session-idle',
    ]);
    const values = allByTestId(fixture, 'setting-value').map((cell) => cell.textContent?.trim());
    expect(values).toEqual(['20', 'Hello', `${Keys.Values.On}|value=true`, `${Keys.Values.Duration}|value=900`]);
    expect(rows[0].textContent).toContain('الحد اليومي');
    expect(rows[0].textContent).toContain(Keys.Values.NeverChanged);
  });

  it('shows each group by its translated name in the filter and the Group column, else as sent', async () => {
    grant(SettingsPermissionKeys.View);
    await render();
    const options = [...byTestId(fixture, 'filter-group')!.querySelectorAll('option')].map((option) => option.textContent?.trim());
    expect(options).toEqual([Keys.Filters.AllGroups, Keys.Groups.Portal, 'security']);
    expect(allByTestId(fixture, 'setting-group').map((cell) => cell.textContent?.trim())).toEqual([
      Keys.Groups.Portal,
      Keys.Groups.Portal,
      'security',
      'security',
    ]);
  });

  it('filters by group', async () => {
    grant(SettingsPermissionKeys.View);
    await render();
    choose(byTestId(fixture, 'filter-group'), 'security');
    await settle(fixture);
    expect(host.querySelectorAll('tbody tr')).toHaveLength(2);
  });

  it('hides editing from a viewer', async () => {
    grant(SettingsPermissionKeys.View);
    await render();
    expect(allByTestId(fixture, 'setting-edit')).toHaveLength(0);
    expect(host.querySelector('app-editor-dialog-pattern')).toBeNull();
  });

  it('edits a number, refusing a non-number on the client', async () => {
    client.update.mockReturnValue(of(runtimeSetting({ value: '25' })));
    grant(SettingsPermissionKeys.View, SettingsPermissionKeys.Manage);
    await render();
    press(allByTestId(fixture, 'setting-edit')[0]);
    await settle(fixture);
    expect(dialog().textContent).toContain(Keys.Editor.Title);
    expect(dialog().textContent).toContain(Keys.Editor.IntegerHint);
    await typeValue('abc');
    expect(dialog().textContent).toContain(Keys.Editor.IntegerInvalid);
    await submit();
    expect(client.update).not.toHaveBeenCalled();
    await typeValue('25');
    await submit();
    expect(client.update).toHaveBeenCalledWith('portal.contact.daily-limit', { value: '25' });
    expect(host.querySelector('dialog[open]')).toBeNull();
    expect(allByTestId(fixture, 'setting-value')[0].textContent?.trim()).toBe('25');
  });

  it('edits a boolean with the checkbox and offers the default back', async () => {
    client.update.mockReturnValue(of(runtimeSetting({ key: 'security.lockout-enabled', valueType: SettingValueType.Boolean, value: 'false' })));
    grant(SettingsPermissionKeys.View, SettingsPermissionKeys.Manage);
    await render();
    press(allByTestId(fixture, 'setting-edit')[2]);
    await settle(fixture);
    expect(dialog().querySelector('[data-testid="editor-checkbox"]')).not.toBeNull();
    expect(dialog().querySelector('[data-testid="editor-value"]')).toBeNull();
    expect(byTestId(fixture, 'editor-default')?.textContent).toContain(`${Keys.Editor.Default}|value=${Keys.Values.Off}|value=false`);
    press(byTestId(fixture, 'editor-reset'));
    await settle(fixture);
    expect(byTestId(fixture, 'editor-reset')).toBeNull();
    await submit();
    expect(client.update).toHaveBeenCalledWith('security.lockout-enabled', { value: 'false' });
  });

  it('shows the server field error under the value', async () => {
    client.update.mockReturnValue(throwError(() => problem(HttpStatus.badRequest, 'VALIDATION_FAILED', { value: ['INVALID_FORMAT'] })));
    grant(SettingsPermissionKeys.View, SettingsPermissionKeys.Manage);
    await render();
    press(allByTestId(fixture, 'setting-edit')[1]);
    await settle(fixture);
    await typeValue('Welcome');
    await submit();
    expect(dialog().textContent).toContain('errors.INVALID_FORMAT');
  });

  it('shows the error state when the list fails', async () => {
    client.list.mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    grant(SettingsPermissionKeys.View);
    await render();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.INTERNAL_ERROR');
  });
});
