import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  type ElementRef,
  inject,
  Injector,
  input,
  model,
  signal,
  viewChild,
} from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { hasText, joinIds } from '../internal/aria';
import { createDgaId } from '../internal/unique-id';
import { type DgaSelectCompareWith, type DgaSelectOption, dgaSelectDefaultCompare } from '../select/select-options';
import {
  chosenOptions,
  initialActiveOption,
  isChosen,
  isListMoveKey,
  isListToggleKey,
  moveActiveOption,
  NO_ACTIVE_OPTION,
  toggleValue,
  withoutValue,
} from './multi-select-state';

const ESCAPE_KEY = 'Escape';
const TAB_KEY = 'Tab';
/** Keys on the closed trigger that open the list (Enter and Space open it through the native click). */
const OPEN_KEYS: readonly string[] = ['ArrowDown', 'ArrowUp'];
const NEAREST: ScrollIntoViewOptions = { block: 'nearest' };

/**
 * The DGA multi-select: a trigger that opens a listbox of options with check marks, and the chosen options as chips
 * under it, each with a remove button. For choosing several values from a short or medium list.
 *
 * - The value is a signal model of `T[]`: bind [(value)] or a signal-forms field (value, touched, disabled, required,
 *   invalid, name). New choices are appended; chips show in option order. `compareWith` matches values to options.
 * - Keyboard (WAI-ARIA listbox): Arrow Down/Up on the trigger opens the list; in the list Arrow Down/Up, Home and End
 *   move the active option (disabled ones skipped), Space or Enter toggles it, Escape closes and returns focus to the
 *   trigger, Tab closes. The list stays open while toggling, and closes when focus leaves the component.
 * - All texts arrive translated: label, placeholder (the trigger text), removeLabel (the remove buttons are named
 *   "<removeLabel> <option label>") and emptyText (shown when there are no options).
 *
 * @example
 * <dga-multi-select [label]="'request.services' | translate" [placeholder]="'common.chooseMany' | translate"
 *                   [removeLabel]="'common.remove' | translate" [options]="serviceOptions()" [(value)]="services" />
 */
@Component({
  selector: 'dga-multi-select',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-multi-select.component.html',
  styleUrl: './dga-multi-select.component.css',
})
export class DgaMultiSelectComponent<T> {
  readonly value = model<readonly T[]>([]);
  readonly touched = model(false);
  readonly options = input.required<readonly DgaSelectOption<T>[]>();
  readonly compareWith = input<DgaSelectCompareWith<T>>(dgaSelectDefaultCompare);
  /** Visible label (already translated). */
  readonly label = input<string | null>(null);
  /** Accessible name when there is no visible label (already translated). */
  readonly ariaLabel = input<string | null>(null);
  /** The trigger text (already translated), for example "Choose one or more". */
  readonly placeholder = input<string | null>(null);
  /** The first word of each chip's remove button name (already translated), for example "Remove". */
  readonly removeLabel = input<string | null>(null);
  /** Shown in the open list when there are no options (already translated). */
  readonly emptyText = input<string | null>(null);
  readonly hint = input<string | null>(null);
  /** The error message (already translated); a non-empty error marks the control invalid. */
  readonly error = input<string | null>(null);
  readonly name = input<string | null>(null);
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Marks the control invalid without a message (a form field may set it). */
  readonly invalid = input(false, { transform: booleanAttribute });

  readonly baseId = createDgaId('dga-multi-select');
  readonly labelId = `${this.baseId}-label`;
  readonly triggerId = `${this.baseId}-trigger`;
  readonly listboxId = `${this.baseId}-listbox`;
  readonly hintId = `${this.baseId}-hint`;
  readonly errorId = `${this.baseId}-error`;

  readonly open = signal(false);
  readonly active = signal(NO_ACTIVE_OPTION);

  private readonly injector = inject(Injector);
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly listbox = viewChild.required<ElementRef<HTMLElement>>('listbox');

  readonly chips = computed(() => chosenOptions(this.options(), this.value(), this.compareWith()));
  readonly activeDescendant = computed(() => (this.open() && this.active() !== NO_ACTIVE_OPTION ? this.optionId(this.active()) : null));
  readonly hasError = computed(() => hasText(this.error()));
  readonly isInvalid = computed(() => this.invalid() || this.hasError());
  readonly describedBy = computed(() =>
    joinIds([this.hasError() ? this.errorId : null, hasText(this.hint()) ? this.hintId : null]),
  );
  readonly labelledBy = computed(() => (this.label() ? `${this.labelId} ${this.triggerId}` : null));

  optionId(index: number): string {
    return `${this.baseId}-option-${index}`;
  }

  isOptionChosen(option: DgaSelectOption<T>): boolean {
    return isChosen(this.value(), option.value, this.compareWith());
  }

  removeName(optionLabel: string): string {
    const prefix = this.removeLabel();
    return hasText(prefix) ? `${prefix} ${optionLabel}` : optionLabel;
  }

  toggleOpen(): void {
    if (this.open()) this.close({ returnFocus: true });
    else this.openList();
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    if (this.open() || !OPEN_KEYS.includes(event.key)) return;
    event.preventDefault();
    this.openList();
  }

  onListKeydown(event: KeyboardEvent): void {
    if (isListMoveKey(event.key)) {
      event.preventDefault();
      this.setActive(moveActiveOption(this.options(), { from: this.active(), key: event.key }));
    } else if (isListToggleKey(event.key)) {
      event.preventDefault();
      this.choose(this.active());
    } else if (event.key === ESCAPE_KEY) {
      // Close this list only, never an enclosing dialog.
      event.preventDefault();
      event.stopPropagation();
      this.close({ returnFocus: true });
    } else if (event.key === TAB_KEY) {
      this.close({ returnFocus: false });
    }
  }

  choose(index: number): void {
    const option = this.options()[index];
    if (option === undefined || option.disabled === true || this.disabled()) return;
    this.value.set(toggleValue(this.value(), option.value, this.compareWith()));
    this.active.set(index);
  }

  remove(option: DgaSelectOption<T>): void {
    if (this.disabled()) return;
    this.value.set(withoutValue(this.value(), option.value, this.compareWith()));
  }

  /** Closes the list and marks the control touched when focus leaves the whole component. */
  onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (next !== null && (event.currentTarget as HTMLElement).contains(next)) return;
    this.open.set(false);
    if (!this.touched()) this.touched.set(true);
  }

  private openList(): void {
    if (this.disabled()) return;
    const options = this.options();
    const firstChosen = options.findIndex((option) => this.isOptionChosen(option));
    this.active.set(initialActiveOption(options, firstChosen));
    this.open.set(true);
    afterNextRender(
      () => {
        this.listbox().nativeElement.focus();
        this.scrollActiveIntoView();
      },
      { injector: this.injector },
    );
  }

  private close(options: { readonly returnFocus: boolean }): void {
    this.open.set(false);
    if (options.returnFocus) this.trigger().nativeElement.focus();
  }

  private setActive(index: number): void {
    this.active.set(index);
    this.scrollActiveIntoView();
  }

  private scrollActiveIntoView(): void {
    if (this.active() === NO_ACTIVE_OPTION) return;
    const option = this.listbox().nativeElement.querySelector(`#${this.optionId(this.active())}`);
    // jsdom has no scrollIntoView; browsers do.
    option?.scrollIntoView?.(NEAREST);
  }
}
