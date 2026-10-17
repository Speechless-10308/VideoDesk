using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class CommentTimestampTests
{
    [Theory]
    [InlineData("01:23", 83, null)]
    [InlineData("1:02:03", 3723, null)]
    [InlineData("2#03:10", 190, 2)]
    [InlineData("01：23", 83, null)]
    public void TryParse_ValidTimestamp_ReturnsPositionAndPart(string text, long seconds, int? page)
    {
        Assert.True(CommentTimestamps.TryParse(text, out var actual, out var actualPage));
        Assert.Equal(seconds, actual);
        Assert.Equal(page, actualPage);
    }

    [Theory]
    [InlineData("1:60")]
    [InlineData("1:60:00")]
    [InlineData("0#01:20")]
    [InlineData("-1:20")]
    [InlineData("9223372036854775807:00")]
    public void TryParse_InvalidTimestamp_RejectsInput(string text)
    {
        Assert.False(CommentTimestamps.TryParse(text, out _, out _));
    }

    [Fact]
    public void Find_MixedComment_PreservesTextOffsetsAndSkipsInvalidTime()
    {
        const string comment = "从 01:23 看起，第二段 2#03:10；忽略 99:99";

        var matches = CommentTimestamps.Find(comment).ToArray();

        Assert.Equal(2, matches.Length);
        Assert.Equal("01:23", comment.Substring(matches[0].Index, matches[0].Length));
        Assert.Equal("2#03:10", comment.Substring(matches[1].Index, matches[1].Length));
    }

    [Theory]
    [InlineData("83.5", 83.5)]
    [InlineData("1h2m3s", 3723)]
    [InlineData("01:23", 83)]
    public void TryParseQuery_OfficialLinkTime_ReturnsSeconds(string text, double expected)
    {
        Assert.True(CommentTimestamps.TryParseQuery(text, out var actual));
        Assert.Equal(expected, actual);
    }
}
