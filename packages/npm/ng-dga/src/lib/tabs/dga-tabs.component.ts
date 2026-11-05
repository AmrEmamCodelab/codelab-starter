import { booleanAttribute, ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, model, viewChildren } from '@angular/core';
import { DgaIconComponent } from '../icon/dga-icon.component';
import { isRightToLeft } from '../internal/direction';
import { createDgaId } from '../internal/unique-id';
import { dgaTabPanelId, type DgaTab, type DgaTabValue, focusableTabIndex, nextTabIndex } from './tab-navigation';

/**
 * DGA tabs: an underline tab list (WAI-ARIA tabs pattern). The selected value is two-way bound with [(value)];
 * labels arrive translated. Keyboard: arrow keys move and select along the visual order (mirrored in RTL), Home and
 * End jump to the ends, disabled tabs are skipped; only the selected tab is in the Tab order (roving tabindex).
 * To link tabs and panels, set `panelIdPrefix` and render each panel with `id = dgaTabPanelId(prefix, value)` and
 * `role="tabpanel"`.
 *
 * Layout: by default each tab keeps its natural width and the list scrolls sideways when the tabs do not fit (for a
 * long list of views). Set `fill` for a short chooser that must stay fully visible on a phone (for example the sign-in
 * methods): the tabs share the full width equally and long labels wrap inside their tab instead of overflowing.
 *
 * @example
 * <dga-tabs [tabs]="tabs()" [(value)]="selected" [label]="'records.views' | translate" panelIdPrefix="records" />
 * <dga-tabs fill [tabs]="methods()" [(value)]="method" [label]="'auth.methods' | translate" />
 */
@Component({
  selector: 'dga-tabs',
  imports: [DgaIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dga-tabs.component.html',
  styleUrl: './dga-tabs.component.css',
  host: {
    '[class.dga-tabs--fill]': 'fill()',
  },
})
export class DgaTabsComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly tabButtons = viewChildren<ElementRef<HTMLButtonElement>>('tabButton');
  private readonly baseId = createDgaId('dga-tabs');

  readonly tabs = input.required<readonly DgaTab[]>();
  readonly value = model.required<DgaTabValue>();
  /** Accessible name of the tab list (already translated). */
  readonly label = input<string | null>(null);
  /** When set, each tab gets aria-controls = dgaTabPanelId(panelIdPrefix, value). */
  readonly panelIdPrefix = input<string | null>(null);
  /** Tabs share the full width and wrap long labels, so every tab stays visible at phone width (no sideways scroll). */
  readonly fill = input(false, { transform: booleanAttribute });

  readonly focusableIndex = computed(() => focusableTabIndex(this.tabs(), this.value()));

  tabId(value: DgaTabValue): string {
    return `${this.baseId}-${value}`;
  }

  panelId(value: DgaTabValue): string | null {
    const prefix = this.panelIdPrefix();
    return prefix === null ? null : dgaTabPanelId(prefix, value);
  }

  select(tab: DgaTab): void {
    if (tab.disabled || tab.value === this.value()) return;
    this.value.set(tab.value);
  }

  onKeydown(event: KeyboardEvent, index: number): void {
    const next = nextTabIndex(this.tabs(), index, event.key, isRightToLeft(this.host.nativeElement));
    if (next === null) return;
    event.preventDefault();
    this.select(this.tabs()[next]);
    this.tabButtons()[next]?.nativeElement.focus();
  }
}
