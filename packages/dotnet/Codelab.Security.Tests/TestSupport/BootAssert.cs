using Microsoft.Extensions.Options;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>Asserts that a host configured with <see cref="TestHostOptions"/> refuses to start.</summary>
internal static class BootAssert
{
    /// <summary>Builds and starts the host; the build or the start must throw, naming <paramref name="expectedFragment"/>.</summary>
    public static async Task<Exception> FailsAsync(TestHostOptions options, string expectedFragment)
    {
        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var host = await SecurityTestHost.StartAsync(options);
        });

        Assert.NotNull(failure);
        var causes = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToList() : [failure];
        Assert.All(causes, cause => Assert.True(cause is OptionsValidationException or InvalidOperationException, $"Unexpected failure type {cause.GetType()}: {cause.Message}"));
        Assert.Contains(causes, cause => cause.Message.Contains(expectedFragment, StringComparison.Ordinal));
        return failure;
    }

    /// <summary>Builds and starts the host, which must succeed, then stops it.</summary>
    public static async Task SucceedsAsync(TestHostOptions options)
    {
        await using var host = await SecurityTestHost.StartAsync(options);
    }
}
