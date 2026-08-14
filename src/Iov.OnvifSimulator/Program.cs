namespace Iov.OnvifSimulator;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UI.MainForm());
    }
}
