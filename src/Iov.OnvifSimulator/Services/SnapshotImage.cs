using System.Drawing;
using System.Drawing.Imaging;
using Iov.OnvifSimulator.Models;

namespace Iov.OnvifSimulator.Services;

public static class SnapshotImage
{
    public static byte[] Create(SimulatorConfig config)
    {
        using var bitmap = new Bitmap(1920, 1080);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(24, 32, 48));

        using var titleFont = new Font("Segoe UI", 36, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 18, FontStyle.Regular);
        using var brush = new SolidBrush(Color.WhiteSmoke);

        graphics.DrawString(config.Name, titleFont, brush, 48, 240);
        graphics.DrawString($"{config.Manufacturer} {config.Model}", bodyFont, brush, 48, 320);
        graphics.DrawString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), bodyFont, brush, 48, 370);
        graphics.DrawString($"HTTP {config.HttpPort} / RTSP {config.RtspPort}", bodyFont, brush, 48, 420);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }
}
