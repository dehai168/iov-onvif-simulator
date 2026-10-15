using System.Diagnostics;
using SharpISOBMFF;
using SharpMP4.Readers;
using SharpRTSPServer;

namespace Iov.OnvifSimulator.Services;

internal static class Mp4MediaReader
{
    private const uint RtpClock = 90000;
    private const double PacingLeadSeconds = 0.02;
    private const double DefaultFps = 25;

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

    /// <summary>
    /// Plays at the real average frame rate (ffprobe / sample-table), not SPS DefaultSampleDuration
    /// which is often wrong (e.g. reports 25fps while the file is ~20fps).
    /// </summary>
    public static async Task<uint> PlayAsync(
        string path,
        ITrack rtspTrack,
        uint rtpBaseTime,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        await using var input = new BufferedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        var iso = new IsoStream(input);
        var container = new Container();
        container.Read(iso);

        var reader = new VideoReader();
        reader.Parse(container);
        var videoTrack = reader.GetTracks().First(t => t.HandlerType == HandlerTypes.Video);

        var sourceTimescale = GetMediaTimescale(container, videoTrack.TrackID);
        if (sourceTimescale == 0)
        {
            sourceTimescale = videoTrack.Timescale != 0 ? videoTrack.Timescale : RtpClock;
        }

        var sttsFps = TrySttsFps(videoTrack, sourceTimescale);
        var tableFps = TryMeasureFpsFromSamples(reader, videoTrack.TrackID, sourceTimescale);
        // Rewind after measurement.
        RewindTrack(reader, videoTrack.TrackID);

        var probedFps = FfmpegLocator.TryProbeFrameRate(path);
        var fps = PickFrameRate(probedFps, tableFps, sttsFps);
        var frameSeconds = 1.0 / fps;
        var rtpStep = Math.Max(1.0, RtpClock / fps);

        log?.Invoke(
            $"MP4 推流帧率 {fps:0.####} fps " +
            $"(ffprobe={FormatFps(probedFps)}, sample表={FormatFps(tableFps)}, stts/SPS={FormatFps(sttsFps)}, timescale={sourceTimescale})");

        var clock = Stopwatch.StartNew();
        long frameIndex = 0;
        long lastRtpPts = -1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var sample = reader.ReadSample(videoTrack.TrackID);
            if (sample == null)
            {
                break;
            }

            var units = reader.ParseSample(videoTrack.TrackID, sample.Data)
                .Select(static u => (ReadOnlyMemory<byte>)u.ToArray())
                .ToList();

            // Pace by real fps: container DefaultSampleDuration is unreliable on many recorder MP4s.
            var dueAt = frameIndex * frameSeconds - PacingLeadSeconds;
            while (clock.Elapsed.TotalSeconds < dueAt)
            {
                var waitMs = (int)Math.Ceiling((dueAt - clock.Elapsed.TotalSeconds) * 1000);
                if (waitMs <= 0)
                {
                    break;
                }

                await Task.Delay(Math.Min(waitMs, 15), cancellationToken).ConfigureAwait(false);
            }

            var rtpPts = (long)Math.Round(frameIndex * rtpStep);
            if (lastRtpPts >= 0 && rtpPts <= lastRtpPts)
            {
                rtpPts = lastRtpPts + (long)Math.Round(rtpStep);
            }

            rtspTrack.FeedInRawSamples(unchecked(rtpBaseTime + (uint)rtpPts), units);
            lastRtpPts = rtpPts;
            frameIndex++;
        }

        if (frameIndex == 0)
        {
            return rtpBaseTime;
        }

        return unchecked(rtpBaseTime + (uint)lastRtpPts + (uint)Math.Round(rtpStep));
    }

    private static double PickFrameRate(double? probed, double? table, double? stts)
    {
        // Prefer ffprobe avg_frame_rate (matches VLC). Then measured sample DTS. Last: stts/SPS.
        foreach (var candidate in new[] { probed, table, stts })
        {
            if (candidate is > 0.5 and < 120)
            {
                return candidate.Value;
            }
        }

        return DefaultFps;
    }

    private static string FormatFps(double? fps) => fps is null ? "n/a" : fps.Value.ToString("0.####");

    private static double? TrySttsFps(SharpMP4.Tracks.ITrack videoTrack, uint sourceTimescale)
    {
        if (videoTrack.DefaultSampleDuration <= 0)
        {
            return null;
        }

        var scale = videoTrack.Timescale != 0 ? videoTrack.Timescale : sourceTimescale;
        if (scale == 0)
        {
            return null;
        }

        var seconds = videoTrack.DefaultSampleDuration / (double)scale;
        if (seconds is <= 0.005 or >= 1.0)
        {
            return null;
        }

        return 1.0 / seconds;
    }

    /// <summary>
    /// Reads a prefix of the track and derives fps from median DTS deltas.
    /// </summary>
    private static double? TryMeasureFpsFromSamples(VideoReader reader, uint trackId, uint timescale)
    {
        if (timescale == 0)
        {
            return null;
        }

        const int maxSamples = 120;
        long? prevDts = null;
        var deltas = new List<long>(maxSamples);

        for (var i = 0; i < maxSamples; i++)
        {
            var sample = reader.ReadSample(trackId);
            if (sample == null)
            {
                break;
            }

            if (prevDts is long prev)
            {
                var delta = sample.DTS - prev;
                if (delta > 0)
                {
                    deltas.Add(delta);
                }
            }

            prevDts = sample.DTS;
        }

        if (deltas.Count < 5)
        {
            return null;
        }

        deltas.Sort();
        var median = deltas[deltas.Count / 2];
        if (median <= 0)
        {
            return null;
        }

        var fps = timescale / (double)median;
        return fps is > 0.5 and < 120 ? fps : null;
    }

    private static void RewindTrack(VideoReader reader, uint trackId)
    {
        if (!reader.Tracks.TryGetValue(trackId, out var context))
        {
            return;
        }

        context.SampleIndex = 0;
        context.FragmentIndex = 0;
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

        return 0;
    }
}
