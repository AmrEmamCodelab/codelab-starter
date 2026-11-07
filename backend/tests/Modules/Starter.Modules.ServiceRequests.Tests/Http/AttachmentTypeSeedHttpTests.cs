using System.Net;
using Microsoft.EntityFrameworkCore;
using Starter.Application.Attachments;
using Starter.Domain.Attachments;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;
using Starter.Modules.Attachments.Infrastructure.Startup;
using Starter.Modules.ServiceRequests.Application.Attachments;

namespace Starter.Modules.ServiceRequests.Tests.Http;

/// <summary>
/// The sample's attachment types on real SQL through the real host: after migrate and start they exist (declared by
/// this module, written by the Attachments start-up synchronizer), and the required document gates the submit until a
/// file of it is linked.
/// </summary>
[Trait("Category", "Http")]
[Collection(HttpTestGroup.Name)]
public sealed class ServiceRequestAttachmentTypesHttpTests(ServiceRequestsHttpHost host) : IClassFixture<ServiceRequestsHttpHost>
{
    private const string TypesPath = "/api/attachments/types?recordType=" + AttachableRecordTypes.ServiceRequest;
    private const string KeyProperty = "key";
    private const string IsRequiredProperty = "isRequired";
    private const string MaxFilesProperty = "maxFiles";

    [Fact]
    public async Task The_sample_s_types_exist_after_migrate_and_start()
    {
        using var client = await host.AdministratorClientAsync();

        using var response = await client.GetAsync(TypesPath);

        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, response);
        var types = (await WizardCalls.JsonAsync(response)).EnumerateArray()
            .Select(type => (type.GetProperty(KeyProperty).GetString(), type.GetProperty(IsRequiredProperty).GetBoolean(), type.GetProperty(MaxFilesProperty).GetInt32()))
            .ToList();
        Assert.Equal(
            [
                (AttachmentTypeKeys.ServiceRequestDocument, true, ServiceRequestAttachmentTypes.DocumentMaxFiles),
                (AttachmentTypeKeys.ServiceRequestSupporting, false, ServiceRequestAttachmentTypes.SupportingMaxFiles),
            ],
            types);
    }

    [Fact]
    public async Task The_required_document_gates_the_submit_until_a_file_of_it_is_linked()
    {
        using var client = await host.AdministratorClientAsync();
        var id = await WizardCalls.CreateAsync(client);
        using (var stepOne = await WizardCalls.SaveStepOneAsync(client, id, await WizardCalls.FirstCategoryAsync(client)))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepOne);
        }

        using (var stepTwo = await WizardCalls.SaveStepTwoAsync(client, id, WizardCalls.NextWeek()))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepTwo);
        }

        using (var refused = await WizardCalls.SubmitAsync(client, id))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.Conflict, refused);
            Assert.Equal(AttachmentErrorCodes.RequiredMissing, await WizardCalls.CodeOfAsync(refused));
        }

        await host.LinkRequiredDocumentAsync(id);
        using var submitted = await WizardCalls.SubmitAsync(client, id);

        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, submitted);
    }
}

/// <summary>
/// The upgrade path on real SQL, without the host: each test migrates its own throwaway database
/// <c>p7_sr_&lt;guid&gt;</c> with the Attachments migrations only and runs the start-up synchronizer with this module's
/// declarations. A new database gets the two types once; a database that applied the retired seed migration when it
/// still had a body (its two rows already there) is not seeded twice and keeps the same migration history.
/// </summary>
[Trait("Category", "Http")]
[Collection(HttpTestGroup.Name)]
public sealed class AttachmentTypeSeedUpgradeSqlTests
{
    [Fact]
    public async Task A_new_database_gets_the_sample_s_types_once()
    {
        await WithMigratedDatabaseAsync(async context =>
        {
            var first = await SynchronizerOn(context).SynchronizeAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var second = await SynchronizerOn(context).SynchronizeAsync(CancellationToken.None);

            Assert.Equal(new AttachmentTypeSeedReport(Added: 2, Refreshed: 0, Unchanged: 0), first);
            Assert.Equal(new AttachmentTypeSeedReport(Added: 0, Refreshed: 0, Unchanged: 2), second);
            Assert.Equal([AttachmentTypeIds.ServiceRequestDocument, AttachmentTypeIds.ServiceRequestSupporting], await SampleTypeIdsAsync(context));
        });
    }

    [Fact]
    public async Task A_database_that_applied_the_retired_seed_migration_is_not_seeded_twice()
    {
        await WithMigratedDatabaseAsync(async context =>
        {
            context.Types.AddRange(RowsTheRetiredMigrationWrote());
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var report = await SynchronizerOn(context).SynchronizeAsync(CancellationToken.None);

            Assert.Equal(new AttachmentTypeSeedReport(Added: 0, Refreshed: 0, Unchanged: 2), report);
            Assert.Equal([AttachmentTypeIds.ServiceRequestDocument, AttachmentTypeIds.ServiceRequestSupporting], await SampleTypeIdsAsync(context));
            Assert.Equal(
                [AttachmentsMigrationIds.InitialCreate, AddServiceRequestAttachmentTypes.MigrationId],
                await context.Database.GetAppliedMigrationsAsync());
        });
    }

    private static AttachmentTypeSynchronizer SynchronizerOn(AttachmentsDbContext context) =>
        new(context, new AttachmentTypeDeclarations([new ServiceRequestAttachmentTypes()], AttachmentTypeSeed.Rows));

    private static async Task<List<int>> SampleTypeIdsAsync(AttachmentsDbContext context) =>
        await context.Types.AsNoTracking().Where(type => type.RecordType == AttachableRecordTypes.ServiceRequest)
            .OrderBy(type => type.Id).Select(type => type.Id).ToListAsync();

    /// <summary>
    /// The two rows the Attachments migration 20261106150100 inserted before it was emptied (literal values on purpose:
    /// they are what existing databases hold).
    /// </summary>
    private static ServiceAttachment[] RowsTheRetiredMigrationWrote() =>
    [
        ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: 2,
            Key: "service-request-document",
            Names: new AttachmentTypeNames(Ar: "مستند الطلب", En: "Request document"),
            RecordType: "service-request",
            Rules: new AttachmentTypeRules(IsRequired: true, MaxFiles: 3, AllowedExtensions: [".pdf", ".jpg", ".png"]))),
        ServiceAttachment.Seeded(new ServiceAttachmentSeed(
            Id: 3,
            Key: "service-request-supporting",
            Names: new AttachmentTypeNames(Ar: "مستندات داعمة", En: "Supporting documents"),
            RecordType: "service-request",
            Rules: new AttachmentTypeRules(IsRequired: false, MaxFiles: 5, AllowedExtensions: [".pdf", ".jpg", ".png"]))),
    ];

    private static async Task WithMigratedDatabaseAsync(Func<AttachmentsDbContext, Task> test)
    {
        var database = ServiceRequestsHttpHost.DatabasePrefix + Guid.NewGuid().ToString("N");
        await ServiceRequestsHttpHost.ExecuteOnMasterAsync($"CREATE DATABASE [{database}]");
        try
        {
            await using var context = new AttachmentsDbContext(new DbContextOptionsBuilder<AttachmentsDbContext>()
                .UseSqlServer(
                    ServiceRequestsHttpHost.ConnectionStringFor(database),
                    sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, AttachmentsDbContext.SchemaName))
                .Options);
            await context.Database.MigrateAsync();
            await test(context);
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await ServiceRequestsHttpHost.ExecuteOnMasterAsync(
                $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
        }
    }
}
