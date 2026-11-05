using Starter.DevIdp.Hosting;

namespace Starter.DevIdp;

/// <summary>
/// The console entry point (a named class, not top-level statements: a test project that references this tool and the
/// API host would otherwise see two global <c>Program</c> types).
/// </summary>
internal static class DevIdpEntryPoint
{
    private static async Task<int> Main(string[] args)
    {
        WebApplication app;
        try
        {
            app = DevIdpApp.Build(args);
        }
        catch (DevIdpEnvironmentException refused)
        {
            await Console.Error.WriteLineAsync(refused.Message);
            return (int)DevIdpExitCode.RefusedEnvironment;
        }

        await using (app)
        {
            await app.RunAsync();
        }

        return (int)DevIdpExitCode.Success;
    }
}

/// <summary>The process exit codes of the dev IdP.</summary>
internal enum DevIdpExitCode
{
    /// <summary>The IdP ran and stopped normally.</summary>
    Success = 0,

    /// <summary>The environment is not Development, so the IdP refused to start.</summary>
    RefusedEnvironment = 2,
}
