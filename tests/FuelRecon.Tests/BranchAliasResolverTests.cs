using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class BranchAliasResolverTests
{
    [Theory]
    [InlineData("Mobil Taupo")]
    [InlineData("Mobile - Taupo")]
    [InlineData("Hertz Taupo")]
    [InlineData("Taupo")]
    public void Resolve_maps_configured_aliases_to_same_canonical_branch(string rawAlias)
    {
        var resolver = CreateTaupoResolver();

        var result = resolver.Resolve(rawAlias);

        Assert.True(result.Success);
        Assert.Equal(rawAlias, result.RawValue);
        Assert.Equal("TAUPO", result.BranchId?.Value);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("mobil taupo")]
    [InlineData("MOBIL TAUPO")]
    [InlineData("MoBiL TaUpO")]
    public void Resolve_is_case_insensitive(string rawAlias)
    {
        var resolver = CreateTaupoResolver();

        var result = resolver.Resolve(rawAlias);

        Assert.True(result.Success);
        Assert.Equal("TAUPO", result.BranchId?.Value);
    }

    [Theory]
    [InlineData("MobileTaupo")]
    [InlineData("Mobile    Taupo")]
    [InlineData("Mobile-Taupo")]
    [InlineData("Mobile - Taupo")]
    [InlineData("Mobile--Taupo")]
    public void Resolve_is_whitespace_and_hyphen_insensitive(string rawAlias)
    {
        var resolver = CreateTaupoResolver();

        var result = resolver.Resolve(rawAlias);

        Assert.True(result.Success);
        Assert.Equal("TAUPO", result.BranchId?.Value);
    }

    [Theory]
    [InlineData("Mobile, Taupo")]
    [InlineData("Mobile.Taupo")]
    [InlineData("Mobile/Taupo")]
    public void Resolve_ignores_common_punctuation_differences(string rawAlias)
    {
        var resolver = CreateTaupoResolver();

        var result = resolver.Resolve(rawAlias);

        Assert.True(result.Success);
        Assert.Equal("TAUPO", result.BranchId?.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown Branch")]
    public void Resolve_returns_not_found_for_unknown_or_empty_alias(string? rawAlias)
    {
        var resolver = CreateTaupoResolver();

        var result = resolver.Resolve(rawAlias);

        Assert.False(result.Success);
        Assert.Equal(rawAlias ?? string.Empty, result.RawValue);
        Assert.Null(result.BranchId);
        Assert.Equal(BranchAliasResolver.BranchAliasNotFoundReasonCode, result.ReasonCode);
    }

    [Fact]
    public void Constructor_rejects_alias_pointing_to_missing_canonical_branch()
    {
        var branch = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var invalidAlias = new BranchAlias("Rotorua", new CanonicalBranchId("ROTORUA"));

        var exception = Assert.Throws<ArgumentException>(() => new BranchAliasResolver([branch], [invalidAlias]));

        Assert.Contains("missing canonical branch ID", exception.Message);
        Assert.Contains("ROTORUA", exception.Message);
    }

    [Fact]
    public void Constructor_rejects_conflicting_alias_keys_for_different_branches()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var rotorua = new BranchMaster(new CanonicalBranchId("ROTORUA"), "Rotorua");

        var exception = Assert.Throws<ArgumentException>(() => new BranchAliasResolver(
            [taupo, rotorua],
            [
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("MobileTaupo", rotorua.Id),
            ]));

        Assert.Contains("conflicts with an existing alias", exception.Message);
    }

    [Fact]
    public void BranchMaster_and_BranchAlias_trim_display_values_and_store_normalised_alias_key()
    {
        var branch = new BranchMaster(new CanonicalBranchId(" TAUPO "), " Taupo Branch ");
        var alias = new BranchAlias(" Mobile - Taupo ", branch.Id);

        Assert.Equal("TAUPO", branch.Id.Value);
        Assert.Equal("Taupo Branch", branch.DisplayName);
        Assert.Equal("Mobile - Taupo", alias.Alias);
        Assert.Equal("MOBILETAUPO", alias.NormalisedAliasKey);
    }

    [Fact]
    public void BranchMaster_and_BranchAlias_reject_empty_text()
    {
        Assert.Throws<ArgumentException>(() => new BranchMaster(new CanonicalBranchId("TAUPO"), " "));
        Assert.Throws<ArgumentException>(() => new BranchAlias(" ", new CanonicalBranchId("TAUPO")));
        Assert.Throws<ArgumentException>(() => new BranchAlias(" - / . ", new CanonicalBranchId("TAUPO")));
    }

    private static BranchAliasResolver CreateTaupoResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");

        return new BranchAliasResolver(
            [taupo],
            [
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("Taupo", taupo.Id),
            ]);
    }
}
