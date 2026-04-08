using System.Diagnostics;
using System.IO.Ports;
using System.Text;

internal sealed class SerialScannerListener : IDisposable
{
    private readonly ScannerConfig _config;
    private readonly object _bufferLock = new();
    private readonly StringBuilder _buffer = new();
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly System.Threading.Timer _idleTimer;
    private SerialPort? _port;
    private bool _disposed;

    public SerialScannerListener(ScannerConfig config)
    {
        _config = config;
        _idleTimer = new System.Threading.Timer(FlushOnIdle, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        var parity = Enum.Parse<Parity>(_config.Parity, ignoreCase: true);
        var stopBits = Enum.Parse<StopBits>(_config.StopBits, ignoreCase: true);
        var handshake = Enum.Parse<Handshake>(_config.Handshake, ignoreCase: true);

        _port = new SerialPort(
            _config.PortName,
            _config.BaudRate,
            parity,
            _config.DataBits,
            stopBits
        )
        {
            Handshake = handshake,
            Encoding = Encoding.UTF8,
            NewLine = _config.NewLine,
            DtrEnable = _config.DtrEnable,
            RtsEnable = _config.RtsEnable,
            ReadTimeout = _config.ReadTimeoutMs,
        };

        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
        _port.Open();
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            _shutdownSource
        );
        return Task.Delay(Timeout.Infinite, _shutdownSource.Token);
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs eventArgs)
    {
        if (_port is null)
        {
            return;
        }

        try
        {
            var chunk = _port.ReadExisting();

            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            List<string> scans = [];

            lock (_bufferLock)
            {
                foreach (var character in chunk)
                {
                    if (character is '\r' or '\n')
                    {
                        TryFlushBuffer(scans);
                        continue;
                    }

                    if (!char.IsControl(character))
                    {
                        _buffer.Append(character);
                    }
                }

                if (_buffer.Length > 0)
                {
                    _idleTimer.Change(_config.IdleFlushMs, Timeout.Infinite);
                }
            }

            foreach (var scan in scans)
            {
                HandleScan(scan);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to read scanner data: {exception.Message}");
            _shutdownSource.Cancel();
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs eventArgs)
    {
        Console.Error.WriteLine($"Serial port error: {eventArgs.EventType}");
        _shutdownSource.Cancel();
    }

    private void FlushOnIdle(object? state)
    {
        List<string> scans = [];

        lock (_bufferLock)
        {
            TryFlushBuffer(scans);
        }

        foreach (var scan in scans)
        {
            HandleScan(scan);
        }
    }

    private void TryFlushBuffer(List<string> scans)
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        var value = _buffer.ToString().Trim();
        _buffer.Clear();
        _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (!string.IsNullOrWhiteSpace(value))
        {
            scans.Add(value);
        }
    }

    private void HandleScan(string rawValue)
    {
        Console.WriteLine($"Scanned: {rawValue}");

        var resolvedValue = ResolveScanTarget(rawValue);

        if (TryGetLocalPath(resolvedValue, out var localPath))
        {
            OpenLocalPath(localPath);
            return;
        }

        if (
            Uri.TryCreate(resolvedValue, UriKind.Absolute, out var uri)
            && !uri.IsFile
            && !string.IsNullOrWhiteSpace(uri.Scheme)
        )
        {
            Process.Start(new ProcessStartInfo { FileName = resolvedValue, UseShellExecute = true });

            return;
        }

        Console.WriteLine(
            "Scan ignored because it is neither a valid absolute URL nor a local path."
        );
    }

    private string ResolveScanTarget(string rawValue)
    {
        if (_config.TryGetBarcodeTarget(rawValue, out var mappedTarget))
        {
            Console.WriteLine($"Mapped barcode '{rawValue}' to '{mappedTarget}'.");
            return mappedTarget;
        }

        return rawValue;
    }

    private static bool TryGetLocalPath(string input, out string localPath)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            localPath = uri.LocalPath;
            return true;
        }

        if (Path.IsPathRooted(input))
        {
            localPath = Path.GetFullPath(input);
            return true;
        }

        localPath = string.Empty;
        return false;
    }

    private void OpenLocalPath(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                }
            );

            return;
        }

        if (File.Exists(path))
        {
            if (_config.ShouldOpenInBrowser(path))
            {
                var fileUri = new Uri(Path.GetFullPath(path));
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = fileUri.AbsoluteUri,
                        UseShellExecute = true,
                    }
                );

                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                }
            );

            return;
        }

        Console.WriteLine($"Local path does not exist: {path}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idleTimer.Dispose();
        _shutdownSource.Cancel();
        _shutdownSource.Dispose();

        if (_port is not null)
        {
            _port.DataReceived -= OnDataReceived;
            _port.ErrorReceived -= OnErrorReceived;

            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.Dispose();
        }
    }
}
