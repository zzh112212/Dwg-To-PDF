using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

internal static class Program
{
	// v4.17 修订三（2026-10-15）：三项用户反馈落地
	// ① 二次转换跳过已成功图纸 ② PDF 子文件夹改时间戳名（可自定义）
	// ③ UCS 条件复位。用户决策：**本修订不升版本号，仍以 v4.17 交付**。
	public const string Version = "v4.17";

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
