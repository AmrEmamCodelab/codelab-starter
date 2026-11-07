using System.Text.Json;
using Codelab.Errors;
using Codelab.Gateway.Api.Diagnostics;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Gateway;
using Codelab.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sms;

/// <summary>
/// The SMS relay profile (<see cref="GatewayProfileKey.Sms"/>): POST <see cref="GatewayRoutes.Sms"/> takes an
/// <see cref="SmsRelayRequest"/>, checks it with <see cref="SmsRelayRequestValidator"/> (the same checks the relay
/// client ran), sends it through <see cref="ISmsProvider"/> and answers an <see cref="SmsRelayResult"/> carrying the
/// gateway's reference code, so the relay can be found in the gateway's logs.
/// </summary>
internal sealed class SmsRelayProfile(IOptions<SmsProviderOptions> options) : IGatewayProfile, IRelayUpstreams
{
    private static readonly RelayStep SendStep = new(GatewayProfileKey.Sms, RelayOperations.Send);

    public string Key => GatewayProfileKey.Sms;

    public IEnumerable<Uri?> UpstreamAddresses => [SmsProviderOptionsValidator.SampleAddress(options.Value.UrlTemplate)];

    public void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(GatewayRoutes.Sms, RelaySmsAsync);
    }

    internal static async Task<IResult> RelaySmsAsync(HttpContext context, ISmsProvider provider, ILogger<SmsRelayProfile> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);
        var read = await SmsRelayRequestReader.ReadAsync(context.Request, context.RequestAborted);
        if (read.Refusal is not null)
        {
            return RelayResponses.Refused(logger, SendStep, read.Refusal, read.RefusalCode!);
        }

        var outcome = await provider.SendAsync(read.Message!, context.RequestAborted);
        if (outcome.Failure is not null)
        {
            return RelayResponses.Failed(logger, SendStep, outcome.Failure);
        }

        GatewayLog.RelayCompleted(logger, profile: SendStep.Profile, operation: SendStep.Operation, upstreamStatus: null);
        return TypedResults.Ok(new SmsRelayResult
        {
            Accepted = outcome.Accepted,
            ProviderMessageId = outcome.ProviderMessageId,
            ReferenceCode = ReferenceCode.From(CorrelationId.For(context)),
        });
    }
}

/// <summary>The outcome of reading an SMS relay body: the message, or the refusal and its code.</summary>
internal sealed record SmsRelayRead(SmsRelayRequest? Message, IResult? Refusal, string? RefusalCode);

/// <summary>Reads and checks the SMS relay body: JSON content type, at most 16 KiB, parsable, valid.</summary>
internal static class SmsRelayRequestReader
{
    public static async Task<SmsRelayRead> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RelayRefusals.HasMediaType(request, GatewayMediaTypes.Json))
        {
            return Refused(RelayRefusals.Malformed($"The SMS relay request must be {GatewayMediaTypes.Json}."), ErrorCodes.Malformed);
        }

        var body = await BoundedBody.ReadAsync(request.Body, GatewayRelayLimits.MaxSmsRequestBytes, cancellationToken);
        if (body is null)
        {
            return Refused(RelayRefusals.TooLarge(), ErrorCodes.PayloadTooLarge);
        }

        var message = Deserialize(body);
        if (message is null)
        {
            return Refused(RelayRefusals.Malformed("The SMS relay request is not a JSON object of the relay contract."), ErrorCodes.Malformed);
        }

        var problems = SmsRelayRequestValidator.Validate(message);
        return problems.Count > 0 ? Refused(RelayRefusals.Invalid(problems), ErrorCodes.Validation) : new SmsRelayRead(message, Refusal: null, RefusalCode: null);
    }

    private static SmsRelayRequest? Deserialize(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<SmsRelayRequest>(body, GatewayJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SmsRelayRead Refused(IResult refusal, string code) => new(Message: null, refusal, code);
}

/// <summary>Registers the SMS relay profile; its provider client and checks only when the profile is enabled.</summary>
internal static class SmsRelayRegistration
{
    public static IServiceCollection AddSmsRelay(this IServiceCollection services, IConfiguration configuration, bool enabled)
    {
        services.AddGatewayProfile<SmsRelayProfile>();
        var options = services.AddOptions<SmsProviderOptions>().Bind(configuration.GetSection(SmsProviderOptions.SectionName));
        if (!enabled)
        {
            return services;
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SmsProviderOptions>, SmsProviderOptionsValidator>());
        UpstreamClients.Add<HttpSmsProvider, SmsProviderOptions>(services, HttpSmsProvider.ClientName, static sms => SmsProviderOptionsValidator.SampleAddress(sms.UrlTemplate));
        services.TryAddTransient<ISmsProvider>(static provider => provider.GetRequiredService<HttpSmsProvider>());
        return services;
    }
}
