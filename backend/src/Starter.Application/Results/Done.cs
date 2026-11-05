namespace Starter.Application.Results;

/// <summary>The success value of a use case that returns nothing but may refuse (<c>Outcome&lt;Done&gt;</c>).</summary>
public sealed record Done
{
    private Done()
    {
    }

    /// <summary>The single value.</summary>
    public static Done Value { get; } = new();
}
