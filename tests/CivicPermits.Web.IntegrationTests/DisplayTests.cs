using CivicPermits.Web.Infrastructure;

namespace CivicPermits.Web.IntegrationTests;

public class DisplayTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/applications")]
    [InlineData("/applications?status=Issued&page=2")]
    [InlineData("/applications/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public void A_path_on_this_site_is_accepted(string target)
    {
        Assert.Equal(target, Display.LocalPath(target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("applications")]
    [InlineData("https://evil.example/")]
    [InlineData("http://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public void Anything_that_could_leave_the_site_is_refused(string? target)
    {
        Assert.Null(Display.LocalPath(target));
    }

    [Theory]
    [InlineData("/ok\nSet-Cookie: a=b")]
    [InlineData("/ok\rX: y")]
    [InlineData("/ok\tmore")]
    [InlineData("/ok\0more")]
    [InlineData("/ok\u007Fmore")]
    [InlineData("/ok\u0085more")]
    [InlineData("/\t/evil.example")]
    public void Every_control_character_is_refused_not_only_the_line_breaks(string target)
    {
        Assert.Null(Display.LocalPath(target));
    }
}
