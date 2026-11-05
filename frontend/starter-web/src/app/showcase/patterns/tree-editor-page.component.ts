import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { DgaCheckboxComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TreeEditorPatternComponent, type TreeLabel, type TreeLabelResolver } from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { TREE_INITIAL, TREE_KEYS, TREE_LABELS } from './pattern-fixtures';

/** The label of a tree demo node: its translation, or the key itself for a node the fixture does not name. */
export const treeLabel: TreeLabelResolver = (key: string): TreeLabel => {
  const textKey = TREE_LABELS[key];
  return textKey === undefined ? { text: key } : { textKey };
};

/** The tree editor pattern on a permission-like key set: tri-state groups, filter, expand/collapse, read-only. */
@Component({
  selector: 'app-showcase-tree-editor-page',
  imports: [DgaCheckboxComponent, ShowcaseFrameComponent, TranslatePipe, TreeEditorPatternComponent],
  templateUrl: './tree-editor-page.component.html',
  styleUrl: './pattern-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TreeEditorPageComponent {
  protected readonly pageId = ShowcasePageId.TreeEditor;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly treeKeys = TREE_KEYS;
  protected readonly labelFor = treeLabel;

  readonly selected = signal<ReadonlySet<string>>(TREE_INITIAL);
  readonly readOnly = signal(false);
}
