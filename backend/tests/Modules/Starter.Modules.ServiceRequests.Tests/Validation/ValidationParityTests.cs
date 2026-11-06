using System.Globalization;
using System.Text.Json;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Tests.Support;

namespace Starter.Modules.ServiceRequests.Tests.Validation;

/// <summary>
/// The backend half of the FE/BE validation parity contract: every case of the shared fixture
/// (<c>frontend/starter-web/src/app/features/service-requests/data/validation-parity.fixtures.json</c>, owned with the
/// client rules) must be accepted or rejected by <see cref="ServiceRequestRules"/> exactly as the fixture says, with the
/// same failing fields. The client's <c>validation-parity.spec.ts</c> runs the same cases against its hand-mirrored
/// rules, so the two sides cannot drift apart silently.
/// </summary>
public sealed class ValidationParityTests
{
    private const string ScopeStep1 = "step1";
    private const string ScopeStep2 = "step2";
    private const string ScopeSubmit = "submit";

    private static readonly string[] FixturePath =
        ["frontend", "starter-web", "src", "app", "features", "service-requests", "data", "validation-parity.fixtures.json"];

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Fixture.Value.Cases.Select(@case => @case.Name))
        {
            data.Add(name);
        }

        return data;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<ParityFixture> Fixture = new(Load);

    [Fact]
    public void The_fixture_covers_every_scope_and_both_outcomes()
    {
        var cases = Fixture.Value.Cases;

        Assert.Equal([ScopeStep1, ScopeStep2, ScopeSubmit], cases.Select(@case => @case.Scope).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(cases, @case => @case.Expected.Valid);
        Assert.Contains(cases, @case => !@case.Expected.Valid);
        Assert.Equal(cases.Count, cases.Select(@case => @case.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_server_rules_agree_with_the_fixture(string name)
    {
        var fixture = Fixture.Value;
        var @case = fixture.Cases.Single(candidate => candidate.Name == name);
        var today = DateOnly.ParseExact(fixture.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var errors = ServiceRequestRules.Validate(@case.Input.ToInput(), ScopeOf(@case.Scope), today).ToDictionary();

        Assert.Equal(@case.Expected.Valid, errors.Count == 0);
        Assert.Equal(@case.Expected.ErrorFields, errors.Keys.Order(StringComparer.Ordinal));
    }

    private static ServiceRequestValidationScope ScopeOf(string scope) => scope switch
    {
        ScopeStep1 => ServiceRequestValidationScope.RequestStep,
        ScopeStep2 => ServiceRequestValidationScope.DetailsStep,
        ScopeSubmit => ServiceRequestValidationScope.Submit,
        _ => throw new InvalidOperationException($"The parity fixture names an unknown scope '{scope}'."),
    };

    private static ParityFixture Load()
    {
        var path = Path.Combine([RepositoryRoot.Find(), .. FixturePath]);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The FE/BE validation parity fixture is missing at {path}. It belongs to features/service-requests (frontend); restore it or delete this sample with tools/remove-module.ps1 -Name ServiceRequests.",
                path);
        }

        return JsonSerializer.Deserialize<ParityFixture>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"The parity fixture {path} is empty.");
    }

    private sealed record ParityFixture(string Today, IReadOnlyList<ParityCase> Cases);

    private sealed record ParityCase(string Name, string Scope, ParityInput Input, ParityExpected Expected);

    private sealed record ParityInput(string? CategoryId, string? Title, string? Description, string? ContactPhone, string? PreferredDate)
    {
        public ServiceRequestInput ToInput() => new(CategoryId, Title, Description, ContactPhone, PreferredDate);
    }

    private sealed record ParityExpected(bool Valid, IReadOnlyList<string> ErrorFields);
}
