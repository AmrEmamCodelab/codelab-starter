// The generated design docs: every <selector>.catalog.json is valid against catalog.schema.json and matches its
// component, every exported component has an entry, and docs/design/component-catalog.md and docs/design/tokens.md
// are in sync with their sources. Also unit tests for the pure parts of both generators.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  buildCatalog,
  CATALOG_DOC_FILE,
  catalogProblems,
  cell,
  createSchemaValidator,
  EXIT_USAGE as CATALOG_EXIT_USAGE,
  exportedComponents,
  exportedModulePaths,
  groupByCategory,
  LIB_DIR,
  main as catalogMain,
  memberProblems,
  parseComponents,
  ProblemKind,
  prose,
  readCatalogEntries,
  renderCatalog,
  renderEntry,
  sameText,
} from '../scripts/generate-catalog.mjs';
import {
  BRANDING_GROUP,
  buildTokensDoc,
  EXIT_USAGE as TOKENS_EXIT_USAGE,
  groupOf,
  isColour,
  main as tokensMain,
  OTHER_GROUP,
  parseTokenSource,
  renderTokens,
  resolveValue,
  TOKENS_DOC_FILE,
} from '../scripts/generate-tokens-doc.mjs';

const SINCE = '1.0.0-rc.0';

/** A minimal valid entry; override members per test. */
function entry(overrides = {}) {
  return {
    selector: 'dga-sample',
    className: 'DgaSampleComponent',
    category: 'content',
    summary: 'A sample.',
    inputs: [{ name: 'tone', type: "'a' | 'b'", required: false, default: "'a'", description: 'The tone.' }],
    outputs: [],
    slots: [{ selector: 'default', description: 'The text.' }],
    a11y: 'Plain text.',
    since: SINCE,
    ...overrides,
  };
}

function component(overrides = {}) {
  return { selector: 'dga-sample', className: 'DgaSampleComponent', inputs: ['tone'], models: [], outputs: [], ...overrides };
}

function item(value, fileName = `${value.selector}.catalog.json`) {
  return { file: path.join(LIB_DIR, 'sample', fileName), entry: value };
}

const validate = createSchemaValidator();

describe('component catalog entries (the real package)', () => {
  const items = readCatalogEntries();

  it('are all valid against catalog.schema.json', () => {
    const invalid = items.map(({ file, entry: value }) => ({ file, errors: validate(value) })).filter((result) => result.errors.length > 0);
    expect(invalid).toEqual([]);
  });

  it('cover every exported component and match its class, inputs and outputs', () => {
    const problems = catalogProblems(items, exportedComponents(), validate);
    expect(problems.map((problem) => `${problem.kind} ${problem.selector}: ${problem.message}`)).toEqual([]);
  });

  it('keep docs/design/component-catalog.md in sync (npm run catalog:generate)', () => {
    expect(sameText(readFileSync(CATALOG_DOC_FILE, 'utf8'), buildCatalog().document)).toBe(true);
  });
});

describe('tokens document (the real package)', () => {
  it('keeps docs/design/tokens.md in sync (npm run tokens:generate)', () => {
    const built = buildTokensDoc();
    expect(built.tokenCount).toBeGreaterThan(0);
    expect(sameText(readFileSync(TOKENS_DOC_FILE, 'utf8'), built.document)).toBe(true);
  });
});

describe('component source parsing', () => {
  const source = `
    @Directive({ selector: '[dgaThing]' })
    export class DgaThingDirective { readonly ignored = input(1); }

    @Component({
      selector: 'dga-sample',
      template: '',
    })
    export class DgaSampleComponent {
      readonly tone = input<Tone>('a');
      readonly items = input.required<readonly Item[]>();
      readonly value = model('');
      readonly picked = output<string>();
      private readonly internal = signal(0);
    }

    @Component({ selector: 'dga-sample-item', template: '' })
    export class DgaSampleItemComponent {
      readonly open = model.required<boolean>();
    }
  `;

  it('finds each component with its inputs, models and outputs, and skips directives', () => {
    expect(parseComponents(source)).toEqual([
      { selector: 'dga-sample', className: 'DgaSampleComponent', inputs: ['tone', 'items', 'value'], models: ['value'], outputs: ['picked'] },
      { selector: 'dga-sample-item', className: 'DgaSampleItemComponent', inputs: ['open'], models: ['open'], outputs: [] },
    ]);
  });

  it('reads the re-exported module paths of the public API', () => {
    const api = "export * from './lib/a/a.component';\nexport { B, type C } from './lib/b/b';\n// export * from './lib/commented';";
    expect(exportedModulePaths(api)).toEqual(['./lib/a/a.component', './lib/b/b', './lib/commented']);
  });
});

describe('catalog problems', () => {
  it('accepts a matching entry, and model change events as outputs', () => {
    const value = entry({ inputs: [...entry().inputs, { name: 'value', type: 'string, model', required: false, default: "''", description: 'Two-way.' }], outputs: [{ name: 'valueChange', type: 'string', description: 'Change.' }] });
    expect(memberProblems(value, component({ inputs: ['tone', 'value'], models: ['value'] }))).toEqual([]);
  });

  it('reports class, input and output mismatches', () => {
    const value = entry({ className: 'DgaOtherComponent', outputs: [{ name: 'ghost', type: 'void', description: 'None.' }] });
    const kinds = memberProblems(value, component({ inputs: ['tone', 'size'], outputs: ['picked'] })).map((problem) => problem.kind);
    expect(kinds).toEqual([ProblemKind.ClassName, ProblemKind.Inputs, ProblemKind.Outputs, ProblemKind.Outputs]);
  });

  it('reports schema errors, a wrong file name, duplicates, unknown components and missing entries', () => {
    const problems = catalogProblems(
      [
        item(entry({ category: 'widgets' })),
        item(entry(), 'sample.catalog.json'),
        item(entry()),
        item(entry({ selector: 'dga-ghost', className: 'DgaGhostComponent' })),
      ],
      [component(), component({ selector: 'dga-missing', className: 'DgaMissingComponent' })],
      validate,
    );
    expect(problems.map((problem) => problem.kind)).toEqual([
      ProblemKind.Schema,
      ProblemKind.FileName,
      ProblemKind.Duplicate,
      ProblemKind.UnknownComponent,
      ProblemKind.MissingEntry,
    ]);
  });

  it('rejects entries with unknown members or a bad selector', () => {
    expect(validate(entry({ extra: true }))).not.toEqual([]);
    expect(validate(entry({ selector: 'app-sample' }))).not.toEqual([]);
    expect(validate(entry({ since: 'soon' }))).not.toEqual([]);
    expect(validate(entry())).toEqual([]);
  });
});

describe('catalog rendering', () => {
  it('groups by category in reading order and sorts by selector', () => {
    const groups = groupByCategory([entry({ selector: 'dga-z', category: 'form' }), entry({ selector: 'dga-b' }), entry({ selector: 'dga-a', category: 'layout' })]);
    expect(groups.map((group) => [group.category, group.entries.map((value) => value.selector)])).toEqual([
      ['layout', ['dga-a']],
      ['content', ['dga-b']],
      ['form', ['dga-z']],
    ]);
  });

  it('escapes pipes in cells and shows tags as code', () => {
    expect(cell("'a' | 'b'")).toBe("'a' \\| 'b'");
    expect(prose('Listen on <dga-button> and </dga-button>.')).toBe('Listen on `<dga-button>` and `</dga-button>`.');
  });

  it('renders a section with the showcase anchor, the tables and None for empty lists', () => {
    const section = renderEntry(entry(), 'http://host/showcase');
    expect(section).toContain('### dga-sample');
    expect(section).toContain('[`/showcase#dga-sample`](http://host/showcase#dga-sample)');
    expect(section).toContain("| `tone` | `'a' \\| 'b'` | no | `'a'` | The tone. |");
    expect(section).toContain('#### Outputs\n\nNone.');
    expect(section).toContain("`import { DgaSampleComponent } from '@codelab/ng-dga';`");
  });

  it('is deterministic and ends with one newline', () => {
    const entries = [entry(), entry({ selector: 'dga-other', className: 'DgaOtherComponent', category: 'form' })];
    const first = renderCatalog(entries);
    expect(renderCatalog([...entries].reverse())).toBe(first);
    expect(first.endsWith('.\n')).toBe(true);
    expect(first).toContain('| Form controls | [dga-other](#dga-other) |');
  });

  it('compares text ignoring CRLF line endings', () => {
    expect(sameText('a\r\nb\r\n', 'a\nb\n')).toBe(true);
    expect(sameText('a\n', 'b\n')).toBe(false);
  });

  it('refuses unknown options', () => {
    expect(catalogMain(['--write'])).toBe(CATALOG_EXIT_USAGE);
    expect(tokensMain(['--write'])).toBe(TOKENS_EXIT_USAGE);
  });
});

describe('token parsing and rendering', () => {
  const scss = [
    '$dga-breakpoints: (',
    '  sm: 36rem,',
    '  md: 48rem,',
    ') !default;',
    '$dga-grid-columns: 12 !default;',
    '@mixin root-tokens {',
    '  // --- Palette: primary -----------------',
    '  --dga-palette-primary-600: #1b8354;',
    '  // --- Semantic ----',
    '  --dga-color-primary: var(--dga-palette-primary-600);',
    '  --dga-space-4: 1rem;',
    '  --dga-opacity-disabled: 0.6;',
    '}',
  ].join('\r\n');

  it('reads custom properties with their section and multi-line Sass values', () => {
    const parsed = parseTokenSource(scss, 'tokens.scss');
    expect(parsed.tokens.map((token) => [token.name, token.value, token.section])).toEqual([
      ['--dga-palette-primary-600', '#1b8354', 'Palette: primary'],
      ['--dga-color-primary', 'var(--dga-palette-primary-600)', 'Semantic'],
      ['--dga-space-4', '1rem', 'Semantic'],
      ['--dga-opacity-disabled', '0.6', 'Semantic'],
    ]);
    expect(parsed.sassValues).toEqual([
      { name: '$dga-breakpoints', value: '( sm: 36rem, md: 48rem, )', section: 'tokens.scss' },
      { name: '$dga-grid-columns', value: '12', section: 'tokens.scss' },
    ]);
  });

  it('resolves var() chains and nested fallbacks, and leaves unknown references', () => {
    const byName = new Map([
      ['--a', '#000000'],
      ['--b', 'var(--a)'],
      ['--loop', 'var(--loop)'],
    ]);
    expect(resolveValue('var(--b)', byName)).toBe('#000000');
    expect(resolveValue('var(--brand, var(--b))', byName)).toBe('#000000');
    expect(resolveValue('var(--unknown)', byName)).toBe('var(--unknown)');
    expect(resolveValue('var(--loop)', byName)).toBe('var(--loop)');
  });

  it('recognises colours and groups tokens by source or prefix', () => {
    expect(isColour('#fff')).toBe(true);
    expect(isColour('rgba(0, 0, 0, 0.5)')).toBe(true);
    expect(isColour('1rem')).toBe(false);
    expect(groupOf({ name: '--dga-space-1' })).toBe('Spacing');
    expect(groupOf({ name: '--dga-opacity-disabled' })).toBe(OTHER_GROUP);
    expect(groupOf({ name: '--dga-color-primary', group: BRANDING_GROUP })).toBe(BRANDING_GROUP);
  });

  it('renders colour tables with swatches and other tables without', () => {
    const document = renderTokens([parseTokenSource(scss, 'tokens.scss')], ['tokens.scss']);
    expect(document).toContain('| Swatch | Token | Value | Resolved |');
    expect(document).toContain('background:#1b8354');
    expect(document).toContain('| `--dga-color-primary` | `var(--dga-palette-primary-600)` | `#1b8354` |');
    expect(document).toContain('## Spacing\n\n| Token | Value | Resolved |\n| --- | --- | --- |\n| `--dga-space-4` | `1rem` |  |');
    expect(document).toContain('## Other');
    expect(document).toContain('| `$dga-grid-columns` | `12` |');
    expect(document.indexOf('## Colour')).toBeLessThan(document.indexOf('## Spacing'));
  });
});
