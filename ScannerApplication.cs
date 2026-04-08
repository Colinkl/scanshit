using System.Text.Json;

internal sealed class ScannerApplication
{
    private const string ConfigFileName = "scannerconfig.json";
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

        _statusSink($"Listening on {config.PortName}");

        while (!cancellationToken.IsCancellationRequested)
        {
            using var listener = new SerialScannerListener(config);

            try
            {
                listener.Start();
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
                _statusSink($"Retrying {config.PortName} in 5 seconds");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
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
        var config = JsonSerializer.Deserialize<ScannerConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (config is null)
        {
            throw new InvalidOperationException("Failed to deserialize scanner config.");
        }

        config.Validate();
        return config;
    }
}
