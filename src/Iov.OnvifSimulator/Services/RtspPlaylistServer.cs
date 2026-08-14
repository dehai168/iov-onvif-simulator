using Iov.OnvifSimulator.Models;
using Iov.OnvifSimulator.Onvif;
using SharpRTSPServer;

namespace Iov.OnvifSimulator.Services;

public sealed class RtspPlaylistServer : IAsyncDisposable
{
    public const string StreamId = SimulatorMediaService.StreamPath;

    private readonly SimulatorConfig _config;
    private readonly Action<string> _log;
    private RTSPServer? _server;
    private ITrack? _videoTrack;
    private CancellationTokenSource? _cts;
    private Task? _playTask;
    private uint _rtpBaseTime;

    public RtspPlaylistServer(SimulatorConfig config, Action<string> log)
    {
        _config = config;
        _log = log;
    }

    public string StreamUri
    {
        get
        {
            var ip = NetworkDefaults.GetPrimaryIPv4();
            return $"rtsp://{ip}:{_config.RtspPort}/{StreamId}";
        }
    }

    public void Start()
    {
        var files = GetExistingFiles();
        if (files.Count == 0)
        {
            throw new InvalidOperationException("请至少添加一个存在的 MP4/AVI 视频文件。");
        }

        _rtpBaseTime = (uint)Random.Shared.Next();
        _videoTrack = CreateTrack(files[0]);

        var user = _config.EnableAuthentication ? _config.UserName : string.Empty;
        var password = _config.EnableAuthentication ? _config.Password : string.Empty;
        _server = new RTSPServer(_config.RtspPort, user, password);
        _server.AddStreamSource(new RTSPStreamSource(StreamId, _videoTrack, null));
        _server.StartListen();

        _cts = new CancellationTokenSource();
        _playTask = Task.Run(() => PlayLoopAsync(_cts.Token));
        _log($"RTSP 已监听 {StreamUri}");
    }

    public async Task StopAsync()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();
        if (_playTask != null)
        {
            try
            {
                await _playTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log($"停止推流时出错: {ex.Message}");
            }
        }

        _server?.StopListen();
        _server?.Dispose();
        _server = null;
        _cts.Dispose();
        _cts = null;
        _playTask = null;
        _log("RTSP 已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task PlayLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            do
            {
                var files = GetOrderedFiles();
                if (files.Count == 0)
                {
                    _log("播放列表为空，推流结束。");
                    return;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _log($"开始推流: {Path.GetFileName(file)}");
                    await PlayFileAsync(file, cancellationToken).ConfigureAwait(false);
                }
            }
            while (_config.LoopPlayback && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log($"推流失败: {ex.Message}");
        }
    }

    private async Task PlayFileAsync(string file, CancellationToken cancellationToken)
    {
        if (_videoTrack is H264Track h264 && ShouldUseFfmpeg(file))
        {
            var ffmpeg = FfmpegLocator.Find()
                ?? throw new InvalidOperationException("播放 AVI 或转码需要 FFmpeg，请安装并加入 PATH。");
            using var process = AnnexBNalReader.StartFfmpeg(ffmpeg, file, realtime: true);
            try
            {
                await AnnexBNalReader.PumpToTrackAsync(process.StandardOutput.BaseStream, h264, _rtpBaseTime, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TryKill(process);
            }

            return;
        }

        if (Mp4MediaReader.IsMp4Family(file) && _videoTrack != null)
        {
            await Mp4MediaReader.PlayAsync(file, _videoTrack, _rtpBaseTime, cancellationToken).ConfigureAwait(false);
            return;
        }

        _log($"跳过不支持的文件: {file}");
    }

    private ITrack CreateTrack(string firstFile)
    {
        string? error = null;
        if (Mp4MediaReader.IsMp4Family(firstFile) &&
            Mp4MediaReader.TryCreateVideoTrack(firstFile, out var track, out error) &&
            track != null)
        {
            return track;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            _log($"直接解析 MP4 失败，改用 FFmpeg: {error}");
        }

        var ffmpeg = FfmpegLocator.Find();
        if (ffmpeg == null)
        {
            throw new InvalidOperationException(
                "无法解析该视频文件。请使用 H264/H265 的 MP4，或安装 FFmpeg 以支持 AVI 及其他编码。");
        }

        using var process = AnnexBNalReader.StartFfmpeg(ffmpeg, firstFile, realtime: false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var (sps, pps) = AnnexBNalReader.WaitForParameterSetsAsync(process.StandardOutput.BaseStream, cts.Token)
                .GetAwaiter().GetResult();
            var h264 = new H264Track();
            h264.SetParameterSets(sps, pps);
            return h264;
        }
        finally
        {
            TryKill(process);
        }
    }

    private bool ShouldUseFfmpeg(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is ".avi")
        {
            return true;
        }

        if (_videoTrack is H264Track && Mp4MediaReader.IsMp4Family(file))
        {
            if (Mp4MediaReader.TryCreateVideoTrack(file, out var track, out _) && track is H264Track)
            {
                return false;
            }

            return FfmpegLocator.IsAvailable();
        }

        return ext is not ".mp4" and not ".m4v" and not ".mov";
    }

    private List<string> GetExistingFiles()
    {
        return _config.MediaFiles
            .Where(File.Exists)
            .ToList();
    }

    private List<string> GetOrderedFiles()
    {
        var files = GetExistingFiles();
        if (_config.PlayOrder == PlayOrder.Shuffle)
        {
            return files.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        return files;
    }

    private static void TryKill(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}
