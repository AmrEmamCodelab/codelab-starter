using Codelab.Errors;
using Codelab.Errors.Classifiers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure;
using Starter.Modules.Attachments.Infrastructure.Options;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class AttachmentsOptionsValidatorTests
{
    [Theory]
    [InlineData("http://localhost:5185")]
    [InlineData("https://attachments.test")]
    [InlineData("https://apps.test/attachment-service/")]
    public void A_service_root_with_a_valid_lifetime_passes_in_Development(string root)
    {
        Assert.True(Validate(Environments.Development, root).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("attachments.test")]
    [InlineData("/relative")]
    [InlineData("ftp://attachments.test")]
    [InlineData("https://attachments.test/?x=1")]
    [InlineData("https://attachments.test/#top")]
    public void A_missing_or_malformed_service_root_fails(string? root)
    {
        var result = Validate(Environments.Development, root);

        Assert.True(result.Failed);
        Assert.Contains("Attachments:ServiceBaseUrl", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_http_is_refused_outside_Development()
    {
        var result = Validate(Environments.Production, "http://attachments.test");

        Assert.True(result.Failed);
        Assert.Contains("https", result.FailureMessage, StringComparison.Ordinal);
        Assert.True(Validate(Environments.Production, "https://attachments.test").Succeeded);
    }

    [Theory]
    [InlineData(AttachmentsOptions.MinTicketLifetimeSeconds - 1)]
    [InlineData(AttachmentsOptions.MaxTicketLifetimeSeconds + 1)]
    [InlineData(0)]
    public void A_ticket_lifetime_out_of_bounds_fails(int seconds)
    {
        var result = Validate(Environments.Development, "http://localhost:5185", seconds);

        Assert.True(result.Failed);
        Assert.Contains("Attachments:TicketLifetimeSeconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ticket_lifetime_above_the_upload_ticket_ring_maximum_fails()
    {
        var result = Validate(Environments.Development, "http://localhost:5185", lifetimeSeconds: 600, ticketMaximum: 300);

        Assert.True(result.Failed);
        Assert.Contains("Codelab:Security:UploadTickets:MaxLifetimeSeconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_lifetime_is_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new AttachmentsOptions().TicketLifetime);
    }

    [Fact]
    public void The_service_root_always_ends_with_a_slash()
    {
        Assert.Equal("https://apps.test/svc/", new AttachmentsOptions { ServiceBaseUrl = "https://apps.test/svc" }.ServiceRoot().AbsoluteUri);
        Assert.Equal("https://apps.test/svc/", new AttachmentsOptions { ServiceBaseUrl = " https://apps.test/svc/ " }.ServiceRoot().AbsoluteUri);
        Assert.Throws<InvalidOperationException>(() => new AttachmentsOptions().ServiceRoot());
    }

    private static ValidateOptionsResult Validate(string environment, string? root, int lifetimeSeconds = AttachmentsOptions.DefaultTicketLifetimeSeconds, int ticketMaximum = 3600) =>
        new AttachmentsOptionsValidator(new TestHostEnvironment(environment), Options.Create(new UploadTicketRingOptions { MaxLifetimeSeconds = ticketMaximum }))
            .Validate(name: null, new AttachmentsOptions { ServiceBaseUrl = root, TicketLifetimeSeconds = lifetimeSeconds });
}

public sealed class AttachmentsFailuresTests
{
    [Fact]
    public void A_storage_outage_is_503_with_the_module_code_ahead_of_the_generic_502()
    {
        var options = new CodelabErrorsOptions().AddAttachmentsFailures();
        var outage = new AttachmentStorageUnavailableException(new HttpRequestException("down"));

        var failure = options.Classifiers.Select(classifier => classifier.Classify(outage, context: null)).First(found => found is not null);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure!.Value.Status);
        Assert.Equal(AttachmentsErrorCodes.StorageUnavailable, failure.Value.Code);
        Assert.False(failure.Value.IsServerFault);
    }

    [Fact]
    public void The_classifier_sits_right_before_the_upstream_classifier_and_is_added_once()
    {
        var options = new CodelabErrorsOptions().AddAttachmentsFailures().AddAttachmentsFailures();

        var mine = options.Classifiers.Count(classifier => classifier is ExceptionTypeClassifier<AttachmentStorageUnavailableException>);
        var position = options.Classifiers.ToList().FindIndex(classifier => classifier is ExceptionTypeClassifier<AttachmentStorageUnavailableException>);

        Assert.Equal(1, mine);
        Assert.IsType<UpstreamUnavailableClassifier>(options.Classifiers[position + 1]);
    }

    [Fact]
    public void Without_an_upstream_classifier_it_goes_first()
    {
        var options = new CodelabErrorsOptions();
        options.Classifiers.Clear();

        options.AddAttachmentsFailures();

        Assert.IsType<ExceptionTypeClassifier<AttachmentStorageUnavailableException>>(Assert.Single(options.Classifiers));
    }

    [Fact]
    public void Another_upstream_failure_keeps_the_generic_502()
    {
        var options = new CodelabErrorsOptions().AddAttachmentsFailures();

        var failure = options.Classifiers.Select(classifier => classifier.Classify(new UpstreamUnavailableException("other", null), context: null)).First(found => found is not null);

        Assert.Equal(StatusCodes.Status502BadGateway, failure!.Value.Status);
    }
}
