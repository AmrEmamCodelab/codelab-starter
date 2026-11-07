import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { newTransition } from '../new-transition.mjs';
import { runCli, UsageError } from '../lib/cli.mjs';
import { ExitCode } from '../lib/constants.mjs';
import { ScaffoldRefusal } from '../lib/csharp-edit.mjs';
import { parseNotification, parseWording, transitionRequest, parseTransitionArguments } from '../lib/transition/arguments.mjs';
import {
  addClassMember,
  addListEntry,
  addToAllList,
  classSpan,
  enclosingClass,
  isSameOrParentNamespace,
  nextStatusValue,
} from '../lib/transition/csharp-source.mjs';
import { addBundleKeys, bundleHas } from '../lib/transition/i18n.mjs';
import { addModuleDocNotes } from '../lib/transition/module-doc.mjs';
import { actorCalls, addKitReference, usingLines } from '../lib/transition/tests.mjs';

const MODULE_DIR = 'backend/src/Modules/Demo';
const TESTS_DIR = 'backend/tests/Modules/Starter.Modules.Demo.Tests';
const RECORD_FILE = `${MODULE_DIR}/Domain/DemoRecord.cs`;
const WORKFLOW_FILE = `${MODULE_DIR}/Application/Workflow/DemoWorkflow.cs`;
const KEYS_FILE = `${MODULE_DIR}/Application/DemoPermissionKeys.cs`;
const NOTIFICATIONS_FILE = `${MODULE_DIR}/Application/DemoNotificationKeys.cs`;
const TEST_PROJECT = `${TESTS_DIR}/Starter.Modules.Demo.Tests.csproj`;
const EN = 'frontend/starter-web/src/assets/i18n/en.json';
const AR = 'frontend/starter-web/src/assets/i18n/ar.json';
const RULES_FILE = `${MODULE_DIR}/module.rules.yaml`;
const RULES = 'schemaVersion: 1\nmodule: Demo\nkind: dotnet\nrequired:\n  - id: demo.thin-endpoints\n    statement: Thin.\n    enforcedBy: [review]\n# the recipes\nrecipes: [docs/recipes/add-endpoint.md]\n';
/** A later flow change: a new action between existing statuses. */
const SECOND_CHANGE = Object.freeze(['--action', 'close', '--from', 'draft', '--to', 'closed', '--permission', 'demo.decide', '--side', 'staff',
  '--notify', 'none', '--label-en', 'Close', '--label-ar', 'إغلاق']);

const RECORD = `namespace Starter.Modules.Demo.Domain;

/// <summary>Statuses.</summary>
public static class DemoStatuses
{
    /// <summary>Initial.</summary>
    public const int Draft = 1;

    /// <summary>Filed.</summary>
    public const int Submitted = 2;

    /// <summary>Terminal.</summary>
    public const int Closed = 3;
}

/// <summary>Status keys.</summary>
public static class DemoStatusKeys
{
    /// <summary>Draft.</summary>
    public const string Draft = "draft";

    /// <summary>Submitted.</summary>
    public const string Submitted = "submitted";

    /// <summary>Closed.</summary>
    public const string Closed = "closed";
}

/// <summary>Actions.</summary>
public static class DemoActions
{
    /// <summary>Draft to Submitted.</summary>
    public const string Submit = "submit";

    /// <summary>Submitted to Closed.</summary>
    public const string Close = "close";
}
`;

const WORKFLOW = `using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Demo.Domain;

namespace Starter.Modules.Demo.Application.Workflow;

/// <summary>The demo table.</summary>
public static class DemoWorkflow
{
    /// <summary>The reason field.</summary>
    public const string ReasonField = "reason";

    /// <summary>Statuses.</summary>
    public static IReadOnlyList<WorkflowStatusDefinition> Statuses { get; } =
    [
        Status(DemoStatuses.Draft, DemoStatusKeys.Draft, initial: true, terminal: false),
        Status(DemoStatuses.Submitted, DemoStatusKeys.Submitted, initial: false, terminal: false),
        Status(DemoStatuses.Closed, DemoStatusKeys.Closed, initial: false, terminal: true),
    ];

    /// <summary>Rules.</summary>
    public static IReadOnlyList<TransitionRule> Rules { get; } =
    [
        Rule(DemoActions.Submit, new(DemoStatuses.Draft, DemoStatuses.Submitted)),
        Rule(DemoActions.Close, new(DemoStatuses.Submitted, DemoStatuses.Closed))
    ];

    /// <summary>The catalog.</summary>
    public static TransitionCatalog Catalog { get; } = new("demo-record", Statuses, Rules);

    /// <summary>Wording key.</summary>
    public static string WordingKey(string statusKey, WorkflowAudience audience) => statusKey + audience;

    /// <summary>Label key.</summary>
    public static string LabelKey(string actionKey) => actionKey;
}

/// <summary>The definition.</summary>
public sealed class DemoWorkflowDefinition : IRecordWorkflowDefinition
{
    /// <inheritdoc />
    public string RecordType => "demo-record";

    /// <inheritdoc />
    public TransitionCatalog Catalog => DemoWorkflow.Catalog;
}
`;

const MARKED_WORKFLOW = `using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Demo.Domain;

namespace Starter.Modules.Demo.Application.Workflow;

/// <summary>A table built inline, with markers and no helpers.</summary>
public sealed class DemoWorkflowDefinition : IRecordWorkflowDefinition
{
    /// <inheritdoc />
    public string RecordType => "demo-record";

    /// <inheritdoc />
    public TransitionCatalog Catalog { get; } = new(
        "demo-record",
        [
            new WorkflowStatusDefinition(DemoStatuses.Draft, "draft", true, false, new StatusWording("a", "b", "c")),
            new WorkflowStatusDefinition(DemoStatuses.Submitted, "submitted", false, false, new StatusWording("a", "b", "c")),
            new WorkflowStatusDefinition(DemoStatuses.Closed, "closed", false, true, new StatusWording("a", "b", "c"))
            // scaffold:workflow-statuses
        ],
        [
            new TransitionRule(DemoActions.Submit, DemoStatuses.Draft, DemoStatuses.Submitted, "demo.create", TransitionSide.Requester, ReasonRequirement.None, null, "l", NotificationDecision.None, true),
            // scaffold:workflow-rules
        ]);
}
`;

const PERMISSION_KEYS = `namespace Starter.Modules.Demo.Application;

/// <summary>Keys.</summary>
public static class DemoPermissionKeys
{
    /// <summary>File.</summary>
    public const string Create = "demo.create";

    /// <summary>Decide.</summary>
    public const string Decide = "demo.decide";

    // scaffold:permission-keys
}
`;

const NOTIFICATION_KEYS = `namespace Starter.Modules.Demo.Application;

/// <summary>Templates.</summary>
public static class DemoNotificationKeys
{
    /// <summary>Submitted.</summary>
    public const string Submitted = "demo.submitted";

    /// <summary>Every key.</summary>
    public static IReadOnlyList<string> All { get; } = [Submitted];
}
`;

/** A Windows relative path, built so that no source line holds a doubled backslash (the scrub sweep reads those as UNC). */
function windowsPath(...segments) {
  return segments.join(String.fromCharCode(BACKSLASH_CODE));
}

const BACKSLASH_CODE = 92;
const MODULE_REFERENCE = `<ProjectReference Include="${windowsPath('..', '..', '..', 'src', 'Modules', 'Demo', 'Starter.Modules.Demo.csproj')}" />`;
const KIT_REFERENCE = `<ProjectReference Include="${windowsPath('..', '..', 'Starter.Workflow.Testing', 'Starter.Workflow.Testing.csproj')}" />`;

const TEST_CSPROJ = `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    ${MODULE_REFERENCE}
  </ItemGroup>
</Project>
`;

const MODULE_DOC = '# Demo module\n\n## Purpose\n\nDemo.\n\n## Use\n\n- Permissions: `DemoPermissionKeys`.\n\n## Extend\n\n- More.\n\n## Rules\n\n## Gotchas\n';
const BUNDLE = `${JSON.stringify({ errors: { X: 'x' } }, null, 2)}\n`;

const STATUS_ARGS = ['--status', 'escalated', '--status-en', 'Escalated;Escalated to a manager;Escalated', '--status-ar', 'مُصعَّد', '--terminal'];
const RULE_ARGS = [
  '--action', 'escalate', '--from', 'submitted', '--to', 'escalated', '--permission', 'demo.decide', '--side', 'staff',
  '--reason', 'required', '--label-en', 'Escalate', '--label-ar', 'تصعيد', '--notify', 'requester,assignee', '--template', 'demo.escalated',
];

let root;

function write(relative, content) {
  fs.mkdirSync(path.dirname(path.join(root, relative)), { recursive: true });
  fs.writeFileSync(path.join(root, relative), content);
}

function read(relative) {
  return fs.readFileSync(path.join(root, relative), 'utf8');
}

function snapshot() {
  const files = {};
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else files[path.relative(root, full)] = fs.readFileSync(full, 'utf8');
    }
  };
  walk(root);
  return files;
}

function run(args) {
  return newTransition(['--module', 'Demo', ...args, '--skip-generate', '--root', root], {});
}

function exitOf(args) {
  return runCli('usage', () => newTransition(['--module', 'Demo', ...args, '--skip-generate', '--root', root], {}), { out: () => {}, err: () => {} });
}

function fixtureModule({ workflow = WORKFLOW, notifications = true } = {}) {
  write(`${MODULE_DIR}/Starter.Modules.Demo.csproj`, '<Project />\n');
  write(RECORD_FILE, RECORD);
  write(WORKFLOW_FILE, workflow);
  write(KEYS_FILE, PERMISSION_KEYS);
  if (notifications) write(NOTIFICATIONS_FILE, NOTIFICATION_KEYS);
  write(`${MODULE_DIR}/MODULE.md`, MODULE_DOC);
  write(TEST_PROJECT, TEST_CSPROJ);
  write(EN, BUNDLE);
  write(AR, BUNDLE);
}

beforeEach(() => {
  root = fs.mkdtempSync(path.join(os.tmpdir(), 'scaffold-transition-test-'));
});

afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

describe('new-transition: a status and a transition into it', () => {
  beforeEach(() => fixtureModule());

  it('adds the status constant with the next explicit value and its status key', () => {
    const lines = run([...STATUS_ARGS, ...RULE_ARGS]);

    assert.ok(lines.includes('note: status escalated = 4'));
    const record = read(RECORD_FILE);
    assert.match(record, /\/\/\/ <summary>Escalated \(terminal\)\.<\/summary>\n {4}public const int Escalated = 4;\n\}/);
    assert.match(record, /<summary><see cref="DemoStatuses\.Escalated"\/>\.<\/summary>\n {4}public const string Escalated = "escalated";/);
    assert.match(record, /public const string Escalate = "escalate";/);
  });

  it('adds the status entry and the rule entry with named arguments, using the workflow helpers', () => {
    run([...STATUS_ARGS, ...RULE_ARGS]);

    const workflow = read(WORKFLOW_FILE);
    assert.ok(workflow.includes([
      '        new WorkflowStatusDefinition(',
      '            Status: DemoStatuses.Escalated,',
      '            Key: DemoStatusKeys.Escalated,',
      '            IsInitial: false,',
      '            IsTerminal: true,',
      '            Wording: new StatusWording(',
      '                RequesterKey: WordingKey(DemoStatusKeys.Escalated, WorkflowAudience.Requester),',
      '                StaffKey: WordingKey(DemoStatusKeys.Escalated, WorkflowAudience.Staff),',
      '                AdminKey: WordingKey(DemoStatusKeys.Escalated, WorkflowAudience.Admin))),',
      '    ];',
    ].join('\n')));
    assert.ok(workflow.includes([
      '        Rule(DemoActions.Close, new(DemoStatuses.Submitted, DemoStatuses.Closed)),',
      '        new TransitionRule(',
      '            ActionKey: DemoActions.Escalate,',
      '            FromStatus: DemoStatuses.Submitted,',
      '            ToStatus: DemoStatuses.Escalated,',
      '            PermissionKey: DemoPermissionKeys.Decide,',
      '            Side: TransitionSide.Staff,',
      '            Reason: ReasonRequirement.Required,',
      '            ReasonFieldKey: ReasonField,',
      '            LabelKey: LabelKey(DemoActions.Escalate),',
      '            Notification: new NotificationDecision(TemplateKey: DemoNotificationKeys.Escalated, NotifyRequester: true, NotifyAssignee: true),',
      '            RequiresOwnership: false),',
      '    ];',
    ].join('\n')), 'the rule follows the last entry, which gained its missing trailing comma');
    assert.doesNotMatch(workflow, /using Starter\.Modules\.Demo\.Application;/, 'a parent namespace is never added as a using');
  });

  it('adds the template constant and lists it in the keys class All list', () => {
    run([...STATUS_ARGS, ...RULE_ARGS]);

    const keys = read(NOTIFICATIONS_FILE);
    assert.match(keys, /All \{ get; \} = \[Submitted, Escalated\];/);
    assert.match(keys, /public const string Escalated = "demo\.escalated";/);
  });

  it('writes the ar and en wording and label keys', () => {
    run([...STATUS_ARGS, ...RULE_ARGS]);

    const en = JSON.parse(read(EN));
    const ar = JSON.parse(read(AR));
    assert.deepEqual(en.demo.status.escalated, { requester: 'Escalated', staff: 'Escalated to a manager', admin: 'Escalated' });
    assert.deepEqual(ar.demo.status.escalated, { requester: 'مُصعَّد', staff: 'مُصعَّد', admin: 'مُصعَّد' });
    assert.equal(en.demo.action.escalate, 'Escalate');
    assert.equal(ar.demo.action.escalate, 'تصعيد');
    assert.equal(en.errors.X, 'x');
  });

  it('writes TransitionFixture stubs, references the kit and notes the change in MODULE.md', () => {
    const lines = run([...STATUS_ARGS, ...RULE_ARGS]);

    const status = read(`${TESTS_DIR}/Workflow/EscalatedStatusTests.cs`);
    assert.match(status, /Assert\.True\(status\.IsTerminal\);/);
    assert.match(status, /public void Escalated_offers_no_action\(\) =>/);
    assert.match(status, /using Starter\.Workflow\.Testing;/);
    const rule = read(`${TESTS_DIR}/Workflow/EscalateFromSubmittedTransitionTests.cs`);
    assert.match(rule, /namespace Starter\.Modules\.Demo\.Tests\.Workflow;/);
    assert.match(rule, /new DemoWorkflowDefinition\(\)\.Catalog/);
    assert.match(rule, /new DemoPermissionCatalogSource\(\)/);
    assert.match(rule, /\.Do\(DemoActions\.Escalate, "A reason given in the test\."\)\.ExpectTerminal\(\)\.ExpectStatus\(DemoStatuses\.Escalated\);/);
    assert.match(rule, /public void Escalate_needs_a_reason\(\) =>/);
    assert.doesNotMatch(rule, /AsOwner|AsNonOwner/);
    assert.match(rule, /using Starter\.Modules\.Demo\.Application;\nusing Starter\.Modules\.Demo\.Application\.Workflow;\nusing Starter\.Modules\.Demo\.Domain;\nusing Starter\.Workflow\.Testing;/);
    assert.doesNotMatch(rule, /\{\{/);
    assert.ok(read(TEST_PROJECT).includes(KIT_REFERENCE));
    const doc = read(`${MODULE_DIR}/MODULE.md`);
    assert.ok(doc.indexOf('- Workflow status `escalated` (4, terminal)') < doc.indexOf('## Extend'));
    assert.match(doc, /- Workflow transition `escalate`: `submitted` to `escalated` \(staff, `demo\.decide`, reason required in `reason`, notifies requester and assignee with `demo\.escalated`; label `demo\.action\.escalate`\)/);
    assert.ok(lines.includes('next: dotnet test backend/tests/Modules/Starter.Modules.Demo.Tests --filter FullyQualifiedName~Workflow') || lines.some((line) => line.endsWith('test backend/tests/Modules/Starter.Modules.Demo.Tests --filter FullyQualifiedName~Workflow')));
  });

  it('is idempotent: the same arguments again change nothing', () => {
    run([...STATUS_ARGS, ...RULE_ARGS]);
    const before = snapshot();

    const lines = run([...STATUS_ARGS, ...RULE_ARGS]);

    assert.deepEqual(snapshot(), before);
    assert.match(lines[0], /^unchanged /);
    assert.ok(lines.some((line) => line.includes('unchanged transition escalate from submitted')));
  });

  it('a dry run lists the files and writes nothing', () => {
    const before = snapshot();

    const lines = run([...STATUS_ARGS, ...RULE_ARGS, '--dry-run']);

    assert.deepEqual(snapshot(), before);
    assert.ok(lines.includes(`would change ${RECORD_FILE}`));
    assert.ok(lines.includes(`would create ${TESTS_DIR}/Workflow/EscalatedStatusTests.cs`));
    assert.ok(lines.includes('note: dry run: nothing was written'));
    assert.ok(lines.includes('next: node tools/rules-engine/compile.mjs'), 'a dry run names the generated-docs refresh');
  });

  it('adds the workflow-in-the-catalog rule and the new-transition recipe to the module rules, once', () => {
    write(RULES_FILE, RULES);
    run([...STATUS_ARGS, ...RULE_ARGS]);

    const rules = read(RULES_FILE);
    assert.match(rules, /required:\n {2}- id: demo\.thin-endpoints\n {4}statement: Thin\.\n {4}enforcedBy: \[review\]\n {2}- id: demo\.workflow-in-the-catalog\n {4}statement: "The statuses, [^\n]*DemoWorkflowDefinition[^\n]*"\n {4}enforcedBy: \[review\]\n# the recipes\nrecipes: \[docs\/recipes\/add-endpoint\.md, \.claude\/skills\/new-transition\/SKILL\.md\]\n$/);
    run(SECOND_CHANGE);
    assert.equal(read(RULES_FILE), rules, 'a second change keeps the rules as they are');
  });

  it('keeps a hand-named workflow rule and notes a module without rules', () => {
    write(RULES_FILE, RULES.replace('demo.thin-endpoints', 'demo.workflow-in-the-catalog'));
    run([...STATUS_ARGS, ...RULE_ARGS]);
    assert.equal(read(RULES_FILE).split('workflow-in-the-catalog').length, 2);

    fs.rmSync(path.join(root, RULES_FILE));
    const lines = run(SECOND_CHANGE);
    assert.ok(lines.some((line) => line.startsWith(`note: no ${RULES_FILE}: add the workflow-in-the-catalog rule`)));
  });
});

describe('new-transition: a transition between existing statuses', () => {
  beforeEach(() => fixtureModule());

  it('adds one rule and one test class per from-status, owner-only with an optional reason', () => {
    run(['--action', 'withdraw', '--from', 'draft,submitted', '--to', 'closed', '--permission', 'demo.create', '--side', 'requester',
      '--owner', '--reason', 'optional', '--label-en', 'Withdraw', '--label-ar', 'سحب', '--notify', 'none']);

    const workflow = read(WORKFLOW_FILE);
    assert.equal(workflow.match(/ActionKey: DemoActions\.Withdraw,/g).length, 2);
    assert.match(workflow, /FromStatus: DemoStatuses\.Draft,/);
    assert.match(workflow, /Notification: NotificationDecision\.None,\n {12}RequiresOwnership: true\),/);
    const draft = read(`${TESTS_DIR}/Workflow/WithdrawFromDraftTransitionTests.cs`);
    assert.match(draft, /\.WithPermissions\(DemoPermissionKeys\.Create\)\.AsOwner\(\)/);
    assert.match(draft, /public void Withdraw_is_refused_to_a_non_owner\(\) =>/);
    assert.doesNotMatch(draft, /needs_a_reason/);
    assert.ok(fs.existsSync(path.join(root, TESTS_DIR, 'Workflow', 'WithdrawFromSubmittedTransitionTests.cs')));
    assert.doesNotMatch(read(NOTIFICATIONS_FILE), /Withdraw/);
  });

  it('a system-side rule writes tests that act from the system side, which the engine accepts', () => {
    run(['--action', 'auto-close', '--from', 'draft', '--to', 'closed', '--permission', 'demo.decide', '--side', 'system',
      '--label-en', 'Close automatically', '--label-ar', 'إغلاق تلقائي', '--notify', 'none']);

    const test = read(`${TESTS_DIR}/Workflow/AutoCloseFromDraftTransitionTests.cs`);
    assert.match(test, /\.WithPermissions\(DemoPermissionKeys\.Decide\)\.OnSide\(TransitionSide\.System\)\n\s+\.Do\(DemoActions\.AutoClose\)/);
    assert.match(test, /\.WithPermissions\(\)\.OnSide\(TransitionSide\.System\)\n\s+\.Do\(DemoActions\.AutoClose\)\.ExpectRefused\(WorkflowRefusal\.Forbidden\)/);
    assert.match(test, /isOwner: false, TransitionSide\.System\)/);
  });

  it('a second from-status for an existing action reuses its constant and its label', () => {
    run(['--action', 'close', '--from', 'draft', '--to', 'closed', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none',
      '--label-en', 'Close', '--label-ar', 'إغلاق']);
    const before = read(RECORD_FILE);

    const lines = run(['--action', 'close', '--from', 'draft', '--to', 'closed', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none']);

    assert.equal(read(RECORD_FILE), before);
    assert.equal(before.match(/const string Close = "close";/g).length, 1);
    assert.ok(lines.some((line) => line.includes('unchanged transition close from draft')));
  });

  it('a status-only run warns that nothing reaches the status yet', () => {
    const lines = run(['--status', 'on-hold', '--status-en', 'On hold', '--status-ar', 'معلّق']);

    assert.ok(lines.some((line) => line.includes('no transition reaches on-hold yet')));
    assert.match(read(RECORD_FILE), /public const int OnHold = 4;/);
    assert.match(read(`${TESTS_DIR}/Workflow/OnHoldStatusTests.cs`), /Assert\.False\(status\.IsTerminal\);/);
    assert.doesNotMatch(read(`${TESTS_DIR}/Workflow/OnHoldStatusTests.cs`), /Starter\.Workflow\.Testing/);
  });

  it('runs no generator with --no-gen and prints the docs command instead', () => {
    const lines = newTransition(['--module', 'Demo', '--status', 'on-hold', '--status-en', 'On hold', '--status-ar', 'معلّق', '--no-gen', '--root', root], { CODELAB_DOTNET: 'C:/sdk/dotnet.exe' });

    assert.ok(lines.includes('next: C:/sdk/dotnet.exe run --project backend/tools/Starter.DocsGen/Starter.DocsGen.csproj -- --out docs/generated'));
    assert.ok(lines.includes('next: C:/sdk/dotnet.exe test backend/tests/Modules/Starter.Modules.Demo.Tests --filter FullyQualifiedName~Workflow'));
  });
});

describe('new-transition: a marked workflow without helpers or keys classes', () => {
  beforeEach(() => fixtureModule({ workflow: MARKED_WORKFLOW, notifications: false }));

  it('inserts above the markers with literal keys and fixes the missing comma', () => {
    run([...STATUS_ARGS, ...RULE_ARGS, '--i18n-prefix', 'demoRecords']);

    const workflow = read(WORKFLOW_FILE);
    assert.ok(workflow.includes('new StatusWording("a", "b", "c")),\n            new WorkflowStatusDefinition(\n                Status: DemoStatuses.Escalated,\n                Key: DemoStatusKeys.Escalated,'));
    assert.match(workflow, /RequesterKey: "demoRecords\.status\.escalated\.requester",/);
    assert.match(workflow, /LabelKey: "demoRecords\.action\.escalate",/);
    assert.match(workflow, /TemplateKey: "demo\.escalated"/);
    assert.match(workflow, /ReasonFieldKey: "reason",/);
    assert.match(workflow, / {12}RequiresOwnership: false\),\n {12}\/\/ scaffold:workflow-rules/);
    assert.equal(JSON.parse(read(EN)).demoRecords.action.escalate, 'Escalate');
  });
});

describe('new-transition: refusals change nothing', () => {
  beforeEach(() => fixtureModule());

  const cases = [
    ['an unknown module', ['--module', 'Missing', '--status', 'x', '--status-en', 'X', '--status-ar', 'س']],
    ['an undeclared from-status', ['--module', 'Demo', '--action', 'reopen', '--from', 'archived', '--to', 'draft', '--permission', 'demo.create', '--side', 'requester', '--notify', 'none', '--label-en', 'R', '--label-ar', 'ر']],
    ['a permission the module does not declare', ['--module', 'Demo', '--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.reopen', '--side', 'staff', '--notify', 'none', '--label-en', 'R', '--label-ar', 'ر']],
    ['a new label without its Arabic text', ['--module', 'Demo', '--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none', '--label-en', 'Reopen']],
    ['a constant name that holds another value', ['--module', 'Demo', '--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.create', '--side', 'requester', '--notify', 'requester', '--template', 'other.submitted', '--label-en', 'R', '--label-ar', 'ر']],
  ];
  for (const [name, argv] of cases) {
    it(`refuses ${name}`, () => {
      const before = snapshot();

      const code = runCli('usage', () => newTransition([...argv, '--skip-generate', '--root', root], {}), { out: () => {}, err: () => {} });

      assert.equal(code, ExitCode.refused);
      assert.deepEqual(snapshot(), before);
    });
  }

  it('refuses a module without a workflow', () => {
    fs.rmSync(path.join(root, WORKFLOW_FILE));
    const before = snapshot();

    assert.throws(() => run(STATUS_ARGS), ScaffoldRefusal);
    assert.deepEqual(snapshot(), before);
  });

  it('refuses when the test project cannot take the kit reference', () => {
    write(TEST_PROJECT, '<Project />\n');
    const before = snapshot();

    assert.throws(() => run([...STATUS_ARGS, ...RULE_ARGS]), /test project/);
    assert.deepEqual(snapshot(), before);
  });
});

describe('new-transition: usage errors (exit 2)', () => {
  beforeEach(() => fixtureModule());

  const cases = [
    ['neither a status nor an action', []],
    ['a transition out of the new terminal status', [...STATUS_ARGS, '--action', 'reopen', '--from', 'escalated', '--to', 'draft', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none']],
    ['an action without a notification decision', ['--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.decide', '--side', 'staff']],
    ['a template with --notify none', ['--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none', '--template', 'demo.x']],
    ['two wordings instead of one or three', ['--status', 'x', '--status-en', 'A;B', '--status-ar', 'س']],
    ['an unknown side', ['--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.decide', '--side', 'robot', '--notify', 'none']],
    ['a reason field without a reason', ['--action', 'reopen', '--from', 'closed', '--to', 'draft', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none', '--reason-field', 'why']],
    ['--terminal without --status', ['--terminal']],
    ['a from list naming the to-status', ['--action', 'reopen', '--from', 'draft', '--to', 'draft', '--permission', 'demo.decide', '--side', 'staff', '--notify', 'none']],
    ['an unknown flag', ['--colour', 'red']],
  ];
  for (const [name, args] of cases) {
    it(`rejects ${name}`, () => {
      const before = snapshot();
      assert.equal(exitOf(args), ExitCode.usage);
      assert.deepEqual(snapshot(), before);
    });
  }

  it('prints the usage for --help', () => {
    assert.match(newTransition(['--help'], {})[0], /^Usage: node tools\/scaffold\/new-transition\.mjs --module <Module>/);
  });
});

describe('new-transition: argument helpers', () => {
  it('parses one or three wordings', () => {
    assert.deepEqual(parseWording('Open', '--status-en'), ['Open', 'Open', 'Open']);
    assert.deepEqual(parseWording(' A ; B ;C', '--status-en'), ['A', 'B', 'C']);
    assert.throws(() => parseWording('A;;C', '--status-en'), UsageError);
    assert.throws(() => parseWording(undefined, '--status-en'), UsageError);
  });

  it('parses the notification decision', () => {
    assert.deepEqual(parseNotification('none', undefined), { none: true });
    assert.deepEqual(parseNotification('assignee', 'a.b'), { none: false, requester: false, assignee: true, templateKey: 'a.b' });
    assert.throws(() => parseNotification('requester', undefined), UsageError);
    assert.throws(() => parseNotification('requester', 'Not A Key'), UsageError);
    assert.throws(() => parseNotification('everyone', 'a.b'), UsageError);
  });

  it('defaults the reason field and the i18n prefix', () => {
    const request = transitionRequest(parseTransitionArguments([
      '--module', 'ServiceRequests', '--action', 'return', '--from', 'under-review', '--to', 'returned', '--permission', 'service-requests.decide',
      '--side', 'staff', '--reason', 'required', '--notify', 'none',
    ]));

    assert.equal(request.i18nPrefix, 'serviceRequests');
    assert.equal(request.rule.reasonField, 'reason');
    assert.equal(request.rule.actionConst, 'Return');
    assert.equal(request.generate, true);
  });
});

describe('new-transition: C# text helpers', () => {
  const text = 'namespace N;\n\npublic static class A\n{\n    public const int X = 1;\n\n    public static class Inner\n    {\n    }\n}\n\npublic static class B\n{\n}\n';

  it('finds the closing brace of a top-level class past nested ones', () => {
    const span = classSpan(text, 'A');
    assert.equal(text.slice(span.close, span.close + 2), '}\n');
    assert.ok(span.close > text.indexOf('Inner'));
    assert.equal(classSpan(text, 'Missing'), null);
  });

  it('adds a member at the end of the right class, without a blank line in an empty class', () => {
    const withA = addClassMember(text, 'A', ['public const int Y = 2;']);
    assert.match(withA, /    }\n\n    public const int Y = 2;\n}\n\npublic static class B/);
    const withB = addClassMember(text, 'B', ['public const int Z = 3;']);
    assert.match(withB, /public static class B\n\{\n    public const int Z = 3;\n\}\n$/);
  });

  it('computes the next status value from the highest constant', () => {
    assert.equal(nextStatusValue([{ value: '1' }, { value: '7' }, { value: '3' }]), 8);
    assert.equal(nextStatusValue([]), 1);
  });

  it('appends to a property list and keeps an existing trailing comma', () => {
    const list = 'class W\n{\n    public static IReadOnlyList<int> Rules { get; } =\n    [\n        1,\n    ];\n}\n';
    assert.equal(addListEntry(list, 'rules', ['2,']), 'class W\n{\n    public static IReadOnlyList<int> Rules { get; } =\n    [\n        1,\n        2,\n    ];\n}\n');
    assert.equal(addListEntry(list, 'statuses', ['2,']), null);
  });

  it('appends to an All list once', () => {
    const keys = 'public static class K\n{\n    public static IReadOnlyList<string> All { get; } =\n        [A, B];\n}\n';
    const once = addToAllList(keys, 'K', 'C');
    assert.match(once, /\[A, B, C\];/);
    assert.equal(addToAllList(once, 'K', 'C'), once);
    assert.equal(addToAllList('public static class E\n{\n    public static IReadOnlyList<string> All { get; } = [];\n}\n', 'E', 'C').includes('[C]'), true);
    assert.equal(addToAllList('public static class N\n{\n}\n', 'N', 'C'), 'public static class N\n{\n}\n');
  });

  it('tells the enclosing class and parent namespaces', () => {
    assert.equal(enclosingClass(text, text.indexOf('X = 1')), 'A');
    assert.equal(enclosingClass(text, text.indexOf('class B') + 9), 'B');
    assert.equal(enclosingClass(text, text.length - 1), null);
    assert.ok(isSameOrParentNamespace('Starter.Modules.Demo', 'Starter.Modules.Demo.Application'));
    assert.ok(isSameOrParentNamespace('Starter.Modules.Demo.Application', 'Starter.Modules.Demo.Application'));
    assert.ok(!isSameOrParentNamespace('Starter.Modules.Demo.App', 'Starter.Modules.Demo.Application'));
  });

  it('keeps CRLF line endings', () => {
    const crlf = 'class A\r\n{\r\n    public const int X = 1;\r\n}\r\n';
    assert.equal(addClassMember(crlf, 'A', ['public const int Y = 2;']), 'class A\r\n{\r\n    public const int X = 1;\r\n\r\n    public const int Y = 2;\r\n}\r\n');
  });
});

describe('new-transition: bundles, docs and tests helpers', () => {
  it('adds nested keys, keeps existing texts and refuses a text in the way', () => {
    const bundle = '{\n  "demo": {\n    "action": {\n      "go": "Go now"\n    }\n  }\n}\n';
    const result = addBundleKeys(bundle, [{ key: 'demo.action.go', value: 'Go' }, { key: 'demo.status.open.staff', value: 'Open' }], 'en.json');

    assert.deepEqual(result.added, ['demo.status.open.staff']);
    assert.deepEqual(result.kept, ['demo.action.go']);
    assert.equal(JSON.parse(result.text).demo.action.go, 'Go now');
    assert.ok(result.text.endsWith('}\n'));
    assert.ok(bundleHas(result.text, 'demo.status.open.staff'));
    assert.ok(!bundleHas(result.text, 'demo.status'));
    assert.ok(!bundleHas(null, 'demo'));
    assert.throws(() => addBundleKeys(bundle, [{ key: 'demo.action.go.deeper', value: 'x' }], 'en.json'), ScaffoldRefusal);
    assert.throws(() => addBundleKeys('{ nope', [], 'en.json'), ScaffoldRefusal);
  });

  it('adds MODULE.md notes under the marker, else at the end of Use, else at the end', () => {
    const marked = '## Use\n\n<!-- scaffold:workflow -->\n\n## Extend\n';
    assert.equal(addModuleDocNotes(marked, ['- one']), '## Use\n\n- one\n<!-- scaffold:workflow -->\n\n## Extend\n');
    const use = '## Use\n\n- a\n\n## Extend\n';
    assert.equal(addModuleDocNotes(use, ['- one']), '## Use\n\n- a\n- one\n\n## Extend\n');
    assert.equal(addModuleDocNotes(addModuleDocNotes(use, ['- one']), ['- one']), '## Use\n\n- a\n- one\n\n## Extend\n');
    assert.equal(addModuleDocNotes('# Doc\n', ['- one']), '# Doc\n\n- one\n');
  });

  it('makes the fixture act as the rule actor: OnSide only when Do would pick another side', () => {
    assert.equal(actorCalls({ owner: true, side: 'requester' }), '.AsOwner()');
    assert.equal(actorCalls({ owner: false, side: 'staff' }), '');
    assert.equal(actorCalls({ owner: false, side: 'system' }), '.OnSide(TransitionSide.System)');
    assert.equal(actorCalls({ owner: true, side: 'system' }), '.AsOwner().OnSide(TransitionSide.System)');
    assert.equal(actorCalls({ owner: false, side: 'requester' }), '.OnSide(TransitionSide.Requester)');
    assert.equal(actorCalls({ owner: true, side: 'staff' }), '.AsOwner().OnSide(TransitionSide.Staff)');
  });

  it('adds the kit reference once and builds sorted usings without parents of the test namespace', () => {
    const once = addKitReference(TEST_CSPROJ);
    assert.ok(once.includes(`${MODULE_REFERENCE}\n    ${KIT_REFERENCE}`));
    assert.equal(addKitReference(once), once);
    assert.equal(addKitReference('<Project />'), null);
    assert.equal(
      usingLines(['Starter.Modules.Demo.Domain', 'Starter.Modules.Demo', 'Starter.Domain.Workflow', 'Starter.Modules.Demo.Application', 'Starter.Modules.Demo.Domain'], 'Starter.Modules.Demo.Tests.Workflow'),
      'using Starter.Modules.Demo.Application;\nusing Starter.Modules.Demo.Domain;\n',
    );
  });
});
