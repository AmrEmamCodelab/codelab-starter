import { Component, computed, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import type { ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it } from 'vitest';
import { FieldErrorPipe } from '../editor-dialog/field-error.pipe';
import { patternTestProviders, textOf } from '../patterns.testing';
import type { WizardStepDef } from './wizard-navigation';
import { WizardPatternComponent, type WizardStepChange } from './wizard-pattern.component';
import { WizardReviewDirective, WizardStepDirective } from './wizard-templates.directive';
import { WizardTranslationKeys } from './wizard-translation-keys';

const BAD_REQUEST = 400;

/** Living documentation: how a screen uses the wizard. */
@Component({
  imports: [WizardPatternComponent, WizardStepDirective, WizardReviewDirective, FieldErrorPipe],
  template: `
    <div [attr.dir]="dir()">
      <app-wizard-pattern
        [steps]="steps"
        [(step)]="step"
        [linear]="linear()"
        [busy]="busy()"
        [error]="error()"
        [fieldSteps]="fieldSteps"
        [draftEnabled]="draftEnabled()"
        [cancellable]="cancellable()"
        (next)="nexts.push($event)"
        (back)="backs.push($event)"
        (saveDraft)="drafts.push($event)"
        (submit)="submits = submits + 1"
        (cancel)="cancels = cancels + 1"
      >
        <ng-template wizardStep="applicant" let-errors="errors">
          <p class="applicant-body">applicant</p>
          <p class="applicant-email-error">{{ errors | fieldError: 'applicant.email' }}</p>
        </ng-template>
        <ng-template wizardStep="address" let-errors="errors">
          <p class="address-body">address</p>
          <p class="address-city-error">{{ errors | fieldError: 'address.city' }}</p>
        </ng-template>
        <ng-template wizardStep="documents"><p class="documents-body">documents</p></ng-template>
        <ng-template wizardReview><p class="review-body">review</p></ng-template>
      </app-wizard-pattern>
    </div>
  `,
})
class WizardHost {
  readonly applicantValid = signal(true);
  readonly addressValid = signal(true);
  readonly documentsValid = signal(true);
  readonly steps: readonly WizardStepDef[] = [
    { id: 'applicant', titleKey: 'requests.new.applicant', descriptionKey: 'requests.new.applicant-hint', valid: computed(() => this.applicantValid()) },
    { id: 'address', titleKey: 'requests.new.address', valid: computed(() => this.addressValid()) },
    { id: 'documents', titleKey: 'requests.new.documents', valid: computed(() => this.documentsValid()) },
  ];
  readonly fieldSteps = { applicant: 'applicant', address: 'address' };
  readonly step = signal(0);
  readonly linear = signal(true);
  readonly busy = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly draftEnabled = signal(false);
  readonly cancellable = signal(false);
  readonly dir = signal('ltr');
  readonly nexts: WizardStepChange[] = [];
  readonly backs: WizardStepChange[] = [];
  readonly drafts: string[] = [];
  submits = 0;
  cancels = 0;
}

function validationError(fields: Record<string, string>): ApiError {
  return {
    status: BAD_REQUEST,
    code: 'VALIDATION_FAILED',
    correlationId: 'trace-1',
    referenceCode: 'REF-42',
    fieldErrors: Object.entries(fields).map(([field, message]) => ({ field, message })),
  };
}

describe('WizardPatternComponent', () => {
  let fixture: ComponentFixture<WizardHost>;
  let host: HTMLElement;
  let screen: WizardHost;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [WizardHost], providers: patternTestProviders() });
    fixture = TestBed.createComponent(WizardHost);
    document.body.append(fixture.nativeElement as HTMLElement);
    host = fixture.nativeElement as HTMLElement;
    screen = fixture.componentInstance;
    await fixture.whenStable();
  });

  const button = (name: string) => host.querySelector<HTMLButtonElement>(`.wizard__${name} button`);
  const trailItems = () => [...host.querySelectorAll<HTMLElement>('.dga-stepper__item')];
  const trailButtons = () => [...host.querySelectorAll<HTMLButtonElement>('button.dga-stepper__step')];

  async function click(element: HTMLElement | null): Promise<void> {
    element?.click();
    await fixture.whenStable();
  }

  async function key(element: HTMLElement, name: string): Promise<KeyboardEvent> {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    element.dispatchEvent(event);
    await fixture.whenStable();
    return event;
  }

  it('renders the trail with every step plus the review step, and the current step body and heading', () => {
    expect(trailItems()).toHaveLength(4);
    expect(textOf(host, '.dga-stepper__item:last-child .dga-stepper__label')).toBe(WizardTranslationKeys.Review);
    expect(textOf(host, '.wizard__step-title')).toBe('requests.new.applicant');
    expect(textOf(host, '.wizard__step-description')).toBe('requests.new.applicant-hint');
    expect(textOf(host, '.wizard__step-count')).toBe(`${WizardTranslationKeys.StepOf}|current=1,total=4`);
    expect(host.querySelector('.applicant-body')).not.toBeNull();
    expect(host.querySelector('.address-body')).toBeNull();
  });

  it('moves on with Next when the step is valid, reports the change and focuses the new heading', async () => {
    await click(button('next'));
    expect(screen.nexts).toEqual([{ fromId: 'applicant', toId: 'address' }]);
    expect(screen.step()).toBe(1);
    expect(host.querySelector('.address-body')).not.toBeNull();
    expect(document.activeElement).toBe(host.querySelector('.wizard__step-title'));
    expect(trailItems()[0].dataset['state']).toBe('completed');
  });

  it('refuses Next on an invalid step, flags it and says why', async () => {
    screen.applicantValid.set(false);
    await click(button('next'));
    expect(screen.nexts).toEqual([]);
    expect(screen.step()).toBe(0);
    expect(textOf(host, '.wizard__refusal > span')).toBe(WizardTranslationKeys.FixStep);
    expect(host.querySelector('.wizard__refusal')?.getAttribute('role')).toBe('alert');
    screen.applicantValid.set(true);
    await fixture.whenStable();
    expect(host.querySelector('.wizard__refusal')).toBeNull();
  });

  it('goes back without validating and disables Back on the first step', async () => {
    expect(button('back')?.disabled).toBe(true);
    screen.step.set(2);
    screen.addressValid.set(false);
    await fixture.whenStable();
    await click(button('back'));
    expect(screen.backs).toEqual([{ fromId: 'documents', toId: 'address' }]);
    expect(screen.step()).toBe(1);
  });

  it('shows Submit on the last step and, with an invalid step, moves there instead of submitting', async () => {
    screen.step.set(3);
    await fixture.whenStable();
    expect(button('next')).toBeNull();
    expect(host.querySelector('.review-body')).not.toBeNull();
    screen.addressValid.set(false);
    await fixture.whenStable();
    await click(button('submit'));
    expect(screen.submits).toBe(0);
    expect(screen.step()).toBe(1);
    expect(trailItems()[1].dataset['state']).toBe('current');
    expect(textOf(host, '.wizard__refusal > span')).toBe(WizardTranslationKeys.FixStep);
    screen.addressValid.set(true);
    screen.step.set(3);
    await fixture.whenStable();
    await click(button('submit'));
    expect(screen.submits).toBe(1);
  });

  it('offers Save draft only when enabled, without validating the step', async () => {
    expect(button('draft')).toBeNull();
    screen.draftEnabled.set(true);
    screen.applicantValid.set(false);
    await fixture.whenStable();
    await click(button('draft'));
    expect(screen.drafts).toEqual(['applicant']);
  });

  it('offers Cancel only when cancellable', async () => {
    expect(button('cancel')).toBeNull();
    screen.cancellable.set(true);
    await fixture.whenStable();
    await click(button('cancel'));
    expect(screen.cancels).toBe(1);
  });

  it('maps server field errors to their steps, moves to the first one and passes each step its errors', async () => {
    screen.step.set(2);
    await fixture.whenStable();
    screen.error.set(validationError({ 'address.city': 'CITY_REQUIRED', 'applicant.email': 'EMAIL_INVALID', other: 'OTHER' }));
    await fixture.whenStable();
    expect(screen.step()).toBe(0);
    expect(textOf(host, '.applicant-email-error')).toBe('errors.EMAIL_INVALID');
    expect(trailItems().map((item) => item.dataset['state'])).toEqual(['current', 'invalid', 'completed', 'upcoming']);
    const listed = [...host.querySelectorAll('.wizard__error-list li')].map((item) => item.getAttribute('data-field'));
    expect(listed).toEqual(['applicant.email', 'other']);
    expect(textOf(host, '.wizard__error-reference')).toBe(`patterns.common.reference-code|code=REF-42`);
    const goTo = host.querySelector<HTMLElement>('[data-step="address"] button');
    expect(goTo?.textContent?.trim()).toBe(`${WizardTranslationKeys.GoToStep}|step=requests.new.address`);
    await click(goTo);
    expect(screen.step()).toBe(1);
    expect(textOf(host, '.address-city-error')).toBe('errors.CITY_REQUIRED');
  });

  it('refuses every action while busy and shows Submit loading', async () => {
    screen.busy.set(true);
    await fixture.whenStable();
    expect(button('next')?.disabled).toBe(true);
    expect(host.querySelector('.wizard')?.getAttribute('aria-busy')).toBe('true');
    screen.step.set(3);
    await fixture.whenStable();
    expect(button('submit')?.disabled).toBe(true);
    expect(button('submit')?.getAttribute('aria-busy')).toBe('true');
  });

  it('linear: the trail lets the user revisit earlier steps only (keyboard and click)', async () => {
    screen.step.set(1);
    await fixture.whenStable();
    expect(trailButtons().map((trailButton) => trailButton.disabled)).toEqual([false, false, true, true]);
    trailButtons()[1].focus();
    await key(trailButtons()[1], 'ArrowLeft');
    expect(document.activeElement).toBe(trailButtons()[0]);
    await click(trailButtons()[0]);
    expect(screen.step()).toBe(0);
  });

  it('non-linear: the trail jumps forward without validating the steps in between', async () => {
    screen.linear.set(false);
    screen.applicantValid.set(false);
    await fixture.whenStable();
    await click(trailButtons()[2]);
    expect(screen.step()).toBe(2);
  });

  it('mirrors the trail keys in RTL', async () => {
    screen.dir.set('rtl');
    screen.linear.set(false);
    await fixture.whenStable();
    trailButtons()[0].focus();
    await key(trailButtons()[0], 'ArrowLeft');
    expect(document.activeElement).toBe(trailButtons()[1]);
  });
});
