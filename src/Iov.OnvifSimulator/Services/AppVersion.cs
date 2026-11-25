using System.Reflection;
using System.Security.Cryptography;

namespace Iov.OnvifSimulator.Services;

public static class AppVersion
{
    private static readonly Lazy<string> LazyDisplay = new(BuildDisplay);

    public static string Display => LazyDisplay.Value;

    private static string BuildDisplay()
    {
        var path = ResolveBinaryPath();
        var buildTime = File.GetLastWriteTime(path);
        var hash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        return $"{buildTime:yyyyMMddHHmm}-{hash[..4]}";
    }

    private static string ResolveBinaryPath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        var location = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            return location;
        }

        return typeof(AppVersion).Assembly.Location;
    }
}
