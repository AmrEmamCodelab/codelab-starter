import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  DgaButtonComponent,
  DgaCheckboxComponent,
  DgaMultiSelectComponent,
  DgaRadioGroupComponent,
  DgaSelectComponent,
  DgaTextareaComponent,
  DgaTextInputComponent,
  type DgaButtonSize,
  type DgaButtonVariant,
  type DgaRadioValue,
} from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { ShowcaseDemoComponent } from '../frame/showcase-demo.component';
import { ShowcaseSectionComponent } from '../frame/showcase-section.component';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import {
  CITY_FIXTURE,
  CITY_VALUES,
  LANGUAGE_FIXTURE,
  LANGUAGE_VALUES,
  NOTES_MAX_LENGTH,
  PRIORITY_FIXTURE,
  PRIORITY_VALUES,
  withLabels,
} from './component-fixtures';

/** Every button variant, in the order the DGA kit lists them. */
export const BUTTON_VARIANTS: readonly DgaButtonVariant[] = [
  'primary',
  'neutral',
  'secondary-solid',
  'secondary-outline',
  'subtle',
  'transparent',
];

/** Every button size. */
export const BUTTON_SIZES: readonly DgaButtonSize[] = ['sm', 'md', 'lg'];

/**
 * Gallery sections of the everyday form controls: button, text input, textarea, checkbox, radio group, select and
 * multi-select, each in its default, error and disabled states (plus loading, read-only and the like where the
 * component has them).
 */
@Component({
  selector: 'app-showcase-form-field-demos',
  imports: [
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaMultiSelectComponent,
    DgaRadioGroupComponent,
    DgaSelectComponent,
    DgaTextareaComponent,
    DgaTextInputComponent,
    ShowcaseDemoComponent,
    ShowcaseSectionComponent,
    TranslatePipe,
  ],
  templateUrl: './form-field-demos.component.html',
  styleUrl: './demos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormFieldDemosComponent {
  private readonly translations = inject(TranslationService);
  private readonly translate = (key: string) => this.translations.translate(key);

  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly variants = BUTTON_VARIANTS;
  protected readonly sizes = BUTTON_SIZES;
  protected readonly notesMax = NOTES_MAX_LENGTH;

  protected readonly priorities = computed(() => withLabels(PRIORITY_FIXTURE, this.translate));
  protected readonly cities = computed(() => withLabels(CITY_FIXTURE, this.translate));
  protected readonly languages = computed(() => withLabels(LANGUAGE_FIXTURE, this.translate));

  readonly name = signal('');
  readonly email = signal('');
  readonly notes = signal('');
  readonly agree = signal(false);
  readonly notify = signal(true);
  readonly priority = signal<DgaRadioValue | null>(PRIORITY_VALUES.Normal);
  readonly city = signal<string | null>(null);
  readonly cityChosen = signal<string | null>(CITY_VALUES.Jeddah);
  readonly spoken = signal<readonly string[]>([LANGUAGE_VALUES.Arabic, LANGUAGE_VALUES.English]);
}
