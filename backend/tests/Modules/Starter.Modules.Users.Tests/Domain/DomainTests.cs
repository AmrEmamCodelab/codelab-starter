using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Domain;

public sealed class OrganizationTests
{
    [Fact]
    public void Create_normalizes_the_code_trims_the_names_and_starts_active()
    {
        var organization = Organization.Create(new OrganizationDetails(" fin-01 ", " Finance ", " المالية ", OrganizationKind.External), TestClock.Start);

        Assert.Equal("FIN-01", organization.Code);
        Assert.Equal("Finance", organization.NameEn);
        Assert.Equal("المالية", organization.NameAr);
        Assert.Equal(OrganizationKind.External, organization.Kind);
        Assert.True(organization.IsActive);
        Assert.Equal(TestClock.Start, organization.CreatedAt);
        Assert.Null(organization.UpdatedAt);
    }

    [Fact]
    public void Change_replaces_the_values_and_stamps_the_update()
    {
        var organization = Organization.Create(new OrganizationDetails("A", "A", "A", OrganizationKind.External), TestClock.Start);
        var later = TestClock.Start.AddHours(1);

        organization.Change(new OrganizationDetails("b", "B en", "B ar", OrganizationKind.External), later);

        Assert.Equal("B", organization.Code);
        Assert.Equal("B en", organization.NameEn);
        Assert.Equal(later, organization.UpdatedAt);
    }

    [Fact]
    public void SetActive_reports_a_change_only_when_the_state_moves()
    {
        var organization = Organization.Create(new OrganizationDetails("A", "A", "A", OrganizationKind.External), TestClock.Start);

        Assert.False(organization.SetActive(true, TestClock.Start));
        Assert.True(organization.SetActive(false, TestClock.Start));
        Assert.False(organization.IsActive);
        Assert.True(organization.SetActive(true, TestClock.Start));
    }

    [Fact]
    public void NameIn_picks_the_language()
    {
        var organization = Organization.Create(new OrganizationDetails("A", "English", "عربي", OrganizationKind.External), TestClock.Start);

        Assert.Equal("عربي", organization.NameIn(arabic: true));
        Assert.Equal("English", organization.NameIn(arabic: false));
    }

    [Fact]
    public void Kinds_are_stored_as_explicit_integers()
    {
        Assert.Equal(1, (int)OrganizationKind.@Internal);
        Assert.Equal(2, (int)OrganizationKind.External);
        Assert.Equal(1, (int)ImportBatchStatus.Previewed);
        Assert.Equal(2, (int)ImportBatchStatus.Committed);
        Assert.Equal(3, (int)ImportBatchStatus.Expired);
    }
}

public sealed class OrganizationCodesTests
{
    [Theory]
    [InlineData("FIN", true)]
    [InlineData(" fin_01-a ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("FIN 01", false)]
    [InlineData("FIN.01", false)]
    [InlineData("مالية", false)]
    public void IsWellFormed_accepts_ascii_letters_digits_underscore_and_hyphen(string? code, bool expected) =>
        Assert.Equal(expected, OrganizationCodes.IsWellFormed(code));

    [Fact]
    public void IsWellFormed_refuses_a_code_longer_than_the_limit()
    {
        Assert.True(OrganizationCodes.IsWellFormed(new string('A', UsersLimits.OrganizationCodeMaxLength)));
        Assert.False(OrganizationCodes.IsWellFormed(new string('A', UsersLimits.OrganizationCodeMaxLength + 1)));
    }

    [Fact]
    public void Normalize_trims_and_upper_cases() => Assert.Equal("AB-1", OrganizationCodes.Normalize("  ab-1 "));
}

public sealed class EmailAddressesTests
{
    [Theory]
    [InlineData("amal@example.test", true)]
    [InlineData("first.last@sub.example.test", true)]
    [InlineData("amal@example", false)]
    [InlineData("@example.test", false)]
    [InlineData("amal@@example.test", false)]
    [InlineData("am al@example.test", false)]
    [InlineData("amal@example.", false)]
    [InlineData("amal@.example", false)]
    [InlineData("amal@example..test", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWellFormed_checks_the_shape(string? value, bool expected) => Assert.Equal(expected, EmailAddresses.IsWellFormed(value));

    [Fact]
    public void IsWellFormed_refuses_an_address_longer_than_the_limit() =>
        Assert.False(EmailAddresses.IsWellFormed(new string('a', UsersLimits.EmailMaxLength) + "@example.test"));
}

public sealed class ImportBatchTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public void Preview_starts_previewed_with_its_lifetime()
    {
        var batch = NewBatch();

        Assert.Equal(ImportBatchStatus.Previewed, batch.Status);
        Assert.Equal(TestClock.Start.AddMinutes(30), batch.ExpiresAt);
        Assert.Equal(3, batch.RowCount);
        Assert.Equal(2, batch.ValidRowCount);
    }

    [Fact]
    public void Preview_refuses_a_lifetime_that_is_not_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ImportBatch.Preview(new ImportBatchDraft("[]", 0, 0, Owner, TimeSpan.Zero), TestClock.Start));

    [Fact]
    public void Only_the_owner_may_commit_before_expiry()
    {
        var batch = NewBatch();

        Assert.Equal(ImportCommitDecision.Allowed, batch.CheckCommit(Owner, TestClock.Start.AddMinutes(30)));
        Assert.Equal(ImportCommitDecision.NotOwner, batch.CheckCommit(Guid.NewGuid(), TestClock.Start));
        Assert.Equal(ImportCommitDecision.Expired, batch.CheckCommit(Owner, TestClock.Start.AddMinutes(31)));
    }

    [Fact]
    public void A_batch_commits_once()
    {
        var batch = NewBatch();

        batch.MarkCommitted(TestClock.Start);

        Assert.Equal(ImportBatchStatus.Committed, batch.Status);
        Assert.Equal(TestClock.Start, batch.CommittedAt);
        Assert.Equal(ImportCommitDecision.AlreadyCommitted, batch.CheckCommit(Owner, TestClock.Start));
        Assert.Throws<InvalidOperationException>(() => batch.MarkCommitted(TestClock.Start));
    }

    [Fact]
    public void MarkExpired_expires_a_previewed_batch_but_never_a_committed_one()
    {
        var previewed = NewBatch();
        var committed = NewBatch();
        committed.MarkCommitted(TestClock.Start);

        previewed.MarkExpired();
        committed.MarkExpired();

        Assert.Equal(ImportBatchStatus.Expired, previewed.Status);
        Assert.Equal(ImportCommitDecision.Expired, previewed.CheckCommit(Owner, TestClock.Start));
        Assert.Equal(ImportBatchStatus.Committed, committed.Status);
    }

    private static ImportBatch NewBatch() =>
        ImportBatch.Preview(new ImportBatchDraft("[]", RowCount: 3, ValidRowCount: 2, Owner, TimeSpan.FromMinutes(30)), TestClock.Start);
}
