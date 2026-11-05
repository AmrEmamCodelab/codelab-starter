import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  numberAttribute,
  output,
  viewChildren,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { isRightToLeft } from '../internal/direction';
import {
  clampActiveStep,
  type DgaStep,
  DgaStepperOrientation,
  type DgaStepPosition,
  DgaStepState,
  dgaStepState,
  isDgaStepSelectable,
} from './step-state';
import { nextFocusableStep, rovingStepIndex } from './stepper-navigation';

/** One rendered step: the caller's step plus what the component worked out for it. */
interface DgaStepView {
  readonly step: DgaStep;
  readonly state: DgaStepState;
  readonly selectable: boolean;
  /** A navigable stepper's button can take focus: the current step or a selectable one. */
  readonly focusable: boolean;
}

/**
 * The DGA stepper (the former dga-stepper and app-stepper merged): a trail of completed, current, upcoming and invalid
 * steps, horizontal (a progress bar above a form) or vertical (a sidebar or a procedure list with descriptions).
 *
 * - Progress only by default. Set `navigable` to render each step as a button: a selectable step emits `stepSelect`
 *   with its index, and the app decides whether to move (it owns navigation and any validity guard). `linear` (the
 *   default) lets the user revisit earlier or completed steps only; non-linear allows any enabled step.
 * - `compact` (and, for a horizontal trail, a narrow container by itself) shows only the current step's label next to
 *   the bullets; the other labels stay readable by screen readers.
 * - Accessibility: an ordered list; the current step carries `aria-current="step"`; the optional `completedLabel`,
 *   `currentLabel` and `invalidLabel` (already translated) are read after each step's label so the state is not
 *   conveyed by colour alone. A navigable stepper uses a roving tabindex: Tab reaches the current step, the arrow keys
 *   (mirrored in RTL for a horizontal trail, Up/Down for a vertical one) and Home/End move between steps, Enter/Space
 *   select. The component carries no text of its own.
 *
 * @example
 * <dga-stepper [steps]="steps()" [active]="stepIndex()" [label]="'wizard.progress' | t"
 *              [completedLabel]="'wizard.completed' | t" [currentLabel]="'wizard.current' | t" />
 * <dga-stepper orientation="vertical" navigable [linear]="false" [steps]="steps()" [active]="index()"
 *              (stepSelect)="goTo($event)" />
 */
@Component({
  selector: 'dga-stepper',
  imports: [DgaIconComponent, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-stepper.component.html',
  styleUrl: './dga-stepper.component.css',
  host: {
    '[attr.data-orientation]': 'orientation()',
    '[class.dga-stepper--compact]': 'compact()',
  },
})
export class DgaStepperComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly stepButtons = viewChildren<ElementRef<HTMLButtonElement>>('stepButton');

  readonly steps = input.required<readonly DgaStep[]>();
  /** Zero-based index of the current step; clamped to the steps. `steps.length` shows every step completed. */
  readonly active = input(0, { transform: numberAttribute });
  readonly orientation = input<DgaStepperOrientation>(DgaStepperOrientation.Horizontal);
  /** Linear: earlier steps count as completed and only they (or completed steps) are selectable. */
  readonly linear = input(true, { transform: booleanAttribute });
  /** Steps are buttons that emit `stepSelect`. */
  readonly navigable = input(false, { transform: booleanAttribute });
  /** Shows only the current step's label (mobile). A horizontal trail also compacts by itself in a narrow container. */
  readonly compact = input(false, { transform: booleanAttribute });
  /** Accessible name of the step list (already translated). */
  readonly label = input<string | null>(null);
  /** Screen-reader text read after a completed step's label (already translated). */
  readonly completedLabel = input<string | null>(null);
  /** Screen-reader text read after the current step's label (already translated). */
  readonly currentLabel = input<string | null>(null);
  /** Screen-reader text read after an invalid step's label (already translated). */
  readonly invalidLabel = input<string | null>(null);

  /** The index of a step the user chose (navigable only; never the current or an unselectable step). */
  readonly stepSelect = output<number>();

  readonly activeIndex = computed(() => clampActiveStep(this.active(), this.steps().length));

  readonly views = computed<readonly DgaStepView[]>(() => {
    const active = this.activeIndex();
    const linear = this.linear();
    return this.steps().map((step, index) => {
      const position: DgaStepPosition = { index, active, step, linear };
      const selectable = isDgaStepSelectable(position);
      const state = dgaStepState(position);
      return { step, state, selectable, focusable: selectable || (state === DgaStepState.Current && !step.disabled) };
    });
  });

  readonly tabStop = computed(() => rovingStepIndex(this.views().map((view) => view.focusable), this.activeIndex()));

  protected readonly states = DgaStepState;

  stateText(state: DgaStepState): string | null {
    switch (state) {
      case DgaStepState.Completed:
        return this.completedLabel();
      case DgaStepState.Current:
        return this.currentLabel();
      case DgaStepState.Invalid:
        return this.invalidLabel();
      default:
        return null;
    }
  }

  select(index: number): void {
    if (!this.views()[index]?.selectable) return;
    this.stepSelect.emit(index);
  }

  onKeydown(event: KeyboardEvent, index: number): void {
    const focusable = this.views().map((view) => view.focusable);
    const context = { orientation: this.orientation(), rightToLeft: isRightToLeft(this.host.nativeElement) };
    const next = nextFocusableStep(focusable, index, event.key, context);
    if (next === null) return;
    event.preventDefault();
    this.stepButtons()[next]?.nativeElement.focus();
  }
}
