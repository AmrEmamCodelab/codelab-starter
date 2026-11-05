using System.Diagnostics.CodeAnalysis;
using Microsoft.IdentityModel.Tokens;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Modules.Identity.Infrastructure.Security;

/// <summary>The access token signing key: decoded once from <see cref="JwtOptions.SigningKey"/> (base64).</summary>
public static class SigningKey
{
    /// <summary>The token signature algorithm.</summary>
    public const string Algorithm = SecurityAlgorithms.HmacSha256;

    /// <summary>True when <paramref name="base64"/> decodes to at least <see cref="JwtOptions.MinimumSigningKeyBytes"/> bytes.</summary>
    public static bool TryDecode(string? base64, [NotNullWhen(true)] out byte[]? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        var buffer = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, buffer, out var written) || written < JwtOptions.MinimumSigningKeyBytes)
        {
            return false;
        }

        key = buffer[..written];
        return true;
    }

    /// <summary>The security key of validated <paramref name="options"/>.</summary>
    /// <exception cref="InvalidOperationException">The key is missing or too short (the options validator reports it on start).</exception>
    public static SymmetricSecurityKey Of(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return TryDecode(options.SigningKey, out var key)
            ? new SymmetricSecurityKey(key)
            : throw new InvalidOperationException($"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} is missing or too short.");
    }
}
