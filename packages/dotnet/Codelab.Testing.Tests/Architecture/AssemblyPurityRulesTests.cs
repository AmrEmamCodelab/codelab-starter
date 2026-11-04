using Codelab.Testing.Architecture;

namespace Codelab.Testing.Tests.Architecture;

public sealed class AssemblyPurityRulesTests
{
    private const string Pure = "Codelab.Testing.Tests.Fixtures.Purity.Pure";
    private const string Impure = "Codelab.Testing.Tests.Fixtures.Purity.Impure";

    [Fact]
    public void An_assembly_without_hosting_references_passes()
    {
        var result = AssemblyPurityRules.ReferencesNone(typeof(System.Text.Json.JsonSerializer).Assembly, AssemblyPurityRules.HostingAndPersistencePrefixes);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void An_assembly_referencing_hosting_is_reported()
    {
        var result = AssemblyPurityRules.ReferencesNone(typeof(AssemblyPurityRulesTests).Assembly, AssemblyPurityRules.HostingAndPersistencePrefixes);

        Assert.Contains(result.Violations, violation => violation.StartsWith("references Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.Contains(result.Violations, violation => violation == "references Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void Plain_signatures_pass()
    {
        var result = AssemblyPurityRules.SignaturesMentionNone(typeof(AssemblyPurityRulesTests).Assembly, Pure, AssemblyPurityRules.HostingAndPersistencePrefixes);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void A_configuration_type_hidden_in_a_generic_argument_is_reported()
    {
        var result = AssemblyPurityRules.SignaturesMentionNone(typeof(AssemblyPurityRulesTests).Assembly, Impure, AssemblyPurityRules.HostingAndPersistencePrefixes);

        var violation = Assert.Single(result.Violations);
        Assert.StartsWith("IImpureCanonicalizer -> Microsoft.Extensions.Options.IOptions`1", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_namespace_is_vacuous()
    {
        var result = AssemblyPurityRules.SignaturesMentionNone(typeof(AssemblyPurityRulesTests).Assembly, "No.Such.Namespace", ["X"]);

        Assert.True(result.IsVacuous);
    }
}
