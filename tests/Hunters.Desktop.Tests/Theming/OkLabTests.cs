using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Tests.Theming;

public sealed class OkLabTests
{
    [Theory]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    [InlineData("#8fb3c9")]
    [InlineData("#e0b04a")]
    public void Round_trips_through_oklab(string hex)
    {
        var color = Rgba.Parse(hex);
        var (l, a, b) = OkLab.ToOkLab(color);

        Assert.Equal(hex, OkLab.FromOkLab(l, a, b).ToHex());
    }

    [Fact]
    public void Full_percentage_returns_first_colour()
    {
        Assert.Equal("#8fb3c9", OkLab.Mix(Rgba.Parse("#8fb3c9"), 100, Rgba.Parse("#000000")).ToHex());
    }

    [Fact]
    public void Mixing_with_transparent_keeps_hue_and_sets_alpha()
    {
        // CSS: color-mix(in oklab, #8fb3c9 32%, transparent) = #8fb3c9 at 32% alpha.
        var mixed = OkLab.Mix(Rgba.Parse("#8fb3c9"), 32, Rgba.Transparent);

        Assert.Equal(0x8f, mixed.R8);
        Assert.Equal(0xb3, mixed.G8);
        Assert.Equal(0xc9, mixed.B8);
        Assert.Equal(0.32, mixed.A, precision: 3);
    }

    [Fact]
    public void Black_and_white_midpoint_matches_css()
    {
        // CSS: color-mix(in oklab, white 50%, black) = oklab(0.5 0 0) ≈ rgb(99 99 99).
        var mid = OkLab.Mix(Rgba.Parse("#ffffff"), 50, Rgba.Parse("#000000"));

        Assert.InRange(mid.R8, 98, 100);
        Assert.Equal(mid.R8, mid.G8);
        Assert.Equal(mid.G8, mid.B8);
    }
}
