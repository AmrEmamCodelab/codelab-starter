import { Component, signal, type WritableSignal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DgaFileRejectionReason, DgaFileUploadComponent } from '@codelab/ng-dga';
import { throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { flattenTranslations } from '../../core/i18n/translation.service';
import { AttachmentUploadEventKind, type AttachableRecordId } from '../attachments/attachments.models';
import { TEST_RECORD_ID, TEST_RECORD_TYPE, TEST_TYPE_KEY, testFile } from '../attachments/attachments.testing';
import { AttachmentsPermissionKeys } from './attachments-permission-keys';
import { RecordAttachmentsTestIds } from './record-attachments-keys';
import { RecordAttachmentsComponent } from './record-attachments.component';
import {
  OPTIONAL_TYPE,
  TEST_REFERENCE_CODE,
  TEST_TYPE,
  aList,
  anAttachment,
  fakeAdapter,
  recordAttachmentsTestProviders,
  tooLarge,
  useLanguage,
  type FakeAdapter,
} from './record-attachments.testing';

const BUNDLES = { [AppLanguages.Arabic]: flattenTranslations(ar), [AppLanguages.English]: flattenTranslations(en) };

function text(language: AppLanguage, key: string): string {
  return BUNDLES[language][key] ?? key;
}

@Component({
  selector: 'app-record-attachments-host',
  imports: [RecordAttachmentsComponent],
  template: '<app-record-attachments [recordType]="recordType()" [recordId]="recordId()" [readonly]="readonly()" />',
})
class HostComponent {
  readonly recordType = signal(TEST_RECORD_TYPE);
  readonly recordId = signal<AttachableRecordId>(TEST_RECORD_ID);
  readonly readonly = signal(false);
}

interface Rendered {
  readonly fixture: ComponentFixture<HostComponent>;
  readonly root: HTMLElement;
}

interface RenderOptions {
  readonly language?: AppLanguage;
  readonly held?: readonly string[];
  readonly readonly?: boolean;
}

async function render(adapter: FakeAdapter, options: RenderOptions = {}): Promise<Rendered> {
  const { language = AppLanguages.Arabic, held = Object.values(AttachmentsPermissionKeys), readonly = false } = options;
  TestBed.configureTestingModule({ imports: [HostComponent], providers: recordAttachmentsTestProviders(adapter, language, held) });
  await useLanguage(language);
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.readonly.set(readonly);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, root: fixture.nativeElement as HTMLElement };
}

function uploadControls(fixture: ComponentFixture<HostComponent>): DgaFileUploadComponent[] {
  return fixture.debugElement.queryAll(By.directive(DgaFileUploadComponent)).map((debug) => debug.componentInstance as DgaFileUploadComponent);
}

function pick(fixture: ComponentFixture<HostComponent>, files: File[], index = 0): void {
  uploadControls(fixture)[index]?.filesSelected.emit({ accepted: files, rejected: [] });
  fixture.detectChanges();
}

function button(root: HTMLElement, selector: string): HTMLButtonElement | null {
  return root.querySelector<HTMLButtonElement>(`${selector} button`);
}

describe.each([AppLanguages.Arabic, AppLanguages.English])('RecordAttachmentsComponent (%s)', (language) => {
  it('loads the types and the list, one upload control per type, labelled in the language', async () => {
    const adapter = fakeAdapter([TEST_TYPE, OPTIONAL_TYPE]);
    const { fixture, root } = await render(adapter, { language });
    expect(adapter.types).toHaveBeenCalledWith(TEST_RECORD_TYPE);
    expect(adapter.list).toHaveBeenCalledWith(TEST_RECORD_TYPE, TEST_RECORD_ID);
    const types = root.querySelectorAll(`[data-testid="${RecordAttachmentsTestIds.Type}"]`);
    expect([...types].map((element) => element.getAttribute('data-type-key'))).toEqual([TEST_TYPE.key, OPTIONAL_TYPE.key]);
    const label = language === AppLanguages.Arabic ? TEST_TYPE.nameAr : TEST_TYPE.nameEn;
    expect(uploadControls(fixture)[0]?.label()).toBe(label);
    expect(root.textContent).toContain(text(language, 'attachments.widget.browse'));
  });

  it('shows the oversize refusal in the language with its reference code, on the row and in the page feedback', async () => {
    const adapter = fakeAdapter();
    const { fixture, root } = await render(adapter, { language });
    pick(fixture, [testFile('big.pdf')]);
    adapter.uploads[0]?.error(tooLarge());
    fixture.detectChanges();

    const message = text(language, 'errors.ATTACHMENTS_FILE_TOO_LARGE');
    const row = root.querySelector('[data-status="failed"]');
    expect(row?.textContent).toContain(message);
    expect(row?.querySelector('[dir="ltr"]')?.textContent).toBe(TEST_REFERENCE_CODE);
    const banner = root.querySelector('dga-page-feedback');
    expect(banner?.textContent).toContain(message);
    expect(banner?.textContent).toContain(TEST_REFERENCE_CODE);
    expect(banner?.textContent).toContain(text(language, 'attachments.widget.failed-upload'));
  });

  it('shows the server missing verdict on a required type until a file is linked', async () => {
    const adapter = fakeAdapter([TEST_TYPE], aList([], [TEST_TYPE_KEY]));
    const { fixture, root } = await render(adapter, { language });
    expect(root.textContent).toContain(text(language, 'attachments.widget.missing'));

    adapter.list.mockReturnValue(fakeAdapter([TEST_TYPE], aList([anAttachment()], [])).list());
    pick(fixture, [testFile()]);
    adapter.uploads[0]?.next({ kind: AttachmentUploadEventKind.Linked, attachment: anAttachment() });
    fixture.detectChanges();
    expect(root.textContent).not.toContain(text(language, 'attachments.widget.missing'));
    expect(root.querySelector('[data-status="uploaded"]')?.textContent).toContain(anAttachment().fileName);
  });
});

describe('RecordAttachmentsComponent', () => {
  it('uploads a picked file through the adapter with the record and the type', async () => {
    const adapter = fakeAdapter();
    const { fixture, root } = await render(adapter);
    pick(fixture, [testFile('a.pdf')]);
    expect(adapter.upload).toHaveBeenCalledWith(TEST_RECORD_TYPE, TEST_RECORD_ID, TEST_TYPE_KEY, expect.any(File));
    adapter.uploads[0]?.next({ kind: AttachmentUploadEventKind.Progress, loadedBytes: 1, totalBytes: 4, percent: 25 });
    fixture.detectChanges();
    expect(root.querySelector('[data-status="uploading"] progress')?.getAttribute('value')).toBe('25');
  });

  it('shows the client refusals of a pick as the control error', async () => {
    const { fixture } = await render(fakeAdapter(), { language: AppLanguages.English });
    uploadControls(fixture)[0]?.filesSelected.emit({ accepted: [], rejected: [{ file: testFile('x.exe'), reason: DgaFileRejectionReason.Type }] });
    fixture.detectChanges();
    expect(uploadControls(fixture)[0]?.error()).toBe('x.exe is not an allowed file type.');
  });

  it.each([
    [AppLanguages.Arabic, AppLanguages.English],
    [AppLanguages.English, AppLanguages.Arabic],
  ])('re-translates a client refusal when the language switches (%s to %s)', async (from, to) => {
    const { fixture } = await render(fakeAdapter(), { language: from });
    const fileName = 'notes.txt';
    const rejected = [{ file: testFile(fileName), reason: DgaFileRejectionReason.Type }];
    uploadControls(fixture)[0]?.filesSelected.emit({ accepted: [], rejected });
    fixture.detectChanges();
    const refusal = (language: AppLanguage): string => text(language, 'attachments.widget.rejected-type').replace('{{name}}', fileName);
    expect(uploadControls(fixture)[0]?.error()).toBe(refusal(from));

    await useLanguage(to);
    (TestBed.inject(LanguageService).current as WritableSignal<AppLanguage>).set(to);
    fixture.detectChanges();
    expect(uploadControls(fixture)[0]?.error()).toBe(refusal(to));
  });

  it('downloads and removes a linked attachment from its row', async () => {
    const attachment = anAttachment();
    const adapter = fakeAdapter([TEST_TYPE], aList([attachment]));
    const { fixture, root } = await render(adapter);
    button(root, '.dga-file-upload__download')?.click();
    expect(adapter.download).toHaveBeenCalledWith(attachment);
    button(root, '.dga-file-upload__remove')?.click();
    fixture.detectChanges();
    expect(adapter.remove).toHaveBeenCalledWith(attachment.id);
  });

  it('readonly hides the picker and the remove button, keeps the download', async () => {
    const adapter = fakeAdapter([TEST_TYPE], aList([anAttachment()]));
    const { root } = await render(adapter, { readonly: true });
    expect(root.querySelector('.dga-file-upload__zone')).toBeNull();
    expect(root.querySelector('.dga-file-upload__remove')).toBeNull();
    expect(root.querySelector('.dga-file-upload__download')).not.toBeNull();
  });

  it('without attachments.upload the control is read-only; without attachments.remove there is no remove button', async () => {
    const adapter = fakeAdapter([TEST_TYPE], aList([anAttachment()]));
    const { root } = await render(adapter, { held: [AttachmentsPermissionKeys.View] });
    expect(root.querySelector('.dga-file-upload__zone')).toBeNull();
    expect(root.querySelector('.dga-file-upload__remove')).toBeNull();
    expect(root.querySelector('.dga-file-upload__download')).not.toBeNull();
  });

  it('with upload but not remove the picker shows and rows have no remove button', async () => {
    const adapter = fakeAdapter([TEST_TYPE], aList([anAttachment()]));
    const { root } = await render(adapter, { held: [AttachmentsPermissionKeys.View, AttachmentsPermissionKeys.Upload] });
    expect(root.querySelector('.dga-file-upload__zone')).not.toBeNull();
    expect(root.querySelector('.dga-file-upload__remove')).toBeNull();
  });

  it('renders nothing without attachments.view', async () => {
    const { root } = await render(fakeAdapter(), { held: [] });
    expect(root.querySelector(`[data-testid="${RecordAttachmentsTestIds.Root}"]`)).toBeNull();
  });

  it('shows a failed load with a retry that loads again', async () => {
    const adapter = fakeAdapter();
    adapter.types.mockReturnValueOnce(throwError(() => tooLarge()));
    const { fixture, root } = await render(adapter, { language: AppLanguages.English });
    expect(root.textContent).toContain('The attachments could not be loaded.');
    button(root, `[data-testid="${RecordAttachmentsTestIds.Retry}"]`)?.click();
    fixture.detectChanges();
    expect(adapter.types).toHaveBeenCalledTimes(2);
    expect(root.querySelectorAll(`[data-testid="${RecordAttachmentsTestIds.Type}"]`)).toHaveLength(1);
  });

  it('says so when the record type has no attachment types', async () => {
    const { root } = await render(fakeAdapter([], aList()), { language: AppLanguages.English });
    expect(root.querySelector(`[data-testid="${RecordAttachmentsTestIds.NoTypes}"]`)?.textContent).toContain(
      'No attachment types are defined for this record.',
    );
  });

  it('loads again when the record changes', async () => {
    const adapter = fakeAdapter();
    const { fixture } = await render(adapter);
    fixture.componentInstance.recordType.set('another-record-type');
    fixture.detectChanges();
    expect(adapter.types).toHaveBeenLastCalledWith('another-record-type');
  });
});
