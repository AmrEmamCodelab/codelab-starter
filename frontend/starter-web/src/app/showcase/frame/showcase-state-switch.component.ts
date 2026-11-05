import { ChangeDetectionStrategy, Component, computed, inject, input, model } from '@angular/core';
import { DgaRadioGroupComponent, type DgaRadioOption } from '@codelab/ng-dga';
import { TranslationService } from '../../core/i18n/translation.service';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { ShowcaseState } from './showcase-state';

const STATE_LABELS: Readonly<Record<ShowcaseState, string>> = {
  [ShowcaseState.Ready]: ShowcaseTranslationKeys.StateSwitch.Ready,
  [ShowcaseState.Loading]: ShowcaseTranslationKeys.StateSwitch.Loading,
  [ShowcaseState.Empty]: ShowcaseTranslationKeys.StateSwitch.Empty,
  [ShowcaseState.Error]: ShowcaseTranslationKeys.StateSwitch.Error,
};

/** A horizontal radio group that switches a pattern demo between its data states. */
@Component({
  selector: 'app-showcase-state-switch',
  imports: [DgaRadioGroupComponent],
  templateUrl: './showcase-state-switch.component.html',
  styleUrl: './showcase-state-switch.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShowcaseStateSwitchComponent {
  private readonly translations = inject(TranslationService);

  readonly state = model<ShowcaseState>(ShowcaseState.Ready);
  /** The states offered; a pattern without an empty state leaves `Empty` out. */
  readonly states = input<readonly ShowcaseState[]>(Object.values(ShowcaseState));

  protected readonly options = computed<DgaRadioOption[]>(() =>
    this.states().map(state => ({ value: state, label: this.translations.translate(STATE_LABELS[state]) })),
  );
  protected readonly label = computed(() => this.translations.translate(ShowcaseTranslationKeys.StateSwitch.Label));

  protected select(value: unknown): void {
    const next = this.states().find(state => state === value);
    if (next !== undefined) {
      this.state.set(next);
    }
  }
}
