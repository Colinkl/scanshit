using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class ScannerTrayContext : ApplicationContext
{
    private static readonly Size MenuLogoSize = new(64, 64);
    private const int MenuHeaderHeight = 84;

    private readonly string[] _args;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon? _trayIcon;
    private readonly Bitmap? _menuLogo;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly CancellationTokenSource _shutdownSource = new();
    private Task? _scannerTask;
    private string _statusText = "Starting scanner listener";

    public ScannerTrayContext(string[] args)
    {
        _args = args;
        _menuLogo = LoadMenuLogo();
        _statusMenuItem = new ToolStripMenuItem(_statusText) { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ScancatMenuBanner(_menuLogo));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open config", null, (_, _) => OpenConfig()));
        menu.Items.Add(new ToolStripMenuItem("Restart listener", null, (_, _) => RestartScanner()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(out _trayIcon),
            Text = "Scanner listener",
            Visible = true,
        };

        _notifyIcon.DoubleClick += (_, _) => OpenConfig();
        UpdateStatus("Starting scanner listener");
        StartScanner();
    }

    private void StartScanner()
    {
        var app = new ScannerApplication(UpdateStatus, ShowError);
        _scannerTask = Task.Run(() => app.RunAsync(_args, _shutdownSource.Token));
        _ = ObserveScannerTaskAsync(_scannerTask);
    }

    private async Task ObserveScannerTaskAsync(Task scannerTask)
    {
        try
        {
            await scannerTask;
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ShowError($"Scanner stopped unexpectedly: {exception.Message}");
        }
    }

    private void RestartScanner()
    {
        if (_shutdownSource.IsCancellationRequested)
        {
            return;
        }

        ShowInfo("Restart requested. Restart the app from Startup or run the launcher again.");
    }

    private void OpenConfig()
    {
        var configPath = ScannerApplication.ResolveConfigPath(_args);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{configPath}\"",
                UseShellExecute = true,
            }
        );
    }

    private void UpdateStatus(string message)
    {
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.Invoke(() => UpdateStatus(message));
            return;
        }

        _statusText = message;
        _statusMenuItem.Text = message;
        _notifyIcon.Text = TrimTooltip($"Scanner listener: {message}");
    }

    private void ShowError(string message)
    {
        UpdateStatus(message);
        _notifyIcon.ShowBalloonTip(5000, "Scanner listener", message, ToolTipIcon.Error);
    }

    private void ShowInfo(string message)
    {
        UpdateStatus(message);
        _notifyIcon.ShowBalloonTip(3000, "Scanner listener", message, ToolTipIcon.Info);
    }

    private static string TrimTooltip(string text)
    {
        const int maxLength = 63;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static Icon LoadTrayIcon(out Icon? loadedIcon)
    {
        loadedIcon = null;

        try
        {
            using var resourceStream = GetLogoStream();
            if (resourceStream is null)
            {
                return SystemIcons.Application;
            }

            using var image = Image.FromStream(resourceStream);
            using var bitmap = new Bitmap(image, new Size(32, 32));
            var handle = bitmap.GetHicon();

            try
            {
                using var temporaryIcon = Icon.FromHandle(handle);
                loadedIcon = (Icon)temporaryIcon.Clone();
                return loadedIcon;
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static Bitmap? LoadMenuLogo()
    {
        try
        {
            using var resourceStream = GetLogoStream();
            if (resourceStream is null)
            {
                return null;
            }

            using var image = Image.FromStream(resourceStream);
            return new Bitmap(image, MenuLogoSize);
        }
        catch
        {
            return null;
        }
    }

    private static Stream? GetLogoStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));

        return resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    private void ExitApplication()
    {
        _shutdownSource.Cancel();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _shutdownSource.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menuLogo?.Dispose();
        _trayIcon?.Dispose();
        _shutdownSource.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class ScancatMenuBanner : ToolStripControlHost
    {
        public ScancatMenuBanner(Image? logo)
            : base(CreateContent(logo))
        {
            AutoSize = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Size = new Size(280, MenuHeaderHeight);
        }

        private static Control CreateContent(Image? logo)
        {
            var menuFont = SystemFonts.MenuFont ?? SystemFonts.DefaultFont;

            var panel = new Panel
            {
                BackColor = Color.White,
                Margin = Padding.Empty,
                Padding = new Padding(10),
                Size = new Size(280, MenuHeaderHeight),
            };

            var pictureBox = new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(10, 10),
                Size = MenuLogoSize,
            };

            var titleLabel = new Label
            {
                AutoSize = false,
                Text = "Scancat",
                Font = new Font(menuFont.FontFamily, 12f, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft,
                Location = new Point(86, 16),
                Size = new Size(170, 24),
            };

            var subtitleLabel = new Label
            {
                AutoSize = false,
                Text = "Barcode scanner listener",
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.TopLeft,
                Location = new Point(86, 42),
                Size = new Size(180, 20),
            };

            panel.Controls.Add(pictureBox);
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(subtitleLabel);
            return panel;
        }
    }
}
