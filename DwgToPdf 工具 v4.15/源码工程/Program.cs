using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

internal static class Program
{
	public const string Version = "v4.15";

	[STAThread]
	private static void Main()
	{
		BgLogger.LogStartup();
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
		{
			HandleThread(e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			HandleDomain(e.ExceptionObject);
		};
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.Run(new MainForm());
		BgLogger.LogShutdown();
	}

	private static void HandleThread(Exception ex)
	{
		BgLogger.Error("UI 线程异常: " + Flatten(ex));
	}

	private static void HandleDomain(object obj)
	{
		Exception ex = obj as Exception;
		BgLogger.Error("致命异常: " + ((ex != null) ? Flatten(ex) : ((obj != null) ? obj.ToString() : "(空)")));
	}

	private static string Flatten(Exception ex)
	{
		StringBuilder stringBuilder = new StringBuilder();
		while (ex != null)
		{
			stringBuilder.AppendLine(ex.GetType().Name + ": " + ex.Message);
			stringBuilder.AppendLine(ex.StackTrace);
			ex = ex.InnerException;
			if (ex != null)
			{
				stringBuilder.AppendLine("   -> 内部异常:");
			}
		}
		return stringBuilder.ToString();
	}
}
