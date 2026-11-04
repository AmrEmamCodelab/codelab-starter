using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codelab.Security.Problems;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Signing;

public sealed class SignatureCanonicalFormTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(SessionKeyDerivation.KeyBytes);
    private readonly SignatureCanonicalForm _forms = new(TestProtocols.Test);

    private static SignedRequestFields BaseFields => new(
        Audience: "Starter.Api",
        Method: "POST",
        Path: "/api/records/m1",
        Query: "page=1&size=20",
        BodyHash: SignatureCanonicalForm.HashBody("{\"a\":1}"u8),
        Timestamp: "946684800",
        Nonce: new string('N', SignatureHeader.NonceLength));

    [Fact]
    public void The_request_form_frames_eight_fields_under_the_request_profile()
    {
        var form = _forms.Request(BaseFields);

        Assert.StartsWith("20:CODELABTEST-CANON-v1\n22:CODELABTEST-SIG-v1/REQ\n1:8\n18:CODELABTEST-SIG-v1\n", form, StringComparison.Ordinal);
        Assert.Equal(SignatureCanonicalForm.RequestFieldCount, 8);
    }

    [Fact]
    public void An_absent_body_hashes_to_the_digest_of_zero_bytes()
    {
        Assert.Equal(Convert.ToBase64String(SHA256.HashData([])).TrimEnd('=').Replace('+', '-').Replace('/', '_'), SignatureCanonicalForm.HashBody([]));
    }

    [Fact]
    public void Bodies_of_zero_false_and_empty_string_hash_to_distinct_digests()
    {
        string[] bodies = ["0", "false", "\"\"", string.Empty];

        var hashes = bodies.Select(body => SignatureCanonicalForm.HashBody(Encoding.UTF8.GetBytes(body))).ToHashSet();

        Assert.Equal(bodies.Length, hashes.Count);
    }

    [Theory]
    [InlineData(nameof(SignedRequestFields.Audience))]
    [InlineData(nameof(SignedRequestFields.Method))]
    [InlineData(nameof(SignedRequestFields.Path))]
    [InlineData(nameof(SignedRequestFields.Query))]
    [InlineData(nameof(SignedRequestFields.BodyHash))]
    [InlineData(nameof(SignedRequestFields.Timestamp))]
    [InlineData(nameof(SignedRequestFields.Nonce))]
    public void A_change_to_any_single_signed_field_changes_the_mac(string field)
    {
        var original = HmacSignature.Compute(Key, _forms.RequestBytes(BaseFields));

        Assert.NotEqual(original, HmacSignature.Compute(Key, _forms.RequestBytes(Mutate(BaseFields, field))));
    }

    [Fact]
    public void Query_order_is_part_of_the_signature()
    {
        var reordered = BaseFields with { Query = "size=20&page=1" };

        Assert.NotEqual(_forms.Request(BaseFields), _forms.Request(reordered));
    }

    [Fact]
    public void The_method_is_signed_as_its_allowlist_literal_and_the_path_normalised()
    {
        var spelledDifferently = BaseFields with { Method = "post", Path = "/api/%72ecords/m1" };

        Assert.Equal(_forms.Request(BaseFields), _forms.Request(spelledDifferently));
    }

    [Fact]
    public void No_request_form_can_be_read_as_a_response_form()
    {
        var response = _forms.Response(new SignedResponseFields(BaseFields.Nonce, StatusCodes.Status200OK, "application/json", BaseFields.BodyHash));

        Assert.Contains("CODELABTEST-SIG-v1/RESP", response, StringComparison.Ordinal);
        Assert.NotEqual(_forms.Request(BaseFields), response);
    }

    [Fact]
    public void The_response_form_binds_nonce_status_and_content_type()
    {
        var baseline = new SignedResponseFields("nonce-a", StatusCodes.Status200OK, "application/json", "h");

        Assert.NotEqual(_forms.Response(baseline), _forms.Response(baseline with { RequestNonce = "nonce-b" }));
        Assert.NotEqual(_forms.Response(baseline), _forms.Response(baseline with { Status = StatusCodes.Status201Created }));
        Assert.NotEqual(_forms.Response(baseline), _forms.Response(baseline with { ContentType = "text/plain" }));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void The_signing_input_is_byte_identical_under_every_culture(string culture)
    {
        var invariant = _forms.Response(new SignedResponseFields("n", StatusCodes.Status404NotFound, "t", "h"));
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.Equal(invariant, _forms.Response(new SignedResponseFields("n", StatusCodes.Status404NotFound, "t", "h")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_method_outside_the_allowlist_cannot_be_framed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _forms.Request(BaseFields with { Method = "TRACE" }));
    }

    [Fact]
    public void Every_rejection_reason_has_a_distinct_problem_code()
    {
        var codes = Enum.GetValues<ProofRejectionReason>().Select(SecurityProblemCodes.ForRejection).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.StartsWith("signature_", code, StringComparison.Ordinal));
    }

    private static SignedRequestFields Mutate(SignedRequestFields fields, string field) => field switch
    {
        nameof(SignedRequestFields.Audience) => fields with { Audience = "Other.Api" },
        nameof(SignedRequestFields.Method) => fields with { Method = "PUT" },
        nameof(SignedRequestFields.Path) => fields with { Path = "/api/records/m2" },
        nameof(SignedRequestFields.Query) => fields with { Query = "page=2&size=20" },
        nameof(SignedRequestFields.BodyHash) => fields with { BodyHash = SignatureCanonicalForm.HashBody("{\"a\":2}"u8) },
        nameof(SignedRequestFields.Timestamp) => fields with { Timestamp = "946684801" },
        _ => fields with { Nonce = new string('M', SignatureHeader.NonceLength) },
    };
}
