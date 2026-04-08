using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
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
    private CancellationTokenSource? _listenerSource;
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
        menu.Items.Add(new ToolStripMenuItem("Select COM port", null, (_, _) => SelectComPort()));
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
        var previousSource = _listenerSource;
        var previousTask = _scannerTask;

        _listenerSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdownSource.Token);

        var app = new ScannerApplication(UpdateStatus, ShowError);
        _scannerTask = Task.Run(() => app.RunAsync(_args, _listenerSource.Token));
        _ = ObserveScannerTaskAsync(_scannerTask);

        if (previousSource is null)
        {
            return;
        }

        previousSource.Cancel();

        if (previousTask is null)
        {
            previousSource.Dispose();
            return;
        }

        _ = previousTask.ContinueWith(
            _ => previousSource.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
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

        RestartScannerInternal(showNotification: true);
    }

    private void SelectComPort()
    {
        try
        {
            var configPath = ScannerApplication.ResolveConfigPath(_args);
            var config = ScannerApplication.LoadConfig(configPath);

            using var form = new ComPortSelectorForm(config.PortName);
            if (
                form.ShowDialog() != DialogResult.OK
                || string.IsNullOrWhiteSpace(form.SelectedPortName)
            )
            {
                return;
            }

            if (
                string.Equals(
                    config.PortName,
                    form.SelectedPortName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            config.PortName = form.SelectedPortName;
            ScannerApplication.SaveConfig(configPath, config);
            RestartScannerInternal(showNotification: false);
            ShowInfo($"COM port changed to {config.PortName}");
        }
        catch (Exception exception)
        {
            ShowError($"Failed to update COM port: {exception.Message}");
        }
    }

    private void RestartScannerInternal(bool showNotification)
    {
        if (_shutdownSource.IsCancellationRequested)
        {
            return;
        }

        _listenerSource?.Cancel();
        StartScanner();

        if (showNotification)
        {
            ShowInfo("Listener restarted.");
        }
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
        _listenerSource?.Cancel();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menuLogo?.Dispose();
        _trayIcon?.Dispose();
        _listenerSource?.Dispose();
        _shutdownSource.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class ComPortSelectorForm : Form
    {
        private readonly ComboBox _portsComboBox;
        private readonly Button _okButton;

        public ComPortSelectorForm(string currentPortName)
        {
            Text = "Select COM port";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 150);

            var label = new Label
            {
                Text = "Scanner COM port",
                AutoSize = true,
                Location = new Point(16, 18),
            };

            _portsComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(16, 44),
                Size = new Size(240, 28),
            };

            var refreshButton = new Button
            {
                Text = "Refresh",
                Location = new Point(268, 43),
                Size = new Size(76, 30),
            };
            refreshButton.Click += (_, _) => PopulatePorts(currentPortName);

            _okButton = new Button
            {
                Text = "Save",
                DialogResult = DialogResult.OK,
                Location = new Point(188, 100),
                Size = new Size(75, 30),
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(269, 100),
                Size = new Size(75, 30),
            };

            AcceptButton = _okButton;
            CancelButton = cancelButton;

            Controls.Add(label);
            Controls.Add(_portsComboBox);
            Controls.Add(refreshButton);
            Controls.Add(_okButton);
            Controls.Add(cancelButton);

            PopulatePorts(currentPortName);
        }

        public string? SelectedPortName => _portsComboBox.SelectedItem as string;

        private void PopulatePorts(string preferredPortName)
        {
            preferredPortName = SelectedPortName ?? preferredPortName;

            var portNames = SerialPort.GetPortNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _portsComboBox.BeginUpdate();
            _portsComboBox.Items.Clear();
            _portsComboBox.Items.AddRange(portNames);
            _portsComboBox.EndUpdate();

            var selectedPort = portNames.FirstOrDefault(name =>
                string.Equals(name, preferredPortName, StringComparison.OrdinalIgnoreCase)
            );

            if (selectedPort is not null)
            {
                _portsComboBox.SelectedItem = selectedPort;
            }
            else if (_portsComboBox.Items.Count > 0)
            {
                _portsComboBox.SelectedIndex = 0;
            }

            _okButton.Enabled = _portsComboBox.Items.Count > 0;
        }
    }

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
