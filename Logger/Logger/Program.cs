using Logger.Entities;
using System.Windows.Forms;

static class Program
{
    private static Esp32DataLogger? _logger;
    private static NotifyIcon? _notifyIcon;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SetupTrayIcon();
        InitializeLogger();

        Application.ApplicationExit += (s, e) => Cleanup();
        Application.Run();
    }

    private static void SetupTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)!,
            Text = "ESP32 Data Logger 2.0",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Открыть лог", null, (s, e) => OpenLog());
        contextMenu.Items.Add("Переподключиться", null, (s, e) => Reconnect());
        contextMenu.Items.Add("Выход", null, (s, e) => Application.Exit());
        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.DoubleClick += (s, e) => OpenLog();
    }

    private static void InitializeLogger()
    {
        _logger?.Dispose();
        _logger = new Esp32DataLogger();
        _logger.FindAndConnect();
    }

    private static void Reconnect()
    {
        InitializeLogger();
    }

    private static void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", "sensor_data.db");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка открытия лога: {ex.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Cleanup()
    {
        _logger?.Dispose();
        _notifyIcon?.Dispose();
    }
}