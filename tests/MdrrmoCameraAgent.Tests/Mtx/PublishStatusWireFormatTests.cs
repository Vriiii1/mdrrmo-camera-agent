using FluentAssertions;
using MdrrmoCameraAgent.Mtx;

namespace MdrrmoCameraAgent.Tests.Mtx;

public class PublishStatusWireFormatTests
{
    [Theory]
    [InlineData(PublishStatus.Publishing,        "publishing")]
    [InlineData(PublishStatus.DegradedNoFrames,  "degraded_no_frames")]
    [InlineData(PublishStatus.DegradedAuth,      "degraded_auth")]
    [InlineData(PublishStatus.Disabled,          "disabled")]
    [InlineData(PublishStatus.Unknown,           "unknown")]
    public void ToWireString_MatchesPostgresCheckConstraint(PublishStatus s, string expected)
    {
        s.ToWireString().Should().Be(expected);
    }

    [Fact]
    public void ToWireString_OutOfRangeIntCast_Throws()
    {
        var act = () => ((PublishStatus)99).ToWireString();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
