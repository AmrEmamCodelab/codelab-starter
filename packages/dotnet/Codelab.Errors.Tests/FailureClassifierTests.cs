using Codelab.Errors.Classifiers;
using Codelab.Errors.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codelab.Errors.Tests;

/// <summary>Each built-in classifier claims exactly its failure class; the table walks them in order.</summary>
public sealed class FailureClassifierTests
{
    [Theory]
    [InlineData(SqlExceptionFactory.DeadlockVictim, true)]
    [InlineData(SqlExceptionFactory.CommandTimeout, true)]
    [InlineData(SqlExceptionFactory.LoginFailed, false)]
    [InlineData(SqlExceptionFactory.PrimaryKeyViolation, false)]
    public void Transient_sql_errors_are_retryable_and_others_are_not(int number, bool transient)
    {
        var failure = new TransientFaultClassifier().Classify(SqlExceptionFactory.Create(number), context: null);

        Assert.Equal(transient, failure is not null);
        if (transient)
        {
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure!.Value.Status);
            Assert.Equal(ErrorCodes.Retryable, failure.Value.Code);
            Assert.Equal(TransientFaultClassifier.RetryAfterSeconds, failure.Value.RetryAfterSeconds);
        }
    }

    [Fact]
    public void A_transient_sql_error_wrapped_by_the_execution_strategy_is_still_found()
    {
        var wrapped = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException("save", SqlExceptionFactory.Create(SqlExceptionFactory.DeadlockVictim)));

        Assert.NotNull(new TransientFaultClassifier().Classify(wrapped, context: null));
    }

    [Fact]
    public void A_timeout_is_retryable()
    {
        Assert.Equal(ErrorCodes.Retryable, new TransientFaultClassifier().Classify(new TimeoutException(), context: null)?.Code);
    }

    [Fact]
    public void A_lost_update_is_a_conflict_and_wins_over_the_transient_classifier()
    {
        var table = Table();

        var classification = table.Classify(new DbUpdateConcurrencyException("lost"), context: null);

        Assert.Equal(StatusCodes.Status409Conflict, classification.Failure.Status);
        Assert.Equal(ErrorCodes.Concurrency, classification.Failure.Code);
        Assert.Equal(nameof(ConcurrencyClassifier), classification.Classifier);
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, ErrorCodes.PayloadTooLarge)]
    [InlineData(StatusCodes.Status400BadRequest, ErrorCodes.Malformed)]
    public void A_refused_request_body_keeps_the_status_kestrel_chose(int status, string code)
    {
        var failure = new RequestBodyClassifier().Classify(new BadHttpRequestException("bad", status), context: null);

        Assert.Equal(status, failure?.Status);
        Assert.Equal(code, failure?.Code);
        Assert.Equal(LogLevel.Information, failure?.Level);
    }

    [Fact]
    public void Only_the_buffer_limit_io_failure_is_claimed()
    {
        var classifier = new RequestBodyClassifier();

        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            classifier.Classify(new IOException(RequestBodyClassifier.BufferLimitMessage), context: null)?.Status);
        Assert.Null(classifier.Classify(new IOException("The disk is full."), context: null));
    }

    [Fact]
    public void An_upstream_failure_is_a_bad_gateway_at_warning()
    {
        var failure = new UpstreamUnavailableClassifier().Classify(new UpstreamUnavailableException("mail-relay"), context: null);

        Assert.Equal(StatusCodes.Status502BadGateway, failure?.Status);
        Assert.Equal(ErrorCodes.Upstream, failure?.Code);
        Assert.Equal(LogLevel.Warning, failure?.Level);
        Assert.False(failure?.IsServerFault);
    }

    [Fact]
    public void An_unclaimed_exception_is_unclassified_internal_error()
    {
        var classification = Table().Classify(new FormatException(), context: null);

        Assert.Equal(FailureClassifierTable.UnclassifiedName, classification.Classifier);
        Assert.Equal(ErrorCodes.InternalError, classification.Failure.Code);
        Assert.True(classification.Failure.IsServerFault);
    }

    [Fact]
    public void Host_mappings_apply_to_subclasses_and_registration_order_is_priority()
    {
        var options = new CodelabErrorsOptions()
            .Map<BusinessRuleViolation>(StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRule, LogLevel.Information)
            .Map<InvalidOperationException>(e => new Failure(StatusCodes.Status409Conflict, "OPERATION", "x", LogLevel.Warning));
        var table = new FailureClassifierTable(Options.Create(options));

        Assert.Equal(ErrorCodes.BusinessRule, table.Classify(new SpecificBusinessRuleViolation(), context: null).Failure.Code);
        Assert.Equal("OPERATION", table.Classify(new InvalidOperationException(), context: null).Failure.Code);
        Assert.Equal(
            FrameworkStatusCodes.Describe(StatusCodes.Status422UnprocessableEntity).Detail,
            table.Classify(new BusinessRuleViolation(), context: null).Failure.Detail);
    }

    [Fact]
    public void The_job_resolver_uses_the_same_table()
    {
        var table = Table();

        Assert.Equal(ErrorCodes.Retryable, table.Resolve(new TimeoutException()).Code);
        Assert.Equal(ErrorCodes.InternalError, table.Resolve(null).Code);
        Assert.Equal(LogLevel.Error, table.Resolve(new FormatException()).Level);
    }

    [Fact]
    public void The_built_in_order_is_documented_order()
    {
        Assert.Equal(
            [typeof(RequestBodyClassifier), typeof(UpstreamUnavailableClassifier), typeof(ConcurrencyClassifier), typeof(TransientFaultClassifier)],
            new CodelabErrorsOptions().Classifiers.Select(c => c.GetType()));
    }

    private static FailureClassifierTable Table() => new(Options.Create(new CodelabErrorsOptions()));

    private class BusinessRuleViolation : Exception;

    private sealed class SpecificBusinessRuleViolation : BusinessRuleViolation;
}
