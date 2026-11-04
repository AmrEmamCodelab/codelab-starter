using System.Security.Cryptography;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Signing;

public sealed class SessionKeyDerivationTests
{
    private static readonly byte[] Master = RandomNumberGenerator.GetBytes(SessionKeyDerivation.KeyBytes);
    private readonly SessionKeyDerivation _derivation = new(TestProtocols.Test);

    [Fact]
    public void The_same_session_always_derives_the_same_key_with_no_stored_state()
    {
        var once = _derivation.Derive(Master, new SessionIdentity("7", "a"));
        var again = new SessionKeyDerivation(TestProtocols.Test).Derive(Master, new SessionIdentity("7", "a"));

        Assert.Equal(once, again);
        Assert.Equal(SessionKeyDerivation.KeyBytes, once.Length);
        Assert.NotEqual(Master, once);
    }

    [Fact]
    public void Different_sessions_users_and_prefixes_derive_different_keys()
    {
        var keys = new[]
        {
            _derivation.Derive(Master, new SessionIdentity("7", "a")),
            _derivation.Derive(Master, new SessionIdentity("7", "b")),
            _derivation.Derive(Master, new SessionIdentity("8", "a")),
            new SessionKeyDerivation(TestProtocols.Alternate).Derive(Master, new SessionIdentity("7", "a")),
        };

        Assert.Equal(keys.Length, keys.Select(Convert.ToBase64String).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_derivation_matches_hkdf_over_the_framed_info()
    {
        var info = new Codelab.Security.Canonical.CanonicalFraming(TestProtocols.Test).BuildUtf8("CODELABTEST-SIG-v1/KDF", "CODELABTEST-SIG-v1", "a", "7");
        var expected = HKDF.DeriveKey(HashAlgorithmName.SHA256, Master, SessionKeyDerivation.KeyBytes, salt: [], info: info);

        Assert.Equal(expected, _derivation.Derive(Master, new SessionIdentity("7", "a")));
    }

    [Fact]
    public void A_short_master_or_a_blank_identity_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _derivation.Derive(new byte[16], new SessionIdentity("7", "a")));
        Assert.Throws<ArgumentException>(() => _derivation.Derive(Master, new SessionIdentity(" ", "a")));
        Assert.Throws<ArgumentException>(() => _derivation.Derive(Master, new SessionIdentity("7", string.Empty)));
    }

    [Fact]
    public void The_replay_scope_is_user_then_session()
    {
        Assert.Equal("7.a", new SessionIdentity("7", "a").Scope);
    }
}
