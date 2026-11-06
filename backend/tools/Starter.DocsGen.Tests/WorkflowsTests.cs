using Starter.DocsGen.Collection;
using Starter.DocsGen.Output;
using Starter.DocsGen.Rendering;
using Starter.DocsGen.Tests.Fixtures;
using Starter.Domain.Workflow;

namespace Starter.DocsGen.Tests;

public sealed class MermaidStateDiagramTests
{
    private const string Golden = """
        ```mermaid
        stateDiagram-v2
            state "draft (1)" as s1
            state "filed (2)" as s2
            state "closed (3)" as s3
            state "dropped (4)" as s4
            [*] --> s1
            s1 --> s2 : file (fixture-records.file, reason none)
            s1 --> s4 : drop (fixture-records.file, reason optional)
            s2 --> s3 : close (fixture-records.decide, reason required)
            s2 --> s4 : drop (fixture-records.file, reason optional)
            s3 --> [*]
            s4 --> [*]
            classDef terminal font-weight:bold,stroke-width:3px
            class s3,s4 terminal
        ```

        """;

    [Fact]
    public void The_fixture_catalog_renders_the_golden_diagram() =>
        Assert.Equal(Golden.ReplaceLineEndings("\n"), MermaidStateDiagram.Render(FixtureRecordWorkflow.Catalog));

    [Fact]
    public void The_order_statuses_are_declared_in_does_not_change_the_diagram()
    {
        var catalog = FixtureRecordWorkflow.Catalog;
        var shuffled = new TransitionCatalog(catalog.RecordType, [.. catalog.Statuses.Reverse()], catalog.Rules);

        Assert.Equal(MermaidStateDiagram.Render(catalog), MermaidStateDiagram.Render(shuffled));
    }

    [Fact]
    public void A_catalog_without_terminal_statuses_has_no_terminal_class()
    {
        var open = new TransitionCatalog("open-loop", [Status(1, "open", isInitial: true, isTerminal: false)], []);

        var diagram = MermaidStateDiagram.Render(open);

        Assert.Contains("[*] --> s1", diagram, StringComparison.Ordinal);
        Assert.DoesNotContain("classDef", diagram, StringComparison.Ordinal);
        Assert.DoesNotContain("--> [*]", diagram, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, "s3")]
    [InlineData(0, "s0")]
    [InlineData(-2, "sn2")]
    public void State_ids_come_from_the_value(int status, string id) => Assert.Equal(id, MermaidStateDiagram.StateId(status));

    [Fact]
    public void Labels_drop_characters_that_end_a_mermaid_statement() =>
        Assert.Equal("a 'b', c d  e", MermaidStateDiagram.Label(" a \"b\"; c:d#\ne "));

    private static WorkflowStatusDefinition Status(int status, string key, bool isInitial, bool isTerminal) =>
        new(status, key, isInitial, isTerminal, new StatusWording("r", "s", "a"));
}

public sealed class WorkflowRendererTests
{
    private static readonly string Sheet = WorkflowRenderer.Render(FixtureRecordWorkflow.Doc());

    [Fact]
    public void The_sheet_names_the_record_type_its_owner_and_its_definition()
    {
        Assert.StartsWith("# Workflow: fixture-record\n", Sheet, StringComparison.Ordinal);
        Assert.Contains(GeneratedDocSet.MarkdownNotice, Sheet, StringComparison.Ordinal);
        Assert.Contains($"declared by Fixtures in `{typeof(FixtureRecordWorkflowDefinition).FullName}`", Sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sheet_embeds_the_diagram() =>
        Assert.Contains(MermaidStateDiagram.Render(FixtureRecordWorkflow.Catalog), Sheet, StringComparison.Ordinal);

    [Fact]
    public void Statuses_list_every_audience_wording_key_and_mark_initial_and_terminal()
    {
        Assert.Contains(
            "| 1 | `draft` | yes |  | `fixtureRecords.status.draft.requester` | `fixtureRecords.status.draft.staff` | `fixtureRecords.status.draft.admin` |",
            Sheet,
            StringComparison.Ordinal);
        Assert.Contains("| 3 | `closed` |  | yes | `fixtureRecords.status.closed.requester` |", Sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Transitions_show_permission_side_ownership_reason_and_notification_decision()
    {
        Assert.Contains(
            "| `file` | `draft` (1) | `filed` (2) | requester | `fixture-records.file` | yes | none |  | `fixtureRecords.action.file` | `fixture-records.filed` to assignee |",
            Sheet,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `close` | `filed` (2) | `closed` (3) | staff | `fixture-records.decide` |  | required | `reason` | `fixtureRecords.action.close` | `fixture-records.closed` to requester, assignee |",
            Sheet,
            StringComparison.Ordinal);
        Assert.Contains("| `drop` | `draft` (1) | `dropped` (4) | requester | `fixture-records.file` | yes | optional | `reason` | `fixtureRecords.action.drop` | nobody (on purpose) |", Sheet, StringComparison.Ordinal);
        Assert.Contains("| missing (catalog violation) |", Sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Transition_rows_follow_from_status_then_catalog_order()
    {
        var file = Sheet.IndexOf("| `file` |", StringComparison.Ordinal);
        var dropFromDraft = Sheet.IndexOf("| `drop` | `draft` (1)", StringComparison.Ordinal);
        var close = Sheet.IndexOf("| `close` |", StringComparison.Ordinal);
        var dropFromFiled = Sheet.IndexOf("| `drop` | `filed` (2)", StringComparison.Ordinal);

        Assert.True(file < dropFromDraft && dropFromDraft < close && close < dropFromFiled);
    }

    [Fact]
    public void A_rule_to_an_undefined_status_shows_the_bare_value()
    {
        var catalog = new TransitionCatalog(
            "broken",
            [new WorkflowStatusDefinition(1, "open", IsInitial: true, IsTerminal: false, new StatusWording("r", "s", "a"))],
            [new TransitionRule("go", 1, 9, "broken.go", TransitionSide.Staff, ReasonRequirement.None, null, "l", NotificationDecision.None, false)]);

        var sheet = WorkflowRenderer.Render(new WorkflowDoc("broken", "Kernel", "Broken", catalog));

        Assert.Contains("| `go` | `open` (1) | 9 | staff |", sheet, StringComparison.Ordinal);
        Assert.Contains("s1 --> s9 : go", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_without_rules_says_so() =>
        Assert.Contains("No transitions.", WorkflowRenderer.Render(new WorkflowDoc(AlphaNoteWorkflowDefinition.Type, "Alpha", "Alpha.Note", new AlphaNoteWorkflowDefinition().Catalog)), StringComparison.Ordinal);

    [Theory]
    [InlineData(false, false, "`t` to nobody")]
    [InlineData(true, false, "`t` to requester")]
    [InlineData(false, true, "`t` to assignee")]
    public void Notification_decisions_are_worded(bool requester, bool assignee, string expected) =>
        Assert.Equal(expected, WorkflowText.Describe(new NotificationDecision("t", requester, assignee)));

    [Fact]
    public void A_decision_that_notifies_without_a_template_is_visible() =>
        Assert.Equal("no template to requester", WorkflowText.Describe(new NotificationDecision(null, NotifyRequester: true, NotifyAssignee: false)));

    [Theory]
    [InlineData((int)ReasonRequirement.None, "none")]
    [InlineData((int)ReasonRequirement.Optional, "optional")]
    [InlineData((int)ReasonRequirement.Required, "required")]
    [InlineData(42, "42")]
    public void Reason_requirements_are_worded(int reason, string expected) =>
        Assert.Equal(expected, WorkflowText.Describe((ReasonRequirement)reason));

    [Theory]
    [InlineData((int)TransitionSide.Requester, "requester")]
    [InlineData((int)TransitionSide.Staff, "staff")]
    [InlineData((int)TransitionSide.System, "system")]
    [InlineData(42, "42")]
    public void Sides_are_worded(int side, string expected) =>
        Assert.Equal(expected, WorkflowText.Describe((TransitionSide)side));
}

public sealed class WorkflowIndexAndStatusesTests
{
    [Fact]
    public void The_index_links_every_sheet_with_its_counts()
    {
        var index = WorkflowIndexRenderer.Render([FixtureRecordWorkflow.Doc()]);

        Assert.StartsWith("# Workflows\n", index, StringComparison.Ordinal);
        Assert.Contains("| [fixture-record](fixture-record.md) | Fixtures | 4 | 2 | 4 |", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_workflows_still_render_an_index_and_a_statuses_note()
    {
        var documents = GeneratedDocSet.Render(SurfaceFixtures.Empty());

        Assert.Contains(WorkflowIndexRenderer.NoWorkflows, documents[GeneratedFileNames.WorkflowIndex], StringComparison.Ordinal);
        Assert.Contains(StatusesRenderer.NoWorkflows, documents[GeneratedFileNames.Statuses], StringComparison.Ordinal);
        Assert.DoesNotContain(documents.Keys, name => name.StartsWith(GeneratedFileNames.WorkflowsFolder + "/", StringComparison.Ordinal) && name != GeneratedFileNames.WorkflowIndex);
    }

    [Fact]
    public void Statuses_list_each_workflow_after_the_enums_with_its_wording_and_sheet_link()
    {
        var statuses = StatusesRenderer.Render(SurfaceFixtures.Surface().Statuses, [FixtureRecordWorkflow.Doc()]);

        var enums = statuses.IndexOf("### `RunStatus`", StringComparison.Ordinal);
        var workflows = statuses.IndexOf("## " + StatusesRenderer.WorkflowsHeading, StringComparison.Ordinal);
        Assert.True(enums >= 0 && enums < workflows);
        Assert.Contains("### `fixture-record`", statuses, StringComparison.Ordinal);
        Assert.Contains("[workflows/fixture-record.md](workflows/fixture-record.md)", statuses, StringComparison.Ordinal);
        Assert.Contains("| 4 | `dropped` |  | yes | `fixtureRecords.status.dropped.requester` | `fixtureRecords.status.dropped.staff` | `fixtureRecords.status.dropped.admin` |", statuses, StringComparison.Ordinal);
    }

    [Fact]
    public void Statuses_without_enums_still_list_the_workflows()
    {
        var statuses = StatusesRenderer.Render([], [FixtureRecordWorkflow.Doc()]);

        Assert.Contains("No status enums are declared.", statuses, StringComparison.Ordinal);
        Assert.Contains("### `fixture-record`", statuses, StringComparison.Ordinal);
    }

    [Fact]
    public void One_sheet_per_workflow_in_the_doc_set()
    {
        var surface = SurfaceFixtures.Surface() with
        {
            Workflows = [new WorkflowDoc(AlphaNoteWorkflowDefinition.Type, "Alpha", "Alpha.Note", new AlphaNoteWorkflowDefinition().Catalog), FixtureRecordWorkflow.Doc()],
        };

        var documents = GeneratedDocSet.Render(surface);

        Assert.Contains("workflows/alpha-note.md", documents.Keys);
        Assert.Contains("workflows/fixture-record.md", documents.Keys);
    }
}

public sealed class WorkflowCollectorTests
{
    private static readonly OwnerNaming Owners = new(typeof(Program).Assembly);

    [Fact]
    public void Registered_definitions_are_collected_sorted_by_record_type()
    {
        var workflows = WorkflowCollector.Collect([new FixtureRecordWorkflowDefinition(), new AlphaNoteWorkflowDefinition()], [], Owners);

        Assert.Equal([AlphaNoteWorkflowDefinition.Type, FixtureRecordWorkflow.RecordType], workflows.Select(workflow => workflow.RecordType));
        Assert.Same(FixtureRecordWorkflow.Catalog, workflows[1].Catalog);
        Assert.Equal(typeof(FixtureRecordWorkflowDefinition).FullName, workflows[1].DefinitionType);
        Assert.Equal(OwnerNaming.KernelOwner, workflows[1].Owner);
    }

    [Fact]
    public void Zero_definitions_is_a_valid_host() =>
        Assert.Empty(WorkflowCollector.Collect([], [typeof(Program).Assembly], Owners));

    [Fact]
    public void Declared_definitions_are_concrete_classes_only()
    {
        var declared = WorkflowCollector.DeclaredDefinitionTypes([typeof(WorkflowCollectorTests).Assembly]);

        Assert.Equal(
            [typeof(AlphaNoteWorkflowDefinition), typeof(FixtureRecordWorkflowDefinition), typeof(NamedWorkflowDefinition)],
            declared);
    }

    [Fact]
    public void A_declared_definition_that_is_not_registered_fails_the_collection()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            WorkflowCollector.Collect([new FixtureRecordWorkflowDefinition(), new NamedWorkflowDefinition("named")], [typeof(WorkflowCollectorTests).Assembly], Owners));

        Assert.Contains(typeof(AlphaNoteWorkflowDefinition).FullName!, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(FixtureRecordWorkflowDefinition).FullName!, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_declared_definition_registered_passes_the_cross_check() =>
        Assert.Equal(
            3,
            WorkflowCollector.Collect(
                [new FixtureRecordWorkflowDefinition(), new AlphaNoteWorkflowDefinition(), new NamedWorkflowDefinition("named")],
                [typeof(WorkflowCollectorTests).Assembly],
                Owners).Count);

    [Theory]
    [InlineData("Service-Request")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_record_type_that_is_not_a_kebab_key_is_refused(string recordType) =>
        Assert.Throws<InvalidOperationException>(() => WorkflowCollector.Collect([new NamedWorkflowDefinition(recordType)], [], Owners));

    [Fact]
    public void Two_definitions_of_one_record_type_are_refused()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            WorkflowCollector.Collect([new NamedWorkflowDefinition("same"), new NamedWorkflowDefinition("same")], [], Owners));

        Assert.Contains("same", failure.Message, StringComparison.Ordinal);
    }
}

public sealed class WorkflowWriterTests
{
    private static readonly Dictionary<string, string> Documents = new(StringComparer.Ordinal)
    {
        ["a.md"] = "# A\n",
        ["workflows/README.md"] = "# W\n",
        ["workflows/one.md"] = "# One\n",
    };

    [Fact]
    public void Write_creates_the_subfolder_and_compare_is_clean()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);

        var written = writer.Write(Documents);

        Assert.Equal(["a.md", "workflows/README.md", "workflows/one.md"], written.Written);
        Assert.True(File.Exists(Path.Combine(folder.Path, "workflows", "one.md")));
        Assert.True(writer.Compare(Documents).IsClean);
    }

    [Fact]
    public void A_sheet_of_a_removed_workflow_is_reported_and_kept()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);
        writer.Write(Documents);
        var withoutOne = Documents.Where(entry => entry.Key != "workflows/one.md").ToDictionary(StringComparer.Ordinal);

        var drift = writer.Compare(withoutOne);
        var write = writer.Write(withoutOne);

        Assert.Equal([new FileDrift("workflows/one.md", DriftKind.Unexpected)], drift.Drifts);
        Assert.Equal(["workflows/one.md"], write.Unexpected);
        Assert.True(File.Exists(Path.Combine(folder.Path, "workflows", "one.md")));
    }

    [Fact]
    public void A_subfolder_no_document_lives_in_is_not_inspected()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);
        writer.Write(Documents);
        Directory.CreateDirectory(Path.Combine(folder.Path, "other"));
        File.WriteAllText(Path.Combine(folder.Path, "other", "keep.md"), "x");

        Assert.True(writer.Compare(Documents).IsClean);
    }

    [Fact]
    public void A_missing_sheet_is_reported_missing()
    {
        using var folder = new TempFolder();
        var writer = new GeneratedDocsWriter(folder.Path);
        writer.Write(Documents);
        File.Delete(Path.Combine(folder.Path, "workflows", "one.md"));

        Assert.Equal([new FileDrift("workflows/one.md", DriftKind.Missing)], writer.Compare(Documents).Drifts);
    }
}
