using System.Reflection;
using System.Xml.Linq;
using Codelab.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Starter.Application.Authorization;
using Starter.DocsGen.Collection;
using Starter.DocsGen.Tests.Fixtures;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Modularity;

namespace Starter.DocsGen.Tests;

/// <summary>Handlers whose XML docs the endpoint tests read.</summary>
public static class FixtureHandlers
{
    /// <summary>Reads the <c>fixture</c>.</summary>
    public static string Read() => string.Empty;
}

public sealed class OwnerNamingTests
{
    private static readonly OwnerNaming Owners = new(typeof(Program).Assembly);

    [Fact]
    public void A_module_assembly_is_owned_by_its_module()
    {
        Assert.Equal("Access", Owners.OwnerOf(Assembly.Load("Starter.Modules.Access")));
    }

    [Fact]
    public void Other_solution_assemblies_are_the_kernel()
    {
        Assert.Equal(OwnerNaming.KernelOwner, Owners.OwnerOf(typeof(ModuleCatalog)));
    }

    [Fact]
    public void A_package_is_owned_by_its_assembly_name()
    {
        Assert.Equal("Codelab.Security", Owners.OwnerOf(typeof(SecuritySections)));
    }
}

public sealed class SolutionAssembliesTests
{
    [Fact]
    public void Discovers_the_host_kernel_modules_and_packages_sorted()
    {
        var host = typeof(Program).Assembly;

        var names = SolutionAssemblies.Discover(host, ModuleCatalog.Discover(host)).Select(assembly => assembly.GetName().Name).ToList();

        Assert.Contains("Starter.Api", names);
        Assert.Contains("Starter.Application", names);
        Assert.Contains("Starter.Modules.Access", names);
        Assert.Contains("Starter.Modules.Identity", names);
        Assert.Contains("Codelab.Security", names);
        Assert.DoesNotContain(names, name => name!.StartsWith("Microsoft.", StringComparison.Ordinal));
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
    }

    [Theory]
    [InlineData("Starter.Domain", true)]
    [InlineData("Codelab.Errors", true)]
    [InlineData("Microsoft.AspNetCore", false)]
    [InlineData("StarterX", false)]
    [InlineData(null, false)]
    public void Documents_only_solution_and_package_assemblies(string? name, bool expected)
    {
        Assert.Equal(expected, SolutionAssemblies.IsDocumented(name, "Starter."));
    }
}

public sealed class ConventionScannerTests
{
    private static readonly OwnerNaming Owners = new(typeof(Program).Assembly);

    [Fact]
    public void Error_codes_are_the_string_constants_of_static_ErrorCodes_classes()
    {
        var codes = ConventionScanner.ErrorCodes([typeof(FixtureErrorCodes).Assembly], Owners, new XmlDocumentation());

        Assert.Equal(["FIXTURE_BUSY", "FIXTURE_GONE"], codes.Select(code => code.Code));
        var gone = codes.Single(code => code.Code == FixtureErrorCodes.Gone);
        Assert.Equal("FixtureErrorCodes.Gone", gone.Constant);
        Assert.Equal(OwnerNaming.KernelOwner, gone.Owner);
        Assert.Equal("The fixture is `gone`.", gone.Description);
    }

    [Fact]
    public void Only_public_static_classes_count()
    {
        Assert.True(ConventionScanner.IsErrorCodesClass(typeof(FixtureErrorCodes)));
        Assert.False(ConventionScanner.IsErrorCodesClass(typeof(InstanceErrorCodes)));
        Assert.False(ConventionScanner.IsErrorCodesClass(typeof(FixtureOptions)));
    }

    [Fact]
    public void Permissions_are_owned_by_the_source_and_sorted_by_key()
    {
        var permissions = ConventionScanner.Permissions([new FixtureCatalogSource()], Owners);

        Assert.Equal(["fixture.edit", "fixture.view"], permissions.Select(permission => permission.Key));
        Assert.All(permissions, permission => Assert.Equal(OwnerNaming.KernelOwner, permission.Module));
        Assert.Equal("Edit it.", permissions[0].Description);
    }

    private sealed class FixtureCatalogSource : IPermissionCatalogSource
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
            [new("fixture.view", "View it."), new("fixture.edit", "Edit it.")];
    }
}

public sealed class ConfigKeyScannerTests
{
    private static readonly IReadOnlyList<ConfigKeyDoc> Keys =
        ConfigKeyScanner.Scan([typeof(FixtureOptions).Assembly], new OwnerNaming(typeof(Program).Assembly), new XmlDocumentation());

    [Fact]
    public void Scalars_carry_type_default_and_description()
    {
        var limit = Keys.Single(key => key.Key == "Fixture:Main:Limit");

        Assert.Equal("int", limit.Type);
        Assert.Equal("5", limit.DefaultValue);
        Assert.Equal("The limit.", limit.Description);
        Assert.Equal(FixtureOptions.SectionName, limit.Section);
        Assert.Equal(string.Empty, Keys.Single(key => key.Key == "Fixture:Main:Name").DefaultValue);
    }

    [Fact]
    public void Enums_list_their_members_and_nullable_scalars_unwrap()
    {
        var mode = Keys.Single(key => key.Key == "Fixture:Main:Mode");

        Assert.Equal("enum FixtureMode (Off, On)", mode.Type);
        Assert.Equal("On", mode.DefaultValue);
        Assert.Equal("TimeSpan", Keys.Single(key => key.Key == "Fixture:Main:Timeout").Type);
    }

    [Fact]
    public void Collections_dictionaries_and_nested_objects_follow_the_binder()
    {
        Assert.Contains(Keys, key => key.Key == "Fixture:Main:Origins:<n>" && key.Type == "string");
        Assert.Contains(Keys, key => key.Key == "Fixture:Main:Weights:<name>" && key.Type == "int");
        Assert.Contains(Keys, key => key.Key == "Fixture:Main:Nested:Enabled" && key.DefaultValue == "true");
        Assert.Contains(Keys, key => key.Key == "Fixture:Main:Entries:<n>:Enabled");
    }

    [Fact]
    public void Computed_read_only_properties_are_not_keys()
    {
        Assert.DoesNotContain(Keys, key => key.Key.EndsWith(nameof(FixtureOptions.HasName), StringComparison.Ordinal));
    }

    [Fact]
    public void Root_dictionaries_and_root_collections_bind_at_the_section()
    {
        Assert.Contains(Keys, key => key.Key == "FixtureSwitches:<name>" && key.Type == "bool");
        Assert.Contains(Keys, key => key.Key == "Fixture:Users:<n>:UserName");
        Assert.DoesNotContain(Keys, key => key.Key.Contains(":Users:Users", StringComparison.Ordinal));
    }

    [Fact]
    public void Abstract_classes_and_classes_without_a_section_are_ignored()
    {
        Assert.Null(ConfigKeyScanner.SectionNameOf(typeof(AbstractSectionHolder)));
        Assert.Null(ConfigKeyScanner.SectionNameOf(typeof(FixtureNestedOptions)));
        Assert.Equal(FixtureOptions.SectionName, ConfigKeyScanner.SectionNameOf(typeof(FixtureOptions)));
    }

    [Fact]
    public void Keys_are_sorted_and_unique()
    {
        var ordered = Keys.OrderBy(key => key.Owner, StringComparer.Ordinal).ThenBy(key => key.Section, StringComparer.Ordinal).ThenBy(key => key.Key, StringComparer.Ordinal);

        Assert.Equal(ordered, Keys);
        Assert.Equal(Keys.Count, Keys.Select(key => key.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData(null, "")]
    [InlineData(1.5, "1.5")]
    public void Defaults_are_invariant_text(object? value, string expected)
    {
        Assert.Equal(expected, TypeShapes.FormatDefault(value));
    }
}

public sealed class EndpointCollectorTests
{
    private static readonly OwnerNaming Owners = new(typeof(Program).Assembly);

    [Fact]
    public void Anonymous_wins_over_permissions()
    {
        var (access, permissions) = EndpointCollector.ClassifyAccess([new HasPermissionAttribute("a.b"), new AllowAnonymousAttribute()]);

        Assert.Equal(EndpointAccessKind.Anonymous, access);
        Assert.Empty(permissions);
    }

    [Fact]
    public void Permissions_are_distinct_and_sorted()
    {
        var (access, permissions) = EndpointCollector.ClassifyAccess(
            [new HasPermissionAttribute("z.view"), new HasPermissionAttribute("a.view"), new HasPermissionAttribute("z.view")]);

        Assert.Equal(EndpointAccessKind.Permission, access);
        Assert.Equal(["a.view", "z.view"], permissions);
    }

    [Fact]
    public void Everything_else_is_authenticated()
    {
        Assert.Equal(EndpointAccessKind.Authenticated, EndpointCollector.ClassifyAccess([new AuthenticatedUserAttribute()]).Access);
        Assert.Equal(EndpointAccessKind.Authenticated, EndpointCollector.ClassifyAccess([]).Access);
    }

    [Theory]
    [InlineData("api/x", "/api/x")]
    [InlineData("/api/x", "/api/x")]
    [InlineData("//api/x", "/api/x")]
    [InlineData(null, "/")]
    public void Routes_get_one_leading_slash(string? raw, string expected)
    {
        Assert.Equal(expected, EndpointCollector.NormalizeRoute(raw));
    }

    [Fact]
    public void One_row_per_method_sorted_deduplicated_with_owner_and_summary()
    {
        var handler = typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.Read))!;
        Endpoint[] endpoints =
        [
            Route("api/b", handler, new HttpMethodMetadata(["POST", "GET"])),
            Route("api/b", handler, new HttpMethodMetadata(["GET"])),
            Route("api/a", null, new AllowAnonymousAttribute()),
            new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "not routed"),
        ];

        var rows = EndpointCollector.Collect(endpoints, Owners, new XmlDocumentation());

        Assert.Equal(["ANY /api/a", "GET /api/b", "POST /api/b"], rows.Select(row => row.Method + " " + row.Route));
        Assert.Equal(OwnerNaming.UnknownOwner, rows[0].Owner);
        Assert.Equal(OwnerNaming.KernelOwner, rows[1].Owner);
        Assert.Equal("Reads the `fixture`.", rows[1].Summary);
        Assert.Equal(EndpointAccessKind.Anonymous, rows[0].Access);
    }

    private static RouteEndpoint Route(string pattern, MethodInfo? handler, params object[] metadata) =>
        new(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            new EndpointMetadataCollection(handler is null ? metadata : [.. metadata, handler]),
            displayName: pattern);
}

public sealed class XmlDocumentationTests
{
    [Fact]
    public void Flatten_turns_inline_tags_into_code_spans_on_one_line()
    {
        var summary = XElement.Parse(
            "<summary>Uses <c>x</c> and <see cref=\"T:Ns.Thing\"/>,\n  <see cref=\"M:Ns.Thing.Do(System.Int32)\"/>, <see langword=\"null\"/> or <paramref name=\"value\"/>.</summary>");

        Assert.Equal("Uses `x` and `Thing`, `Do`, `null` or `value`.", XmlDocumentation.Flatten(summary));
    }

    [Fact]
    public void Parse_maps_member_ids_to_summaries()
    {
        var document = XDocument.Parse("<doc><members><member name=\"F:A.B\"><summary> One. </summary></member><member name=\"F:A.C\"/></members></doc>");

        var members = XmlDocumentation.Parse(document);

        Assert.Equal("One.", members["F:A.B"]);
        Assert.False(members.ContainsKey("F:A.C"));
    }

    [Fact]
    public void Reads_the_assembly_documentation_file_for_types_fields_properties_and_methods()
    {
        var documentation = new XmlDocumentation();

        Assert.Equal("The fixture's mode.", documentation.SummaryOf(typeof(FixtureMode)));
        Assert.Equal("The limit.", documentation.SummaryOf(typeof(FixtureOptions).GetProperty(nameof(FixtureOptions.Limit))!));
        Assert.Equal("The fixture is busy.", documentation.SummaryOf(typeof(FixtureErrorCodes).GetField(nameof(FixtureErrorCodes.Busy))!));
        Assert.Equal("Reads the `fixture`.", documentation.SummaryOf(typeof(FixtureHandlers).GetMethod(nameof(FixtureHandlers.Read))!));
    }

    [Fact]
    public void Undocumented_members_have_an_empty_summary()
    {
        var documentation = new XmlDocumentation();

        Assert.Equal(string.Empty, documentation.SummaryOf(typeof(string).GetProperty(nameof(string.Length))!));
        Assert.Equal(string.Empty, documentation.SummaryOf(typeof(FixtureOptions).GetConstructor(Type.EmptyTypes)!));
    }
}

public sealed class DocsHostSettingsTests
{
    [Fact]
    public void Turns_off_the_database_and_uses_memory_stores()
    {
        var settings = DocsHostSettings.Create();

        Assert.Equal(bool.FalseString, settings["Database:ApplyMigrationsOnStartup"]);
        Assert.Equal("Memory", settings["Codelab:Security:Sessions:Store"]);
        Assert.Equal("Memory", settings["Codelab:Security:ReplayProtection:Store"]);
        Assert.Equal(bool.FalseString, settings["Codelab:Security:SqlStore:EnsureSchemaOnStart"]);
        Assert.Equal(DocsHostSettings.UnreachableConnectionString, settings["ConnectionStrings:Starter"]);
    }

    [Fact]
    public void Secrets_are_fresh_random_values_for_every_start()
    {
        var first = DocsHostSettings.Create();
        var second = DocsHostSettings.Create();

        foreach (var key in new[] { DocsHostSettings.JwtSigningKeyPath, DocsHostSettings.BreakGlassPasswordPath, DocsHostSettings.DevUserPasswordPath })
        {
            Assert.NotEqual(first[key], second[key]);
            Assert.Equal(32, Convert.FromBase64String(first[key]).Length);
        }

        Assert.Equal(first["Codelab:Security:PublicIds:ActiveKeyId"], first["Codelab:Security:PublicIds:Keys:0:KeyId"]);
    }
}

public sealed class LocalSecretsFileRemovalTests
{
    [Fact]
    public void Only_the_local_secrets_file_is_removed()
    {
        var kept = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource { Path = "appsettings.Development.json" };
        var local = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource { Path = "appsettings.Development.local.json" };
        var sources = new List<Microsoft.Extensions.Configuration.IConfigurationSource> { kept, local };

        DocsHostFactory.RemoveLocalSecretsFile(sources);

        Assert.Equal([kept], sources);
    }
}
