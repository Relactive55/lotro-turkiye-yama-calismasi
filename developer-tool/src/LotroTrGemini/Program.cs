using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LotroTrGemini;

internal static class Program
{
	private static string _appDir;

	public static string AppDir
	{
		get
		{
			if (string.IsNullOrWhiteSpace(_appDir))
			{
				string configured = Environment.GetEnvironmentVariable("LOTRO_APP_DATA");
				_appDir = string.IsNullOrWhiteSpace(configured)
					? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data")
					: Path.GetFullPath(configured);
				Directory.CreateDirectory(_appDir);
			}
			return _appDir;
		}
	}

	[STAThread]
	private static void Main()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
		{
			Show(e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			Show((e.ExceptionObject as Exception) ?? new Exception(e.ExceptionObject?.ToString() ?? ""));
		};
		Application.Run(new MainForm());
	}

	public static void Log(string msg)
	{
		try
		{
			File.AppendAllText(Path.Combine(AppDir, "app.log"), DateTime.Now.ToString("o") + "  " + msg + Environment.NewLine);
		}
		catch
		{
		}
	}

	private static void Show(Exception ex)
	{
		Log("ERROR " + ex);
		try
		{
			MessageBox.Show(ex.Message, "Lord of the Rings Türkçe Çeviri Aracı - Relactive", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		catch
		{
		}
	}
}
