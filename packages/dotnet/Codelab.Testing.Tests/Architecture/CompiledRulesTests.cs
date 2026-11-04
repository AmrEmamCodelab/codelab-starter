using Codelab.Testing.Architecture;
using Codelab.Testing.Tests.Support;

namespace Codelab.Testing.Tests.Architecture;

public sealed class CompiledRulesTests
{
    [Fact]
    public void The_repository_rules_are_read_and_list_the_package_modules()
    {
        var rules = CompiledRules.LoadFromRepository();

        Assert.Equal(1, rules.SchemaVersion);
        var testing = rules.Get("CodelabTesting");
        Assert.True(testing.IsDotnet);
        Assert.Equal("packages/dotnet/Codelab.Testing", testing.Path);
        Assert.Contains("CodelabSecurity", testing.MustNotDependOn);
        Assert.Contains(rules.DotnetModules, module => module.Module == "CodelabIntegration");
    }

    [Fact]
    public void Missing_lists_default_to_empty_and_unknown_modules_are_refused()
    {
        var rules = CompiledRules.Parse("""{ "schemaVersion": 1, "modules": [ { "module": "A", "kind": "angular", "path": "web" } ] }""", "inline");

        var module = Assert.Single(rules.Modules);
        Assert.False(module.IsDotnet);
        Assert.Empty(module.MayDependOn);
        Assert.Empty(rules.DotnetModules);
        Assert.Throws<KeyNotFoundException>(() => rules.Get("B"));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "modules": [ { "kind": "dotnet", "path": "x" } ] }""")]
    [InlineData("""{ "schemaVersion": 1, "modules": [ { "module": "A", "path": "x" } ] }""")]
    [InlineData("null")]
    public void Malformed_rules_are_refused(string json) =>
        Assert.Throws<InvalidDataException>(() => CompiledRules.Parse(json, "inline"));

    [Fact]
    public void Rules_load_from_a_file_and_the_root_is_found_by_its_marker()
    {
        using var workspace = new TempWorkspace();
        workspace.Write(CompiledRules.RelativePath, """{ "schemaVersion": 2, "modules": [] }""");
        var nested = workspace.Directory("a/b/c");

        Assert.Equal(workspace.Root, RepositoryRoot.Find(CompiledRules.RelativePath, nested));
        Assert.Equal(2, CompiledRules.LoadFromRepository(nested).SchemaVersion);
        Assert.Throws<DirectoryNotFoundException>(() => RepositoryRoot.Find("no/such/marker.json", nested));
    }

    [Fact]
    public void Namespaces_come_from_the_module_projects()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("m/a/Starter.Modules.A.csproj", "<Project><PropertyGroup><RootNamespace>Starter.Modules.Alpha</RootNamespace></PropertyGroup></Project>");
        workspace.Write("m/b/Starter.Modules.B.csproj", "<Project />");
        workspace.Write("m/c/module.rules.yaml", "module: C");
        var rules = CompiledRules.Parse("""
            { "schemaVersion": 1, "modules": [
              { "module": "A", "kind": "dotnet", "path": "m/a" }, { "module": "B", "kind": "dotnet", "path": "m/b" },
              { "module": "C", "kind": "dotnet", "path": "m/c" }, { "module": "D", "kind": "dotnet", "path": "m/missing" } ] }
            """, "inline");

        var map = ModuleNamespaceMap.FromProjects(rules, workspace.Root);

        Assert.Equal("Starter.Modules.Alpha", map.NamespaceOf("A"));
        Assert.Equal("Starter.Modules.B", map.NamespaceOf("B"));
        Assert.Null(map.NamespaceOf("C"));
        Assert.Null(map.NamespaceOf("D"));
        Assert.Equal(2, map.Namespaces.Count);
    }

    [Fact]
    public void The_repository_map_resolves_the_package_namespaces()
    {
        var root = RepositoryRoot.Find(CompiledRules.RelativePath);
        var map = ModuleNamespaceMap.FromProjects(CompiledRules.LoadFromRepository(), root);

        Assert.Equal("Codelab.Testing", map.NamespaceOf("CodelabTesting"));
        Assert.Equal("Codelab.Integration", map.NamespaceOf("CodelabIntegration"));
    }
}
