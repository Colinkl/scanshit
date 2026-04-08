internal sealed class ScannerConfig
{
    public string PortName { get; init; } = "COM3";
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public string Parity { get; init; } = nameof(System.IO.Ports.Parity.None);
    public string StopBits { get; init; } = nameof(System.IO.Ports.StopBits.One);
    public string Handshake { get; init; } = nameof(System.IO.Ports.Handshake.None);
    public string NewLine { get; init; } = "\r\n";
    public bool DtrEnable { get; init; }
    public bool RtsEnable { get; init; }
    public int ReadTimeoutMs { get; init; } = 500;
    public int IdleFlushMs { get; init; } = 150;

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
    }
}
