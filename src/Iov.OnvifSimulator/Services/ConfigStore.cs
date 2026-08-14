using System.Text.Json;
using Iov.OnvifSimulator.Models;

namespace Iov.OnvifSimulator.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string FilePath { get; }

    public ConfigStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Iov.OnvifSimulator");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            var config = CreateDefault();
            Save(config);
            return config;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config?.Simulators == null || config.Simulators.Count == 0)
            {
                return CreateDefault();
            }

            return config;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Simulators =
            [
                new SimulatorConfig
                {
                    Name = "Camera-1",
                    HttpPort = 8080,
                    RtspPort = 8554,
                    SerialNumber = "SN0001"
                }
            ]
        };
    }

    public static SimulatorConfig CreateNext(IReadOnlyList<SimulatorConfig> existing)
    {
        var index = existing.Count + 1;
        var http = 8080;
        var rtsp = 8554;
        while (existing.Any(x => x.HttpPort == http))
        {
            http++;
        }

        while (existing.Any(x => x.RtspPort == rtsp))
        {
            rtsp++;
        }

        return new SimulatorConfig
        {
            Name = $"Camera-{index}",
            HttpPort = http,
            RtspPort = rtsp,
            SerialNumber = $"SN{index:0000}"
        };
    }
}
