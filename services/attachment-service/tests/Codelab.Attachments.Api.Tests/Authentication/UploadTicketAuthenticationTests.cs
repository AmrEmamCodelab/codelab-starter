using System.Net;
using Codelab.Attachments.Api.Authentication;
using Codelab.Attachments.Api.Tests.TestSupport;
using Codelab.Attachments.Core.Errors;
using Codelab.Errors;
using Codelab.Security.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Attachments.Api.Tests.Authentication;

/// <summary>The ticket scheme's outcomes that the real ticket service cannot be driven into on demand.</summary>
public sealed class UploadTicketAuthenticationTests
{
    [Fact]
    public async Task A_replay_store_that_cannot_answer_fails_closed_with_503()
    {
        using var host = HostWith(TicketOpenResult.Refused(TicketOpenStatus.StoreUnavailable));
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.UploadRequest("any-ticket", ("a.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.Retryable);
    }

    [Fact]
    public async Task A_valid_ticket_with_an_empty_subject_is_refused()
    {
        var ticket = new SealedTicket("id-1", UploadTickets.Purpose, Subject: " ", ExpiresAt: DateTimeOffset.MaxValue, Data: null);
        using var host = HostWith(new TicketOpenResult(TicketOpenStatus.Valid, ticket));
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.UploadRequest("any-ticket", ("a.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task The_ticket_is_opened_for_the_upload_purpose_with_the_header_value()
    {
        var tickets = new RecordingTickets(TicketOpenResult.Refused(TicketOpenStatus.AlreadyUsed));
        using var host = new AttachmentsServiceHost(configure: null, services => services.AddSingleton<ISealedTicketService>(tickets));
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.UploadRequest("the-token", ("a.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
        Assert.Equal((UploadTickets.Purpose, "the-token"), Assert.Single(tickets.Opened));
    }

    private static AttachmentsServiceHost HostWith(TicketOpenResult result) =>
        new(configure: null, services => services.AddSingleton<ISealedTicketService>(new RecordingTickets(result)));

    private sealed class RecordingTickets(TicketOpenResult result) : ISealedTicketService
    {
        public List<(string Purpose, string Token)> Opened { get; } = [];

        public string Seal(TicketRequest request) => throw new NotSupportedException();

        public Task<TicketOpenResult> OpenAsync(string purpose, string token, CancellationToken cancellationToken)
        {
            Opened.Add((purpose, token));
            return Task.FromResult(result);
        }
    }
}
