using Starter.Application.Auditing;
using Starter.Application.Authorization;
using Starter.Application.Validation;

namespace Starter.Application.Tests;

public sealed class ValidationErrorsTests
{
    [Fact]
    public void A_new_collection_is_valid_and_empty()
    {
        var errors = new ValidationErrors();

        Assert.True(errors.IsValid);
        Assert.Empty(errors.ToDictionary());
    }

    [Fact]
    public void A_field_collects_several_messages_in_order()
    {
        var errors = new ValidationErrors()
            .Add("name", ValidationMessages.Required)
            .Add("name", ValidationMessages.InvalidFormat);

        Assert.False(errors.IsValid);
        Assert.Equal([ValidationMessages.Required, ValidationMessages.InvalidFormat], errors.ToDictionary()["name"]);
    }

    [Theory]
    [InlineData(null, ValidationMessages.Required)]
    [InlineData("", ValidationMessages.Required)]
    [InlineData("   ", ValidationMessages.Required)]
    [InlineData("abcdef", ValidationMessages.TooLong)]
    public void RequireText_reports_missing_and_too_long(string? value, string expected)
    {
        var errors = new ValidationErrors().RequireText("name", value, maxLength: 5);

        Assert.Equal([expected], errors.ToDictionary()["name"]);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("abcde")]
    public void RequireText_accepts_text_within_the_limit(string value)
    {
        Assert.True(new ValidationErrors().RequireText("name", value, maxLength: 5).IsValid);
    }

    [Fact]
    public void ToDictionary_returns_a_copy()
    {
        var errors = new ValidationErrors().Add("name", ValidationMessages.Required);
        var first = errors.ToDictionary();

        errors.Add("name", ValidationMessages.TooLong);

        Assert.Single(first["name"]);
    }

    [Theory]
    [InlineData("", "REQUIRED")]
    [InlineData("name", "")]
    public void Blank_field_or_message_is_refused(string field, string message)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ValidationErrors().Add(field, message));
    }

    [Theory]
    [InlineData("UserName", "userName")]
    [InlineData("Password", "password")]
    [InlineData("refreshToken", "refreshToken")]
    public void FieldNames_are_camel_case_wire_names(string member, string expected)
    {
        Assert.Equal(expected, FieldNames.Of(member));
    }
}

public sealed class PermissionKeyFormatTests
{
    [Theory]
    [InlineData("dashboard.view")]
    [InlineData("access.admin.view")]
    [InlineData("service-requests.approve")]
    [InlineData("a1.b2")]
    public void Well_formed_keys_are_valid(string key)
    {
        Assert.True(PermissionKeyFormat.IsValid(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dashboard")]
    [InlineData("Dashboard.view")]
    [InlineData("dashboard.View")]
    [InlineData("dashboard..view")]
    [InlineData(".dashboard.view")]
    [InlineData("dashboard.view.")]
    [InlineData("1dashboard.view")]
    [InlineData("dashboard.-view")]
    [InlineData("dashboard view")]
    [InlineData("dashboard_x.view")]
    public void Malformed_keys_are_invalid(string? key)
    {
        Assert.False(PermissionKeyFormat.IsValid(key));
    }

    [Fact]
    public void Keys_longer_than_the_maximum_are_invalid()
    {
        var atLimit = "a." + new string('b', PermissionKeyFormat.MaxLength - 2);

        Assert.True(PermissionKeyFormat.IsValid(atLimit));
        Assert.False(PermissionKeyFormat.IsValid(atLimit + "c"));
    }

    [Theory]
    [InlineData("dashboard.view", "dashboard")]
    [InlineData("access.admin.view", "access")]
    [InlineData("single", "single")]
    public void OwnerOf_is_the_first_segment(string key, string owner)
    {
        Assert.Equal(owner, PermissionKeyFormat.OwnerOf(key));
        Assert.Equal(owner, new PermissionDefinition(key, "Sample.").Owner);
    }

    [Fact]
    public void OwnerOf_rejects_blank_keys()
    {
        Assert.ThrowsAny<ArgumentException>(() => PermissionKeyFormat.OwnerOf(" "));
    }
}

public sealed class AuditContextTests
{
    [Fact]
    public void The_system_actor_has_no_subject()
    {
        Assert.True(AuditContext.System.IsSystem);
        Assert.False(new AuditContext(Guid.NewGuid(), ClientAddress: null, CorrelationId: null).IsSystem);
    }
}
