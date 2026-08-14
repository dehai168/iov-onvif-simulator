using CoreWCF;
using Iov.OnvifSimulator.Models;
using SharpOnvifCommon;
using SharpOnvifServer.DeviceMgmt;

namespace Iov.OnvifSimulator.Onvif;

public sealed class SimulatorDeviceService : DeviceBase
{
    private readonly SimulatorConfig _config;

    public SimulatorDeviceService(SimulatorConfig config)
    {
        _config = config;
    }

    public override GetCapabilitiesResponse GetCapabilities(GetCapabilitiesRequest request)
    {
        var endpointUri = OperationContext.Current.IncomingMessageProperties.Via;
        return new GetCapabilitiesResponse
        {
            Capabilities = new Capabilities
            {
                Device = new DeviceCapabilities
                {
                    XAddr = OnvifHelpers.ChangeUriPath(endpointUri, "/onvif/device_service").ToString(),
                    Network = new NetworkCapabilities1
                    {
                        IPFilter = false,
                        ZeroConfiguration = false,
                        IPVersion6 = false,
                        DynDNS = false
                    },
                    System = new SystemCapabilities1
                    {
                        SystemLogging = false,
                        SupportedVersions =
                        [
                            new OnvifVersion { Major = 2, Minor = 40 }
                        ]
                    },
                    Security = new SecurityCapabilities1()
                },
                Media = new MediaCapabilities
                {
                    XAddr = OnvifHelpers.ChangeUriPath(endpointUri, "/onvif/media_service").ToString(),
                    StreamingCapabilities = new RealTimeStreamingCapabilities
                    {
                        RTP_RTSP_TCP = true,
                        RTP_RTSP_TCPSpecified = true,
                        RTPMulticast = false,
                        RTPMulticastSpecified = true
                    }
                }
            }
        };
    }

    public override GetDeviceInformationResponse GetDeviceInformation(GetDeviceInformationRequest request)
    {
        return new GetDeviceInformationResponse
        {
            Manufacturer = _config.Manufacturer,
            Model = _config.Model,
            FirmwareVersion = _config.FirmwareVersion,
            SerialNumber = _config.SerialNumber,
            HardwareId = _config.HardwareId
        };
    }

    public override GetServicesResponse GetServices(GetServicesRequest request)
    {
        var endpointUri = OperationContext.Current.IncomingMessageProperties.Via;
        return new GetServicesResponse
        {
            Service =
            [
                new Service
                {
                    Namespace = OnvifServices.DEVICE_MGMT,
                    XAddr = OnvifHelpers.ChangeUriPath(endpointUri, "/onvif/device_service").ToString(),
                    Version = new OnvifVersion { Major = 2, Minor = 40 }
                },
                new Service
                {
                    Namespace = OnvifServices.MEDIA,
                    XAddr = OnvifHelpers.ChangeUriPath(endpointUri, "/onvif/media_service").ToString(),
                    Version = new OnvifVersion { Major = 2, Minor = 40 }
                }
            ]
        };
    }

    public override GetScopesResponse GetScopes(GetScopesRequest request)
    {
        return new GetScopesResponse
        {
            Scopes =
            [
                new Scope { ScopeDef = ScopeDefinition.Fixed, ScopeItem = "onvif://www.onvif.org/type/video_encoder" },
                new Scope { ScopeDef = ScopeDefinition.Fixed, ScopeItem = "onvif://www.onvif.org/Profile/Streaming" },
                new Scope { ScopeDef = ScopeDefinition.Fixed, ScopeItem = $"onvif://www.onvif.org/name/{Uri.EscapeDataString(_config.Name)}" },
                new Scope { ScopeDef = ScopeDefinition.Fixed, ScopeItem = $"onvif://www.onvif.org/hardware/{Uri.EscapeDataString(_config.Model)}" },
                new Scope { ScopeDef = ScopeDefinition.Fixed, ScopeItem = $"onvif://www.onvif.org/location/city/lab" }
            ]
        };
    }

    public override SystemDateTime GetSystemDateAndTime()
    {
        var utc = System.DateTime.UtcNow;
        var local = System.DateTime.Now;
        return new SystemDateTime
        {
            DateTimeType = SetDateTimeType.NTP,
            DaylightSavings = TimeZoneInfo.Local.IsDaylightSavingTime(local),
            TimeZone = new SharpOnvifServer.DeviceMgmt.TimeZone { TZ = TimeZoneInfo.Local.Id },
            UTCDateTime = ToOnvifDateTime(utc),
            LocalDateTime = ToOnvifDateTime(local)
        };
    }

    public override GetNetworkInterfacesResponse GetNetworkInterfaces(GetNetworkInterfacesRequest request)
    {
        return new GetNetworkInterfacesResponse
        {
            NetworkInterfaces =
            [
                new NetworkInterface
                {
                    Enabled = true,
                    Info = new NetworkInterfaceInfo
                    {
                        Name = "eth0",
                        HwAddress = _config.MacAddress
                    },
                    IPv4 = new IPv4NetworkInterface
                    {
                        Enabled = true,
                        Config = new IPv4Configuration
                        {
                            DHCP = false,
                            Manual =
                            [
                                new PrefixedIPv4Address
                                {
                                    Address = NetworkDefaults.GetPrimaryIPv4(),
                                    PrefixLength = 24
                                }
                            ]
                        }
                    }
                }
            ]
        };
    }

    public override GetNetworkProtocolsResponse GetNetworkProtocols(GetNetworkProtocolsRequest request)
    {
        return new GetNetworkProtocolsResponse
        {
            NetworkProtocols =
            [
                new NetworkProtocol
                {
                    Enabled = true,
                    Name = NetworkProtocolType.HTTP,
                    Port = [_config.HttpPort]
                },
                new NetworkProtocol
                {
                    Enabled = true,
                    Name = NetworkProtocolType.RTSP,
                    Port = [_config.RtspPort]
                }
            ]
        };
    }

    public override HostnameInformation GetHostname()
    {
        return new HostnameInformation { Name = _config.Name };
    }

    public override DiscoveryMode GetDiscoveryMode()
    {
        return DiscoveryMode.Discoverable;
    }

    public override GetUsersResponse GetUsers(GetUsersRequest request)
    {
        return new GetUsersResponse
        {
            User =
            [
                new User
                {
                    Username = _config.UserName,
                    UserLevel = UserLevel.Administrator
                }
            ]
        };
    }

    public override DeviceServiceCapabilities GetServiceCapabilities()
    {
        return new DeviceServiceCapabilities();
    }

    private static SharpOnvifServer.DeviceMgmt.DateTime ToOnvifDateTime(System.DateTime value)
    {
        return new SharpOnvifServer.DeviceMgmt.DateTime
        {
            Date = new Date
            {
                Year = value.Year,
                Month = value.Month,
                Day = value.Day
            },
            Time = new Time
            {
                Hour = value.Hour,
                Minute = value.Minute,
                Second = value.Second
            }
        };
    }
}
