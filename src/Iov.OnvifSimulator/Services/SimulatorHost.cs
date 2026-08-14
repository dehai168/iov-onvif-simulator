using Iov.OnvifSimulator.Models;

namespace Iov.OnvifSimulator.Services;

public sealed class SimulatorHost : IAsyncDisposable
{
    private readonly Action<string> _log;
    private OnvifWebHost? _onvif;
    private RtspPlaylistServer? _rtsp;

    public SimulatorHost(SimulatorConfig config, Action<string> log)
    {
        Config = config;
        _log = log;
    }

    public SimulatorConfig Config { get; }

    public bool IsRunning { get; private set; }

    public string StatusText => IsRunning ? "运行中" : "已停止";

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Validate();

        var onvif = new OnvifWebHost(Config, _log);
        var rtsp = new RtspPlaylistServer(Config, _log);
        try
        {
            rtsp.Start();
            await onvif.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            await rtsp.StopAsync().ConfigureAwait(false);
            await onvif.StopAsync().ConfigureAwait(false);
            throw;
        }

        _onvif = onvif;
        _rtsp = rtsp;
        IsRunning = true;
        _log($"{Config.Name} 启动完成");
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        if (_rtsp != null)
        {
            await _rtsp.StopAsync().ConfigureAwait(false);
            _rtsp = null;
        }

        if (_onvif != null)
        {
            await _onvif.StopAsync().ConfigureAwait(false);
            _onvif = null;
        }

        IsRunning = false;
        _log($"{Config.Name} 已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void Validate()
    {
        if (Config.HttpPort is <= 0 or > 65535)
        {
            throw new InvalidOperationException("HTTP 端口必须介于 1 和 65535 之间。");
        }

        if (Config.RtspPort is <= 0 or > 65535)
        {
            throw new InvalidOperationException("RTSP 端口必须介于 1 和 65535 之间。");
        }

        if (string.IsNullOrWhiteSpace(Config.Name))
        {
            throw new InvalidOperationException("请填写模拟器名称。");
        }

        if (Config.MediaFiles.Count == 0 || Config.MediaFiles.All(f => !File.Exists(f)))
        {
            throw new InvalidOperationException("请至少选择一个有效的 MP4/AVI 视频文件。");
        }
    }
}
