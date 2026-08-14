using CoreWCF;
using CoreWCF.Configuration;
using CoreWCF.Description;
using Iov.OnvifSimulator.Models;
using Iov.OnvifSimulator.Onvif;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpOnvifServer;
using SharpOnvifServer.Discovery;
using SharpOnvifServer.Security;

namespace Iov.OnvifSimulator.Services;

public sealed class OnvifWebHost : IAsyncDisposable
{
    private readonly SimulatorConfig _config;
    private readonly Action<string> _log;
    private WebApplication? _app;

    public OnvifWebHost(SimulatorConfig config, Action<string> log)
    {
        _config = config;
        _log = log;
    }

    public string DeviceServiceUri
    {
        get
        {
            var ip = NetworkDefaults.GetPrimaryIPv4();
            return $"http://{ip}:{_config.HttpPort}/onvif/device_service";
        }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(OnvifWebHost).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Production"
        });

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(_config.HttpPort));

        builder.Services.AddSingleton(_config);
        builder.Services.AddSingleton<IUserRepository, SimulatorUserRepository>();
        builder.Services.AddSingleton<SimulatorDeviceService>();
        builder.Services.AddSingleton<SimulatorMediaService>();

        builder.Services.AddServiceModelServices();
        builder.Services.AddServiceModelMetadata();
        builder.Services.AddSingleton<IServiceBehavior, UseRequestHeadersForMetadataAddressBehavior>();

        if (_config.EnableAuthentication)
        {
            builder.Services.AddOnvifDigestAuthentication(CreateDigestOptions());
        }

        builder.Services.AddOnvifDiscovery(CreateDiscoveryOptions());

        _app = builder.Build();

        if (_config.EnableAuthentication)
        {
            _app.UseAuthentication();
            _app.UseAuthorization();
        }

        _app.UseOnvif();

        _app.MapGet("/", () => Results.Text(
            $"IOV ONVIF Simulator - {_config.Name}{Environment.NewLine}" +
            $"Device: {DeviceServiceUri}{Environment.NewLine}" +
            $"Media:  http://{NetworkDefaults.GetPrimaryIPv4()}:{_config.HttpPort}/onvif/media_service{Environment.NewLine}" +
            $"RTSP:   rtsp://{NetworkDefaults.GetPrimaryIPv4()}:{_config.RtspPort}/stream{Environment.NewLine}",
            "text/plain"));

        _app.MapGet("/snapshot", () => Results.File(SnapshotImage.Create(_config), "image/jpeg"));

        ((IApplicationBuilder)_app).UseServiceModel(serviceBuilder =>
        {
            var metadata = _app.Services.GetRequiredService<ServiceMetadataBehavior>();
            metadata.HttpGetEnabled = true;

            var binding = OnvifBindingFactory.CreateBinding();
            serviceBuilder.AddService<SimulatorDeviceService>();
            serviceBuilder.AddServiceEndpoint<SimulatorDeviceService, SharpOnvifServer.DeviceMgmt.Device>(
                binding, "/onvif/device_service");

            serviceBuilder.AddService<SimulatorMediaService>();
            serviceBuilder.AddServiceEndpoint<SimulatorMediaService, SharpOnvifServer.Media.Media>(
                binding, "/onvif/media_service");
        });

        await _app.StartAsync().ConfigureAwait(false);
        _log($"ONVIF 已启动 {DeviceServiceUri}");
    }

    public async Task StopAsync()
    {
        if (_app == null)
        {
            return;
        }

        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
        _log("ONVIF 已停止");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private OnvifDiscoveryOptions CreateDiscoveryOptions()
    {
        var ip = NetworkDefaults.GetPrimaryIPv4();
        return new OnvifDiscoveryOptions
        {
            Name = _config.Name,
            Manufacturer = _config.Manufacturer,
            Hardware = _config.Model,
            MAC = _config.MacAddress,
            ServiceAddresses =
            [
                $"http://{ip}:{_config.HttpPort}/onvif/device_service"
            ],
            Scopes =
            [
                "onvif://www.onvif.org/type/video_encoder",
                "onvif://www.onvif.org/Profile/Streaming",
                $"onvif://www.onvif.org/name/{Uri.EscapeDataString(_config.Name)}",
                $"onvif://www.onvif.org/hardware/{Uri.EscapeDataString(_config.Model)}"
            ],
            Types =
            [
                new OnvifType("http://www.onvif.org/ver10/network/wsdl", "NetworkVideoTransmitter"),
                new OnvifType("http://www.onvif.org/ver10/device/wsdl", "Device")
            ]
        };
    }

    private DigestAuthenticationSchemeOptions CreateDigestOptions()
    {
        return new DigestAuthenticationSchemeOptions
        {
            Authentication = DigestAuthentication.WsUsernameToken | DigestAuthentication.HttpDigest,
            HttpDigestRealm = _config.Name,
            HttpDigestUserHash = false,
            HttpDigestAlgorithms = ["MD5", "SHA-256"],
            HttpDigestQop = ["auth"],
            PreAuthActions =
            [
                "http://www.onvif.org/ver10/device/wsdl/GetWsdlUrl",
                "http://www.onvif.org/ver10/device/wsdl/GetServices",
                "http://www.onvif.org/ver10/device/wsdl/GetServiceCapabilities",
                "http://www.onvif.org/ver10/device/wsdl/GetCapabilities",
                "http://www.onvif.org/ver10/device/wsdl/GetHostname",
                "http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime",
                "http://www.onvif.org/ver10/device/wsdl/GetEndpointReference"
            ]
        };
    }
}
