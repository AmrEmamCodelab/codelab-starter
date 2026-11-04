using System.Security.Claims;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Package.Sample.Api.Authentication;

/// <summary>Authentication of the sample: bearer sessions for users, ES256 assertions for services.</summary>
public static class SampleAuthentication
{
    /// <summary>The policy of service-to-service endpoints: only the Codelab service-assertion scheme.</summary>
    public const string InternalServicePolicy = "internal-service";

    /// <summary>
    /// Registers bearer sessions (the default scheme), the Codelab service-assertion scheme
    /// (<c>Codelab:Security:ServiceAssertions:Server</c>) and the internal-service policy.
    /// </summary>
    public static IServiceCollection AddSampleAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IValidateOptions<SampleAuthenticationOptions>, SampleAuthenticationOptionsValidator>();
        services.AddOptions<SampleAuthenticationOptions>()
            .Bind(configuration.GetSection(SampleAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, BearerSetup>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddCodelabServiceAssertion(configuration);
        services.AddAuthorizationBuilder().AddPolicy(InternalServicePolicy, policy => policy
            .AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());
        return services;
    }

    /// <summary>Token validation from <see cref="SampleAuthenticationOptions"/>; claim names stay as issued.</summary>
    private sealed class BearerSetup(IOptions<SampleAuthenticationOptions> sample) : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(string? name, JwtBearerOptions options)
        {
            if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            {
                return;
            }

            var settings = sample.Value;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKeyBytes() ?? []),
                NameClaimType = SecurityClaimTypes.Subject,
            };
            options.Events = new JwtBearerEvents { OnTokenValidated = AddNameIdentifier };
        }

        // Claim names stay as issued (sub, sid, skid, sver: what Codelab.Security reads). The observability user hash
        // reads the NameIdentifier claim, so the subject is also offered under that type.
        private static Task AddNameIdentifier(TokenValidatedContext context)
        {
            var subject = context.Principal?.FindFirst(SecurityClaimTypes.Subject)?.Value;
            if (context.Principal?.Identity is ClaimsIdentity identity && subject is not null && !identity.HasClaim(claim => claim.Type == ClaimTypes.NameIdentifier))
            {
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
            }

            return Task.CompletedTask;
        }

        public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
    }
}
