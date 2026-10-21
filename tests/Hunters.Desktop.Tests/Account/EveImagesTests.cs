using NewEdenSoft.Hunters.Client;

namespace NewEdenSoft.Hunters.Desktop.Tests.Account;

public sealed class EveImagesTests
{
    [Fact]
    public void Character_portrait_url() =>
        Assert.Equal("https://images.evetech.net/characters/90000001/portrait?size=128", EveImages.CharacterPortrait(90000001, 128).AbsoluteUri);

    [Fact]
    public void Unsupported_sizes_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EveImages.CharacterPortrait(90000001, 100));
}
