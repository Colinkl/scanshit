internal sealed class ScannerConfig
{
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = nameof(System.IO.Ports.Parity.None);
    public string StopBits { get; set; } = nameof(System.IO.Ports.StopBits.One);
    public string Handshake { get; set; } = nameof(System.IO.Ports.Handshake.None);
    public string NewLine { get; set; } = "\r\n";
    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }
    public int ReadTimeoutMs { get; set; } = 500;
    public int IdleFlushMs { get; set; } = 150;
    public string[] BrowserFileExtensions { get; set; } = [".html", ".htm"];
    public Dictionary<string, string> BarcodeTargets { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName))
        {
            throw new InvalidOperationException("PortName is required.");
        }

        if (BaudRate <= 0)
        {
            throw new InvalidOperationException("BaudRate must be greater than 0.");
        }

        if (DataBits is < 5 or > 8)
        {
            throw new InvalidOperationException("DataBits must be between 5 and 8.");
        }

        if (IdleFlushMs <= 0)
        {
            throw new InvalidOperationException("IdleFlushMs must be greater than 0.");
        }

        BrowserFileExtensions = BrowserFileExtensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        BarcodeTargets = BarcodeTargets
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)
            )
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    public bool ShouldOpenInBrowser(string path)
    {
        var extension = Path.GetExtension(path);
        return BrowserFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetBarcodeTarget(string barcode, out string target)
    {
        return BarcodeTargets.TryGetValue(barcode.Trim(), out target!);
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }
}
