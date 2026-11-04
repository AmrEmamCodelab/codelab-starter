namespace Codelab.Security;

/// <summary>The fixed suffixes appended to the product prefix to build every wire name (see <see cref="SecurityProtocol"/>).</summary>
internal static class ProtocolNames
{
    internal const string CanonicalSuffix = "-CANON-v1";
    internal const string SignatureSuffix = "-SIG-v1";
    internal const string PublicIdSuffix = "-PUBLICID-v1";
    internal const string TicketSuffix = "-TICKET-v1";
    internal const string ReceiptSuffix = "-RECEIPT-v1";
    internal const string ServiceAssertionSuffix = "-S2S-v1";
    internal const string RequestProfileSuffix = "/REQ";
    internal const string ResponseProfileSuffix = "/RESP";
    internal const string SessionKeyProfileSuffix = "/KDF";
    internal const string HeaderPrefix = "X-";
    internal const string SignatureHeaderSuffix = "-Signature";
    internal const string ResponseSignatureHeaderSuffix = "-Response-Signature";
    internal const string ServiceAssertionHeaderSuffix = "-Service-Assertion";
}
