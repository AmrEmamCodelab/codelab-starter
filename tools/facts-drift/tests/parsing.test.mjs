// Run: node --test "tools/facts-drift/tests/**/*.test.mjs"
// Pure parsing units: Markdown lines, code spans and tables; shell commands; PowerShell param blocks.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { CommandKinds, analyzeShellLine, tokenizeShellLine } from '../lib/commands.mjs';
import { columnHeaderAt, readCodeSpans, readMarkdownLines, readTableColumns, splitTableCells } from '../lib/markdown.mjs';
import { readScriptParameters, switchResolves } from '../lib/powershell-params.mjs';

describe('readMarkdownLines', () => {
  it('separates prose from fenced lines and drops the fence delimiters', () => {
    const lines = readMarkdownLines('a\n```powershell\nnpm test\n```\nb\n');
    assert.deepEqual(lines.map((line) => [line.number, line.text, line.fence?.language ?? null]),
      [[1, 'a', null], [3, 'npm test', 'powershell'], [5, 'b', null], [6, '', null]]);
  });

  it('gives every block its own id and the paragraph before it as intro, across a blank line', () => {
    const lines = readMarkdownLines('Run it from `frontend/web`:\n\n```\nx\n```\n\nOther text\n```\ny\n```\n');
    const fenced = lines.filter((line) => line.fence !== null);
    assert.equal(fenced[0].fence.intro, 'Run it from `frontend/web`:');
    assert.equal(fenced[1].fence.intro, 'Other text');
    assert.notEqual(fenced[0].fence.id, fenced[1].fence.id);
  });

  it('keeps a longer or different fence open until its own closing marker', () => {
    const lines = readMarkdownLines('````\n```\ninside\n````\nout\n');
    assert.deepEqual(lines.filter((line) => line.fence === null).map((line) => line.text), ['out', '']);
  });
});

describe('readCodeSpans', () => {
  it('reads single and double backtick spans with their offsets', () => {
    assert.deepEqual(readCodeSpans('use `a` and `` b`c `` here'), [
      { content: 'a', start: 4, end: 7 },
      { content: 'b`c', start: 12, end: 21 },
    ]);
  });
});

describe('tables', () => {
  it('splits cells but not on a pipe inside a code span or an escaped pipe', () => {
    const row = '| `a|b` | c \\| d | e |';
    assert.deepEqual(splitTableCells(row).map((cell) => row.slice(cell.start, cell.end).trim()), ['`a|b`', 'c \\| d', 'e']);
  });

  it('maps each data row offset to its lower-cased header', () => {
    const lines = readMarkdownLines('| Intent | Skill |\n| --- | --- |\n| new area | `new-module` |\n');
    const rows = readTableColumns(lines);
    const row = rows.get(3);
    assert.equal(columnHeaderAt(row, lines[2].text.indexOf('`new-module`')), 'skill');
    assert.equal(columnHeaderAt(row, 2), 'intent');
    assert.equal(rows.has(1), false);
  });
});

describe('tokenizeShellLine and analyzeShellLine', () => {
  it('splits commands on operators, honours quotes and drops comments and prompts', () => {
    assert.deepEqual(tokenizeShellLine('$ node "a b.mjs" --x && npm test # note'), [['node', 'a b.mjs', '--x'], ['npm', 'test']]);
  });

  it('recognises npm scripts with a workspace given either way', () => {
    assert.deepEqual(analyzeShellLine('npm run api:gen --workspace frontend/web'),
      [{ kind: CommandKinds.npmScript, script: 'api:gen', workspace: 'frontend/web' }]);
    assert.deepEqual(analyzeShellLine('npm -w=@codelab/web run lint'),
      [{ kind: CommandKinds.npmScript, script: 'lint', workspace: '@codelab/web' }]);
    assert.deepEqual(analyzeShellLine('npm test --workspaces'), [{ kind: CommandKinds.npmScript, script: 'test', workspace: null }]);
    assert.deepEqual(analyzeShellLine('npm ci'), []);
  });

  it('recognises node scripts and node --test globs, and ignores inline code', () => {
    assert.deepEqual(analyzeShellLine('node tools/check.mjs --root .'), [{ kind: CommandKinds.nodeScript, file: 'tools/check.mjs' }]);
    assert.deepEqual(analyzeShellLine('node --test "tools/**/*.test.mjs"'), [{ kind: CommandKinds.nodeTest, patterns: ['tools/**/*.test.mjs'] }]);
    assert.deepEqual(analyzeShellLine('node -e "console.log(1)"'), []);
  });

  it('recognises PowerShell scripts through -File, -Command, the call operator or a bare path', () => {
    const viaFile = analyzeShellLine('powershell -NoProfile -ExecutionPolicy Bypass -File tools\\build.ps1 -Target CI -Pack');
    assert.deepEqual(viaFile, [{ kind: CommandKinds.powershellScript, file: 'tools\\build.ps1', switches: ['Target', 'Pack'] }]);
    assert.deepEqual(analyzeShellLine('& .\\run.ps1 -Slot 1'), [{ kind: CommandKinds.powershellScript, file: '.\\run.ps1', switches: ['Slot'] }]);
    assert.equal(analyzeShellLine('pwsh -Command "& ./x.ps1 -Y"')[0].file, './x.ps1');
  });

  it('recognises cd and Set-Location', () => {
    assert.deepEqual(analyzeShellLine('Set-Location frontend/web'), [{ kind: CommandKinds.changeDirectory, target: 'frontend/web' }]);
  });
});

describe('readScriptParameters', () => {
  it('reads the declared names, not attribute arguments, defaults or function parameters', () => {
    const script = '<# help: param($Fake) #>\n[CmdletBinding()]\nparam(\n  [ValidateSet(\'A\')] [string] $Target, # comment $Nope\n'
      + '  [Parameter(Mandatory)] [switch] $Pack,\n  [string] $Root = (Join-Path $PSScriptRoot \'x\'),\n  $Plain = $env:X\n)\n'
      + 'function F { param($Inner) }\n';
    assert.deepEqual([...readScriptParameters(script)].sort(), ['pack', 'plain', 'root', 'target']);
  });

  it('returns null without a script-level param block', () => {
    assert.equal(readScriptParameters('function F { param($Inner) }\n'), null);
    assert.equal(readScriptParameters('Write-Host hi\n'), null);
  });

  it('binds exact names and unique prefixes only', () => {
    const parameters = new Set(['target', 'pack', 'packagedir']);
    assert.equal(switchResolves('Target', parameters), true);
    assert.equal(switchResolves('Tar', parameters), true);
    assert.equal(switchResolves('Pack', parameters), true);
    assert.equal(switchResolves('Pa', parameters), false);
    assert.equal(switchResolves('Nope', parameters), false);
  });
});
