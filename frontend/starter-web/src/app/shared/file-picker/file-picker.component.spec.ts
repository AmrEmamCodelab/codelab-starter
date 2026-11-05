import { Component, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { DgaFileRejectionReason } from '@codelab/ng-dga';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslationService } from '../../core/i18n/translation.service';
import { FilePickerTestIds, FilePickerTranslationKeys } from './file-picker-keys';
import { FilePickerComponent, pickedFile } from './file-picker.component';

const LABEL_KEY = 'admin.import.file.label';

@Component({
  imports: [FilePickerComponent],
  template: `<app-file-picker [labelKey]="labelKey" accept=".xlsx" [disabled]="disabled()" (fileChange)="picked($event)" />`,
})
class PickerHost {
  readonly labelKey = LABEL_KEY;
  readonly disabled = signal(false);
  readonly picker = viewChild.required(FilePickerComponent);
  readonly files: (File | null)[] = [];

  picked(file: File | null): void {
    this.files.push(file);
  }
}

describe('FilePickerComponent', () => {
  let fixture: ComponentFixture<PickerHost>;
  let host: HTMLElement;

  const byTestId = (id: string) => host.querySelector(`[data-testid="${id}"]`) as HTMLElement;
  const nativeInput = () => byTestId(FilePickerTestIds.Input) as HTMLInputElement;
  const chooseButton = () => byTestId(FilePickerTestIds.Choose).querySelector('button') as HTMLButtonElement;
  const nameText = () => byTestId(FilePickerTestIds.Name).textContent?.trim();

  function pick(file: File | null): void {
    Object.defineProperty(nativeInput(), 'files', { value: { item: (index: number) => (index === 0 ? file : null) }, configurable: true });
    nativeInput().dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PickerHost],
      providers: [{ provide: TranslationService, useValue: { translate: (key: string) => key } }],
    });
    fixture = TestBed.createComponent(PickerHost);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  it('shows a translated label, choose button and empty state, with the native control hidden', () => {
    expect(nativeInput().type).toBe('file');
    expect(nativeInput().getAttribute('aria-hidden')).toBe('true');
    expect(nativeInput().tabIndex).toBe(-1);
    expect(nativeInput().accept).toBe('.xlsx');
    expect(nativeInput().multiple).toBe(false);
    expect(host.querySelector('.file-picker__label')?.textContent?.trim()).toBe(LABEL_KEY);
    expect(chooseButton().textContent).toContain(FilePickerTranslationKeys.Choose);
    expect(nameText()).toBe(FilePickerTranslationKeys.None);
  });

  it('labels the upload group with its label', () => {
    const group = host.querySelector('[role="group"]') as HTMLElement;
    const label = host.querySelector('.file-picker__label') as HTMLElement;
    expect(label.id).not.toBe('');
    expect(group.getAttribute('aria-labelledby')).toBe(label.id);
  });

  it('opens the native dialog from the button', () => {
    const click = vi.spyOn(nativeInput(), 'click');
    chooseButton().click();
    expect(click).toHaveBeenCalledOnce();
  });

  it('does not open the dialog while disabled', () => {
    fixture.componentInstance.disabled.set(true);
    fixture.detectChanges();
    const click = vi.spyOn(nativeInput(), 'click');
    fixture.componentInstance.picker().choose();
    expect(click).not.toHaveBeenCalled();
    expect(nativeInput().disabled).toBe(true);
    expect(chooseButton().disabled).toBe(true);
  });

  it('emits the picked file and shows its name', () => {
    const file = new File(['x'], 'users.xlsx');
    pick(file);
    expect(fixture.componentInstance.files).toEqual([file]);
    expect(nameText()).toBe('users.xlsx');
  });

  it('still emits a file outside accept, so the screen can name the problem', () => {
    const file = new File(['x'], 'users.csv', { type: 'text/csv' });
    pick(file);
    expect(fixture.componentInstance.files).toEqual([file]);
  });

  it('emits null and shows the empty state when the dialog is left empty', () => {
    pick(new File(['x'], 'users.xlsx'));
    pick(null);
    expect(fixture.componentInstance.files.at(-1)).toBeNull();
    expect(nameText()).toBe(FilePickerTranslationKeys.None);
  });

  it('clears the picked file without emitting', () => {
    pick(new File(['x'], 'users.xlsx'));
    fixture.componentInstance.picker().clear();
    fixture.detectChanges();
    expect(nativeInput().value).toBe('');
    expect(nameText()).toBe(FilePickerTranslationKeys.None);
    expect(fixture.componentInstance.files).toHaveLength(1);
  });
});

describe('pickedFile', () => {
  const file = new File(['x'], 'a.xlsx');

  it('prefers the accepted file, then the refused one, then null', () => {
    expect(pickedFile({ accepted: [file], rejected: [] })).toBe(file);
    expect(pickedFile({ accepted: [], rejected: [{ file, reason: DgaFileRejectionReason.Type }] })).toBe(file);
    expect(pickedFile({ accepted: [], rejected: [] })).toBeNull();
  });
});
