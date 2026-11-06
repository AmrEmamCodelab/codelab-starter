using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codelab.Errors;
using Starter.Application.Attachments;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Infrastructure.Numbering;
using Starter.Modules.ServiceRequests.Application.Drafts;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Modules.ServiceRequests.Tests.Fakes;

namespace Starter.Modules.ServiceRequests.Tests.Http;

/// <summary>The wizard calls a requester makes, as JSON over HTTP (one owner of the paths and bodies of these tests).</summary>
internal static class WizardCalls
{
    public const string Root = "/api/service-requests";
    public const string CategoriesPath = "/api/lookups/service-request-categories";
    public const string Title = "Passport renewal";
    public const string Description = "I need to renew my passport before travelling next month.";
    public const string Phone = TestValues.ValidPhone;

    private const string IdProperty = "id";
    private const string NumberProperty = "number";

    public static async Task<string> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(Root, new { });
        await WizardCalls.AssertStatusAsync(HttpStatusCode.Created, response);
        return (await JsonAsync(response)).GetProperty(IdProperty).GetString()!;
    }

    public static async Task<string> FirstCategoryAsync(HttpClient client)
    {
        using var response = await client.GetAsync(CategoriesPath);
        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, response);
        return (await JsonAsync(response))[0].GetProperty(IdProperty).GetString()!;
    }

    public static Task<HttpResponseMessage> SaveStepOneAsync(HttpClient client, string id, string category) =>
        client.PutAsJsonAsync(
            $"{Root}/{id}/draft",
            new { categoryId = category, title = Title, description = (string?)null, contactPhone = (string?)null, preferredDate = (string?)null, step = (int)ServiceRequestDraftStep.Request });

    public static Task<HttpResponseMessage> SaveStepTwoAsync(HttpClient client, string id, string preferredDate) =>
        client.PutAsJsonAsync(
            $"{Root}/{id}/draft",
            new { categoryId = (string?)null, title = (string?)null, description = Description, contactPhone = Phone, preferredDate, step = (int)ServiceRequestDraftStep.Details });

    public static Task<HttpResponseMessage> GetDraftAsync(HttpClient client, string id) => client.GetAsync($"{Root}/{id}/draft");

    public static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string id) => client.PostAsJsonAsync($"{Root}/{id}/submit", new { });

    public static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task<string> NumberOfAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty(NumberProperty).GetString()!;

    public static async Task<string> CodeOfAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty(ErrorEnvelope.CodeMember).GetString()!;

    public static async Task<IReadOnlyList<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        var errors = (await JsonAsync(response)).GetProperty(ErrorEnvelope.ErrorsMember);
        return [.. errors.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal)];
    }

    /// <summary>A date a week from now, so the preferred-date rule never fails on the day the tests run.</summary>
    public static string NextWeek() =>
        ServiceRequestRules.FormatDate(DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().AddDays(7).UtcDateTime));

    /// <summary>The current year in the Numbering time zone (the year of a new reference number).</summary>
    public static int NumberingYear() =>
        TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(NumberingOptions.DefaultTimeZoneId)).Year;
}

/// <summary>
/// The exit-criterion path on real SQL through the real host: draft, save each step, resume, submit gives
/// <c>SR-&lt;year&gt;-001</c> once the required document is linked. Its own database, so the number is the first.
/// </summary>
[Trait("Category", "Http")]
[Collection(HttpTestGroup.Name)]
public sealed class ServiceRequestWizardHttpTests(ServiceRequestsHttpHost host) : IClassFixture<ServiceRequestsHttpHost>
{
    [Fact]
    public async Task Draft_save_resume_and_submit_gives_the_first_sr_number_of_the_year()
    {
        using var client = await host.AdministratorClientAsync();
        var id = await WizardCalls.CreateAsync(client);
        var category = await WizardCalls.FirstCategoryAsync(client);
        var preferredDate = WizardCalls.NextWeek();

        using (var stepOne = await WizardCalls.SaveStepOneAsync(client, id, category))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepOne);
        }

        using (var stepTwo = await WizardCalls.SaveStepTwoAsync(client, id, preferredDate))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepTwo);
        }

        using (var resumed = await WizardCalls.GetDraftAsync(client, id))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, resumed);
            var draft = await WizardCalls.JsonAsync(resumed);
            Assert.Equal(
                (id, category, WizardCalls.Title, WizardCalls.Description, WizardCalls.Phone, preferredDate),
                (draft.GetProperty("id").GetString(), draft.GetProperty("categoryId").GetString(), draft.GetProperty("title").GetString(),
                 draft.GetProperty("description").GetString(), draft.GetProperty("contactPhone").GetString(), draft.GetProperty("preferredDate").GetString()));
        }

        await host.LinkRequiredDocumentAsync(id);
        using var submitted = await WizardCalls.SubmitAsync(client, id);

        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, submitted);
        Assert.Equal($"SR-{WizardCalls.NumberingYear()}-001", await WizardCalls.NumberOfAsync(submitted));
    }
}

/// <summary>The submit refusals on real SQL: a crafted submit that skipped step 2, and a submit without the required document.</summary>
[Trait("Category", "Http")]
[Collection(HttpTestGroup.Name)]
public sealed class ServiceRequestSubmitRefusalHttpTests(ServiceRequestsHttpHost host) : IClassFixture<ServiceRequestsHttpHost>
{
    [Fact]
    public async Task A_crafted_submit_that_skipped_step_two_is_refused_with_its_field_errors()
    {
        using var client = await host.AdministratorClientAsync();
        var id = await WizardCalls.CreateAsync(client);
        using (var stepOne = await WizardCalls.SaveStepOneAsync(client, id, await WizardCalls.FirstCategoryAsync(client)))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepOne);
        }

        await host.LinkRequiredDocumentAsync(id);
        using var submitted = await WizardCalls.SubmitAsync(client, id);

        await WizardCalls.AssertStatusAsync(HttpStatusCode.BadRequest, submitted);
        Assert.Equal(PlatformErrorCodes.Validation, await WizardCalls.CodeOfAsync(submitted));
        Assert.Equal([ServiceRequestFields.ContactPhone, ServiceRequestFields.Description], await WizardCalls.ErrorFieldsAsync(submitted));
    }

    [Fact]
    public async Task A_complete_request_without_the_required_document_is_refused_by_the_interlock()
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

        using var submitted = await WizardCalls.SubmitAsync(client, id);

        await WizardCalls.AssertStatusAsync(HttpStatusCode.Conflict, submitted);
        Assert.Equal(AttachmentErrorCodes.RequiredMissing, await WizardCalls.CodeOfAsync(submitted));
    }

    [Fact]
    public async Task A_step_save_with_invalid_fields_is_refused_with_field_errors()
    {
        using var client = await host.AdministratorClientAsync();
        var id = await WizardCalls.CreateAsync(client);

        using var saved = await WizardCalls.SaveStepTwoAsync(client, id, "20/11/2026");

        await WizardCalls.AssertStatusAsync(HttpStatusCode.BadRequest, saved);
        Assert.Equal([ServiceRequestFields.PreferredDate], await WizardCalls.ErrorFieldsAsync(saved));
    }
}

/// <summary>
/// Who sees and moves a service request on real SQL: the Records module's visibility decides the sample's details
/// (a draft is private to its owner), the record's files are reachable without host wiring (Records publishes the
/// attachable access), and a requester holding every staff key still cannot review or decide their own request.
/// </summary>
[Trait("Category", "Http")]
[Collection(HttpTestGroup.Name)]
public sealed class ServiceRequestAccessHttpTests(ServiceRequestsHttpHost host) : IClassFixture<ServiceRequestsHttpHost>
{
    private const string RecordsRoot = "/api/records/service-request";

    [Fact]
    public async Task A_never_submitted_draft_is_hidden_from_a_queue_holder_and_shown_to_its_owner()
    {
        using var requester = await host.RequesterClientAsync();
        using var staff = await host.AdministratorClientAsync();
        var id = await WizardCalls.CreateAsync(requester);
        using (var stepOne = await WizardCalls.SaveStepOneAsync(requester, id, await WizardCalls.FirstCategoryAsync(requester)))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepOne);
        }

        using var hidden = await staff.GetAsync($"{WizardCalls.Root}/{id}");
        await WizardCalls.AssertStatusAsync(HttpStatusCode.NotFound, hidden);
        Assert.Equal(RecordsErrorCodes.NotFound, await WizardCalls.CodeOfAsync(hidden));
        Assert.DoesNotContain(WizardCalls.Title, await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var own = await requester.GetAsync($"{WizardCalls.Root}/{id}");
        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, own);
    }

    [Fact]
    public async Task The_files_of_a_service_request_are_reachable_through_the_attachments_routes()
    {
        using var client = await host.AdministratorClientAsync();
        var id = await WizardCalls.CreateAsync(client);
        var detail = await DetailAsync(client, id);

        using var files = await client.GetAsync(
            $"/api/records/{detail.GetProperty("attachableRecordType").GetString()}/{detail.GetProperty("attachmentsRecordId").GetString()}/attachments");

        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, files);
    }

    [Fact]
    public async Task A_requester_holding_every_staff_key_cannot_review_or_decide_their_own_request()
    {
        using var client = await host.AdministratorClientAsync();
        var id = await SubmittedAsync(client);

        Assert.Equal([ServiceRequestActions.Cancel], ActionKeys(await DetailAsync(client, id)));
        foreach (var action in new[] { ServiceRequestActions.StartReview, ServiceRequestActions.Approve })
        {
            using var refused = await client.PostAsJsonAsync($"{RecordsRoot}/{id}/transitions", new { action, reason = (string?)null });
            await WizardCalls.AssertStatusAsync(HttpStatusCode.Forbidden, refused);
            Assert.Equal(WorkflowErrorCodes.Forbidden, await WizardCalls.CodeOfAsync(refused));
        }

        Assert.Equal(ServiceRequestStatuses.Submitted, (await DetailAsync(client, id)).GetProperty("status").GetInt32());
    }

    private async Task<string> SubmittedAsync(HttpClient client)
    {
        var id = await WizardCalls.CreateAsync(client);
        using (var stepOne = await WizardCalls.SaveStepOneAsync(client, id, await WizardCalls.FirstCategoryAsync(client)))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepOne);
        }

        using (var stepTwo = await WizardCalls.SaveStepTwoAsync(client, id, WizardCalls.NextWeek()))
        {
            await WizardCalls.AssertStatusAsync(HttpStatusCode.NoContent, stepTwo);
        }

        await host.LinkRequiredDocumentAsync(id);
        using var submitted = await WizardCalls.SubmitAsync(client, id);
        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, submitted);
        return id;
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync($"{RecordsRoot}/{id}");
        await WizardCalls.AssertStatusAsync(HttpStatusCode.OK, response);
        return await WizardCalls.JsonAsync(response);
    }

    private static IReadOnlyList<string> ActionKeys(JsonElement detail) =>
        [.. detail.GetProperty("allowedActions").EnumerateArray().Select(action => action.GetProperty("actionKey").GetString()!)];
}
