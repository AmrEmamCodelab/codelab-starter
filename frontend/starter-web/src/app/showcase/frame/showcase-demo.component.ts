import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/**
 * One labelled demo inside a showcase section: the state or variant it shows ("Default", "Disabled", "Error",
 * "Loading"...) above the component. `wide` lets a demo take the whole row and its children the
 * whole width (headers, steppers, sliders); `stack` stacks its children full width in a normal-width demo.
 */
@Component({
  selector: 'app-showcase-demo',
  imports: [TranslatePipe],
  templateUrl: './showcase-demo.component.html',
  styleUrl: './showcase-demo.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.demo--wide]': 'wide()', '[class.demo--stack]': 'stack()' },
})
export class ShowcaseDemoComponent {
  readonly labelKey = input.required<string>();
  readonly wide = input(false, { transform: booleanAttribute });
  /** Stacks the demo's children full width (form fields) instead of laying them out in a wrapping row. */
  readonly stack = input(false, { transform: booleanAttribute });
}
