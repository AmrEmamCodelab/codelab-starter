import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  ExitCodes,
  GateFindingKinds,
  checkPermissionGates,
  extractInlineTemplates,
  findUngatedButtons,
  findUngatedButtonsInSource,
  formatGateFindings,
  listGateSources,
  main,
  parsePermissionGateArguments,
  scanTemplate,
} from '../src/permission-gates/index.mjs';

const BIN = fileURLToPath(new URL('../src/bin/codelab-ng-tools.mjs', import.meta.url));
const lines = (findings) => findings.map((finding) => finding.line);

describe('scanTemplate', () => {
  it('reads tags, attribute names with their case, quoted, unquoted and valueless values, and positions', () => {
    const [open, close] = scanTemplate('<dga-button *hasPermission="K.A" size=md data-public-action (click)="go(a > b)">x</dga-button>');
    expect(open).toMatchObject({ type: 'open', name: 'dga-button', selfClosing: false, line: 1, column: 1 });
    expect([...open.attributes]).toEqual([['*hasPermission', 'K.A'], ['size', 'md'], ['data-public-action', null], ['(click)', 'go(a > b)']]);
    expect(close).toEqual({ type: 'close', name: 'dga-button' });
  });

  it('marks void and self-closing elements, and finds lines after CRLF', () => {
    const events = scanTemplate('<p>\r\n  <input [value]="v">\r\n  <dga-button />\r\n</p>');
    expect(events.map((event) => [event.type, event.name, event.selfClosing, event.line])).toEqual([
      ['open', 'p', false, 1],
      ['open', 'input', true, 2],
      ['open', 'dga-button', true, 3],
      ['close', 'p', undefined, undefined],
    ]);
  });

  it.each([
    ['a comment', '<!-- <dga-button> -->'],
    ['an interpolation', "{{ '<dga-button>' }} {{ a<b }}"],
    ['a block parameter', '@if (a<b) { } @for (x of xs; track x) { } @let y = a<b;'],
    ['an attribute value', '<p [title]="\'<dga-button>\'" title=\'<button (click)="x">\'></p>'],
    ['a script body', '<script>if (a<dga-button) {}</script>'],
    ['a declaration', '<!doctype html>'],
  ])('sees no element inside %s', (_label, text) => {
    expect(scanTemplate(text).filter((event) => event.name === 'dga-button' || event.name === 'button' || event.name === 'b')).toEqual([]);
  });

  it('runs unclosed constructs to the end without throwing', () => {
    for (const text of ['<!-- open', '{{ open', '<p title="open', '@if (open', '<dga-button', '<script>x']) {
      expect(() => scanTemplate(text)).not.toThrow();
    }
  });
});

describe('findUngatedButtons', () => {
  it.each([
    ['a dga-button', '<dga-button>x</dga-button>'],
    ['a self-closing dga-button', '<dga-button />'],
    ['a button with (click)', '<button type="button" (click)="go()">x</button>'],
    ['a button with an empty *hasPermission', '<button (click)="go()" *hasPermission="">x</button>'],
    ['a button with [hasPermission] outside ng-template', '<button (click)="go()" [hasPermission]="K.A">x</button>'],
    ['a button after a closed gated sibling', '<div *hasPermission="K.A"></div><dga-button>x</dga-button>'],
    ['a button beside a data-public-action sibling', '<div data-public-action></div><button (click)="go()">x</button>'],
    ['a native submit button', '<button type="submit">Delete</button>'],
    ['a button with no type inside an (ngSubmit) form', '<form (ngSubmit)="del()"><div><button>Delete</button></div></form>'],
    ['a button with no type inside an on-submit form', '<form on-submit="del()"><button type="">Delete</button></form>'],
    ['a button with on-click', '<button on-click="del()">x</button>'],
    ['a button with a key event', '<button (keyup.enter)="del()">x</button>'],
    ['an anchor with (click)', '<a (click)="del()">x</a>'],
    ['a role=button div with (click)', '<div role="button" (click)="del()">x</div>'],
    ['a role=button span with a key event', '<span role="button" (keydown.space)="del()">x</span>'],
    ['a card component with (click)', '<dga-card (click)="del()"></dga-card>'],
    ['a dialog pattern with an ungated (confirm)', '<app-editor-dialog-pattern (confirm)="store.deleteAll()"></app-editor-dialog-pattern>'],
    ['a dialog pattern with on-confirm', '<app-editor-dialog-pattern on-confirm="store.save()" />'],
  ])('refuses %s', (_label, text) => {
    expect(findUngatedButtons(text)).toHaveLength(1);
  });

  it.each([
    ['*hasPermission on the button', '<dga-button *hasPermission="PermissionKeys.UsersManage">x</dga-button>'],
    ['data-public-action on the button', '<button type="button" data-public-action (click)="retry()">x</button>'],
    ['*hasPermission on an ancestor', '<section *hasPermission="K.A"><div><dga-button>x</dga-button></div></section>'],
    ['an ng-container ancestor', '<ng-container *hasPermission="K.A">@if (on) {<button (click)="go()">x</button>}</ng-container>'],
    ['an ng-template [hasPermission] wrapper', '<ng-template [hasPermission]="K.A"><dga-button>x</dga-button></ng-template>'],
    ['a plain button and a non-submit button inside a submitting form',
      '<button type="button">x</button><form (ngSubmit)="s()"><button type="button">y</button><button type="reset">z</button></form>'],
    ['a button with no type outside any submitting form', '<form><button>x</button></form><button>y</button>'],
    ['a two-way binding and a property binding on a button', '<button [(value)]="v" [disabled]="d">x</button>'],
    ['an element with a non-action output', '<dga-select (selectionChange)="pick($event)"></dga-select><input (keyup.enter)="search()">'],
    ['a gated (confirm) dialog host', '<app-editor-dialog-pattern *hasPermission="K.A" (confirm)="store.save()"></app-editor-dialog-pattern>'],
    ['a submit button under a gated form', '<form *hasPermission="K.A" (ngSubmit)="s()"><button>x</button></form>'],
    ['buttons in comments and strings', '<!-- <dga-button> --><p [title]="\'<dga-button>\'"></p>'],
  ])('accepts %s', (_label, text) => {
    expect(findUngatedButtons(text)).toEqual([]);
  });

  it('closes elements by name, tolerating unclosed inner elements', () => {
    const text = '<section *hasPermission="K.A"><p>unclosed<dga-button>a</dga-button></section>\n<dga-button>b</dga-button>';
    expect(findUngatedButtons(text)).toEqual([{ kind: GateFindingKinds.ungatedButton, line: 2, column: 1, element: 'dga-button' }]);
  });

  it('refuses a string-literal key on *hasPermission or [hasPermission], even though it gates the button', () => {
    const text = "<dga-button *hasPermission=\"'users.manage'\">x</dga-button>\n<ng-template [hasPermission]=\" `a.b`\"></ng-template>";
    expect(findUngatedButtons(text)).toEqual([
      { kind: GateFindingKinds.literalKey, line: 1, column: 1, element: 'dga-button' },
      { kind: GateFindingKinds.literalKey, line: 2, column: 1, element: 'ng-template' },
    ]);
    expect(formatGateFindings([{ kind: GateFindingKinds.literalKey, file: 'a.html', line: 1, column: 1, element: 'dga-button' }]))
      .toMatch(/a\.html:1:1 <dga-button> gates on a string literal/);
  });

  it('stops treating buttons as submit buttons once the submitting form closes', () => {
    expect(lines(findUngatedButtons('<form (submit)="s()"></form>\n<button>x</button>\n<form (ngSubmit)="s()">\n<button>y</button></form>'))).toEqual([4]);
  });

  it('ignores a stray closing tag', () => {
    expect(lines(findUngatedButtons('</div>\n<div *hasPermission="K.A"></span><dga-button>x</dga-button></div>'))).toEqual([]);
  });
});

describe('extractInlineTemplates', () => {
  it('returns each template literal with the line of its opening quote', () => {
    const source = "@Component({\n  selector: 'app-a',\n  template: `\n    <dga-button>x</dga-button>\n  `,\n})\nclass A {}\n@Component({ template: '<p>b</p>' })\nclass B {}";
    expect(extractInlineTemplates(source)).toEqual([
      { text: '\n    <dga-button>x</dga-button>\n  ', line: 3 },
      { text: '<p>b</p>', line: 8 },
    ]);
  });

  it('unescapes quotes and newlines inside single- and double-quoted templates', () => {
    expect(extractInlineTemplates("x = { template: '<p title=\\'a\\'>\\n</p>' }")).toEqual([{ text: "<p title='a'>\n</p>", line: 1 }]);
    expect(extractInlineTemplates('x = { template: "<b>\\"</b>" }')).toEqual([{ text: '<b>"</b>', line: 1 }]);
  });

  it.each([
    ['a line comment', "// template: '<dga-button>'\nconst a = 1;"],
    ['a block comment', "/* template: '<dga-button>' */"],
    ['a string', "const s = \"template: '<dga-button>'\";"],
    ['a template literal with a substitution', "const s = `template: ${ { template: 1 }.template } '<dga-button>'`;"],
    ['a regular expression', "const r = /template: '<dga-button>'/;"],
    ['a templateUrl', "@Component({ templateUrl: './a.html' })"],
    ['a member access', "const t = this.template; const u = obj.template;"],
    ['a template naming a constant the file does not declare', 'const o = { template: someVariable };'],
    ['a constant compared, not assigned', "const o = { template: T }; if (T === '<dga-button>') {}"],
  ])('finds nothing in %s', (_label, source) => {
    expect(extractInlineTemplates(source)).toEqual([]);
  });

  it('still finds a template after a division that looks like a regex start', () => {
    expect(extractInlineTemplates("const half = total / 2; const o = { template: '<i></i>' }; const q = a / b;")).toEqual([{ text: '<i></i>', line: 1 }]);
  });

  it('resolves a template that names a constant declared in the same file, on the line of the constant', () => {
    const source = "const OTHER = '<i></i>';\nconst T = `\n<dga-button>x</dga-button>`;\n@Component({ template: T })\nclass A {}\n@Component({ template: '<p></p>' })\nclass B {}";
    expect(extractInlineTemplates(source)).toEqual([
      { text: '\n<dga-button>x</dga-button>', line: 2 },
      { text: '<p></p>', line: 6 },
    ]);
    expect(findUngatedButtonsInSource('a.component.ts', source)).toEqual([{ kind: GateFindingKinds.ungatedButton, line: 3, column: 1, element: 'dga-button' }]);
  });

  it('skips a comment between the property, the colon and the literal', () => {
    expect(extractInlineTemplates("({ template /* c */ : // c\n '<p></p>' })")).toEqual([{ text: '<p></p>', line: 2 }]);
  });
});

describe('findUngatedButtonsInSource', () => {
  it('reports inline template findings on their source lines', () => {
    const source = "@Component({\n  template: `\n    <p></p>\n    <dga-button>x</dga-button>\n  `,\n})\nexport class A {}\n";
    expect(findUngatedButtonsInSource('a.component.ts', source)).toEqual([{ kind: GateFindingKinds.ungatedButton, line: 4, column: 5, element: 'dga-button' }]);
  });

  it('reads an .html file as one template', () => {
    expect(lines(findUngatedButtonsInSource('a.component.html', '<p></p>\n<dga-button></dga-button>'))).toEqual([2]);
  });
});

describe('checkPermissionGates and the CLI', () => {
  let dir;
  const write = (relative, text) => {
    mkdirSync(dirname(join(dir, relative)), { recursive: true });
    writeFileSync(join(dir, relative), text);
  };
  const io = () => {
    const sink = { logs: [], errors: [] };
    return { sink, log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) };
  };

  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'ng-tools-gates-'));
  });

  afterEach(() => {
    rmSync(dir, { recursive: true, force: true });
  });

  it('lists templates and app scripts, skipping specs, test helpers, declarations and build output', () => {
    for (const file of ['a/a.html', 'a/a.ts', 'a/a.spec.ts', 'a/a.test.ts', 'a/a.testing.ts', 'a/a.d.ts', 'a/a.scss', 'node_modules/x/b.html', 'dist/c.html']) write(file, '');
    expect(listGateSources(dir)).toEqual([join(dir, 'a', 'a.html'), join(dir, 'a', 'a.ts')]);
  });

  it('reports findings with relative forward-slash paths', () => {
    write('orders/list/list.component.html', '<dga-button></dga-button>');
    write('orders/list/list.component.ts', "@Component({ template: '<button (click)=\"x()\"></button>' })");
    write('orders/list/list.component.spec.ts', "@Component({ template: '<dga-button></dga-button>' })");
    const result = checkPermissionGates(dir);
    expect(result.fileCount).toBe(2);
    expect(result.findings).toEqual([
      { kind: GateFindingKinds.ungatedButton, file: 'orders/list/list.component.html', line: 1, column: 1, element: 'dga-button' },
      { kind: GateFindingKinds.ungatedButton, file: 'orders/list/list.component.ts', line: 1, column: 1, element: 'button' },
    ]);
    expect(formatGateFindings(result.findings)).toMatch(/2 finding\(s\):\n {2}orders\/list\/list\.component\.html:1:1 <dga-button>[\s\S]*Fix: Add \*hasPermission/);
  });

  it('reports a missing folder', () => {
    expect(checkPermissionGates(join(dir, 'missing')).error).toMatch(/folder not found/);
  });

  it('parses its arguments', () => {
    expect(parsePermissionGateArguments(['--dir', 'src', '--fix', 'see recipe'])).toEqual({ options: { dir: 'src', fix: 'see recipe' } });
    expect(parsePermissionGateArguments([]).error).toMatch(/--dir is required/);
    expect(parsePermissionGateArguments(['--dir']).error).toMatch(/Unexpected argument/);
    expect(parsePermissionGateArguments(['--other', 'x']).error).toMatch(/Unexpected argument/);
  });

  it('exits 0, 1 and 2', () => {
    write('ok.html', '<dga-button data-public-action></dga-button>');
    const ok = io();
    expect(main(['permission-gates', '--dir', dir], ok)).toBe(ExitCodes.ok);
    expect(ok.sink.logs[0]).toMatch(/1 file\(s\), every action button gated/);
    write('bad.html', '<dga-button></dga-button>');
    const bad = io();
    expect(main(['permission-gates', '--dir', dir, '--fix', 'see the recipe'], bad)).toBe(ExitCodes.failure);
    expect(bad.sink.errors[0]).toMatch(/bad\.html:1:1[\s\S]*Fix: see the recipe/);
    expect(main(['unknown'], io())).toBe(ExitCodes.usage);
    expect(main([], io())).toBe(ExitCodes.usage);
    expect(main(['permission-gates'], io())).toBe(ExitCodes.usage);
    expect(main(['permission-gates', '--dir', join(dir, 'missing')], io())).toBe(ExitCodes.usage);
  });

  it('runs as a bin', () => {
    write('bad.html', '<button (click)="x()"></button>');
    const result = spawnSync(process.execPath, [BIN, 'permission-gates', '--dir', dir], { encoding: 'utf8' });
    expect(result.status).toBe(ExitCodes.failure);
    expect(result.stderr).toMatch(/bad\.html:1:1 <button>/);
  });
});
