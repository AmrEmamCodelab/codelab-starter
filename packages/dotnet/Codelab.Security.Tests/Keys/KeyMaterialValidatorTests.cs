using Codelab.Security.Keys;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Keys;

public sealed class KeyMaterialValidatorTests
{
    private const string Setting = "Codelab:Security:Test:Key";

    [Fact]
    public void Exactly_thirty_two_decoded_bytes_are_accepted()
    {
        Assert.True(KeyMaterialValidator.ValidateBase64Key(TestKeys.NewKey(), Setting).IsValid);
    }

    [Fact]
    public void A_twenty_four_byte_key_is_refused_even_though_its_text_is_thirty_two_characters()
    {
        var shortKey = Convert.ToBase64String(new byte[24]);

        var result = KeyMaterialValidator.ValidateBase64Key(shortKey, Setting);

        Assert.Equal(32, shortKey.Length);
        Assert.False(result.IsValid);
        Assert.Contains("24 bytes", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_material_is_refused(string? value)
    {
        Assert.False(KeyMaterialValidator.ValidateBase64Key(value, Setting).IsValid);
    }

    [Fact]
    public void The_committed_placeholder_is_refused_even_when_long_enough_to_look_valid()
    {
        var placeholder = KeyMaterialValidator.PlaceholderMarker + new string('x', 64);

        Assert.Contains("placeholder", KeyMaterialValidator.ValidateBase64Key(placeholder, Setting).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Material_that_is_not_base64_is_refused_and_never_echoed()
    {
        const string notBase64 = "this is not base64 at all, not even close!!";

        var result = KeyMaterialValidator.ValidateBase64Key(notBase64, Setting);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(notBase64, result.Error, StringComparison.Ordinal);
        Assert.Contains(Setting, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ring_whose_active_key_is_a_member_is_accepted()
    {
        Assert.True(KeyMaterialValidator.ValidateRing(TestKeys.Ring("k1", "k2"), "S").IsValid);
    }

    [Fact]
    public void An_active_key_id_naming_no_member_or_missing_is_refused()
    {
        var ring = TestKeys.Ring("k1");
        ring.ActiveKeyId = "k9";
        Assert.False(KeyMaterialValidator.ValidateRing(ring, "S").IsValid);

        ring.ActiveKeyId = null;
        Assert.Contains("ActiveKeyId", KeyMaterialValidator.ValidateRing(ring, "S").Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_empty_and_absent_key_ids_are_refused()
    {
        var duplicate = TestKeys.Ring("k1", "k1");
        var empty = TestKeys.Ring("k1");
        empty.Keys.Add(new KeyEntryOptions { KeyId = " ", MaterialBase64 = TestKeys.NewKey() });

        Assert.Contains("twice", KeyMaterialValidator.ValidateRing(duplicate, "S").Error, StringComparison.Ordinal);
        Assert.False(KeyMaterialValidator.ValidateRing(empty, "S").IsValid);
        Assert.False(KeyMaterialValidator.ValidateRing(new KeyRingOptions(), "S").IsValid);
    }

    [Fact]
    public void A_ring_containing_one_weak_key_is_refused()
    {
        var ring = TestKeys.Ring("k1");
        ring.Keys.Add(new KeyEntryOptions { KeyId = "k2", MaterialBase64 = Convert.ToBase64String(new byte[16]) });

        Assert.Contains("k2", KeyMaterialValidator.ValidateRing(ring, "S").Error, StringComparison.Ordinal);
    }
}
