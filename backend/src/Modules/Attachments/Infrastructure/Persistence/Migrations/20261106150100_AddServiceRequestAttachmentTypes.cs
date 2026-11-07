using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;

/// <summary>
/// Kept empty on purpose. This migration once inserted the sample's two attachment types (ids 2 and 3); those types are
/// now declared by the module that owns them (ServiceRequests, through the kernel seam
/// <c>Starter.Application.Attachments.IAttachmentTypeSeedSource</c>) and written at start by
/// <see cref="Startup.AttachmentTypeSynchronizer"/>, matched by key, so removing that module seeds nothing. The id stays
/// in the history so a database that applied the old body (its rows already there; the synchronizer finds them by key
/// and inserts nothing) and a new database (the synchronizer inserts them) carry the same migration list, and the DBA
/// script of this context is unchanged for both. <see cref="Down"/> is empty too: the rows belong to the synchronizer.
/// Never delete or reuse this id, and never put a body back.
/// </summary>
[DbContext(typeof(AttachmentsDbContext))]
[Migration(MigrationId)]
public sealed class AddServiceRequestAttachmentTypes : Migration
{
    /// <summary>The migration id (UTC timestamp and class name), frozen in every database that applied it.</summary>
    public const string MigrationId = "20261106150100_AddServiceRequestAttachmentTypes";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => ArgumentNullException.ThrowIfNull(migrationBuilder);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => ArgumentNullException.ThrowIfNull(migrationBuilder);

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AttachmentsMigrationModel.Build(modelBuilder);
}
