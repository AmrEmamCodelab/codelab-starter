using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Codelab.Observability;

/// <summary>
/// One startup Warning when the <see cref="UserIdHash"/> runs on a random per-process key (no key configured outside
/// Production): hashes then differ across restarts and nodes. A hosted service, because the key is read before the
/// logger exists. Logs the key name only, never a value.
/// </summary>
internal sealed partial class UserIdHashStartupNotice(UserIdHash userIdHash, ILogger<UserIdHashStartupNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (userIdHash.IsEphemeral)
        {
            LogEphemeralKey(logger, UserIdHashSettings.KeyName, LogPropertyNames.UserIdHash);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{ConfigurationKey} is not set, so {Property} uses a random per-process key and differs across restarts and nodes.")]
    private static partial void LogEphemeralKey(ILogger logger, string configurationKey, string property);
}
