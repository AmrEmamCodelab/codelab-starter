import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  linkedSignal,
  model,
  output,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { DgaButtonComponent, DgaCheckboxComponent, DgaIconComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { TranslationService } from '../../core/i18n/translation.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import {
  TreeCheckState,
  allSelectableKeys,
  branchKeys,
  buildKeyTree,
  checkState,
  diffSelection,
  filterTree,
  lastSegmentLabel,
  normalizeForSearch,
  toggleSubtree,
  type TreeLabelResolver,
  type TreeNode,
  type TreeSelectionChange,
} from './key-tree';

/**
 * The permission tree editor page pattern: a checkbox tree over dotted keys (or explicit nodes) with tri-state
 * parents, a filter box, expand/collapse all, select/clear all and a read-only mode.
 *
 * Inputs: `labelKey` (translation key naming the tree; the fieldset legend), `keys` (dotted keys, built with
 * `buildKeyTree`) or `nodes` (explicit; wins over `keys`), `labelFor` (label resolver; default: the last segment as
 * text), `readOnly`, `searchable`, `initiallyExpanded`. Model: `value` (the selected keys, a new Set on every change).
 * Output: `selectionChange` with the added and removed keys.
 *
 * Checking a parent selects every key under it; a parent with some keys selected shows the mixed state.
 */
@Component({
  selector: 'app-tree-editor-pattern',
  imports: [DgaButtonComponent, DgaCheckboxComponent, DgaIconComponent, DgaTextInputComponent, NgTemplateOutlet, TranslatePipe],
  templateUrl: './tree-editor-pattern.component.html',
  styleUrl: './tree-editor-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TreeEditorPatternComponent {
  /** Translation key naming the tree (the group's legend). */
  readonly labelKey = input.required<string>();
  readonly keys = input<readonly string[]>([]);
  /** Explicit nodes; when set, `keys` is ignored. */
  readonly nodes = input<readonly TreeNode[] | null>(null);
  readonly labelFor = input<TreeLabelResolver>(lastSegmentLabel);
  readonly value = model<ReadonlySet<string>>(new Set());
  readonly readOnly = input(false, { transform: booleanAttribute });
  readonly searchable = input(true, { transform: booleanAttribute });
  readonly initiallyExpanded = input(true, { transform: booleanAttribute });
  readonly selectionChange = output<TreeSelectionChange>();

  private readonly translations = inject(TranslationService);

  protected readonly keysText = PatternTranslationKeys.Tree;
  protected readonly states = TreeCheckState;
  protected readonly treeId = `app-tree-editor-${nextTreeId()}`;
  readonly query = signal('');
  readonly tree = computed<readonly TreeNode[]>(() => this.nodes() ?? buildKeyTree(this.keys(), this.labelFor()));
  readonly expanded = linkedSignal<ReadonlySet<string>>(() =>
    new Set(this.initiallyExpanded() ? branchKeys(this.tree()) : []));
  readonly filtering = computed(() => normalizeForSearch(this.query()).length > 0);
  readonly visible = computed<readonly TreeNode[]>(() => {
    const query = normalizeForSearch(this.query());
    return query.length === 0 ? this.tree() : filterTree(this.tree(), (node) => this.matches(node, query));
  });
  readonly totalCount = computed(() => allSelectableKeys(this.tree()).length);
  readonly selectedCount = computed(() => {
    const selected = this.value();
    return allSelectableKeys(this.tree()).filter((key) => selected.has(key)).length;
  });

  labelText(node: TreeNode): string {
    return 'textKey' in node.label ? this.translations.translate(node.label.textKey) : node.label.text;
  }

  stateOf(node: TreeNode): TreeCheckState {
    return checkState(node, this.value());
  }

  isExpanded(node: TreeNode): boolean {
    return this.filtering() || this.expanded().has(node.key);
  }

  groupId(node: TreeNode): string {
    return `${this.treeId}-${node.key}`;
  }

  toggleExpanded(node: TreeNode): void {
    const next = new Set(this.expanded());
    if (next.has(node.key)) {
      next.delete(node.key);
    } else {
      next.add(node.key);
    }
    this.expanded.set(next);
  }

  expandAll(): void {
    this.expanded.set(new Set(branchKeys(this.tree())));
  }

  collapseAll(): void {
    this.expanded.set(new Set());
  }

  toggle(node: TreeNode, checked: boolean): void {
    this.applySelection(toggleSubtree(node, this.value(), checked));
  }

  /** Selects every visible key (with a filter, only the matches). */
  selectAll(): void {
    const next = new Set(this.value());
    allSelectableKeys(this.visible()).forEach((key) => next.add(key));
    this.applySelection(next);
  }

  /** Clears every visible key (with a filter, only the matches). */
  clearAll(): void {
    const next = new Set(this.value());
    allSelectableKeys(this.visible()).forEach((key) => next.delete(key));
    this.applySelection(next);
  }

  private applySelection(next: ReadonlySet<string>): void {
    if (this.readOnly()) {
      return;
    }
    const change = diffSelection(this.value(), next);
    if (change.added.size === 0 && change.removed.size === 0) {
      return;
    }
    this.value.set(next);
    this.selectionChange.emit(change);
  }

  private matches(node: TreeNode, query: string): boolean {
    return normalizeForSearch(this.labelText(node)).includes(query) || normalizeForSearch(node.key).includes(query);
  }
}

let treeCount = 0;

function nextTreeId(): number {
  treeCount += 1;
  return treeCount;
}
