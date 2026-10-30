using PrintInvoicer.Core.Updates;

namespace PrintInvoicer.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v3.2.0", "3.2.0")]
    [InlineData("V3.3", "3.3.0")]
    [InlineData("3.2.0+32d0eea5a30994ab858c6030029ba5f2d72d28b5", "3.2.0")]
    [InlineData("v4.0.0-beta.1", "4.0.0")]
    [InlineData(" 3.10.2 ", "3.10.2")]
    public void Parses_tags(string tag, string expected) => Assert.Equal(Version.Parse(expected), ReleaseVersion.Parse(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("PrintInvoicer")]
    public void Rejects_non_versions(string? tag) => Assert.Null(ReleaseVersion.Parse(tag));

    [Fact]
    public void Compares_tag_with_assembly_version()
    {
        var installed = new Version(3, 2, 0, 0);   // assembly versions have four parts
        Assert.False(ReleaseVersion.IsNewer(ReleaseVersion.Parse("v3.2.0")!, installed));
        Assert.True(ReleaseVersion.IsNewer(ReleaseVersion.Parse("v3.2.1")!, installed));
        Assert.True(ReleaseVersion.IsNewer(ReleaseVersion.Parse("v3.10.0")!, installed));   // numeric, not text order
        Assert.False(ReleaseVersion.IsNewer(ReleaseVersion.Parse("v3.1.9")!, installed));
        Assert.Equal("3.2.0", ReleaseVersion.Display(installed));
    }

    [Fact]
    public void Checks_at_most_once_a_day()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ReleaseVersion.IsDue(null, now));
        Assert.False(ReleaseVersion.IsDue(now.AddHours(-3), now));
        Assert.True(ReleaseVersion.IsDue(now.AddHours(-24), now));
        Assert.True(ReleaseVersion.IsDue(now.AddDays(2), now));      // clock moved back
    }
}
