import { HttpErrorResponse } from '@angular/common/http';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AppLanguages, type AppLanguage } from '../../../core/i18n/language';
import { AttachmentError } from '../../../shared/attachments/attachment-error';
import type { AttachmentDemoRecord } from '../../../shared/attachments/attachments.models';
import { AttachmentsClient } from '../../../shared/attachments/attachments.client';
import { TEST_RECORD_ID, TEST_RECORD_TYPE } from '../../../shared/attachments/attachments.testing';
import { RecordAttachmentsComponent } from '../../../shared/record-attachments/record-attachments.component';
import {
  TEST_REFERENCE_CODE,
  fakeAdapter,
  recordAttachmentsTestProviders,
  useLanguage,
} from '../../../shared/record-attachments/record-attachments.testing';
import { By } from '@angular/platform-browser';
import { AttachmentsDemoPageComponent, demoRecordApiError } from './attachments-demo-page.component';

const DEMO_RECORD: AttachmentDemoRecord = { recordType: TEST_RECORD_TYPE, recordId: TEST_RECORD_ID };

async function render(
  demoRecord: () => ReturnType<AttachmentsClient['demoRecord']>,
  language: AppLanguage = AppLanguages.English,
): Promise<{ fixture: ComponentFixture<AttachmentsDemoPageComponent>; client: { demoRecord: ReturnType<typeof vi.fn> } }> {
  const client = { demoRecord: vi.fn(demoRecord) };
  TestBed.configureTestingModule({
    imports: [AttachmentsDemoPageComponent],
    providers: [...recordAttachmentsTestProviders(fakeAdapter(), language), { provide: AttachmentsClient, useValue: client }],
  });
  await useLanguage(language);
  const fixture = TestBed.createComponent(AttachmentsDemoPageComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, client };
}

describe('demoRecordApiError', () => {
  it('carries the code, the status and the reference code for the pattern', () => {
    expect(demoRecordApiError(new AttachmentError('NOT_FOUND', TEST_REFERENCE_CODE, 404))).toEqual({
      status: 404,
      code: 'NOT_FOUND',
      referenceCode: TEST_REFERENCE_CODE,
      fieldErrors: [],
    });
    expect(demoRecordApiError(new AttachmentError('NETWORK', null, 0)).referenceCode).toBeUndefined();
  });
});

describe('AttachmentsDemoPageComponent', () => {
  it('resolves the demo record and hosts the widget on it', async () => {
    const { fixture, client } = await render(() => of(DEMO_RECORD));
    expect(client.demoRecord).toHaveBeenCalledTimes(1);
    const widget = fixture.debugElement.query(By.directive(RecordAttachmentsComponent))?.componentInstance as RecordAttachmentsComponent;
    expect(widget.recordType()).toBe(TEST_RECORD_TYPE);
    expect(widget.recordId()).toBe(TEST_RECORD_ID);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('h1')?.textContent).toContain('Attachments demo');
  });

  it('shows the loading state while the record is on its way', async () => {
    const { fixture } = await render(() => new Subject<AttachmentDemoRecord>());
    expect(fixture.componentInstance.loading()).toBe(true);
    expect(fixture.debugElement.query(By.directive(RecordAttachmentsComponent))).toBeNull();
  });

  it.each([AppLanguages.Arabic, AppLanguages.English])('shows a refused demo record with its text and reference code (%s), and retries', async (language) => {
    const refusal = new HttpErrorResponse({
      status: 503,
      error: { code: 'ATTACHMENTS_STORAGE_UNAVAILABLE', referenceCode: TEST_REFERENCE_CODE, correlationId: 'trace-1' },
    });
    let calls = 0;
    const { fixture, client } = await render(() => (calls++ === 0 ? throwError(() => refusal) : of(DEMO_RECORD)), language);
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.error()?.code).toBe('ATTACHMENTS_STORAGE_UNAVAILABLE');
    expect(root.textContent).toContain(TEST_REFERENCE_CODE);
    const expected =
      language === AppLanguages.Arabic ? 'مخزن الملفات غير متاح حاليًا' : 'The file storage is not available at the moment';
    expect(root.textContent).toContain(expected);

    root.querySelector<HTMLButtonElement>('.requester-detail__retry button')?.click();
    fixture.detectChanges();
    expect(client.demoRecord).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.record()).toEqual(DEMO_RECORD);
  });
});
