import { ChangeDetectionStrategy, Component, computed, input, numberAttribute } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { clampActiveStep, type DgaStep, type DgaStepState, dgaStepState } from './step-state';

/**
 * The DGA stepper: a horizontal progress trail of completed, current and upcoming steps. It shows progress only; the
 * app owns navigation between steps. Rendered as an ordered list, the current step carries `aria-current="step"`, and
 * the optional `completedLabel` / `currentLabel` (already translated) are read after each step's label so the state is
 * not conveyed by colour alone. Set `active` to `steps.length` to show every step completed.
 *
 * @example
 * <dga-stepper [steps]="steps()" [active]="stepIndex()" [label]="'wizard.progress' | translate"
 *              [completedLabel]="'wizard.completed' | translate" [currentLabel]="'wizard.current' | translate" />
 */
@Component({
  selector: 'dga-stepper',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-stepper.component.html',
  styleUrl: './dga-stepper.component.css',
})
export class DgaStepperComponent {
  readonly steps = input.required<readonly DgaStep[]>();
  /** Zero-based index of the current step; clamped to the steps. */
  readonly active = input(0, { transform: numberAttribute });
  /** Accessible name of the step list (already translated). */
  readonly label = input<string | null>(null);
  /** Screen-reader text read after a completed step's label (already translated). */
  readonly completedLabel = input<string | null>(null);
  /** Screen-reader text read after the current step's label (already translated). */
  readonly currentLabel = input<string | null>(null);

  readonly activeIndex = computed(() => clampActiveStep(this.active(), this.steps().length));

  stateAt(index: number): DgaStepState {
    return dgaStepState(index, this.activeIndex());
  }

  stateText(state: DgaStepState): string | null {
    if (state === 'completed') return this.completedLabel();
    if (state === 'current') return this.currentLabel();
    return null;
  }
}
