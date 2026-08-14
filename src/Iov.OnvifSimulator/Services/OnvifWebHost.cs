using CoreWCF;
using CoreWCF.Channels;
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
using System.Net;

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

    public IReadOnlyList<string> DeviceServiceUris =>
        NetworkDefaults.GetAllIPv4()
            .Select(ip => $"http://{ip}:{_config.HttpPort}/onvif/device_service")
            .ToList();

    public string DeviceServiceUri => DeviceServiceUris[0];

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(OnvifWebHost).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Production"
        });

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(_config.HttpPort);
        });

        builder.Services.AddSingleton(_config);
        builder.Services.AddSingleton<IUserRepository, SimulatorUserRepository>();
        builder.Services.AddSingleton<SimulatorDeviceService>();
        builder.Services.AddSingleton<SimulatorMediaService>();

        builder.Services.AddServiceModelServices();
        builder.Services.AddServiceModelMetadata();
        builder.Services.AddSingleton<IServiceBehavior, UseRequestHeadersForMetadataAddressBehavior>();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        if (_config.EnableAuthentication)
        {
            builder.Services.AddOnvifDigestAuthentication(CreateDigestOptions());
        }

        builder.Services.AddOnvifDiscovery(CreateDiscoveryOptions());

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseOnvif();

        _app.MapGet("/", () =>
        {
            var lines = new List<string>
            {
                $"IOV ONVIF Simulator - {_config.Name}",
                "Listening on all network interfaces:"
            };
            foreach (var nic in NetworkDefaults.GetListenInterfaces())
            {
                lines.Add($"  [{nic.Name}] http://{nic.IPv4}:{_config.HttpPort}/onvif/device_service");
                lines.Add($"  [{nic.Name}] rtsp://{nic.IPv4}:{_config.RtspPort}/stream");
            }

            if (lines.Count == 2)
            {
                lines.Add($"  Device: {DeviceServiceUri}");
            }

            return Results.Text(string.Join(Environment.NewLine, lines) + Environment.NewLine, "text/plain");
        });

        _app.MapGet("/snapshot", () => Results.File(SnapshotImage.Create(_config), "image/jpeg"));

        ((IApplicationBuilder)_app).UseServiceModel(serviceBuilder =>
        {
            var metadata = _app.Services.GetRequiredService<ServiceMetadataBehavior>();
            metadata.HttpGetEnabled = true;

            var binding = CreateBinding();
            serviceBuilder.AddService<SimulatorDeviceService>();
            serviceBuilder.AddServiceEndpoint<SimulatorDeviceService, SharpOnvifServer.DeviceMgmt.Device>(
                binding, "/onvif/device_service");

            serviceBuilder.AddService<SimulatorMediaService>();
            serviceBuilder.AddServiceEndpoint<SimulatorMediaService, SharpOnvifServer.Media.Media>(
                binding, "/onvif/media_service");
        });

        await _app.StartAsync().ConfigureAwait(false);
        var nics = NetworkDefaults.GetListenInterfaces();
        if (nics.Count == 0)
        {
            _log($"ONVIF 已启动 {DeviceServiceUri}");
        }
        else
        {
            _log($"ONVIF HTTP {_config.HttpPort} 已在 {nics.Count} 个网卡地址上监听:");
            foreach (var nic in nics)
            {
                _log($"  [{nic.Name}] http://{nic.IPv4}:{_config.HttpPort}/onvif/device_service");
            }
        }
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
        var ipv4s = NetworkDefaults.GetAllIPv4();
        return new OnvifDiscoveryOptions
        {
            Name = _config.Name,
            Manufacturer = _config.Manufacturer,
            Hardware = _config.Model,
            MAC = _config.MacAddress,
            NetworkInterfaces = [.. ipv4s, "0.0.0.0"],
            ServiceAddresses = ipv4s
                .Select(ip => $"http://{ip}:{_config.HttpPort}/onvif/device_service")
                .ToList(),
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

    private CustomBinding CreateBinding()
    {
        if (_config.EnableAuthentication)
        {
            return OnvifBindingFactory.CreateBinding();
        }

        const int maxMessageSize = 1048576;
        return new CustomBinding(
            new TextMessageEncodingBindingElement
            {
                MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None)
            },
            new HttpTransportBindingElement
            {
                AuthenticationScheme = AuthenticationSchemes.Anonymous,
                MaxReceivedMessageSize = maxMessageSize,
                MaxBufferSize = maxMessageSize
            });
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
