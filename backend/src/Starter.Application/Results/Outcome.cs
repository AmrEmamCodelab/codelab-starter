using System.Diagnostics.CodeAnalysis;

namespace Starter.Application.Results;

/// <summary>The result of a use case: a value, or a <see cref="Results.Refusal"/>. Never both.</summary>
/// <typeparam name="T">The success value.</typeparam>
public sealed class Outcome<T>
{
    private readonly T? _value;

    internal Outcome(T? value, Refusal? refusal)
    {
        _value = value;
        Refusal = refusal;
    }

    /// <summary>The refusal, or null on success.</summary>
    public Refusal? Refusal { get; }

    /// <summary>True when the use case succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool IsSuccess => Refusal is null;

    /// <summary>The success value.</summary>
    /// <exception cref="InvalidOperationException">The outcome is a refusal.</exception>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"A refused outcome ({Refusal.Code}) has no value.");

    /// <summary>Lets a use case <c>return refusal;</c>.</summary>
    public static implicit operator Outcome<T>(Refusal refusal) => Outcome.Refused<T>(refusal);

    /// <summary>Lets a use case <c>return value;</c>.</summary>
    public static implicit operator Outcome<T>(T value) => Outcome.Success(value);
}

/// <summary>Creates <see cref="Outcome{T}"/> values.</summary>
public static class Outcome
{
    /// <summary>A success carrying <paramref name="value"/>.</summary>
    public static Outcome<T> Success<T>(T value) => new(value, refusal: null);

    /// <summary>A refusal.</summary>
    public static Outcome<T> Refused<T>(Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new Outcome<T>(value: default, refusal);
    }
}
