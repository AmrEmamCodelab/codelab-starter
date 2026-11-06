using Codelab.Security.PublicIds;
using Starter.Application.Lookups;
using Starter.Application.Records;
using Starter.Application.Users;
using Starter.Domain.Lookups;
using Starter.Domain.Records;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Access;
using Starter.Modules.ServiceRequests.Application.Drafts;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Tests.Fakes;

/// <summary>The fixed values the tests share.</summary>
public static class TestValues
{
    /// <summary>The mobile prefix (kept apart from the digits so no source line holds a whole phone number).</summary>
    public const string MobilePrefix = "05";

    /// <summary>A well-formed contact phone.</summary>
    public const string ValidPhone = MobilePrefix + "12345678";

    /// <summary>Another well-formed contact phone.</summary>
    public const string OtherPhone = MobilePrefix + "98765432";

    public const long OwnerUserId = 11;
    public const long OtherUserId = 22;
    public const long RecordId = 501;
    public const long OrganizationId = 7;

    public static readonly Guid OwnerSubject = Guid.Parse("4a1d6b1e-5f0e-4b55-9d2f-000000000011");
    public static readonly Guid OtherSubject = Guid.Parse("4a1d6b1e-5f0e-4b55-9d2f-000000000022");
    public static readonly DateOnly Today = new(2026, 11, 6);
    public static readonly DateTimeOffset At = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    public static RecordSummary Record(int status, long ownerUserId = OwnerUserId, long id = RecordId, string? recordType = null) => new(
        Id: id,
        RecordType: recordType ?? ServiceRequestRecord.RecordType,
        Number: status == ServiceRequestStatuses.Draft ? null : "SR-2026-001",
        Status: status,
        OwnerUserId: ownerUserId,
        OrganizationId: OrganizationId,
        CreatedAt: At,
        SubmittedAt: status == ServiceRequestStatuses.Draft ? null : At,
        UpdatedAt: At,
        IsTerminal: status is ServiceRequestStatuses.Approved or ServiceRequestStatuses.Rejected or ServiceRequestStatuses.Cancelled);
}

/// <summary>A codec that writes <c>tok:&lt;domain&gt;:&lt;id&gt;</c>, so tests can build and read tokens.</summary>
public sealed class FakeCodec : IPublicIdCodec
{
    private const string Prefix = "tok:";
    private const char Separator = ':';

    public string Protect(string domain, long id) => $"{Prefix}{domain}{Separator}{id}";

    public bool TryUnprotect(string domain, string? token, out long id)
    {
        id = 0;
        var expected = $"{Prefix}{domain}{Separator}";
        return token is not null && token.StartsWith(expected, StringComparison.Ordinal) && long.TryParse(token[expected.Length..], out id);
    }

    public static string CategoryToken(int id) => new FakeCodec().Protect(ServiceRequestCategoryTokens.DomainLabel, id);
}

public sealed class FakeLookups : ILookupValues
{
    public HashSet<int> Retired { get; } = [];

    public Task<ActiveLookupValue?> FindActiveAsync(string typeKey, int id, CancellationToken cancellationToken)
    {
        var known = typeKey == LookupTypes.ServiceRequestCategories
            && id is ServiceRequestCategoryIds.General or ServiceRequestCategoryIds.Certificate or ServiceRequestCategoryIds.DataUpdate or ServiceRequestCategoryIds.Complaint
            && !Retired.Contains(id);
        return Task.FromResult(known ? new ActiveLookupValue(id, $"code-{id}", $"اسم {id}", $"Name {id}") : null);
    }
}

public sealed class FakeCurrentUser(Guid? subject) : ICurrentUserContext
{
    public bool IsAuthenticated => SubjectId is not null;

    public Guid? SubjectId { get; set; } = subject;

    public string? SessionId => null;
}

public sealed class FakeUserDirectory : IUserDirectory
{
    public int Lookups { get; private set; }

    public Task<DirectoryUser?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        Lookups++;
        DirectoryUser? user = subjectId == TestValues.OwnerSubject
            ? new DirectoryUser(TestValues.OwnerUserId, IsActive: true, HoldsAllPermissions: false)
            : subjectId == TestValues.OtherSubject ? new DirectoryUser(TestValues.OtherUserId, IsActive: true, HoldsAllPermissions: false) : null;
        return Task.FromResult(user);
    }
}

public sealed class FakeRecordDirectory : IRecordDirectory
{
    public Dictionary<long, RecordSummary> Records { get; } = [];

    public Task<IReadOnlyList<RecordSummary>> GetAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecordSummary>>([.. ids.Where(Records.ContainsKey).Select(id => Records[id])]);

    public Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The sample never queries the directory.");
}

/// <summary>The Records module's visibility decision, as a set of visible record ids (the rule itself is tested in Records).</summary>
public sealed class FakeRecordVisibility : IRecordVisibility
{
    public HashSet<long> Visible { get; } = [];

    public List<RecordRef> Asked { get; } = [];

    public Task<bool> CanViewAsync(RecordRef record, CancellationToken cancellationToken)
    {
        Asked.Add(record);
        return Task.FromResult(Visible.Contains(record.Id));
    }
}

public sealed class InMemoryServiceRequestStore : IServiceRequestStore
{
    public Dictionary<long, ServiceRequest> Rows { get; } = [];

    public int Saves { get; private set; }

    public Task<ServiceRequest?> FindAsync(long recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(recordId));

    public void Add(ServiceRequest request) => Rows.Add(request.RecordId, request);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

public sealed class FixedCalendar(DateOnly today) : IServiceRequestCalendar
{
    public DateOnly Today() => today;
}

public sealed class FakeLifecycle : IRecordLifecycle
{
    public long NextId { get; set; } = TestValues.RecordId;

    public SubmitResult SubmitResult { get; set; } = new SubmitResult.Submitted("SR-2026-001");

    public List<string> CreatedTypes { get; } = [];

    public List<RecordRef> Submitted { get; } = [];

    public Task<RecordRef> CreateDraftAsync(string recordType, CancellationToken cancellationToken)
    {
        CreatedTypes.Add(recordType);
        return Task.FromResult(new RecordRef(NextId, recordType));
    }

    public Task<SubmitResult> SubmitAsync(RecordRef record, CancellationToken cancellationToken)
    {
        Submitted.Add(record);
        return Task.FromResult(SubmitResult);
    }

    public Task<TransitionResult> TransitionAsync(RecordRef record, string actionKey, string? reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The typed endpoints never transition.");

    public Task<TransitionResult> CancelAsync(RecordRef record, string? reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The typed endpoints never cancel.");

    public Task<SubmitResult> ResubmitAsync(RecordRef record, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The typed endpoints never resubmit.");
}

/// <summary>The module's application layer over the fakes, as a caller with the given subject and permissions.</summary>
public sealed class Harness
{
    public Harness(Guid? subject = null)
    {
        CurrentUser = new FakeCurrentUser(subject ?? TestValues.OwnerSubject);
        Access = new ServiceRequestAccess(CurrentUser, Users, Directory, Visibility);
        Categories = new ServiceRequestCategories(Lookups, Codec);
        Views = new ServiceRequestViews(Categories);
    }

    public FakeCurrentUser CurrentUser { get; }

    public FakeUserDirectory Users { get; } = new();

    public FakeRecordDirectory Directory { get; } = new();

    public FakeRecordVisibility Visibility { get; } = new();

    public FakeLookups Lookups { get; } = new();

    public FakeCodec Codec { get; } = new();

    public InMemoryServiceRequestStore Store { get; } = new();

    public FakeLifecycle Lifecycle { get; } = new();

    public FixedCalendar Calendar { get; } = new(TestValues.Today);

    public ServiceRequestAccess Access { get; }

    public ServiceRequestCategories Categories { get; }

    public ServiceRequestViews Views { get; }

    public Harness WithRecord(RecordSummary record)
    {
        Directory.Records[record.Id] = record;
        return this;
    }

    public SaveServiceRequestDraft SaveDraft() =>
        new(new SaveServiceRequestDraftValidator(Calendar), Access, Categories, Store);

    public ServiceRequestSubmitValidator SubmitValidator() => new(Store, Categories, Calendar);
}
