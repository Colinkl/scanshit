using System.Text.Json;

internal sealed class ScannerApplication
{
    private const string ConfigFileName = "scannerconfig.json";

    public async Task RunAsync(string[] args)
    {
        var configPath = ResolveConfigPath(args);
        var config = LoadConfig(configPath);

        using var cancellationSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => cancellationSource.Cancel();

        Console.WriteLine($"Listening for scanner input on {config.PortName}.");
        Console.WriteLine("Press Ctrl+C to stop.");

        while (!cancellationSource.IsCancellationRequested)
        {
            using var listener = new SerialScannerListener(config);

            try
            {
                listener.Start();
                await listener.WaitForShutdownAsync(cancellationSource.Token);
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Scanner listener failed: {exception.Message}");
            }

            if (!cancellationSource.IsCancellationRequested)
            {
                Console.WriteLine("Retrying COM connection in 5 seconds...");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static string ResolveConfigPath(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        return Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    private static ScannerConfig LoadConfig(string configPath)
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
