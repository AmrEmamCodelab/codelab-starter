namespace Starter.Infrastructure.Hosting;

/// <summary>The route prefix every API endpoint shares. Modules build their routes from it (no route literal elsewhere).</summary>
public static class ApiRoutes
{
    /// <summary>The prefix of every API route (<c>/api/...</c>).</summary>
    public const string Prefix = "api";

    /// <summary>The separator between route segments.</summary>
    public const string Separator = "/";
}
