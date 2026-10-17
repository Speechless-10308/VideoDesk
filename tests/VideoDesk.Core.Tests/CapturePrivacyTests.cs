using System.Text.Json.Nodes;
using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class CapturePrivacyTests
{
    [Fact]
    public void Sanitize_NestedSecrets_RedactsCopyAndKeepsOriginalUnchanged()
    {
        var original = JsonNode.Parse("""
            {"refresh_token":"secret-refresh","nested":{"qrcode_key":"secret-qr",
            "baseUrl":"https://cdn.example/video?e=secret-stream&upsig=secret-sign"},"title":"保留标题"}
            """)!;
        var before = original.ToJsonString();

        var sanitized = CaptureStore.Sanitize(original)!;

        Assert.DoesNotContain("secret-", sanitized.ToJsonString());
        Assert.Equal("保留标题", sanitized["title"]!.GetValue<string>());
        Assert.Equal(before, original.ToJsonString());
    }
}
