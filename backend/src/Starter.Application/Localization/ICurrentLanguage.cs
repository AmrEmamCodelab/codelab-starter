using Starter.Domain.Localization;

namespace Starter.Application.Localization;

/// <summary>The language of the current request (from the client's <c>Accept-Language</c>; Arabic by default).</summary>
public interface ICurrentLanguage
{
    /// <summary>The resolved language; never fails, falls back to <see cref="AppLanguage.Arabic"/>.</summary>
    AppLanguage Language { get; }
}
