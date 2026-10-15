using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Iov.OnvifSimulator.Services;

public static class FfmpegLocator
{
    public static string? Find()
    {
        var fromPath = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
        if (fromPath != null)
        {
            return fromPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsAvailable() => Find() != null;

    public static string? FindFfprobe()
    {
        var ffmpeg = Find();
        if (ffmpeg != null)
        {
            var dir = Path.GetDirectoryName(ffmpeg);
            if (!string.IsNullOrEmpty(dir))
            {
                var probe = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
                if (File.Exists(probe))
                {
                    return probe;
                }
            }
        }

        return FindOnPath("ffprobe.exe") ?? FindOnPath("ffprobe");
    }

    /// <summary>
    /// Probes frame rate. Prefers avg_frame_rate (matches VLC); falls back to r_frame_rate.
    /// </summary>
    public static double? TryProbeFrameRate(string mediaFile)
    {
        var ffprobe = FindFfprobe();
        if (ffprobe == null)
        {
            return TryProbeFrameRateViaFfmpeg(mediaFile);
        }

        // avg_frame_rate matches VLC; r_frame_rate is often the codec tick rate (e.g. 25) and wrong for pacing.
        var avg = ProbeFfprobeRate(ffprobe, mediaFile, "avg_frame_rate");
        if (avg is > 0.5 and < 120)
        {
            return avg;
        }

        var nominal = ProbeFfprobeRate(ffprobe, mediaFile, "r_frame_rate");
        if (nominal is > 0.5 and < 120)
        {
            return nominal;
        }

        return TryProbeFrameRateViaFfmpeg(mediaFile);
    }

    private static double? ProbeFfprobeRate(string ffprobe, string mediaFile, string field)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments =
                    $"-v error -select_streams v:0 -show_entries stream={field} -of default=noprint_wrappers=1:nokey=1 \"{mediaFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(8000);
            if (TryParseRate(output, out var fps) && fps > 0.5 && fps < 120)
            {
                return fps;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static double? TryProbeFrameRateViaFfmpeg(string mediaFile)
    {
        var ffmpeg = Find();
        if (ffmpeg == null)
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-hide_banner -i \"{mediaFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            var match = Regex.Match(stderr, @"(\d+(?:\.\d+)?)\s*fps", RegexOptions.IgnoreCase);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) &&
                fps > 0.5 && fps < 120)
            {
                return fps;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool TryParseRate(string text, out double fps)
    {
        fps = 0;
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den > 0)
        {
            fps = num / den;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore invalid PATH entries
            }
        }

        return null;
    }
}
