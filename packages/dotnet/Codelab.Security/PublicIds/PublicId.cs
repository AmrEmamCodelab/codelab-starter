using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Codelab.Security.PublicIds;

/// <summary>
/// A surrogate identifier of family <typeparamref name="TDomain"/> that only ever travels as an opaque token. There is
/// no conversion to or from a number on the wire: JSON writes and reads the token (via its converter, whatever
/// serializer options the host uses), route and query values bind through <see cref="TryParse(string?, out PublicId{TDomain})"/>
/// (minimal APIs) or the MVC model binder, and <c>default</c> is "no identifier" (model absence with a nullable).
/// </summary>
[JsonConverter(typeof(PublicIdJsonConverterFactory))]
public readonly struct PublicId<TDomain> : IEquatable<PublicId<TDomain>>
    where TDomain : IIdDomain
{
    private readonly long _value;

    private PublicId(long value) => _value = value;

    /// <summary>True when an identifier is carried (default carries none).</summary>
    public bool HasValue => _value >= PublicIdFormat.MinimumId;

    /// <summary>The internal identifier (server side only).</summary>
    /// <exception cref="InvalidOperationException">No identifier is carried.</exception>
    public long Value => HasValue
        ? _value
        : throw new InvalidOperationException($"This PublicId<{TDomain.Domain}> carries no identifier; model absence with a nullable.");

    /// <summary>Wraps an internal identifier.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "A typed identifier's factory belongs on the type; PublicId<T>.From(id) names the family at the call site.")]
    public static PublicId<TDomain> From(long value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, PublicIdFormat.MinimumId);
        return new PublicId<TDomain>(value);
    }

    /// <summary>
    /// Reads a token minted for this family (also the minimal-API binding hook for route and query values: a value that
    /// does not parse is answered 400 by the framework). A decimal number is never accepted.
    /// </summary>
    /// <exception cref="InvalidOperationException">No codec is installed (a host configuration error).</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Minimal APIs bind route and query values through a static TryParse on the parameter type.")]
    public static bool TryParse([NotNullWhen(true)] string? token, out PublicId<TDomain> id)
    {
        if (PublicIdCodecAccessor.Codec.TryUnprotect(TDomain.Domain, token, out var value))
        {
            id = new PublicId<TDomain>(value);
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>The token.</summary>
    /// <exception cref="InvalidOperationException">No identifier is carried, or no codec is installed.</exception>
    public string ToToken() => HasValue
        ? PublicIdCodecAccessor.Codec.Protect(TDomain.Domain, _value)
        : throw new InvalidOperationException($"This PublicId<{TDomain.Domain}> carries no identifier, so it has no token.");

    /// <summary>The token, or a placeholder; never the number.</summary>
    public override string ToString() =>
        HasValue && PublicIdCodecAccessor.TryGetCodec(out var codec) ? codec.Protect(TDomain.Domain, _value) : $"PublicId<{TDomain.Domain}>()";

    /// <inheritdoc />
    public bool Equals(PublicId<TDomain> other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PublicId<TDomain> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>Equality.</summary>
    public static bool operator ==(PublicId<TDomain> left, PublicId<TDomain> right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(PublicId<TDomain> left, PublicId<TDomain> right) => !left.Equals(right);
}

/// <summary>Recognises <c>PublicId&lt;T&gt;</c> and <c>PublicId&lt;T&gt;?</c> (one owner for the converter, binder and schema transformer).</summary>
public static class PublicIdTypes
{
    /// <summary>True when <paramref name="type"/> is a PublicId (optionally nullable); returns its domain type.</summary>
    public static bool TryGetDomain(Type type, [NotNullWhen(true)] out Type? domain, out bool nullable)
    {
        ArgumentNullException.ThrowIfNull(type);
        var underlying = Nullable.GetUnderlyingType(type);
        nullable = underlying is not null;
        var candidate = underlying ?? type;
        domain = candidate.IsGenericType && !candidate.IsGenericTypeDefinition && candidate.GetGenericTypeDefinition() == typeof(PublicId<>)
            ? candidate.GetGenericArguments()[0]
            : null;
        return domain is not null;
    }
}
