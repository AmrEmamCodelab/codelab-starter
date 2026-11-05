import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { DgaFileUploadComponent } from './dga-file-upload.component';
import { type DgaFileUploadItem, DgaFileUploadStatus } from './file-upload-item';
import { DgaFileRejectionReason, type DgaFileSelection } from './file-validation';

const MAX_BYTES = 10;

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
        [maxSizeBytes]="maxBytes"
        [maxFiles]="maxFiles()"
        [disabled]="disabled()"
        [error]="error()"
        [items]="items()"
        [removeLabel]="removeLabel()"
        (filesSelected)="selections.push($event)"
        (removed)="removedIds.push($event)"
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
  readonly maxFiles = signal(2);
  readonly disabled = signal(false);
  readonly error = signal<string | null>(null);
  readonly items = signal<readonly DgaFileUploadItem[]>([]);
  readonly removeLabel = signal<string | null>('Remove {name}');
  readonly maxBytes = MAX_BYTES;
  readonly selections: DgaFileSelection[] = [];
  readonly removedIds: string[] = [];
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
  return { fixture, root, input, pick, drop, zone, host: fixture.componentInstance };
}

describe('DgaFileUploadComponent', () => {
  it.each(['rtl', 'ltr'])('is a group named by its label and described by its hint, with a hidden native input (%s)', async (dir) => {
    const { root, input } = await render((host) => host.dir.set(dir));
    const group = root.querySelector('[role="group"]') as HTMLElement;
    const label = root.querySelector('.dga-file-upload__label') as HTMLElement;
    expect(label.textContent).toBe('Attachments');
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

  it.each(['rtl', 'ltr'])('lists the files with status, isolated names, progress and named remove buttons (%s)', async (dir) => {
    const { root, host } = await render((current) => {
      current.dir.set(dir);
      current.items.set([
        { id: 'f1', name: 'scan.pdf', status: DgaFileUploadStatus.Uploading, progress: 40.4, detail: '1 MB' },
        { id: 'f2', name: 'bad.pdf', status: DgaFileUploadStatus.Failed, errorText: 'Upload failed.' },
        { id: 'f3', name: 'wait.pdf', status: DgaFileUploadStatus.Uploading, progress: null },
      ]);
    });
    const rows = [...root.querySelectorAll<HTMLElement>('.dga-file-upload__file')];
    expect(rows.map((row) => row.dataset['status'])).toEqual(['uploading', 'failed', 'uploading']);
    expect(rows[0].querySelector('bdi')?.hasAttribute('dir')).toBe(false);
    const progress = rows[0].querySelector('progress') as HTMLProgressElement;
    expect(progress.getAttribute('value')).toBe('40');
    expect(progress.getAttribute('max')).toBe('100');
    expect(progress.getAttribute('aria-labelledby')).toBe(rows[0].querySelector('.dga-file-upload__file-name')?.id);
    expect(rows[2].querySelector('progress')?.hasAttribute('value')).toBe(false);
    expect(rows[1].querySelector('.dga-file-upload__file-error')?.textContent).toBe('Upload failed.');
    const remove = rows[1].querySelector('.dga-file-upload__remove button') as HTMLButtonElement;
    expect(remove.getAttribute('aria-label')).toBe('Remove bad.pdf');
    remove.click();
    expect(host.removedIds).toEqual(['f2']);
  });

  it('has no remove buttons without a remove label', async () => {
    const { root } = await render((host) => {
      host.removeLabel.set(null);
      host.items.set([{ id: 'f1', name: 'a.pdf', status: DgaFileUploadStatus.Pending }]);
    });
    expect(root.querySelector('.dga-file-upload__remove')).toBeNull();
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
