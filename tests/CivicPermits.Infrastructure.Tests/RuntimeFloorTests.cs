using CivicPermits.Infrastructure.Hosting;

namespace CivicPermits.Infrastructure.Tests;

public class RuntimeFloorTests
{
    [Theory]
    [InlineData("10.0.7")]
    [InlineData("10.0.14")]
    [InlineData("10.1.0")]
    [InlineData("11.0.0")]
    public void A_host_on_the_fixed_runtime_or_later_may_start_anywhere(string running) =>
        RuntimeFloor.Require(isDevelopment: false, Version.Parse(running));

    [Theory]
    [InlineData("10.0.0")]
    [InlineData("10.0.6")]
    [InlineData("9.0.100")]
    public void A_host_on_a_runtime_that_let_cookies_be_forged_is_refused_outside_development(string running)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => RuntimeFloor.Require(isDevelopment: false, Version.Parse(running)));

        Assert.Contains(RuntimeFloor.Minimum.ToString(), refused.Message, StringComparison.Ordinal);
        Assert.Contains(running, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runtime_whose_version_cannot_be_read_is_refused_outside_development() =>
        Assert.Throws<InvalidOperationException>(() => RuntimeFloor.Require(isDevelopment: false, running: null));

    [Fact]
    public void Development_is_never_refused_so_a_developer_can_work_on_any_machine()
    {
        RuntimeFloor.Require(isDevelopment: true, new Version(10, 0, 0));
        RuntimeFloor.Require(isDevelopment: true, running: null);
    }

    [Fact]
    public void The_runtime_these_tests_run_on_is_readable_and_meets_the_floor()
    {
        Version? running = RuntimeFloor.Running();

        Assert.NotNull(running);
        Assert.True(running >= RuntimeFloor.Minimum, $"The tests run on ASP.NET Core {running}, below the floor {RuntimeFloor.Minimum}.");
    }
}
