import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import * as real from './dev-autofill.impl';
import { DEV_AUTOFILL_MARKER } from './dev-autofill.impl';
import { DEV_AUTOFILL_SETS, type DevAutofillSet } from './dev-autofill.model';
import { APP_DEV_AUTOFILL_SETS } from './dev-autofill.sets.stub';
import * as stub from './dev-autofill.stub';

const HERE = dirname(fileURLToPath(import.meta.url));
const FORM_KEY = 'test-record-form';
const VALID: DevAutofillSet = { formKey: FORM_KEY, labelKey: 'testRecords.devAutofill.valid', values: { title: 'A valid title' } };
/** The exports a production build gets in place of the real ones; the marker is deliberately absent. */
const REAL_ONLY_EXPORTS = ['DEV_AUTOFILL_MARKER'];

function source(file: string): string {
  return readFileSync(join(HERE, file), 'utf8');
}

@Component({
  imports: [ReactiveFormsModule, stub.DevAutofillComponent, stub.DevAutofillDirective],
  template: `
    <dga-dev-autofill [formKey]="formKey" [form]="form" />
    <form [formGroup]="form" [devAutofill]="formKey"><input formControlName="title" /></form>
  `,
})
class StubHost {
  readonly formKey = FORM_KEY;
  readonly form = new FormGroup({ title: new FormControl('') });
}

describe('DevAutofill production stub', () => {
  it('exports everything the real implementation exports, except the marker', () => {
    const realExports = Object.keys(real).filter((name) => !REAL_ONLY_EXPORTS.includes(name)).sort();
    expect(Object.keys(stub).sort()).toEqual(realExports);
  });

  it('registers no data set', () => {
    TestBed.configureTestingModule({ providers: [stub.provideDevAutofill(VALID)] });
    expect(TestBed.inject(DEV_AUTOFILL_SETS, null)).toBeNull();
  });

  it('renders nothing, even when a set for the form is registered', () => {
    TestBed.configureTestingModule({ providers: [real.provideDevAutofill(VALID)] });
    const fixture = TestBed.createComponent(StubHost);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('dga-dev-autofill')?.innerHTML).toBe('');
    expect(host.querySelector('form')?.nextElementSibling).toBeNull();
    expect(host.querySelectorAll('button')).toHaveLength(0);
    expect(fixture.componentInstance.form.value).toEqual({ title: '' });
  });

  it('holds no marker, no template file and no translation key', () => {
    const text = source('dev-autofill.stub.ts');
    expect(text).not.toContain(DEV_AUTOFILL_MARKER);
    expect(text).not.toContain('templateUrl');
    expect(text).not.toContain('shared.dev-autofill');
  });

  it('gives a production build an empty data-set registry', () => {
    expect(APP_DEV_AUTOFILL_SETS).toEqual([]);
  });

  it('keeps the marker in the real implementation only', () => {
    expect(source('dev-autofill.impl.ts')).toContain(`DEV_AUTOFILL_MARKER = '${DEV_AUTOFILL_MARKER}'`);
  });
});
