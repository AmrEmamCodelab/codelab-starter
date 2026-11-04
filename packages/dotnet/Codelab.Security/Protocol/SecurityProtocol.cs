namespace Codelab.Security;

/// <summary>
/// Every protocol, profile and header name the package puts on the wire, derived from one product prefix.
/// <para>
/// A fork sets the prefix once (init.ps1 writes it to <c>Codelab:Security:Protocol:Prefix</c>); the signature scheme
/// becomes <c>{Prefix}-SIG-v1</c>, opaque identifiers <c>{Prefix}-PUBLICID-v1</c>, the canonical framing
/// <c>{Prefix}-CANON-v1</c> and the headers <c>X-{Prefix}-...</c>. The prefix is bound into every MAC, AAD and key
/// derivation, so two deployments with different prefixes never accept each other's signatures or identifiers.
/// Tests and golden vectors use the fixed prefix <see cref="TestPrefix"/>.
/// </para>
/// </summary>
public sealed class SecurityProtocol
{
    /// <summary>The prefix the golden vectors and the package tests are pinned to.</summary>
    public const string TestPrefix = "CODELABTEST";

    /// <summary>Shortest accepted prefix.</summary>
    public const int MinPrefixLength = 2;

    /// <summary>Longest accepted prefix (keeps header names and the PublicId AAD bounded).</summary>
    public const int MaxPrefixLength = 16;

    private SecurityProtocol(string prefix)
    {
        Prefix = prefix;
        CanonicalVersion = prefix + ProtocolNames.CanonicalSuffix;
        SignatureVersion = prefix + ProtocolNames.SignatureSuffix;
        PublicIdSpec = prefix + ProtocolNames.PublicIdSuffix;
        TicketSpec = prefix + ProtocolNames.TicketSuffix;
        ReceiptSpec = prefix + ProtocolNames.ReceiptSuffix;
        ServiceAssertionType = prefix + ProtocolNames.ServiceAssertionSuffix;
        RequestProfile = SignatureVersion + ProtocolNames.RequestProfileSuffix;
        ResponseProfile = SignatureVersion + ProtocolNames.ResponseProfileSuffix;
        SessionKeyProfile = SignatureVersion + ProtocolNames.SessionKeyProfileSuffix;
        SignatureHeaderName = ProtocolNames.HeaderPrefix + prefix + ProtocolNames.SignatureHeaderSuffix;
        ResponseSignatureHeaderName = ProtocolNames.HeaderPrefix + prefix + ProtocolNames.ResponseSignatureHeaderSuffix;
        ServiceAssertionHeaderName = ProtocolNames.HeaderPrefix + prefix + ProtocolNames.ServiceAssertionHeaderSuffix;
    }

    /// <summary>The product prefix, e.g. <c>CODELABTEST</c>.</summary>
    public string Prefix { get; }

    /// <summary><c>{Prefix}-CANON-v1</c>: the version tag that opens every canonical form.</summary>
    public string CanonicalVersion { get; }

    /// <summary><c>{Prefix}-SIG-v1</c>: the request/response signature scheme.</summary>
    public string SignatureVersion { get; }

    /// <summary><c>{Prefix}-SIG-v1/REQ</c>: the request canonical-form profile.</summary>
    public string RequestProfile { get; }

    /// <summary><c>{Prefix}-SIG-v1/RESP</c>: the response canonical-form profile.</summary>
    public string ResponseProfile { get; }

    /// <summary><c>{Prefix}-SIG-v1/KDF</c>: the session-key derivation profile.</summary>
    public string SessionKeyProfile { get; }

    /// <summary><c>{Prefix}-PUBLICID-v1</c>: the opaque identifier specification bound into the AAD.</summary>
    public string PublicIdSpec { get; }

    /// <summary><c>{Prefix}-TICKET-v1</c>: the sealed one-time ticket specification.</summary>
    public string TicketSpec { get; }

    /// <summary><c>{Prefix}-RECEIPT-v1</c>: the upload receipt specification.</summary>
    public string ReceiptSpec { get; }

    /// <summary><c>{Prefix}-S2S-v1</c>: the <c>typ</c> of a service-to-service assertion.</summary>
    public string ServiceAssertionType { get; }

    /// <summary><c>X-{Prefix}-Signature</c>: the request proof header.</summary>
    public string SignatureHeaderName { get; }

    /// <summary><c>X-{Prefix}-Response-Signature</c>: the response signature header.</summary>
    public string ResponseSignatureHeaderName { get; }

    /// <summary><c>X-{Prefix}-Service-Assertion</c>: the service-to-service assertion header.</summary>
    public string ServiceAssertionHeaderName { get; }

    /// <summary>Creates the protocol for <paramref name="prefix"/>, or throws when it is not a valid prefix.</summary>
    /// <exception cref="ArgumentException">The prefix is not 2-16 characters of <c>A-Z</c> then <c>A-Z0-9</c>.</exception>
    public static SecurityProtocol Create(string prefix)
    {
        if (!IsValidPrefix(prefix))
        {
            throw new ArgumentException(
                $"A protocol prefix is {MinPrefixLength}-{MaxPrefixLength} characters: an upper-case ASCII letter, then upper-case ASCII letters or digits.",
                nameof(prefix));
        }

        return new SecurityProtocol(prefix);
    }

    /// <summary>True when <paramref name="prefix"/> is 2-16 characters: <c>A-Z</c> first, then <c>A-Z</c> or <c>0-9</c>.</summary>
    public static bool IsValidPrefix(string? prefix)
    {
        if (prefix is null || prefix.Length is < MinPrefixLength or > MaxPrefixLength || !IsUpperAscii(prefix[0]))
        {
            return false;
        }

        foreach (var c in prefix)
        {
            if (!IsUpperAscii(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => SignatureVersion;

    private static bool IsUpperAscii(char c) => c is >= 'A' and <= 'Z';
}
