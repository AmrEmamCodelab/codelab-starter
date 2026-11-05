using Starter.Domain.Users;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

public sealed class ImportColumnsTests
{
    [Theory]
    [InlineData("UserName")]
    [InlineData("user name")]
    [InlineData("USER_NAME")]
    [InlineData("user-name")]
    [InlineData(" username ")]
    public void Header_spellings_match_the_same_column(string header)
    {
        var map = ImportColumns.Map(new SheetRow(1, [header, "DisplayName", "Kind", "Binding"]));

        Assert.Equal(0, map.Positions[ImportColumn.UserName]);
        Assert.Empty(map.Missing);
    }

    [Fact]
    public void Column_order_does_not_matter_unknown_columns_are_ignored_and_the_first_duplicate_wins()
    {
        var map = ImportColumns.Map(new SheetRow(1, ["Notes", "Binding", "kind", "display name", "UserName", "UserName", "organization_code"]));

        Assert.Equal(4, map.Positions[ImportColumn.UserName]);
        Assert.Equal(1, map.Positions[ImportColumn.Binding]);
        Assert.Equal(6, map.Positions[ImportColumn.OrganizationCode]);
        Assert.False(map.Positions.ContainsKey(ImportColumn.Email));
    }

    [Fact]
    public void Missing_required_columns_are_listed_in_template_order()
    {
        var map = ImportColumns.Map(new SheetRow(1, ["Email", "Binding"]));

        Assert.Equal(["UserName", "DisplayName", "Kind"], map.Missing.Select(column => column.Header));
    }

    [Fact]
    public void RowOf_reads_the_mapped_cells_and_absent_columns_as_null()
    {
        var map = ImportColumns.Map(new SheetRow(1, ["Kind", "UserName", "DisplayName", "Binding"]));

        var row = ImportColumns.RowOf(map, new SheetRow(7, ["external", "amal", "Amal"]));

        Assert.Equal(new ImportRow(7, "amal", "Amal", null, null, null, "external", null, null), row);
    }
}

public sealed class ImportRowRulesTests
{
    [Theory]
    [InlineData("internal", UserKind.@Internal)]
    [InlineData(" External ", UserKind.External)]
    [InlineData("staff", null)]
    [InlineData(null, null)]
    public void Kind_words(string? text, UserKind? expected) => Assert.Equal(expected, ImportValues.KindOf(text));

    [Theory]
    [InlineData("password", AccountBinding.Password)]
    [InlineData("AD", AccountBinding.ActiveDirectory)]
    [InlineData("active directory", AccountBinding.ActiveDirectory)]
    [InlineData("sso", AccountBinding.Sso)]
    [InlineData("ldap", null)]
    [InlineData(null, null)]
    public void Binding_words(string? text, AccountBinding? expected) => Assert.Equal(expected, ImportValues.BindingOf(text));

    [Fact]
    public void A_complete_row_has_no_field_error() =>
        Assert.Empty(ImportRowRules.FieldErrors(new ImportRow(2, "amal", "Amal", "amal@example.test", null, null, "internal", "password", null)));

    [Fact]
    public void Every_field_error_is_listed()
    {
        var errors = ImportRowRules.FieldErrors(new ImportRow(
            2, null, new string('d', UsersLimits.DisplayNameMaxLength + 1), "nope", new string('l', UsersLimits.LoginNameMaxLength + 1), null, "staff", "ldap", null));

        Assert.Equal(
            [
                UsersImportRowErrorCodes.UserNameRequired, UsersImportRowErrorCodes.DisplayNameTooLong, UsersImportRowErrorCodes.EmailInvalid,
                UsersImportRowErrorCodes.DirectoryNameTooLong, UsersImportRowErrorCodes.KindInvalid, UsersImportRowErrorCodes.BindingInvalid,
            ],
            errors);
    }

    [Fact]
    public void A_too_long_user_name_and_a_missing_display_name()
    {
        var errors = ImportRowRules.FieldErrors(new ImportRow(2, new string('u', UsersLimits.UserNameMaxLength + 1), null, null, null, null, "internal", "sso", null));

        Assert.Equal([UsersImportRowErrorCodes.UserNameTooLong, UsersImportRowErrorCodes.DisplayNameRequired], errors);
    }
}

public sealed class ImportRowValidatorTests
{
    [Fact]
    public async Task Valid_rows_get_their_kind_binding_and_organization()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);

        var rows = await harness.RowValidator.ValidateAsync([Row(2, "amal", kind: "external", organizationCode: "fin")], CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.True(row.IsValid);
        Assert.Equal(UserKind.External, row.Kind);
        Assert.Equal(AccountBinding.Password, row.Binding);
        Assert.Equal(organization.Id, row.OrganizationId);
    }

    [Fact]
    public async Task Duplicates_within_the_file_are_refused_on_every_copy()
    {
        var harness = new UsersHarness();

        var rows = await harness.RowValidator.ValidateAsync(
            [Row(2, "amal", email: "a@example.test"), Row(3, " AMAL ", email: "b@example.test"), Row(4, "badr", email: "A@example.test")],
            CancellationToken.None);

        Assert.Contains(UsersImportRowErrorCodes.UserNameDuplicate, rows[0].Errors);
        Assert.Contains(UsersImportRowErrorCodes.UserNameDuplicate, rows[1].Errors);
        Assert.Contains(UsersImportRowErrorCodes.EmailDuplicate, rows[0].Errors);
        Assert.Equal([UsersImportRowErrorCodes.EmailDuplicate], rows[2].Errors);
    }

    [Fact]
    public async Task An_existing_user_name_is_taken()
    {
        var harness = new UsersHarness();
        harness.Accounts.Add("Amal");
        harness.Accounts.Add("amal2");

        var rows = await harness.RowValidator.ValidateAsync([Row(2, "amal"), Row(3, "amal3")], CancellationToken.None);

        Assert.Equal([UsersImportRowErrorCodes.UserNameTaken], rows[0].Errors);
        Assert.True(rows[1].IsValid);
    }

    [Fact]
    public async Task Organization_rules()
    {
        var harness = new UsersHarness();
        harness.Organizations.Add("OLD", OrganizationKind.External, isActive: false);
        harness.Organizations.Add("HQ", OrganizationKind.@Internal);

        var rows = await harness.RowValidator.ValidateAsync(
            [
                Row(2, "a", kind: "external"),
                Row(3, "b", kind: "external", organizationCode: "NOPE"),
                Row(4, "c", kind: "external", organizationCode: "OLD"),
                Row(5, "d", kind: "external", organizationCode: "HQ"),
                Row(6, "e", kind: "internal", organizationCode: "HQ"),
                Row(7, "f", kind: "internal"),
            ],
            CancellationToken.None);

        Assert.Equal([UsersImportRowErrorCodes.OrganizationRequired], rows[0].Errors);
        Assert.Equal([UsersImportRowErrorCodes.OrganizationNotFound], rows[1].Errors);
        Assert.Equal([UsersImportRowErrorCodes.OrganizationInactive], rows[2].Errors);
        Assert.Equal([UsersImportRowErrorCodes.OrganizationKindMismatch], rows[3].Errors);
        Assert.True(rows[4].IsValid);
        Assert.True(rows[5].IsValid);
    }

    [Fact]
    public async Task A_row_with_field_errors_does_not_cost_an_identity_lookup()
    {
        var harness = new UsersHarness();

        await harness.RowValidator.ValidateAsync([Row(2, "amal", kind: "nope")], CancellationToken.None);

        Assert.Empty(harness.Accounts.Queries);
    }

    internal static ImportRow Row(int number, string? userName, string kind = "internal", string? email = null, string? organizationCode = null) =>
        new(number, userName, userName is null ? null : userName.Trim() + " Name", email, null, null, kind, "password", organizationCode);
}
