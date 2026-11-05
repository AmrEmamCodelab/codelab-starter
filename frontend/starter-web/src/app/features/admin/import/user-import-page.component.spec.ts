import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { adminTestProviders, byTestId, grant, press, previewRow, problem, settle } from '../organizations/admin.testing';
import { AccountBinding } from '../users/data/user.models';
import { UserImportClient } from './data/user-import-api';
import { IMPORT_FILE_MEDIA_TYPE } from './import-file';
import { UserImportPageComponent } from './user-import-page.component';

const PREVIEW = {
  batchId: 'batch-1',
  rows: [previewRow(), previewRow({ rowNumber: 3, userName: 'bad', valid: false, errors: ['USERS_IMPORT_ROW_EMAIL_INVALID'] })],
  validCount: 1,
  invalidCount: 1,
};
const COMMIT = {
  created: [
    { id: 'user-9', userName: 'omar', binding: AccountBinding.Password, activationLink: 'http://x/activate#ticket=t', expiresAt: '2026-11-06T10:00:00Z', activationErrorCode: null },
    { id: 'user-10', userName: 'dir', binding: AccountBinding.ActiveDirectory, activationLink: null, expiresAt: null, activationErrorCode: null },
  ],
  rejected: [{ rowNumber: 3, errors: ['USERS_IMPORT_ROW_EMAIL_INVALID'] }],
};

describe('UserImportPageComponent', () => {
  let fixture: ComponentFixture<UserImportPageComponent>;
  let host: HTMLElement;
  let client: { preview: ReturnType<typeof vi.fn>; commit: ReturnType<typeof vi.fn> };

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(UserImportPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  async function pick(file: File): Promise<void> {
    const input = fileInput();
    Object.defineProperty(input, 'files', { value: { item: (index: number) => (index === 0 ? file : null) }, configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle(fixture);
  }

  const fileInput = () => byTestId(fixture, 'file-picker-input') as HTMLInputElement;
  const buttonOf = (id: string) => byTestId(fixture, id)?.querySelector('button') as HTMLButtonElement;

  beforeEach(() => {
    client = { preview: vi.fn().mockReturnValue(of(PREVIEW)), commit: vi.fn().mockReturnValue(of(COMMIT)) };
    TestBed.configureTestingModule({
      imports: [UserImportPageComponent],
      providers: [...adminTestProviders(), { provide: UserImportClient, useValue: client }],
    });
  });

  it('hides the actions from someone without users.import', async () => {
    await render();
    expect(byTestId(fixture, 'import-preview')).toBeNull();
  });

  it('picks the workbook through the translated DGA picker, never the native control text', async () => {
    grant(PermissionKeys.UsersImport);
    await render();
    const field = byTestId(fixture, 'import-file') as HTMLElement;
    expect(field.querySelector('.file-picker__label')?.textContent?.trim()).toBe('admin.import.file.label');
    expect(byTestId(fixture, 'file-picker-choose')?.textContent).toContain('shared.file-picker.choose');
    expect(byTestId(fixture, 'file-picker-name')?.textContent?.trim()).toBe('shared.file-picker.none');
    expect(fileInput().getAttribute('aria-hidden')).toBe('true');
    expect(fileInput().accept).toBe(`.xlsx,${IMPORT_FILE_MEDIA_TYPE}`);
    await pick(new File(['x'], 'users.xlsx'));
    expect(byTestId(fixture, 'file-picker-name')?.textContent?.trim()).toBe('users.xlsx');
    expect(buttonOf('import-preview').disabled).toBe(false);
  });

  it('refuses a wrong file type on the client', async () => {
    grant(PermissionKeys.UsersImport);
    await render();
    await pick(new File(['x'], 'users.csv', { type: 'text/csv' }));
    expect(byTestId(fixture, 'import-file-problem')?.textContent).toContain('admin.import.file.wrong-type');
    expect(buttonOf('import-preview').disabled).toBe(true);
  });

  it('previews, commits and shows the created accounts with their links and the rejected rows', async () => {
    grant(PermissionKeys.UsersImport);
    await render();
    await pick(new File(['x'], 'users.xlsx', { type: IMPORT_FILE_MEDIA_TYPE }));
    press(byTestId(fixture, 'import-preview'));
    await settle(fixture);
    expect(client.preview).toHaveBeenCalled();
    expect(byTestId(fixture, 'import-summary')?.textContent).toContain('admin.import.preview.summary|valid=1,invalid=1');
    const rows = [...host.querySelectorAll('tbody tr')].map((row) => row.textContent ?? '');
    expect(rows[0]).toContain('admin.import.preview.valid');
    expect(rows[1]).toContain('errors.USERS_IMPORT_ROW_EMAIL_INVALID');
    expect(buttonOf('import-commit').disabled).toBe(false);

    press(byTestId(fixture, 'import-commit'));
    await settle(fixture);
    expect(client.commit).toHaveBeenCalledWith('batch-1');
    expect(byTestId(fixture, 'import-commit')).toBeNull();
    expect(host.textContent).toContain('admin.import.result.created-title');
    expect((host.querySelector('app-activation-link input') as HTMLInputElement).value).toBe('http://x/activate#ticket=t');
    expect(host.textContent).toContain('admin.import.result.no-link');
    expect(host.textContent).toContain('admin.import.result.rejected-title');
  });

  it('shows an upload refusal with the missing columns and the reference code', async () => {
    client.preview.mockReturnValue(throwError(() => problem(HttpStatus.badRequest, 'USERS_IMPORT_COLUMNS_MISSING', { Email: ['REQUIRED'] })));
    grant(PermissionKeys.UsersImport);
    await render();
    await pick(new File(['x'], 'users.xlsx'));
    press(byTestId(fixture, 'import-preview'));
    await settle(fixture);
    const alert = byTestId(fixture, 'import-error')?.textContent ?? '';
    expect(alert).toContain('errors.USERS_IMPORT_COLUMNS_MISSING');
    expect(alert).toContain('admin.import.error-detail|field=Email,message=errors.REQUIRED');
    expect(alert).toContain('patterns.common.reference-code');
  });

  it('disables the commit when no row is valid, and starts over', async () => {
    client.preview.mockReturnValue(of({ ...PREVIEW, validCount: 0, invalidCount: 2 }));
    grant(PermissionKeys.UsersImport);
    await render();
    await pick(new File(['x'], 'users.xlsx'));
    press(byTestId(fixture, 'import-preview'));
    await settle(fixture);
    expect(buttonOf('import-commit').disabled).toBe(true);
    press(byTestId(fixture, 'import-start-over'));
    await settle(fixture);
    expect(byTestId(fixture, 'import-commit')).toBeNull();
    expect(fileInput().value).toBe('');
    expect(byTestId(fixture, 'file-picker-name')?.textContent?.trim()).toBe('shared.file-picker.none');
  });
});
