using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Core.Storage;

/// <summary>Where stored files live (<c>Attachments:Storage</c>).</summary>
public sealed class AttachmentStorageOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Attachments:Storage";

    /// <summary>
    /// The storage root: a fully qualified local folder or a UNC share. Files are written to
    /// <c>&lt;RootPath&gt;/&lt;yyyy&gt;/&lt;MM&gt;/&lt;fileId&gt;</c>; the app pool identity needs modify rights here only.
    /// </summary>
    public string? RootPath { get; set; }
}

/// <summary>The storage root must be set and fully qualified (a relative root would follow the working directory).</summary>
internal sealed class AttachmentStorageOptionsValidator : IValidateOptions<AttachmentStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, AttachmentStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return ValidateOptionsResult.Fail($"{AttachmentStorageOptions.SectionName}:RootPath is required.");
        }

        return Path.IsPathFullyQualified(options.RootPath)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{AttachmentStorageOptions.SectionName}:RootPath must be a fully qualified folder or UNC share.");
    }
}
