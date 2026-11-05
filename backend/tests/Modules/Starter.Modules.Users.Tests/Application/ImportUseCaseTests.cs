using Codelab.Security.PublicIds;
using Starter.Application.Messaging;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

public sealed class PreviewUserImportTests
{
    internal static readonly string?[] Header = ["UserName", "DisplayName", "Email", "Kind", "Binding", "OrganizationCode"];

    [Fact]
    public async Task Previews_the_three_row_sample_two_valid_one_rejected_with_a_reason()
    {
        var harness = new UsersHarness();
        harness.Organizations.Add("SAMPLE-ORG", OrganizationKind.External);

        var outcome = await harness.Preview().ExecuteAsync(Upload(SampleWorkbook()), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, outcome.Value.ValidCount);
        Assert.Equal(1, outcome.Value.InvalidCount);
        Assert.Equal([2, 3, 4], outcome.Value.Rows.Select(row => row.Row.RowNumber));
        Assert.Contains(UsersImportRowErrorCodes.EmailInvalid, outcome.Value.Rows[2].Errors);
        var batch = Assert.Single(harness.Batches.Items);
        Assert.Equal(batch.Id, outcome.Value.BatchId);
        Assert.Equal(ImportBatchStatus.Previewed, batch.Status);
        Assert.Equal(harness.CurrentUser.SubjectId, batch.CreatedBySubjectId);
        Assert.DoesNotContain(harness.Accounts.Calls, call => call.StartsWith("create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_columns_are_named_in_the_field_errors()
    {
        var harness = new UsersHarness();
        var workbook = XlsxBuilder.WithRows(["UserName", "Email"], ["amal", "amal@example.test"]).Build();

        var outcome = await harness.Preview().ExecuteAsync(Upload(workbook), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ImportColumnsMissing, outcome.Refusal!.Code);
        Assert.Equal(["binding", "displayName", "kind"], outcome.Refusal.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.All(outcome.Refusal.FieldErrors.Values, messages => Assert.Equal([ValidationMessages.Required], messages));
        Assert.Empty(harness.Batches.Items);
    }

    [Fact]
    public async Task Upload_refusals()
    {
        var harness = new UsersHarness();
        var workbook = SampleWorkbook();

        var none = await harness.Preview().ExecuteAsync(new ImportUpload(null, 0, null), CancellationToken.None);
        var empty = await harness.Preview().ExecuteAsync(new ImportUpload(new MemoryStream(), 0, "users.xlsx"), CancellationToken.None);
        var tooLarge = await harness.Preview().ExecuteAsync(new ImportUpload(new MemoryStream(workbook), ImportLimits.MaxUploadBytes + 1, "users.xlsx"), CancellationToken.None);
        var wrongType = await harness.Preview().ExecuteAsync(new ImportUpload(new MemoryStream(workbook), workbook.Length, "users.csv"), CancellationToken.None);
        var notAZip = await harness.Preview().ExecuteAsync(new ImportUpload(new MemoryStream([1, 2, 3]), 3, "users.xlsx"), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ImportFileRequired, none.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.ImportFileRequired, empty.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.ImportFileTooLarge, tooLarge.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.ImportFileInvalid, wrongType.Refusal!.Code);
        Assert.Equal(UsersErrorCodes.ImportFileInvalid, notAZip.Refusal!.Code);
        Assert.All([none, empty, tooLarge, wrongType, notAZip], outcome => Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category));
    }

    [Fact]
    public async Task Sheet_refusals()
    {
        var harness = new UsersHarness { ImportSettings = new ImportSettings(MaxRows: 2, BatchLifetime: TimeSpan.FromMinutes(5)) };
        var headerOnly = XlsxBuilder.WithRows(Header).Build();
        var tooMany = XlsxBuilder.WithRows(Header, Cells("a"), Cells("b"), Cells("c")).Build();
        var blank = XlsxBuilder.WithRows().Build();

        Assert.Equal(UsersErrorCodes.ImportNoRows, (await harness.Preview().ExecuteAsync(Upload(headerOnly), CancellationToken.None)).Refusal!.Code);
        Assert.Equal(UsersErrorCodes.ImportTooManyRows, (await harness.Preview().ExecuteAsync(Upload(tooMany), CancellationToken.None)).Refusal!.Code);
        Assert.Equal(UsersErrorCodes.ImportNoRows, (await harness.Preview().ExecuteAsync(Upload(blank), CancellationToken.None)).Refusal!.Code);
    }

    internal static byte[] SampleWorkbook() =>
        XlsxBuilder.WithRows(
            Header,
            ["sample.one", "Sample One", "sample.one@example.test", "external", "password", "SAMPLE-ORG"],
            ["sample.two", "Sample Two", "sample.two@example.test", "external", "password", "SAMPLE-ORG"],
            ["sample.three", "Sample Three", "not-an-email", "external", "password", "SAMPLE-ORG"]).Build();

    internal static ImportUpload Upload(byte[] workbook) => new(new MemoryStream(workbook), workbook.Length, "users.xlsx");

    private static string?[] Cells(string userName) => [userName, userName, null, "internal", "sso", null];
}

public sealed class ImportBatchesTests
{
    [Fact]
    public async Task A_batch_of_another_administrator_is_not_found()
    {
        var harness = new UsersHarness();
        var batch = await harness.ImportBatches.SavePreviewAsync([], CancellationToken.None);
        harness.CurrentUser.SubjectId = Guid.NewGuid();

        var outcome = await harness.ImportBatches.FindCommittableAsync(PublicId<UserImportIds>.From(batch.Id), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ImportBatchNotFound, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task An_expired_batch_is_marked_expired()
    {
        var harness = new UsersHarness();
        var batch = await harness.ImportBatches.SavePreviewAsync([], CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMinutes(31));

        var outcome = await harness.ImportBatches.FindCommittableAsync(PublicId<UserImportIds>.From(batch.Id), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ImportBatchExpired, outcome.Refusal!.Code);
        Assert.Equal(ImportBatchStatus.Expired, batch.Status);
    }

    [Fact]
    public async Task The_rows_round_trip_through_the_stored_json()
    {
        var harness = new UsersHarness();
        var row = ImportRowValidatorTests.Row(9, "amal", email: "amal@example.test", organizationCode: "FIN");
        var checkedRow = new CheckedImportRow(row, Kind: null, Binding: null, OrganizationId: null, Errors: []);

        var batch = await harness.ImportBatches.SavePreviewAsync([checkedRow], CancellationToken.None);

        Assert.Equal([row], ImportBatches.RowsOf(batch));
        Assert.Equal(1, batch.RowCount);
        Assert.Equal(1, batch.ValidRowCount);
    }

    [Fact]
    public async Task A_preview_without_a_subject_is_a_programming_error()
    {
        var harness = new UsersHarness();
        harness.CurrentUser.SubjectId = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ImportBatches.SavePreviewAsync([], CancellationToken.None));
    }
}

public sealed class CommitUserImportTests
{
    [Fact]
    public async Task Commits_the_sample_two_accounts_without_passwords_two_links_two_pending_outbox_rows_one_rejected()
    {
        var harness = new UsersHarness();
        harness.Organizations.Add("SAMPLE-ORG", OrganizationKind.External);
        var preview = await harness.Preview().ExecuteAsync(PreviewUserImportTests.Upload(PreviewUserImportTests.SampleWorkbook()), CancellationToken.None);

        var outcome = await harness.Commit().ExecuteAsync(PublicId<UserImportIds>.From(preview.Value.BatchId), CancellationToken.None);

        Assert.Equal(["sample.one", "sample.two"], outcome.Value.Created.Select(user => user.UserName));
        Assert.All(outcome.Value.Created, user => Assert.StartsWith(UsersHarness.LinkBase + "/activate#ticket=", user.Activation!.ActivationLink, StringComparison.Ordinal));
        Assert.All(harness.Accounts.Accounts, account => Assert.False(account.HasPassword));
        Assert.Equal(2, harness.Outbox.Messages.Count(message => message.Type == OutboxMessageTypes.AccountActivation));
        var rejected = Assert.Single(outcome.Value.Rejected);
        Assert.Equal(4, rejected.RowNumber);
        Assert.Contains(UsersImportRowErrorCodes.EmailInvalid, rejected.Errors);
        Assert.Equal(ImportBatchStatus.Committed, harness.Batches.Items[0].Status);
    }

    [Fact]
    public async Task Directory_and_sso_rows_get_no_ticket()
    {
        var harness = new UsersHarness();
        var workbook = XlsxBuilder.WithRows(PreviewUserImportTests.Header, ["ad.user", "AD User", null, "internal", "ad", null], ["sso.user", "SSO User", null, "internal", "sso", null]).Build();
        var preview = await harness.Preview().ExecuteAsync(PreviewUserImportTests.Upload(workbook), CancellationToken.None);

        var outcome = await harness.Commit().ExecuteAsync(PublicId<UserImportIds>.From(preview.Value.BatchId), CancellationToken.None);

        Assert.Equal([AccountBinding.ActiveDirectory, AccountBinding.Sso], outcome.Value.Created.Select(user => user.Binding));
        Assert.All(outcome.Value.Created, user => Assert.Null(user.Activation));
        Assert.Empty(harness.Outbox.Messages);
    }

    [Fact]
    public async Task The_commit_revalidates_and_identity_refusals_become_rejected_rows()
    {
        var harness = new UsersHarness();
        var workbook = XlsxBuilder.WithRows(PreviewUserImportTests.Header, ["late", "Late", null, "internal", "sso", null], ["raced", "Raced", null, "internal", "sso", null]).Build();
        var preview = await harness.Preview().ExecuteAsync(PreviewUserImportTests.Upload(workbook), CancellationToken.None);
        harness.Accounts.Add("late");
        harness.Accounts.RefuseCreate = account => account.UserName == "raced" ? Refusal.Conflict("IDENTITY_EMAIL_TAKEN") : null;

        var outcome = await harness.Commit().ExecuteAsync(PublicId<UserImportIds>.From(preview.Value.BatchId), CancellationToken.None);

        Assert.Empty(outcome.Value.Created);
        Assert.Equal([UsersImportRowErrorCodes.UserNameTaken], outcome.Value.Rejected[0].Errors);
        Assert.Equal(["IDENTITY_EMAIL_TAKEN"], outcome.Value.Rejected[1].Errors);
    }

    [Fact]
    public async Task A_refused_ticket_leaves_the_account_with_the_reason_instead_of_a_link()
    {
        var harness = new UsersHarness();
        var workbook = XlsxBuilder.WithRows(PreviewUserImportTests.Header, ["amal", "Amal", null, "internal", "password", null]).Build();
        var preview = await harness.Preview().ExecuteAsync(PreviewUserImportTests.Upload(workbook), CancellationToken.None);
        harness.Accounts.IssueRefusal = Refusal.Conflict("IDENTITY_SOMETHING");

        var outcome = await harness.Commit().ExecuteAsync(PublicId<UserImportIds>.From(preview.Value.BatchId), CancellationToken.None);

        var created = Assert.Single(outcome.Value.Created);
        Assert.Null(created.Activation);
        Assert.Equal("IDENTITY_SOMETHING", created.ActivationErrorCode);
    }

    [Fact]
    public async Task A_batch_commits_once_and_a_lost_race_is_a_conflict()
    {
        var harness = new UsersHarness();
        var preview = await harness.Preview().ExecuteAsync(PreviewUserImportTests.Upload(PreviewUserImportTests.SampleWorkbook()), CancellationToken.None);
        var id = PublicId<UserImportIds>.From(preview.Value.BatchId);
        await harness.Commit().ExecuteAsync(id, CancellationToken.None);

        var again = await harness.Commit().ExecuteAsync(id, CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ImportBatchCommitted, again.Refusal!.Code);

        var racing = new UsersHarness();
        racing.Organizations.Add("SAMPLE-ORG", OrganizationKind.External);
        var racedPreview = await racing.Preview().ExecuteAsync(PreviewUserImportTests.Upload(PreviewUserImportTests.SampleWorkbook()), CancellationToken.None);
        racing.UnitOfWork.NextTrySaveSucceeds = false;
        var lost = await racing.Commit().ExecuteAsync(PublicId<UserImportIds>.From(racedPreview.Value.BatchId), CancellationToken.None);
        Assert.Equal(UsersErrorCodes.ImportBatchCommitted, lost.Refusal!.Code);
        Assert.DoesNotContain(racing.Accounts.Calls, call => call.StartsWith("create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_link_base_password_rows_are_refused_before_the_claim()
    {
        var harness = new UsersHarness();
        harness.Organizations.Add("SAMPLE-ORG", OrganizationKind.External);
        var preview = await harness.Preview().ExecuteAsync(PreviewUserImportTests.Upload(PreviewUserImportTests.SampleWorkbook()), CancellationToken.None);
        harness.Links = new ActivationLinkSettings(LinkBase: null);

        var outcome = await harness.Commit().ExecuteAsync(PublicId<UserImportIds>.From(preview.Value.BatchId), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ActivationLinkUnconfigured, outcome.Refusal!.Code);
        Assert.Equal(ImportBatchStatus.Previewed, harness.Batches.Items[0].Status);
        Assert.Empty(harness.Accounts.Accounts);
    }

    [Fact]
    public async Task An_unknown_batch_is_not_found()
    {
        var outcome = await new UsersHarness().Commit().ExecuteAsync(PublicId<UserImportIds>.From(77), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ImportBatchNotFound, outcome.Refusal!.Code);
    }
}
