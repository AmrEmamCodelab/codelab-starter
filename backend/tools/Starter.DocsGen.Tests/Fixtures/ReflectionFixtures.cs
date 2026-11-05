namespace Starter.DocsGen.Tests.Fixtures;

/// <summary>Fixture error codes (scanned by the convention tests).</summary>
public static class FixtureErrorCodes
{
    /// <summary>The fixture is <c>gone</c>.</summary>
    public const string Gone = "FIXTURE_GONE";

    /// <summary>The fixture is busy.</summary>
    public const string Busy = "FIXTURE_BUSY";

    /// <summary>Not a constant, so not a code.</summary>
    public static readonly string NotACode = "IGNORED";

    /// <summary>Not a string, so not a code.</summary>
    public const int NotAString = 7;
}

/// <summary>Named like an error codes class but not static, so it is ignored.</summary>
public sealed class InstanceErrorCodes
{
    /// <summary>Ignored.</summary>
    public const string Ignored = "INSTANCE_IGNORED";
}

/// <summary>The fixture's mode.</summary>
public enum FixtureMode
{
    /// <summary>Off.</summary>
    Off = 0,

    /// <summary>On.</summary>
    On = 1,
}

/// <summary>Fixture options (section <c>Fixture:Main</c>).</summary>
public sealed class FixtureOptions
{
    /// <summary>The section.</summary>
    public const string SectionName = "Fixture:Main";

    /// <summary>The default limit.</summary>
    public const int DefaultLimit = 5;

    /// <summary>The limit.</summary>
    public int Limit { get; set; } = DefaultLimit;

    /// <summary>The name.</summary>
    public string? Name { get; set; }

    /// <summary>The mode.</summary>
    public FixtureMode Mode { get; set; } = FixtureMode.On;

    /// <summary>An optional timeout.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Computed, never bound.</summary>
    public bool HasName => !string.IsNullOrEmpty(Name);

    /// <summary>The origins.</summary>
    public IList<string> Origins { get; init; } = [];

    /// <summary>The nested part.</summary>
    public FixtureNestedOptions Nested { get; set; } = new();

    /// <summary>The entries.</summary>
    public IList<FixtureNestedOptions> Entries { get; init; } = [];

    /// <summary>The weights.</summary>
    public IDictionary<string, int> Weights { get; init; } = new Dictionary<string, int>();
}

/// <summary>A nested fixture part.</summary>
public sealed class FixtureNestedOptions
{
    /// <summary>Whether it is on.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Switches bound at the section root.</summary>
public sealed class FixtureSwitches : Dictionary<string, bool>
{
    /// <summary>The section.</summary>
    public const string SectionName = "FixtureSwitches";
}

/// <summary>Users bound at the section root (an array).</summary>
public sealed class FixtureUsersOptions
{
    /// <summary>The section.</summary>
    public const string SectionName = "Fixture:Users";

    /// <summary>The users.</summary>
    public IList<FixtureUser> Users { get; } = [];
}

/// <summary>One fixture user.</summary>
public sealed class FixtureUser
{
    /// <summary>The user name.</summary>
    public string? UserName { get; set; }
}

/// <summary>An abstract class with a section is not an options class.</summary>
public abstract class AbstractSectionHolder
{
    /// <summary>The section.</summary>
    public const string SectionName = "Ignored:Abstract";
}
