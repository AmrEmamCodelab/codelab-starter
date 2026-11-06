import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { DgaFileUploadComponent, type DgaFileUploadStatusLabels } from './dga-file-upload.component';
import { type DgaFileUploadItem, DgaFileUploadStatus } from './file-upload-item';
import { failedFileUpload, queuedFileUpload, uploadedFileUpload, uploadingFileUpload } from './file-upload-state';
import { DgaFileRejectionReason, type DgaFileSelection } from './file-validation';

const MAX_BYTES = 10;
const REFERENCE = 'REF-7Q2K9';

@Component({
  imports: [DgaFileUploadComponent],
  template: `
    <div [attr.dir]="dir()">
      <dga-file-upload
        [label]="label()"
        hint="PDF only"
        dropLabel="Drop files here"
        browseLabel="Browse"
        accept=".pdf"
        [maxSizeBytes]="maxBytes()"
        [maxFiles]="maxFiles()"
        [disabled]="disabled()"
        [readonly]="readonly()"
        [required]="required()"
        requiredLabel="required"
        [missingText]="missingText()"
        [error]="error()"
        [items]="items()"
        [statusLabels]="statusLabels()"
        [referenceCodeLabel]="referenceCodeLabel()"
        [downloadable]="downloadable()"
        downloadLabel="Download {name}"
        [removeLabel]="removeLabel()"
        (filesSelected)="selections.push($event)"
        (download)="downloaded.push($event)"
        (remove)="removed.push($event)"
      >
        <span class="chosen">none</span>
      </dga-file-upload>
    </div>
  `,
})
class HostComponent {
  readonly upload = viewChild.required(DgaFileUploadComponent);
  readonly dir = signal('rtl');
  readonly label = signal<string | null>('Attachments');
  readonly maxBytes = signal<number | null>(MAX_BYTES);
  readonly maxFiles = signal(2);
  readonly disabled = signal(false);
  readonly readonly = signal(false);
  readonly required = signal(false);
  readonly missingText = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly items = signal<readonly DgaFileUploadItem[]>([]);
  readonly statusLabels = signal<DgaFileUploadStatusLabels>({});
  readonly referenceCodeLabel = signal<string | null>('Reference code');
  readonly downloadable = signal(false);
  readonly removeLabel = signal<string | null>('Remove {name}');
  readonly selections: DgaFileSelection[] = [];
  readonly downloaded: DgaFileUploadItem[] = [];
  readonly removed: DgaFileUploadItem[] = [];
}

function pdf(name: string, size = 1): File {
  return new File(['x'.repeat(size)], name, { type: 'application/pdf' });
}

function fileList(files: readonly File[]) {
  return { length: files.length, item: (index: number) => files[index] ?? null };
}

async function render(setup: (host: HostComponent) => void = () => undefined) {
  const fixture = TestBed.createComponent(HostComponent);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const input = () => root.querySelector('input[type="file"]') as HTMLInputElement;
  const pick = (files: readonly File[]) => {
    Object.defineProperty(input(), 'files', { value: fileList(files), configurable: true });
    input().dispatchEvent(new Event('change'));
  };
  const zone = () => root.querySelector('.dga-file-upload__zone') as HTMLElement;
  const drop = (files: readonly File[]) => {
    const event = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: { files: fileList(files) } });
    zone().dispatchEvent(event);
    return event;
  };
  const rows = () => [...root.querySelectorAll<HTMLElement>('.dga-file-upload__file')];
  return { fixture, root, input, pick, drop, zone, rows, host: fixture.componentInstance };
}

describe('DgaFileUploadComponent', () => {
  it.each(['rtl', 'ltr'])('is a group named by its label and described by its hint, with a hidden native input (%s)', async (dir) => {
    const { root, input } = await render((host) => host.dir.set(dir));
    const group = root.querySelector('[role="group"]') as HTMLElement;
    const label = root.querySelector('.dga-file-upload__label') as HTMLElement;
    expect(label.textContent?.trim()).toBe('Attachments');
    expect(group.getAttribute('aria-labelledby')).toBe(label.id);
    expect(group.getAttribute('aria-describedby')).toBe(root.querySelector('.dga-file-upload__hint')?.id);
    expect(input().getAttribute('aria-hidden')).toBe('true');
    expect(input().tabIndex).toBe(-1);
    expect(input().accept).toBe('.pdf');
    expect(input().multiple).toBe(true);
    expect(root.querySelector('.dga-file-upload__prompt')?.textContent).toBe('Drop files here');
    expect(root.querySelector('.dga-file-upload__extra .chosen')).not.toBeNull();
  });

  it('opens the native dialog from the browse button, never while disabled', async () => {
    const { fixture, root, input, host } = await render();
    const click = vi.spyOn(input(), 'click');
    (root.querySelector('.dga-file-upload__browse button') as HTMLButtonElement).click();
    expect(click).toHaveBeenCalledOnce();
    host.disabled.set(true);
    await fixture.whenStable();
    host.upload().browse();
    expect(click).toHaveBeenCalledOnce();
    expect((root.querySelector('.dga-file-upload__browse button') as HTMLButtonElement).disabled).toBe(true);
    expect(input().disabled).toBe(true);
  });

  it('emits accepted and rejected files with their reasons on pick', async () => {
    const { pick, host } = await render();
    const [good, big, wrong, extra] = [pdf('a.pdf'), pdf('b.pdf', MAX_BYTES + 1), new File(['x'], 'c.png'), pdf('d.pdf')];
    pick([good, big, wrong, pdf('e.pdf'), extra]);
    const [selection] = host.selections;
    expect(selection.accepted.map((file) => file.name)).toEqual(['a.pdf', 'e.pdf']);
    expect(selection.rejected.map((rejection) => [rejection.file.name, rejection.reason])).toEqual([
      ['b.pdf', DgaFileRejectionReason.Size],
      ['c.png', DgaFileRejectionReason.Type],
      ['d.pdf', DgaFileRejectionReason.Count],
    ]);
  });

  it('leaves the size to the server when the optional size check is off, but still checks the type', async () => {
    const { pick, host } = await render((current) => current.maxBytes.set(null));
    const big = pdf('big.pdf', MAX_BYTES * MAX_BYTES);
    pick([big, new File(['x'], 'c.png')]);
    expect(host.selections[0].accepted).toEqual([big]);
    expect(host.selections[0].rejected.map((rejection) => rejection.reason)).toEqual([DgaFileRejectionReason.Type]);
  });

  it('counts the listed files against maxFiles and allows one file without multi-select', async () => {
    const { pick, input, host } = await render((current) => {
      current.maxFiles.set(1);
      current.items.set([{ id: 'f1', name: 'held.pdf', status: DgaFileUploadStatus.Uploaded }]);
    });
    expect(input().multiple).toBe(false);
    pick([pdf('a.pdf')]);
    expect(host.selections[0].rejected[0].reason).toBe(DgaFileRejectionReason.Count);
  });

  it('emits an empty selection when the dialog is left empty', async () => {
    const { pick, host } = await render();
    pick([]);
    expect(host.selections).toEqual([{ accepted: [], rejected: [] }]);
  });

  it('takes dropped files and shows the drag state, but ignores drops while disabled', async () => {
    const { fixture, zone, drop, host } = await render();
    const over = new Event('dragover', { bubbles: true, cancelable: true });
    zone().dispatchEvent(over);
    await fixture.whenStable();
    expect(over.defaultPrevented).toBe(true);
    expect(zone().classList).toContain('dga-file-upload__zone--dragging');
    const event = drop([pdf('a.pdf')]);
    await fixture.whenStable();
    expect(event.defaultPrevented).toBe(true);
    expect(zone().classList).not.toContain('dga-file-upload__zone--dragging');
    expect(host.selections[0].accepted.map((file) => file.name)).toEqual(['a.pdf']);
    host.disabled.set(true);
    await fixture.whenStable();
    drop([pdf('b.pdf')]);
    expect(host.selections).toHaveLength(1);
  });

  it('shows an error as an alert that also describes the group', async () => {
    const { root } = await render((host) => host.error.set('Only PDF files.'));
    const error = root.querySelector('.dga-file-upload__error') as HTMLElement;
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toContain('Only PDF files.');
    expect(root.querySelector('[role="group"]')?.getAttribute('aria-describedby')?.split(' ')).toContain(error.id);
  });

  it.each(['rtl', 'ltr'])('renders each state with its data-status, isolated names and progress (%s)', async (dir) => {
    const { rows } = await render((current) => {
      current.dir.set(dir);
      current.items.set([
        queuedFileUpload('q', 'queued.pdf'),
        { id: 'u', name: 'scan.pdf', status: DgaFileUploadStatus.Uploading, progress: 40.4, detail: '1 MB' },
        { id: 'i', name: 'wait.pdf', status: DgaFileUploadStatus.Uploading, progress: null },
        { id: 'd', name: 'done.pdf', status: DgaFileUploadStatus.Uploaded },
        { id: 'f', name: 'bad.pdf', status: DgaFileUploadStatus.Failed, errorText: 'Upload failed.' },
      ]);
    });
    const [queued, uploading, indeterminate, uploaded, failed] = rows();
    expect(rows().map((row) => row.dataset['status'])).toEqual(['queued', 'uploading', 'uploading', 'uploaded', 'failed']);
    expect(uploading.querySelector('bdi')?.hasAttribute('dir')).toBe(false);
    expect(uploading.getAttribute('aria-busy')).toBe('true');
    const progress = uploading.querySelector('progress') as HTMLProgressElement;
    expect(progress.getAttribute('value')).toBe('40');
    expect(progress.getAttribute('max')).toBe('100');
    expect(progress.getAttribute('aria-labelledby')).toBe(uploading.querySelector('.dga-file-upload__file-name')?.id);
    expect(indeterminate.querySelector('progress')?.hasAttribute('value')).toBe(false);
    for (const row of [queued, uploaded, failed]) expect(row.querySelector('progress')).toBeNull();
    expect(failed.querySelector('.dga-file-upload__file-error')?.textContent).toBe('Upload failed.');
    expect(uploading.querySelector('.dga-file-upload__file-detail')?.textContent).toBe('1 MB');
  });

  it('follows a row through its transitions, from queued to uploading to failed', async () => {
    const queued = queuedFileUpload('f1', 'scan.pdf');
    const { fixture, rows, host } = await render((current) => current.items.set([queued]));
    expect(rows()[0].dataset['status']).toBe('queued');
    host.items.set([uploadingFileUpload(queued, 75)]);
    await fixture.whenStable();
    expect(rows()[0].querySelector('progress')?.getAttribute('value')).toBe('75');
    host.items.set([failedFileUpload(uploadingFileUpload(queued, 75), { errorText: 'Too large.', referenceCode: REFERENCE })]);
    await fixture.whenStable();
    expect(rows()[0].dataset['status']).toBe('failed');
    expect(rows()[0].querySelector('progress')).toBeNull();
    expect(rows()[0].querySelector('.dga-file-upload__reference-code')?.textContent).toBe(REFERENCE);
  });

  it('shows a failed row reference code left-to-right after its label, and only on failed rows', async () => {
    const { fixture, rows, host } = await render((current) =>
      current.items.set([
        { id: 'f', name: 'big.pdf', status: DgaFileUploadStatus.Failed, errorText: 'The file is too large.', referenceCode: REFERENCE },
        { id: 'u', name: 'ok.pdf', status: DgaFileUploadStatus.Uploaded, errorText: 'stale', referenceCode: 'STALE' },
      ]),
    );
    const reference = rows()[0].querySelector('.dga-file-upload__reference') as HTMLElement;
    expect(reference.querySelector('.dga-file-upload__reference-label')?.textContent).toBe('Reference code');
    const code = reference.querySelector('code') as HTMLElement;
    expect(code.textContent).toBe(REFERENCE);
    expect(code.getAttribute('dir')).toBe('ltr');
    expect(rows()[0].querySelector('.dga-file-upload__file-error')?.textContent).toBe('The file is too large.');
    expect(rows()[1].querySelector('.dga-file-upload__reference')).toBeNull();
    expect(rows()[1].querySelector('.dga-file-upload__file-error')).toBeNull();
    host.referenceCodeLabel.set(null);
    await fixture.whenStable();
    expect(rows()[0].querySelector('.dga-file-upload__reference-label')).toBeNull();
    expect(rows()[0].querySelector('code')?.textContent).toBe(REFERENCE);
  });

  it('announces each row status through a polite live region', async () => {
    const queued = queuedFileUpload('f1', 'scan.pdf');
    const { fixture, root, rows, host } = await render((current) => {
      current.statusLabels.set({ [DgaFileUploadStatus.Uploaded]: '{name} uploaded', [DgaFileUploadStatus.Failed]: '{name} failed' });
      current.items.set([queued]);
    });
    const live = root.querySelector('.dga-file-upload__files') as HTMLElement;
    expect(live.getAttribute('aria-live')).toBe('polite');
    expect(rows()[0].querySelector('.dga-file-upload__status')).toBeNull();
    host.items.set([uploadedFileUpload(uploadingFileUpload(queued, null))]);
    await fixture.whenStable();
    expect(live.contains(rows()[0])).toBe(true);
    expect(rows()[0].querySelector('.dga-file-upload__status')?.textContent).toBe('scan.pdf uploaded');
  });

  it('emits remove with the row from its named button, never while disabled', async () => {
    const item: DgaFileUploadItem = { id: 'f2', name: 'bad.pdf', status: DgaFileUploadStatus.Failed };
    const { fixture, rows, host } = await render((current) => current.items.set([item]));
    const remove = () => rows()[0].querySelector('.dga-file-upload__remove button') as HTMLButtonElement;
    expect(remove().getAttribute('aria-label')).toBe('Remove bad.pdf');
    remove().click();
    expect(host.removed).toEqual([item]);
    host.disabled.set(true);
    await fixture.whenStable();
    host.upload().onRemove(item);
    expect(host.removed).toHaveLength(1);
  });

  it('has no remove buttons without a remove label', async () => {
    const { root } = await render((host) => {
      host.removeLabel.set(null);
      host.items.set([queuedFileUpload('f1', 'a.pdf')]);
    });
    expect(root.querySelector('.dga-file-upload__remove')).toBeNull();
  });

  it('offers a named download on uploaded rows only when downloadable, and emits download with the row', async () => {
    const uploaded: DgaFileUploadItem = { id: 'd', name: 'done.pdf', status: DgaFileUploadStatus.Uploaded };
    const failed: DgaFileUploadItem = { id: 'f', name: 'bad.pdf', status: DgaFileUploadStatus.Failed };
    const { fixture, rows, host } = await render((current) => current.items.set([uploaded, failed]));
    expect(rows()[0].querySelector('.dga-file-upload__download')).toBeNull();
    host.downloadable.set(true);
    host.disabled.set(true);
    await fixture.whenStable();
    const download = rows()[0].querySelector('.dga-file-upload__download button') as HTMLButtonElement;
    expect(download.getAttribute('aria-label')).toBe('Download done.pdf');
    download.click();
    expect(host.downloaded).toEqual([uploaded]);
    expect(rows()[1].querySelector('.dga-file-upload__download')).toBeNull();
    host.upload().onDownload(failed);
    expect(host.downloaded).toHaveLength(1);
  });

  it('marks a required control and says it is missing until a file is uploaded', async () => {
    const { fixture, root, host } = await render((current) => {
      current.required.set(true);
      current.missingText.set('Add the document.');
      current.items.set([queuedFileUpload('q', 'a.pdf')]);
    });
    const group = root.querySelector('[role="group"]') as HTMLElement;
    expect(root.querySelector('.dga-file-upload__required')?.getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelector('.dga-file-upload__label .dga-file-upload__sr-only')?.textContent).toBe('required');
    const missing = root.querySelector('.dga-file-upload__missing') as HTMLElement;
    expect(missing.textContent).toContain('Add the document.');
    expect(group.getAttribute('aria-describedby')?.split(' ')).toContain(missing.id);
    expect(root.querySelector('.dga-file-upload__zone')?.classList).toContain('dga-file-upload__zone--invalid');
    host.items.set([{ id: 'q', name: 'a.pdf', status: DgaFileUploadStatus.Uploaded }]);
    await fixture.whenStable();
    expect(root.querySelector('.dga-file-upload__missing')).toBeNull();
    expect(group.getAttribute('aria-describedby')?.split(' ')).not.toContain(missing.id);
  });

  it('shows no required marker or missing text when not required', async () => {
    const { root } = await render((host) => host.missingText.set('Add the document.'));
    expect(root.querySelector('.dga-file-upload__required')).toBeNull();
    expect(root.querySelector('.dga-file-upload__missing')).toBeNull();
  });

  it('shows only the list when read-only: no drop zone, no remove, downloads kept', async () => {
    const uploaded: DgaFileUploadItem = { id: 'd', name: 'done.pdf', status: DgaFileUploadStatus.Uploaded };
    const { root, rows, host } = await render((current) => {
      current.readonly.set(true);
      current.downloadable.set(true);
      current.items.set([uploaded]);
    });
    expect(root.querySelector('.dga-file-upload__zone')).toBeNull();
    expect(root.querySelector('input[type="file"]')).toBeNull();
    expect(rows()[0].querySelector('.dga-file-upload__remove')).toBeNull();
    expect(rows()[0].querySelector('.dga-file-upload__download')).not.toBeNull();
    host.upload().browse();
    host.upload().onRemove(uploaded);
    expect(host.removed).toEqual([]);
  });

  it('uses an external label id when it has no visible label, and clears the native input on request', async () => {
    const fixture = TestBed.createComponent(DgaFileUploadComponent);
    fixture.componentRef.setInput('browseLabel', 'Browse');
    fixture.componentRef.setInput('ariaLabelledBy', 'outside-label');
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[role="group"]')?.getAttribute('aria-labelledby')).toBe('outside-label');
    expect(root.querySelector('.dga-file-upload__label')).toBeNull();
    fixture.componentInstance.clearInput();
    expect((root.querySelector('input[type="file"]') as HTMLInputElement).value).toBe('');
  });
});
