using SharpISOBMFF;
using SharpMP4.Readers;
using SharpRTSPServer;

namespace Iov.OnvifSimulator.Services;

internal static class Mp4MediaReader
{
    public static bool IsMp4Family(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".m4v" or ".mov";
    }

    public static bool TryCreateVideoTrack(string path, out ITrack? track, out string? error)
    {
        track = null;
        error = null;

        try
        {
            using var input = new BufferedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var iso = new IsoStream(input);
            var container = new Container();
            container.Read(iso);

            var reader = new VideoReader();
            reader.Parse(container);
            var videoTrack = reader.GetTracks().FirstOrDefault(t => t.HandlerType == HandlerTypes.Video);
            if (videoTrack == null)
            {
                error = "文件中没有视频轨道。";
                return false;
            }

            var units = videoTrack.GetContainerSamples()?.ToList() ?? [];
            if (videoTrack is SharpMP4.Tracks.H264Track)
            {
                if (units.Count < 2)
                {
                    error = "无法读取 H264 SPS/PPS。";
                    return false;
                }

                var h264 = new H264Track();
                h264.SetParameterSets(units[0], units[1]);
                track = h264;
                return true;
            }

            if (videoTrack is SharpMP4.Tracks.H265Track)
            {
                if (units.Count < 3)
                {
                    error = "无法读取 H265 参数集。";
                    return false;
                }

                var h265 = new H265Track();
                h265.SetParameterSets(units[0], units[1], units[2]);
                track = h265;
                return true;
            }

            error = $"暂不直接支持该 MP4 编码（{videoTrack.GetType().Name}），将尝试使用 FFmpeg。";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static async Task PlayAsync(string path, ITrack rtspTrack, uint rtpBaseTime, CancellationToken cancellationToken)
    {
        await using var input = new BufferedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        var iso = new IsoStream(input);
        var container = new Container();
        container.Read(iso);

        var reader = new VideoReader();
        reader.Parse(container);
        var videoTrack = reader.GetTracks().First(t => t.HandlerType == HandlerTypes.Video);
        var timescale = GetMediaTimescale(container, videoTrack.TrackID);
        const int videoClock = 90000;
        var started = StopwatchStart();

        while (!cancellationToken.IsCancellationRequested)
        {
            var sample = reader.ReadSample(videoTrack.TrackID);
            if (sample == null)
            {
                break;
            }

            var units = reader.ParseSample(videoTrack.TrackID, sample.Data);
            var pts = sample.PTS * videoClock / Math.Max(1, timescale);
            var elapsedTicks = started();
            var targetMs = pts * 1000d / videoClock;
            var delay = (int)(targetMs - elapsedTicks);
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            rtspTrack.FeedInRawSamples(
                unchecked(rtpBaseTime + (uint)pts),
                units.Select(u => (ReadOnlyMemory<byte>)u).ToList());
        }
    }

    private static Func<double> StopwatchStart()
    {
        var start = Environment.TickCount64;
        return () => Environment.TickCount64 - start;
    }

    private static uint GetMediaTimescale(Container container, uint trackId)
    {
        foreach (var moov in container.Children.OfType<MovieBox>())
        {
            foreach (var trak in moov.Children.OfType<TrackBox>())
            {
                var tkhd = trak.Children.OfType<TrackHeaderBox>().FirstOrDefault();
                if (tkhd == null || tkhd.TrackID != trackId)
                {
                    continue;
                }

                var mdhd = trak.Children.OfType<MediaBox>().Single()
                    .Children.OfType<MediaHeaderBox>().Single();
                return mdhd.Timescale;
            }
        }

        return 90000;
    }
}
