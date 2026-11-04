import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { globToRegExp, matchesAnyGlob, findViolations, evaluateFile, moduleForPath } from '../lib/match.mjs';
import { compileRepository } from '../lib/engine.mjs';
import { createWorkspace, removeWorkspace, writeWorkspaceFile, moduleYaml } from './helpers/workspace.mjs';

describe('globToRegExp', () => {
  const cases = [
    ['**/*.cs', 'a/b/C.cs', true],
    ['**/*.cs', 'C.cs', true],
    ['**/*.cs', 'a/C.csproj', false],
    ['src/*.ts', 'src/a.ts', true],
    ['src/*.ts', 'src/x/a.ts', false],
    ['**/Api/**/*.cs', 'backend/Modules/X/Api/Controllers/A.cs', true],
    ['**/Api/**/*.cs', 'backend/Modules/X/Api/A.cs', true],
    ['**/Api/**/*.cs', 'backend/Starter.Api/A.cs', false],
    ['**/*.{scss,css}', 'a/b.scss', true],
    ['**/*.{scss,css}', 'a/b.less', false],
    ['a?c.md', 'abc.md', true],
    ['docs/**', 'docs/x/y.md', true],
    ['file.name+x.md', 'file.name+x.md', true],
  ];
  for (const [glob, candidate, expected] of cases) {
    test(`${glob} ${expected ? 'matches' : 'does not match'} ${candidate}`, () => {
      assert.equal(globToRegExp(glob).test(candidate), expected);
    });
  }

  test('matchesAnyGlob is false for an empty list', () => {
    assert.equal(matchesAnyGlob('a.cs', []), false);
  });
});

describe('findViolations', () => {
  test('reports 1-based line and column and the matched text', () => {
    const rule = { pattern: 'DateTime\\.Now', flags: '' };
    const violations = findViolations('var a = 1;\n  var b = DateTime.Now;\n', rule);
    assert.deepEqual(violations, [{ line: 2, column: 11, text: 'DateTime.Now' }]);
  });

  test('ignores g and y in the data and finds every match', () => {
    const rule = { pattern: 'x', flags: 'gy' };
    assert.equal(findViolations('x x x', rule).length, 3);
  });
});

describe('base forbidden rules (rules/base.rules.yaml)', () => {
  let root;
  let compiled;
  before(() => {
    root = createWorkspace();
    compiled = compileRepository(root).compiled;
  });
  after(() => removeWorkspace(root));

  const hits = (relativePath, content) => evaluateFile(compiled, relativePath, content).map((result) => result.rule.id);

  const cases = [
    ['base.time-provider', 'src/A.cs', 'var now = DateTime.UtcNow;', 'var now = timeProvider.GetUtcNow();'],
    ['base.time-provider', 'src/A.cs', 'var now = DateTimeOffset.Now;', 'var later = DateTimeOffset.MaxValue;'],
    ['base.no-string-permission-key', 'src/A.cs', '[HasPermission("records.read")]', '[HasPermission(Permissions.Records.Read)]'],
    ['base.no-string-permission-key', 'web/a.ts', "if (this.perm.hasPermission('x')) {}", 'if (this.perm.hasPermission(PermissionKeys.X)) {}'],
    ['base.no-string-role-check', 'src/A.cs', 'if (User.IsInRole("ADMIN")) {}', 'if (allowed) {}'],
    ['base.no-string-config-key', 'src/A.cs', 'var v = configuration["Smtp:Host"];', 'var v = options.Value.Host;'],
    ['base.no-string-config-key', 'src/A.cs', 'services.Configure<X>(config.GetSection("Smtp"));', 'config.GetSection(SmtpOptions.SectionName)'],
    ['base.enum-stored-as-int', 'src/A.cs', 'b.Property(x => x.Status).HasConversion<string>();', 'b.Property(x => x.Status);'],
    ['base.enum-explicit-values', 'src/A.cs', 'public enum S { Draft, Submitted }', 'public enum S { Draft = 1, Submitted = 2 }'],
    ['base.enum-explicit-values', 'src/A.cs', 'public enum S : byte\n{\n    Draft = 1,\n    Submitted\n}', 'public enum S : byte\n{\n    Draft = 1,\n    Submitted = 2,\n}'],
    ['base.no-dbcontext-in-api', 'backend/Modules/X/Api/RecordsEndpoints.cs', 'public sealed class E(AppDbContext db) {}', 'public sealed class E(IRecordService service) {}'],
    ['base.no-ef-in-application', 'backend/Modules/X/Application/Svc.cs', 'using Microsoft.EntityFrameworkCore;', 'using Starter.Domain;'],
    ['base.public-id-routes', 'src/A.cs', '[HttpGet("{id:int}")]', '[HttpGet("{id}")]'],
    ['base.no-empty-catch', 'src/A.cs', 'try { Run(); } catch (Exception) { }', 'try { Run(); } catch (IOException ex) { logger.LogWarning(ex, "x"); }'],
    ['base.no-commented-out-code', 'src/A.cs', '    // var x = Load();', '    // Loads the record before the transition.'],
    ['base.fe-http-via-api-service', 'web/features/a.ts', 'constructor(private http: HttpClient) {}', 'constructor(private api: ApiService) {}'],
    ['base.fe-no-hardcoded-paths', 'web/features/a.ts', "this.router.navigate(['/records']);", 'this.router.navigate([AppRoutes.records]);'],
    ['base.fe-ui-library-only', 'web/features/a.ts', "import { MatButton } from '@angular/material/button';", "import { DgaButton } from '@codelab/ng-dga';"],
    ['base.fe-raw-text-in-template', 'web/a.html', '<p>Submit request</p>', "<p>{{ 'records.submit' | translate }}</p>"],
    ['base.fe-logical-css', 'web/a.scss', '.a { margin-left: 4px; }', '.a { margin-inline-start: 4px; }'],
    ['base.fe-logical-css', 'web/a.scss', '.a { text-align: right; }', '.a { text-align: end; }'],
    ['base.fe-design-tokens', 'web/a.scss', '.a { color: #fff; }', '.a { color: var(--codelab-color-text); }'],
  ];
  for (const [ruleId, file, bad, good] of cases) {
    test(`${ruleId}: flags "${bad.split('\n')[0]}"`, () => {
      assert.ok(hits(file, bad).includes(ruleId), `expected ${ruleId} in ${JSON.stringify(hits(file, bad))}`);
    });
    test(`${ruleId}: accepts "${good.split('\n')[0]}"`, () => {
      assert.ok(!hits(file, good).includes(ruleId), `unexpected ${ruleId}`);
    });
  }

  test('excludes win over appliesTo (HttpClient allowed in core/http)', () => {
    assert.deepEqual(hits('web/core/http/api.service.ts', 'constructor(private http: HttpClient) {}'), []);
  });

  test('rules for one language do not fire on another', () => {
    assert.deepEqual(hits('docs/a.md', 'DateTime.Now and margin-left: 1px'), []);
  });
});

describe('moduleForPath', () => {
  let root;
  after(() => removeWorkspace(root));

  test('the deepest module folder owns a path; outside modules there is no owner', () => {
    root = createWorkspace();
    writeWorkspaceFile(root, 'src/Modules/Records/module.rules.yaml', moduleYaml('Records'));
    writeWorkspaceFile(root, 'src/Modules/Records/Child/module.rules.yaml', moduleYaml('Child'));
    const { compiled } = compileRepository(root);
    assert.equal(moduleForPath(compiled, 'src/Modules/Records/A.cs').module, 'Records');
    assert.equal(moduleForPath(compiled, 'src/Modules/Records/Child/B.cs').module, 'Child');
    assert.equal(moduleForPath(compiled, 'src/Modules/RecordsX/C.cs'), undefined);
    assert.equal(moduleForPath(compiled, 'src/Kernel/D.cs'), undefined);
  });
});
