using System.Globalization;
using System.Text;

namespace VideoDesk.Core;

public static class MpvMediaSource
{
    // mpv's EDL new_stream header gives DASH audio and video a single cache
    // and lifetime. Length quoting is in UTF-8 bytes, not .NET characters.
    public static string Create(PlayStream stream)
    {
        if (string.IsNullOrEmpty(stream.AudioUrl)) return stream.VideoUrl;
        return "edl://!no_chapters;" + Quote(stream.VideoUrl) +
            ";!new_stream;!no_chapters;" + Quote(stream.AudioUrl);
    }

    private static string Quote(string value) =>
        "%" + Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + "%" + value;
}
