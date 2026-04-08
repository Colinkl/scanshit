using System;
using System.Windows.Forms;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		ApplicationConfiguration.Initialize();
		Application.Run(new ScannerTrayContext(args));
	}
}
