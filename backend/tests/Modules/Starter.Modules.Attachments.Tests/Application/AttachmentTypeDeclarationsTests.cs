using Starter.Application.Attachments;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Attachments.Tests.Application;

public sealed class AttachmentTypeDeclarationsTests
{
    private const string RecordType = "declared-records";

    private static AttachmentTypeDefinition Report { get; } = Definition(id: 21, key: "declared-report");

    private static AttachmentTypeDefinition Scan { get; } = Definition(id: 20, key: "declared-scan");

    [Fact]
    public void No_source_declares_nothing()
    {
        var declarations = new AttachmentTypeDeclarations([], AttachmentTypeSeed.Rows);

        Assert.Empty(declarations.Seeds);
        Assert.Empty(declarations.Types);
    }

    [Fact]
    public void Every_source_s_types_are_collected_in_id_order_with_their_names_and_rules()
    {
        var declarations = new AttachmentTypeDeclarations([new Source(Report), new Source(Scan)], AttachmentTypeSeed.Rows);

        Assert.Equal([20, 21], declarations.Seeds.Select(seed => seed.Id));
        var scan = declarations.Types[0];
        Assert.Equal((Scan.Key, Scan.RecordType, Scan.NameAr, Scan.NameEn), (scan.Key, scan.RecordType, scan.NameAr, scan.NameEn));
        Assert.Equal((Scan.IsRequired, Scan.MaxFiles), (scan.IsRequired, scan.MaxFiles));
        Assert.Equal([".pdf", ".png"], scan.AllowedExtensions);
        Assert.True(scan.IsActive);
    }

    [Fact]
    public void Extensions_are_normalised_like_a_migration_seed()
    {
        var seed = AttachmentTypeDeclarations.ToSeed(Scan with { AllowedExtensions = ["PDF", ".Png"] });

        Assert.Equal([".pdf", ".png"], ServiceAttachment.Seeded(seed).AllowedExtensions);
    }

    [Theory]
    [InlineData(0, "declared-scan", 1)]
    [InlineData(20, "Declared Scan", 1)]
    [InlineData(20, "declared-scan", 0)]
    [InlineData(20, "declared-scan", 51)]
    public void A_malformed_definition_stops_the_host(int id, string key, int maxFiles)
    {
        var malformed = Scan with { Id = id, Key = key, MaxFiles = maxFiles };

        var refused = Assert.Throws<InvalidOperationException>(() => new AttachmentTypeDeclarations([new Source(malformed)], []));

        Assert.Contains(key, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_definition_without_names_or_extensions_stops_the_host()
    {
        Assert.Throws<InvalidOperationException>(() => new AttachmentTypeDeclarations([new Source(Scan with { NameAr = " " })], []));
        Assert.Throws<InvalidOperationException>(() => new AttachmentTypeDeclarations([new Source(Scan with { AllowedExtensions = [] })], []));
        Assert.Throws<InvalidOperationException>(() => new AttachmentTypeDeclarations([new Source(Scan with { AllowedExtensions = ["not an extension"] })], []));
    }

    [Fact]
    public void The_same_id_declared_twice_stops_the_host()
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            new AttachmentTypeDeclarations([new Source(Scan), new Source(Report with { Id = Scan.Id })], []));

        Assert.Contains("id 20", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_key_declared_twice_stops_the_host()
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            new AttachmentTypeDeclarations([new Source(Scan, Report with { Key = Scan.Key })], []));

        Assert.Contains($"key {Scan.Key}", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_the_attachments_migrations_own_cannot_be_declared_by_id_or_key()
    {
        var demo = Assert.Single(AttachmentTypeSeed.Rows);

        Assert.Throws<InvalidOperationException>(() => new AttachmentTypeDeclarations([new Source(Scan with { Id = demo.Id })], AttachmentTypeSeed.Rows));
        Assert.Throws<InvalidOperationException>(() => new AttachmentTypeDeclarations([new Source(Scan with { Key = demo.Key })], AttachmentTypeSeed.Rows));
    }

    private static AttachmentTypeDefinition Definition(int id, string key) => new(
        Id: id,
        Key: key,
        RecordType: RecordType,
        NameAr: "مستند",
        NameEn: "Document",
        IsRequired: true,
        MaxFiles: 2,
        AllowedExtensions: [".pdf", ".png"]);

    private sealed class Source(params AttachmentTypeDefinition[] types) : IAttachmentTypeSeedSource
    {
        public IEnumerable<AttachmentTypeDefinition> Types => types;
    }
}
