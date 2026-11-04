using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace Codelab.Observability;

/// <summary>
/// The caller's identity as a log line may carry it: HMAC-SHA256(key, NameIdentifier), first 16 lowercase hex
/// characters. Same user and key, same value, so one person's requests can be followed across lines; without the
/// key it cannot be reversed, which a plain hash over a small id space can be. Logs only: never spans or metrics.
/// <para>
/// <b>The key</b> is <see cref="UserIdHashSettings.KeyName"/>, base64 of at least <see cref="MinimumKeyBytes"/>
/// bytes, generated per environment by init and held as an app-pool variable. There is no committed default:
/// Development without a key uses a random per-process key (<see cref="IsEphemeral"/>), so no published value
/// exists; Production without a key refuses to start. Outside Development a key that is published material (a
/// harvest-source key or a starter test fixture key, <see cref="PublishedKeyDenylist"/>) refuses to start too: anyone
/// holding it could reverse the pseudonyms in the logs.
/// </para>
/// </summary>
public sealed class UserIdHash
{
    /// <summary>The shortest key accepted, in decoded bytes.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The length of a hash value, in hex characters.</summary>
    public const int HashLength = 16;

    private readonly byte[] _key;

    private UserIdHash(byte[] key, bool isEphemeral)
    {
        _key = key;
        IsEphemeral = isEphemeral;
    }

    /// <summary>True when no key was configured and a random per-process key is in use (hashes differ across restarts).</summary>
    public bool IsEphemeral { get; }

    /// <summary>
    /// The hash for the configured key. Throws <see cref="InvalidOperationException"/>, naming the key but never its
    /// value, when it is missing in Production, is not base64, decodes to fewer than <see cref="MinimumKeyBytes"/>, or
    /// is published key material outside Development.
    /// </summary>
    public static UserIdHash Create(UserIdHashSettings settings, IHostEnvironment environment) =>
        Create(settings, environment, PublishedKeyDenylist.Embedded);

    internal static UserIdHash Create(UserIdHashSettings settings, IHostEnvironment environment, PublishedKeyDenylist denylist)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(settings.Key))
        {
            var key = Decode(settings.Key);
            RefusePublished(settings.Key, key, environment, denylist);
            return new UserIdHash(key, isEphemeral: false);
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{UserIdHashSettings.KeyName} is required in Production. Generate a per-environment key (init does) and set it as an app-pool variable.");
        }

        return new UserIdHash(RandomNumberGenerator.GetBytes(MinimumKeyBytes), isEphemeral: true);
    }

    /// <summary>The hash of <paramref name="nameIdentifier"/>, or null when it is blank.</summary>
    public string? Of(string? nameIdentifier)
    {
        if (string.IsNullOrWhiteSpace(nameIdentifier))
        {
            return null;
        }

        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(nameIdentifier));
        return Convert.ToHexStringLower(mac)[..HashLength];
    }

    /// <summary>The hash of the principal's <see cref="ClaimTypes.NameIdentifier"/>; null for an anonymous caller.</summary>
    public string? Of(ClaimsPrincipal? user) => Of(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value);

    private static void RefusePublished(string keyText, byte[] key, IHostEnvironment environment, PublishedKeyDenylist denylist)
    {
        if (!environment.IsDevelopment() && denylist.IsPublished(keyText, key))
        {
            throw new InvalidOperationException(
                $"{UserIdHashSettings.KeyName} is PUBLISHED key material (a harvest-source key or a starter test fixture key); anyone with the source can reverse the user hashes in the logs. Generate a fresh per-environment key (init does) and set it as an app-pool variable.");
        }
    }

    private static byte[] Decode(string keyBase64)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(keyBase64.Trim());
        }
        catch (FormatException ex)
        {
            // The parser's message names no part of the input, so the inner exception is safe to keep.
            throw new InvalidOperationException($"{UserIdHashSettings.KeyName} is not valid base64.", ex);
        }

        if (decoded.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"{UserIdHashSettings.KeyName} decodes to {decoded.Length} bytes; at least {MinimumKeyBytes} are required.");
        }

        return decoded;
    }
}
