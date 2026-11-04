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

describe('generated-code and test-project opt-outs decided by the path', () => {
  let root;
  let compiled;
  before(() => {
    root = createWorkspace();
    compiled = compileRepository(root).compiled;
  });
  after(() => removeWorkspace(root));

  const hits = (relativePath, content) => evaluateFile(compiled, relativePath, content).map((result) => result.rule.id);

  for (const name of ['Sample.g.cs', 'Sample.g.i.cs', 'Sample.generated.cs', 'Sample.designer.cs', 'Sample.Designer.cs']) {
    test(`a hand-written ${name} is refused`, () => {
      assert.ok(hits(`src/${name}`, 'public class Sample {}').includes('base.no-generated-file-name'));
    });
  }

  test('a plain *.cs name is accepted', () => {
    assert.ok(!hits('src/Sample.cs', 'public class Sample {}').includes('base.no-generated-file-name'));
  });

  test('a *.Tests project may declare IsTestProject', () => {
    assert.ok(!hits('packages/dotnet/Codelab.Security.Tests/Codelab.Security.Tests.csproj', '<IsTestProject>true</IsTestProject>').includes('base.no-test-project-opt-in'));
    assert.ok(!hits('tests/Starter.Records.Tests/Starter.Records.Tests.csproj', '<IsTestProject>true</IsTestProject>').includes('base.no-test-project-opt-in'));
  });

  test('a Directory.Build.props outside a *.Tests folder may not declare it for everything below', () => {
    assert.ok(hits('src/Directory.Build.props', '<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>').includes('base.no-test-project-opt-in'));
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
    ['base.no-thread-sleep', 'src/A.cs', 'Thread.Sleep(500);', 'await Task.Delay(delay, timeProvider, cancellationToken);'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<NoWarn>$(NoWarn);CLB1004</NoWarn>', '<NoWarn>$(NoWarn);CS1591</NoWarn>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<CodelabEnableBuildAnalyzers>false</CodelabEnableBuildAnalyzers>', '<CodelabEnableBuildAnalyzers>true</CodelabEnableBuildAnalyzers>'],
    ['base.no-analyzer-opt-out', 'src/A.cs', '#pragma warning disable CLB1001', '#pragma warning disable CA1822'],
    ['base.no-analyzer-opt-out', 'src/.editorconfig', 'dotnet_diagnostic.CLB1002.severity = none', 'dotnet_diagnostic.CLB1002.severity = error'],
    ['base.no-analyzer-opt-out', 'src/A.cs', '#pragma warning disable\nclass A {}', '#pragma warning restore CA1822\nclass A {}'],
    ['base.no-analyzer-opt-out', 'src/A.cs', 'class A {}\n#pragma warning disable // legacy', '#pragma warning disable CA1822 // reason'],
    ['base.no-analyzer-opt-out', 'src/A.cs', 'class A {}\n#pragma warning disable', 'class A {}\n#pragma warning restore'],
    ['base.no-analyzer-opt-out', 'src/.editorconfig', 'dotnet_analyzer_diagnostic.category-Maintainability.severity = none', 'dotnet_analyzer_diagnostic.category-Maintainability.severity = error'],
    ['base.no-analyzer-opt-out', 'src/.editorconfig', 'dotnet_analyzer_diagnostic.severity = silent', 'dotnet_analyzer_diagnostic.severity = warning'],
    ['base.no-analyzer-opt-out', 'src/.editorconfig', '[*.cs]\ngenerated_code = true', '[*.cs]\ngenerated_code = false'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<TreatWarningsAsErrors>false</TreatWarningsAsErrors>', '<TreatWarningsAsErrors>true</TreatWarningsAsErrors>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<WarningsNotAsErrors>$(WarningsNotAsErrors);CLB1001</WarningsNotAsErrors>', '<WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901</WarningsNotAsErrors>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>', '<RunAnalyzersDuringBuild>true</RunAnalyzersDuringBuild>'],
    // Regression: the SDK enables a gate only for exactly 'true', so every other value and spelling turns it off.
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<CodelabEnableBuildAnalyzers>False</CodelabEnableBuildAnalyzers>', '<CodelabEnableBuildAnalyzers>True</CodelabEnableBuildAnalyzers>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<CodelabEnableBannedApis>off</CodelabEnableBannedApis>', '<CodelabEnableBannedApis Condition="\'$(CodelabEnableBannedApis)\' == \'\'">true</CodelabEnableBannedApis>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<CodelabEnableBuildAnalyzers></CodelabEnableBuildAnalyzers>', '<Analyzer Condition="\'$(CodelabEnableBuildAnalyzers)\' == \'true\'" />'],
    ['base.no-analyzer-opt-out', 'src/A.props', '<codelabenablebannedapis>0</codelabenablebannedapis>', '<PropertyGroup />'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<EnableNETAnalyzers>false</EnableNETAnalyzers>', '<EnableNETAnalyzers>true</EnableNETAnalyzers>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<AnalysisLevel>none</AnalysisLevel>', '<AnalysisLevel>latest-recommended</AnalysisLevel>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<AnalysisLevel>latest-None</AnalysisLevel>', '<AnalysisLevel>latest-all</AnalysisLevel>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<AnalysisMode>None</AnalysisMode>', '<AnalysisMode>Recommended</AnalysisMode>'],
    ['base.no-analyzer-opt-out', 'src/A.csproj', '<AnalysisModeMaintainability>none</AnalysisModeMaintainability>', '<AnalysisModeMaintainability>all</AnalysisModeMaintainability>'],
    ['base.no-fixture-gate-opt-out', 'src/A.csproj', '<CodelabFixtureGateOptOut>true</CodelabFixtureGateOptOut>', '<IsPackable>false</IsPackable>'],
    ['base.no-fixture-gate-opt-out', 'Directory.Build.rsp', '-p:CodelabFixtureGateOptOut=true', '-m'],
    ['base.no-auto-generated-marker', 'src/A.cs', '// <auto-generated/>\nclass A {}', '// Generated keys are read from configuration.\nclass A {}'],
    ['base.no-auto-generated-marker', 'src/A.cs', 'using X;\n/* <auto-generated> */\nclass A {}', 'var source = "// <auto-generated/>";'],
    ['base.no-test-project-opt-in', 'src/Starter.Records/Starter.Records.csproj', '<IsTestProject>true</IsTestProject>', '<IsPackable>false</IsPackable>'],
    // Regression: Microsoft.NET.Test.Sdk sets IsTestProject through its own props; an AssemblyName can end in .Tests.
    ['base.no-test-project-opt-in', 'src/Starter.Records/Starter.Records.csproj', '<PackageReference Include="Microsoft.NET.Test.Sdk" />', '<PackageReference Include="Microsoft.Extensions.Http" />'],
    ['base.no-test-project-opt-in', 'src/Starter.Records/Starter.Records.csproj', '<PackageReference Include="xunit.v3" />', '<PackageReference Include="Serilog" />'],
    ['base.no-test-project-opt-in', 'src/Starter.Records/Starter.Records.csproj', '<AssemblyName>Starter.Records.Tests</AssemblyName>', '<AssemblyName>Starter.Records</AssemblyName>'],
    ['base.no-test-project-opt-in', 'src/Starter.Records/Starter.Records.csproj', '<AssemblyName>Starter.Records.Test</AssemblyName>', '<AssemblyName>Starter.Testing</AssemblyName>'],
    ['base.no-accept-any-certificate', 'src/A.cs', 'h.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;', 'h.ServerCertificateCustomValidationCallback = policy.Validate;'],
    ['base.no-accept-any-certificate', 'src/A.cs', 'ssl.RemoteCertificateValidationCallback = (_, _, _, _) => true;', 'ssl.RemoteCertificateValidationCallback = ServerCertificatePolicy.CreateCallback(options, timeProvider);'],
    ['base.no-accept-any-certificate', 'src/A.cs', 'ssl.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;', 'ssl.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => validator.Validate(cert, chain, errors);'],
    ['base.no-accept-any-certificate', 'src/A.cs', 'h.ServerCertificateCustomValidationCallback = delegate (HttpRequestMessage m, X509Certificate2? c, X509Chain? ch, SslPolicyErrors e) { return true; };', 'h.ServerCertificateCustomValidationCallback = null;'],
    ['base.no-threshold-override', '.editorconfig', '[*.cs]\ncodelab_method_lines = 400', '[*.cs]\nindent_size = 4'],
    ['base.no-threshold-override', 'tools/x.globalconfig', 'is_global = true\ncodelab_parameters = 12', 'is_global = true'],
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

  test('only the engine outputs, by path, may carry codelab_* keys; a forged GENERATED marker proves nothing', () => {
    const generated = '# GENERATED by tools/rules-engine from rules/base.rules.yaml. Do not edit.\n';
    assert.deepEqual(hits('rules/codelab.globalconfig', `${generated}is_global = true\ncodelab_method_lines = 40`), []);
    assert.deepEqual(hits('src/Modules/X/.editorconfig', '[*.cs]\ncodelab_method_lines = 300'), ['base.no-threshold-override']);
    // Regression: a hand-written .globalconfig with the marker raised the thresholds of a test project unnoticed.
    const forged = `# GENERATED by tools/rules-engine (forged)\nis_global = true\nglobal_level = 100\ncodelab_method_lines = 400`;
    assert.deepEqual(
      hits('packages/dotnet/Codelab.Errors.Tests/.globalconfig', forged).sort(),
      ['base.no-hand-written-globalconfig', 'base.no-threshold-override'],
    );
    assert.deepEqual(hits('src/Modules/X/.editorconfig', `${generated}[*.cs]\ncodelab_magic_numbers_enforced = false`), ['base.no-threshold-override']);
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

describe('engine outputs are exempt by path, never by content', () => {
  let root;
  after(() => removeWorkspace(root));

  test("a module's own .editorconfig may set codelab_* keys; any other .editorconfig inside it may not", () => {
    root = createWorkspace();
    writeWorkspaceFile(root, 'src/Modules/Records/module.rules.yaml', moduleYaml('Records'));
    const { compiled } = compileRepository(root);
    const hits = (relativePath, content) => evaluateFile(compiled, relativePath, content).map((result) => result.rule.id);
    const keys = '[*.cs]\ncodelab_method_lines = 30';

    assert.deepEqual(hits('src/Modules/Records/.editorconfig', keys), []);
    assert.deepEqual(hits('src/Modules/Records/Sub/.editorconfig', keys), ['base.no-threshold-override']);
    assert.deepEqual(hits('src/Modules/Records.Tests/.editorconfig', keys), ['base.no-threshold-override']);
  });
});
