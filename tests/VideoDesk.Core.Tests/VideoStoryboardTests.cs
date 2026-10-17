using System.Text.Json.Nodes;
using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class VideoStoryboardTests
{
    [Fact]
    public void ParseVideoStoryboard_OfficialResponse_SkipsSentinelAndKeepsSpritePages()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "videoshot.json"));

        var result = BilibiliClient.ParseVideoStoryboard(JsonNode.Parse(json)!);

        Assert.NotNull(result);
        Assert.Equal(154, result.Times.Count);
        Assert.Equal(2, result.Images.Count);
        Assert.Equal(6, result.FrameAt(33));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(1000, 3)]
    public void FrameAt_Position_SelectsPrecedingFrameWithinBounds(double seconds, int expected)
    {
        var storyboard = new VideoStoryboard(2, 2, 160, 90, ["https://example.test/sprite.jpg"], [0, 5, 10, 15]);

        Assert.Equal(expected, storyboard.FrameAt(seconds));
    }

    [Theory]
    [InlineData("{\"img_x_len\":0}")]
    [InlineData("{\"img_x_len\":2,\"img_y_len\":2,\"img_x_size\":160,\"img_y_size\":90,\"image\":[\"https://example.test/sprite.jpg\"],\"index\":[0,10,5]}")]
    public void ParseVideoStoryboard_InvalidMetadata_ReturnsNoPreview(string data)
    {
        Assert.Null(BilibiliClient.ParseVideoStoryboard(new JsonObject { ["data"] = JsonNode.Parse(data) }));
    }
}
