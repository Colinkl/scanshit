using System.Text.Json;

internal sealed class ScannerApplication
{
    private const string ConfigFileName = "scannerconfig.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly Action<string> _statusSink;
    private readonly Action<string> _errorSink;

    public ScannerApplication(Action<string>? statusSink = null, Action<string>? errorSink = null)
    {
        _statusSink = statusSink ?? (_ => { });
        _errorSink = errorSink ?? (_ => { });
    }

    public async Task RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var configPath = ResolveConfigPath(args);
        var config = LoadConfig(configPath);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var listener = new SerialScannerListener(config);

            try
            {
                listener.Start();
                _statusSink($"Listening on {config.PortName}");
                await listener.WaitForShutdownAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _errorSink($"Scanner listener failed: {exception.Message}");
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await WaitToRetryAsync(config.PortName, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private Task WaitToRetryAsync(string portName, CancellationToken cancellationToken)
    {
        return WaitToRetryAsyncCore(portName, cancellationToken);
    }

    private async Task WaitToRetryAsyncCore(string portName, CancellationToken cancellationToken)
    {
        const int retrySeconds = 5;

        for (var remainingSeconds = retrySeconds; remainingSeconds >= 1; remainingSeconds--)
        {
            _statusSink($"Retrying {portName} in {remainingSeconds}s");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    public static string ResolveConfigPath(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        return Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    public static ScannerConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Config file was not found. Expected it at '{configPath}'.",
                configPath
            );
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ScannerConfig>(json, JsonOptions);

        if (config is null)
        {
            throw new InvalidOperationException("Failed to deserialize scanner config.");
        }

        config.Validate();
        return config;
    }

    public static void SaveConfig(string configPath, ScannerConfig config)
    {
        config.Validate();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(configPath, json);
    }
}
