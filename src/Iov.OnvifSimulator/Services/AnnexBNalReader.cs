using System.Diagnostics;
using SharpRTSPServer;

namespace Iov.OnvifSimulator.Services;

internal static class AnnexBNalReader
{
    public static async Task<(byte[] Sps, byte[] Pps)> WaitForParameterSetsAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[]? sps = null;
        byte[]? pps = null;
        await foreach (var nal in ReadNalsAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            var type = nal[0] & 0x1F;
            if (type == 7)
            {
                sps = nal;
            }
            else if (type == 8)
            {
                pps = nal;
            }

            if (sps != null && pps != null)
            {
                return (sps, pps);
            }
        }

        throw new InvalidOperationException("未能从视频流中解析 H264 SPS/PPS。");
    }

    public static async IAsyncEnumerable<byte[]> ReadNalsAsync(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var start = -1;

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                if (start >= 0 && buffer.Length - start > 4)
                {
                    yield return Extract(buffer, start, (int)buffer.Length);
                }

                yield break;
            }

            var offset = (int)buffer.Length;
            buffer.Write(chunk, 0, read);
            var data = buffer.GetBuffer();
            var length = (int)buffer.Length;

            for (var i = Math.Max(0, offset - 3); i < length - 3; i++)
            {
                if (!IsStartCode(data, i, out var codeLength))
                {
                    continue;
                }

                if (start >= 0)
                {
                    var nal = Extract(buffer, start, i);
                    if (nal.Length > 0)
                    {
                        yield return nal;
                    }
                }

                start = i + codeLength;
            }

            if (start > 64 * 1024)
            {
                Compact(buffer, ref start);
            }
        }
    }

    public static async Task PumpToTrackAsync(
        Stream stream,
        H264Track track,
        uint rtpBaseTime,
        CancellationToken cancellationToken)
    {
        uint timestamp = rtpBaseTime;
        const uint step = 3600;
        var accessUnit = new List<byte[]>();

        await foreach (var nal in ReadNalsAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            var type = nal[0] & 0x1F;
            accessUnit.Add(nal);
            if (type is < 1 or > 5)
            {
                continue;
            }

            track.FeedInRawSamples(timestamp, accessUnit.Select(x => (ReadOnlyMemory<byte>)x).ToList());
            accessUnit.Clear();
            timestamp = unchecked(timestamp + step);
        }
    }

    public static Process StartFfmpeg(string ffmpegPath, string inputFile, bool realtime)
    {
        var re = realtime ? "-re " : string.Empty;
        var args =
            $"-hide_banner -loglevel error {re}-i \"{inputFile}\" -an -c:v libx264 -preset ultrafast -tune zerolatency -bf 0 -pix_fmt yuv420p -g 25 -f h264 pipe:1";

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("启动 FFmpeg 失败。");
        }

        return process;
    }

    private static bool IsStartCode(byte[] data, int index, out int length)
    {
        if (data[index] == 0 && data[index + 1] == 0 && data[index + 2] == 1)
        {
            length = 3;
            return true;
        }

        if (index + 3 < data.Length && data[index] == 0 && data[index + 1] == 0 && data[index + 2] == 0 && data[index + 3] == 1)
        {
            length = 4;
            return true;
        }

        length = 0;
        return false;
    }

    private static byte[] Extract(MemoryStream buffer, int start, int end)
    {
        var length = end - start;
        if (length <= 0)
        {
            return [];
        }

        var nal = new byte[length];
        Buffer.BlockCopy(buffer.GetBuffer(), start, nal, 0, length);
        return nal;
    }

    private static void Compact(MemoryStream buffer, ref int start)
    {
        var data = buffer.GetBuffer();
        var remaining = (int)buffer.Length - start;
        Buffer.BlockCopy(data, start, data, 0, remaining);
        buffer.SetLength(remaining);
        buffer.Position = remaining;
        start = 0;
    }
}
