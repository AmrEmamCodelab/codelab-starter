namespace Starter.DocsGen.Rendering;

/// <summary>Renders the folder's <c>README.md</c> (static text: what the files are and the commands).</summary>
internal static class ReadmeRenderer
{
    private const string Title = "Generated docs";

    /// <summary>The README.</summary>
    public static string Render() =>
        MarkdownText.Document(Title)
            .Line("Every file in this folder is generated from the code by `backend/tools/Starter.DocsGen`, which starts the")
            .Line("API host in-process (Development, no database) and reads its modules by reflection. Never edit a file here:")
            .Line("change the code, then regenerate. CI fails when a file drifts from the code.")
            .Line()
            .Line("| File | Content |")
            .Line("| --- | --- |")
            .Line($"| `{GeneratedFileNames.OpenApi}` | The OpenAPI document `v1` (normalized: no `servers`, sorted paths and components). Input of the frontend client. |")
            .Line($"| `{GeneratedFileNames.PermissionsJson}` | The permission catalog, `{{permissions:[{{key, module, description}}]}}`. Input of the frontend `PermissionKeys`. |")
            .Line($"| `{GeneratedFileNames.PermissionsMarkdown}` | The permission catalog with the endpoints each key guards. |")
            .Line($"| `{GeneratedFileNames.Endpoints}` | Every endpoint: method, route, permission or allowlisted access, owner, summary. |")
            .Line($"| `{GeneratedFileNames.ErrorCodes}` | Every `*ErrorCodes` constant, by owner. |")
            .Line($"| `{GeneratedFileNames.ConfigKeys}` | Every configuration key of every options class with a `SectionName`. |")
            .Line()
            .Line("Commands (from the repository root; the .NET 10 SDK is private on the build machine, see `tools/README.md`):")
            .Line()
            .Line("```powershell")
            .Line("# regenerate this folder")
            .Line(GeneratedDocSet.RegenerateCommand)
            .Line("# fail (exit 1) and list the drifting files when this folder does not match the code")
            .Line(GeneratedDocSet.CheckCommand)
            .Line("# then regenerate the frontend client and permission keys from openapi.v1.json and permissions.json")
            .Line("npm run api:gen --workspace frontend/starter-web")
            .Line("```")
            .ToString();
}
