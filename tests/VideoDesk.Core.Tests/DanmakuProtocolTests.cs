using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class DanmakuProtocolTests
{
    [Fact]
    public void Parse_ProtobufSegment_PreservesServerIdsAndMilliseconds()
    {
        // Two protobuf messages: id=2 at 4000ms, then id=1 at 0ms.
        byte[] segment = [10, 10, 8, 2, 16, 0xA0, 0x1F, 24, 1, 58, 1, 98,
                          10, 9, 8, 1, 16, 0, 24, 1, 58, 1, 97];

        var result = DanmakuProtocol.Parse(segment);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == 2 && item.Time == 4 && item.Text == "b");
        Assert.Contains(result, item => item.Id == 1 && item.Time == 0 && item.Text == "a");
    }

    [Fact]
    public void Parse_TruncatedProtobuf_RejectsInvalidMessage()
    {
        Assert.Throws<InvalidDataException>(() => DanmakuProtocol.Parse([10, 100, 8, 1]));
    }
}
