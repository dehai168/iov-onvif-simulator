namespace Iov.OnvifSimulator.Models;

public sealed class SimulatorConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Camera-1";

    public string Manufacturer { get; set; } = "IOV";

    public string Model { get; set; } = "ONVIF-SIM";

    public string SerialNumber { get; set; } = "SN0001";

    public string FirmwareVersion { get; set; } = "1.0.0";

    public string HardwareId { get; set; } = "HW-1";

    public int HttpPort { get; set; } = 8080;

    public int RtspPort { get; set; } = 8554;

    public string UserName { get; set; } = "admin";

    public string Password { get; set; } = "admin";

    public bool EnableAuthentication { get; set; } = true;

    public bool LoopPlayback { get; set; } = true;

    public PlayOrder PlayOrder { get; set; } = PlayOrder.Sequential;

    public List<string> MediaFiles { get; set; } = [];

    public string MacAddress { get; set; } = NetworkDefaults.CreateLocalMac();

    public SimulatorConfig Clone()
    {
        return new SimulatorConfig
        {
            Id = Id,
            Name = Name,
            Manufacturer = Manufacturer,
            Model = Model,
            SerialNumber = SerialNumber,
            FirmwareVersion = FirmwareVersion,
            HardwareId = HardwareId,
            HttpPort = HttpPort,
            RtspPort = RtspPort,
            UserName = UserName,
            Password = Password,
            EnableAuthentication = EnableAuthentication,
            LoopPlayback = LoopPlayback,
            PlayOrder = PlayOrder,
            MediaFiles = [.. MediaFiles],
            MacAddress = MacAddress
        };
    }
}
