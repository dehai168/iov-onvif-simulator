# IOV ONVIF Simulator

基于 C# WinForms 的 ONVIF 协议模拟器。每个模拟器占用独立的 HTTP / RTSP 端口，可被 ONVIF Device Manager 等客户端发现，并把本地 MP4 / AVI 文件按播放列表推流出去。

## 功能

- 同时配置并运行多个模拟器，通过端口区分
- 独立的设备属性：名称、厂商、型号、序列号、固件、账号
- WS-Discovery 发现 + Device / Media 核心服务
- 视频源支持多个文件组成播放列表，可循环、顺序或随机播放
- 快照接口返回带设备名称的 JPEG 占位图

## 运行环境

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（开发）或 .NET 8 Desktop Runtime（运行）
- 推流 H264/H265 的 MP4 可直接工作
- 播放 AVI 或其他编码时需要本机安装 [FFmpeg](https://ffmpeg.org/) 并加入 `PATH`

## 编译

```bash
dotnet build Iov.OnvifSimulator.slnx -c Release
```

运行：

```bash
dotnet run --project src/Iov.OnvifSimulator -c Release
```

生成结果位于 `src/Iov.OnvifSimulator/bin/Release/net8.0-windows/Iov.OnvifSimulator.exe`。

## 使用说明

1. 左侧添加模拟器，为每个实例分配不冲突的 HTTP 端口和 RTSP 端口
2. 在右侧填写设备信息，并添加一个或多个 `mp4` / `avi` 文件
3. 按需勾选循环推流，选择顺序或随机播放
4. 点击「启动」。局域网内可用 ONVIF Device Manager 搜索设备
5. 取流地址形如 `rtsp://<本机IP>:<RTSP端口>/stream`

配置保存在 `%AppData%\Iov.OnvifSimulator\config.json`。

## 协议实现

- ONVIF： [SharpOnvifServer](https://github.com/jimm98y/SharpOnvif)（CoreWCF Device / Media + WS-Discovery）
- RTSP： [SharpRTSPServer](https://github.com/jimm98y/SharpRealTimeStreaming)
- MP4 解析： [SharpMP4](https://github.com/jimm98y/SharpMP4)

当前覆盖发现和预览推流所需的核心接口：`GetSystemDateAndTime`、`GetCapabilities`、`GetServices`、`GetDeviceInformation`、`GetProfiles`、`GetStreamUri`。

## License

MIT
