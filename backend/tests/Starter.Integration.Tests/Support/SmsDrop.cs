using System.Text;
using System.Text.Json;

namespace Starter.Integration.Tests.Support;

/// <summary>One SMS the file SMS sender recorded.</summary>
/// <param name="At">When it was recorded (host clock).</param>
/// <param name="To">The mobile number.</param>
/// <param name="Language">The message language as its <c>AppLanguage</c> integer.</param>
/// <param name="Text">The rendered text.</param>
public sealed record RecordedSms(DateTimeOffset At, string To, int Language, string Text);

/// <summary>The JSON-lines file a test host's file SMS sender appends to (the stand-in for an SMS gateway).</summary>
/// <param name="path">The file (it may not exist yet: no SMS was sent).</param>
public sealed class SmsDrop(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The SMS file.</summary>
    public string Path { get; } = path;

    /// <summary>Every recorded SMS, in file order.</summary>
    public IReadOnlyList<RecordedSms> ReadAll() =>
        File.Exists(Path) ? Parse(File.ReadAllText(Path, Encoding.UTF8)) : [];

    /// <summary>Every recorded SMS to <paramref name="mobile"/>, in file order.</summary>
    public IReadOnlyList<RecordedSms> To(string mobile) =>
        ReadAll().Where(sms => string.Equals(sms.To, mobile, StringComparison.Ordinal)).ToList();

    /// <summary>Parses JSON lines (blank lines are skipped).</summary>
    /// <exception cref="JsonException">A line is not an SMS record.</exception>
    public static IReadOnlyList<RecordedSms> Parse(string jsonLines)
    {
        ArgumentNullException.ThrowIfNull(jsonLines);
        return jsonLines
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<RecordedSms>(line, Json) ?? throw new JsonException("An empty SMS record."))
            .ToList();
    }
}
