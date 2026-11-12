using PaceLaps.SimHub.Core;

namespace PaceLaps.SimHub.Core.Tests;

public sealed class OverlayLinkTests
{
    private const string Token = "Ab3dEfGh1jKlMn0pQrStUvWxYz0123456789abcdefABCDEF";

    [Fact]
    public void An_overlay_url_gives_the_site_and_the_token()
    {
        var link = OverlayLink.Parse($"  https://app.pacelaps.com/overlay/{Token}?theme=dark  ", out var problem)!;

        Assert.Null(problem);
        Assert.Equal(new Uri("https://app.pacelaps.com/"), link.Site);
        Assert.Equal(new Uri($"https://app.pacelaps.com/overlay/{Token}"), link.Page);
        Assert.Equal(Token, link.Token);
    }

    [Fact]
    public void The_link_never_shows_its_token() =>
        Assert.DoesNotContain(Token, OverlayLink.Parse($"https://app.pacelaps.com/overlay/{Token}", out _)!.ToString(), StringComparison.Ordinal);

    [Fact]
    public void Plain_http_works_only_on_a_development_host()
    {
        Assert.NotNull(OverlayLink.Parse($"http://localhost:8081/overlay/{Token}", out _));
        Assert.NotNull(OverlayLink.Parse($"http://pacelaps.test/overlay/{Token}", out _));
        Assert.Null(OverlayLink.Parse($"http://app.pacelaps.com/overlay/{Token}", out var problem));
        Assert.Contains("https", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://app.pacelaps.com/driver")]
    [InlineData("https://app.pacelaps.com/overlay/short")]
    [InlineData("https://app.pacelaps.com/overlay/" + Token + "/extra")]
    [InlineData("ftp://app.pacelaps.com/overlay/" + Token)]
    public void Anything_else_is_refused_with_a_reason(string text)
    {
        Assert.Null(OverlayLink.Parse(text, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }
}
