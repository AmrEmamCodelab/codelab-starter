using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codelab.Security.PublicIds;

/// <summary>Creates the converters for <c>PublicId&lt;T&gt;</c> and <c>PublicId&lt;T&gt;?</c>.</summary>
public sealed class PublicIdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => PublicIdTypes.TryGetDomain(typeToConvert, out _, out _);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!PublicIdTypes.TryGetDomain(typeToConvert, out var domain, out var nullable))
        {
            throw new ArgumentException($"{nameof(PublicIdJsonConverterFactory)} converts PublicId<T> and PublicId<T>? only.", nameof(typeToConvert));
        }

        var converter = nullable ? typeof(NullablePublicIdJsonConverter<>) : typeof(PublicIdJsonConverter<>);
        return (JsonConverter)Activator.CreateInstance(converter.MakeGenericType(domain))!;
    }
}

/// <summary>
/// Writes a PublicId as its token string and reads only a token string: a JSON number, a decimal string, null or any
/// other JSON type is refused with one message that names no reason and never echoes the value.
/// </summary>
public sealed class PublicIdJsonConverter<TDomain> : JsonConverter<PublicId<TDomain>>
    where TDomain : IIdDomain
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override PublicId<TDomain> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType is JsonTokenType.String ? Decode(reader.GetString()) : throw new JsonException(PublicIdFormat.UnreadableMessage);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PublicId<TDomain> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToToken());
    }

    /// <inheritdoc />
    public override PublicId<TDomain> ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Decode(reader.GetString());

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, PublicId<TDomain> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.ToToken());
    }

    internal static PublicId<TDomain> Decode(string? token) =>
        PublicId<TDomain>.TryParse(token, out var id) ? id : throw new JsonException(PublicIdFormat.UnreadableMessage);
}

/// <summary>The nullable twin: JSON null reads as absent; everything else follows <see cref="PublicIdJsonConverter{TDomain}"/>.</summary>
public sealed class NullablePublicIdJsonConverter<TDomain> : JsonConverter<PublicId<TDomain>?>
    where TDomain : IIdDomain
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override PublicId<TDomain>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.String => PublicIdJsonConverter<TDomain>.Decode(reader.GetString()),
        _ => throw new JsonException(PublicIdFormat.UnreadableMessage),
    };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PublicId<TDomain>? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is { } id)
        {
            writer.WriteStringValue(id.ToToken());
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
