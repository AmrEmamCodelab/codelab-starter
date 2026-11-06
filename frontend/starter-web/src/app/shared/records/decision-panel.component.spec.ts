import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DgaConfirmDialogComponent } from '@codelab/ng-dga';
import type { ApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { DecisionTone, type DecisionToneResolver, type RecordDecision } from './decision';
import { DecisionPanelComponent } from './decision-panel.component';
import { WorkflowErrorCodes } from './records-error';
import { RecordsTranslationKeys } from './records-translation-keys';
import { type RecordAllowedAction } from './records.models';
import { aRecordDetail, aRequiredReasonAction, anAction, anOptionalReasonAction, recordsUiTestProviders, translated } from './records.testing';

@Component({
  selector: 'app-decision-panel-host',
  imports: [DecisionPanelComponent],
  template: `<app-decision-panel [allowedActions]="actions()" [busy]="busy()" [terminal]="terminal()" [error]="error()"
    [toneFor]="toneFor" [maxReasonLength]="maxReasonLength()" (decide)="onDecide($event)" />`,
})
class HostComponent {
  readonly actions = signal<readonly RecordAllowedAction[]>([]);
  readonly busy = signal(false);
  readonly terminal = signal(false);
  readonly error = signal<ApiError | null>(null);
  readonly maxReasonLength = signal(1000);
  readonly decisions: RecordDecision[] = [];
  /** What the screen does on decide: by default it starts a call that stays busy. */
  respond: (decision: RecordDecision) => void = () => this.busy.set(true);
  readonly toneFor: DecisionToneResolver = (actionKey) =>
    actionKey === 'reject' ? DecisionTone.Destructive : actionKey === 'approve' ? DecisionTone.Primary : DecisionTone.Neutral;

  onDecide(decision: RecordDecision): void {
    this.decisions.push(decision);
    this.respond(decision);
  }
}

interface Rendered {
  readonly fixture: ComponentFixture<HostComponent>;
  readonly host: HostComponent;
  readonly root: HTMLElement;
  readonly panel: DecisionPanelComponent;
}

async function render(actions: readonly RecordAllowedAction[], configure: (host: HostComponent) => void = () => undefined): Promise<Rendered> {
  TestBed.configureTestingModule({ imports: [HostComponent], providers: recordsUiTestProviders() });
  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.actions.set(actions);
  configure(fixture.componentInstance);
  fixture.detectChanges();
  await fixture.whenStable();
  const panel = fixture.debugElement.query(By.directive(DecisionPanelComponent)).componentInstance as DecisionPanelComponent;
  return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement, panel };
}

async function settle(fixture: ComponentFixture<HostComponent>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

function actionButtons(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>('.decision-panel__actions dga-button'));
}

async function press(rendered: Rendered, actionKey: string): Promise<void> {
  rendered.root.querySelector<HTMLButtonElement>(`dga-button[data-action="${actionKey}"] button`)?.click();
  await settle(rendered.fixture);
}

function dialog(rendered: Rendered): DgaConfirmDialogComponent {
  return rendered.fixture.debugElement.query(By.directive(DgaConfirmDialogComponent)).componentInstance as DgaConfirmDialogComponent;
}

function dialogIsOpen(rendered: Rendered): boolean {
  return rendered.root.querySelector('dialog')?.hasAttribute('open') ?? false;
}

async function typeReason(rendered: Rendered, text: string): Promise<void> {
  const textarea = rendered.root.querySelector<HTMLTextAreaElement>('dga-textarea textarea');
  if (!textarea) throw new Error('no reason field');
  textarea.value = text;
  textarea.dispatchEvent(new Event('input'));
  await settle(rendered.fixture);
}

async function confirm(rendered: Rendered): Promise<void> {
  dialog(rendered).confirm();
  await settle(rendered.fixture);
}

describe('DecisionPanelComponent', () => {
  it('offers nothing for a terminal record (the server sends no allowed action)', async () => {
    const detail = aRecordDetail({ status: 6, isTerminal: true, allowedActions: [] });
    const { root } = await render(detail.allowedActions, (host) => host.terminal.set(detail.isTerminal));
    expect(actionButtons(root)).toHaveLength(0);
    expect(root.textContent).toContain(RecordsTranslationKeys.Decision.None);
  });

  it('hides a stale list once the record is terminal', async () => {
    const { root } = await render([anAction()], (host) => host.terminal.set(true));
    expect(actionButtons(root)).toHaveLength(0);
  });

  it('renders exactly the server-computed actions, refusals before the expected step', async () => {
    const { root } = await render([anAction(), aRequiredReasonAction(), anAction({ actionKey: 'return', labelKey: 'testRecords.action.return' })]);
    const buttons = actionButtons(root);
    expect(buttons.map((button) => button.dataset['action'])).toEqual(['return', 'reject', 'approve']);
    expect(buttons.map((button) => button.textContent?.trim())).toEqual([
      'testRecords.action.return',
      'testRecords.action.reject',
      'testRecords.action.approve',
    ]);
    expect(buttons.every((button) => button.hasAttribute('data-public-action'))).toBe(true);
  });

  it('emits an action without a reason at once', async () => {
    const rendered = await render([anAction()]);
    await press(rendered, 'approve');
    expect(rendered.host.decisions).toEqual([{ actionKey: 'approve', reason: null }]);
    expect(dialogIsOpen(rendered)).toBe(false);
  });

  it('opens the reason dialog for a mandatory reason and blocks an empty one', async () => {
    const rendered = await render([aRequiredReasonAction()]);
    await press(rendered, 'reject');
    expect(dialogIsOpen(rendered)).toBe(true);
    expect(rendered.root.querySelector('dialog')?.textContent).toContain(RecordsTranslationKeys.Decision.ReasonRequiredHint);

    await typeReason(rendered, '    ');
    await confirm(rendered);

    expect(rendered.host.decisions).toEqual([]);
    expect(dialogIsOpen(rendered)).toBe(true);
    expect(rendered.root.querySelector('dga-textarea')?.textContent).toContain(RecordsTranslationKeys.Decision.ReasonRequired);
  });

  it('sends the trimmed mandatory reason and closes once the call succeeds', async () => {
    const rendered = await render([aRequiredReasonAction()]);
    await press(rendered, 'reject');
    await typeReason(rendered, '  Missing the signed form  ');
    await confirm(rendered);

    expect(rendered.host.decisions).toEqual([{ actionKey: 'reject', reason: 'Missing the signed form' }]);
    expect(dialogIsOpen(rendered)).toBe(true);

    rendered.host.busy.set(false);
    await settle(rendered.fixture);
    expect(dialogIsOpen(rendered)).toBe(false);
    expect(rendered.panel.pending()).toBeNull();
  });

  it('keeps the dialog open with the failure and the typed reason when the call fails', async () => {
    const rendered = await render([aRequiredReasonAction()]);
    await press(rendered, 'reject');
    await typeReason(rendered, 'Too late');
    await confirm(rendered);

    rendered.host.error.set({ status: 409, code: WorkflowErrorCodes.ActionNotAllowed, serverCode: WorkflowErrorCodes.ActionNotAllowed, correlationId: 'c', referenceCode: 'R7K2-9QX4', fieldErrors: [] });
    rendered.host.busy.set(false);
    await settle(rendered.fixture);

    expect(dialogIsOpen(rendered)).toBe(true);
    const text = rendered.root.querySelector('dga-alert')?.textContent ?? '';
    expect(text).toContain(`errors.${WorkflowErrorCodes.ActionNotAllowed}`);
    expect(text).toContain(translated(RecordsTranslationKeys.Decision.ReferenceCode, { code: 'R7K2-9QX4' }));
    expect(rendered.panel.reason()).toBe('Too late');
  });

  it('lets an optional reason stay blank and sends null', async () => {
    const rendered = await render([anOptionalReasonAction()]);
    await press(rendered, 'cancel');
    expect(rendered.root.querySelector('dialog')?.textContent).toContain(RecordsTranslationKeys.Decision.ReasonOptionalHint);
    await confirm(rendered);
    expect(rendered.host.decisions).toEqual([{ actionKey: 'cancel', reason: null }]);
  });

  it('blocks a reason longer than the limit', async () => {
    const rendered = await render([aRequiredReasonAction()], (host) => host.maxReasonLength.set(5));
    await press(rendered, 'reject');
    rendered.panel.onReason('abcdefgh');
    await confirm(rendered);
    expect(rendered.host.decisions).toEqual([]);
    expect(rendered.panel.problemText()).toBe(translated(RecordsTranslationKeys.Decision.ReasonTooLong, { max: 5 }));
  });

  it('closes on cancel without emitting, and starts empty the next time', async () => {
    const rendered = await render([aRequiredReasonAction()]);
    await press(rendered, 'reject');
    await typeReason(rendered, 'draft text');
    dialog(rendered).cancel();
    await settle(rendered.fixture);
    expect(dialogIsOpen(rendered)).toBe(false);
    expect(rendered.host.decisions).toEqual([]);

    await press(rendered, 'reject');
    expect(rendered.panel.reason()).toBe('');
  });

  it('disables every button while busy', async () => {
    const rendered = await render([anAction(), aRequiredReasonAction()], (host) => host.busy.set(true));
    expect(rendered.root.querySelectorAll('.decision-panel__actions button:disabled')).toHaveLength(2);
    rendered.panel.choose(rendered.panel.buttons()[0]!);
    expect(rendered.host.decisions).toEqual([]);
  });

  it('asks a destructive decision in a danger dialog', async () => {
    const rendered = await render([aRequiredReasonAction()]);
    await press(rendered, 'reject');
    expect(dialog(rendered).variant()).toBe('danger');
  });
});
