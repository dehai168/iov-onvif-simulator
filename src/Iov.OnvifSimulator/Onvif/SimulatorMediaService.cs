using CoreWCF;
using Iov.OnvifSimulator.Models;
using SharpOnvifServer;
using SharpOnvifServer.Media;

namespace Iov.OnvifSimulator.Onvif;

public sealed class SimulatorMediaService : MediaBase
{
    public const string ProfileToken = "Profile_1";
    public const string VideoSourceToken = "VideoSource_1";
    public const string VideoEncoderToken = "VideoEncoder_1";
    public const string StreamPath = "stream";

    private readonly SimulatorConfig _config;

    public SimulatorMediaService(SimulatorConfig config)
    {
        _config = config;
    }

    public override GetProfilesResponse GetProfiles(GetProfilesRequest request)
    {
        return new GetProfilesResponse
        {
            Profiles = [CreateProfile()]
        };
    }

    public override Profile GetProfile(string ProfileToken)
    {
        EnsureProfile(ProfileToken);
        return CreateProfile();
    }

    public override MediaUri GetStreamUri(StreamSetup StreamSetup, string ProfileToken)
    {
        EnsureProfile(ProfileToken);
        var host = OperationContext.Current.IncomingMessageProperties.Via.Host;
        return new MediaUri
        {
            Uri = $"rtsp://{host}:{_config.RtspPort}/{StreamPath}",
            InvalidAfterConnect = false,
            InvalidAfterReboot = false,
            Timeout = "PT60S"
        };
    }

    public override MediaUri GetSnapshotUri(string ProfileToken)
    {
        EnsureProfile(ProfileToken);
        var via = OperationContext.Current.IncomingMessageProperties.Via;
        var builder = new UriBuilder(via)
        {
            Path = "/snapshot",
            Query = string.Empty
        };
        return new MediaUri
        {
            Uri = builder.Uri.ToString(),
            InvalidAfterConnect = false,
            InvalidAfterReboot = false,
            Timeout = "PT60S"
        };
    }

    public override GetVideoSourcesResponse GetVideoSources(GetVideoSourcesRequest request)
    {
        return new GetVideoSourcesResponse
        {
            VideoSources = [CreateVideoSource()]
        };
    }

    public override VideoSourceConfiguration GetVideoSourceConfiguration(string ConfigurationToken)
    {
        return CreateVideoSourceConfiguration();
    }

    public override VideoEncoderConfiguration GetVideoEncoderConfiguration(string ConfigurationToken)
    {
        return CreateVideoEncoderConfiguration();
    }

    public override GetVideoSourceConfigurationsResponse GetVideoSourceConfigurations(GetVideoSourceConfigurationsRequest request)
    {
        return new GetVideoSourceConfigurationsResponse
        {
            Configurations = [CreateVideoSourceConfiguration()]
        };
    }

    public override GetVideoEncoderConfigurationsResponse GetVideoEncoderConfigurations(GetVideoEncoderConfigurationsRequest request)
    {
        return new GetVideoEncoderConfigurationsResponse
        {
            Configurations = [CreateVideoEncoderConfiguration()]
        };
    }

    public override GetCompatibleVideoEncoderConfigurationsResponse GetCompatibleVideoEncoderConfigurations(GetCompatibleVideoEncoderConfigurationsRequest request)
    {
        return new GetCompatibleVideoEncoderConfigurationsResponse
        {
            Configurations = [CreateVideoEncoderConfiguration()]
        };
    }

    public override VideoEncoderConfigurationOptions GetVideoEncoderConfigurationOptions(string ConfigurationToken, string ProfileToken)
    {
        return new VideoEncoderConfigurationOptions
        {
            QualityRange = new IntRange { Min = 1, Max = 10 },
            H264 = new H264Options
            {
                GovLengthRange = new IntRange { Min = 1, Max = 60 },
                EncodingIntervalRange = new IntRange { Min = 1, Max = 1 },
                FrameRateRange = new IntRange { Min = 1, Max = 30 },
                H264ProfilesSupported = [H264Profile.Main, H264Profile.Baseline],
                ResolutionsAvailable =
                [
                    new VideoResolution { Width = 1920, Height = 1080 },
                    new VideoResolution { Width = 1280, Height = 720 },
                    new VideoResolution { Width = 640, Height = 360 }
                ]
            }
        };
    }

    private static void EnsureProfile(string token)
    {
        if (!string.Equals(token, ProfileToken, StringComparison.Ordinal))
        {
            OnvifErrors.ReturnSenderInvalidArg();
        }
    }

    private static Profile CreateProfile()
    {
        return new Profile
        {
            token = ProfileToken,
            Name = ProfileToken,
            VideoSourceConfiguration = CreateVideoSourceConfiguration(),
            VideoEncoderConfiguration = CreateVideoEncoderConfiguration()
        };
    }

    private static VideoSource CreateVideoSource()
    {
        return new VideoSource
        {
            token = VideoSourceToken,
            Framerate = 25,
            Resolution = new VideoResolution { Width = 1280, Height = 720 }
        };
    }

    private static VideoSourceConfiguration CreateVideoSourceConfiguration()
    {
        return new VideoSourceConfiguration
        {
            token = VideoSourceToken,
            Name = VideoSourceToken,
            SourceToken = VideoSourceToken,
            UseCount = 1,
            Bounds = new IntRectangle { x = 0, y = 0, width = 1280, height = 720 }
        };
    }

    private static VideoEncoderConfiguration CreateVideoEncoderConfiguration()
    {
        return new VideoEncoderConfiguration
        {
            token = VideoEncoderToken,
            Name = VideoEncoderToken,
            UseCount = 1,
            Encoding = VideoEncoding.H264,
            Quality = 5,
            Resolution = new VideoResolution { Width = 1280, Height = 720 },
            RateControl = new VideoRateControl
            {
                FrameRateLimit = 25,
                EncodingInterval = 1,
                BitrateLimit = 2048
            },
            H264 = new H264Configuration
            {
                GovLength = 25,
                H264Profile = H264Profile.Main
            }
        };
    }
}
