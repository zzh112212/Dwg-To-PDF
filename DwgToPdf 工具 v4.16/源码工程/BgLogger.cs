using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DwgToPdf.Batch;

internal static class BgLogger
{
	private static readonly object Lock = new object();

	private static string _path;

	private static readonly string Name = "DwgToPdf";

	private static string EnsureFile()
	{
		if (_path == null)
		{
			string text = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
			if (string.IsNullOrEmpty(text))
			{
				text = AppDomain.CurrentDomain.BaseDirectory;
			}
			string text2 = Path.Combine(text, "运行日志");
			try
			{
				Directory.CreateDirectory(text2);
			}
			catch
			{
				text2 = Path.GetTempPath();
			}
			_path = Path.Combine(text2, Name + "_后台日志_" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
		}
		return _path;
	}

	public static void Line(string level, string msg)
	{
		lock (Lock)
		{
			try
			{
				string text = ((msg == null) ? "" : msg.Replace("\r", "").Replace("\n", " ⏎ "));
				string text2 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + level + "]  " + text;
				File.AppendAllText(EnsureFile(), text2 + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch
			{
			}
		}
	}

	public static void Info(string m)
	{
		Line("信息", m);
	}

	public static void Warn(string m)
	{
		Line("警告", m);
	}

	public static void Error(string m)
	{
		Line("错误", m);
	}

	public static void LogStartup()
	{
		Info("程序启动");
		// v2.3.1：这里只打印"环境信息"（非自检）。真正的首次环境自检由 EnvironmentSelfCheck
		// 完成且仅在首次运行时执行一次（以"自检已通过.txt"标记）；避免用户把每次启动的环境
		// 信息误认为工具在反复"环境检查"。
		Info("—— 环境信息 ——" + Environment.NewLine + DetectEnvironment());
	}

	public static void LogShutdown()
	{
		Info("程序正常退出");
	}

	public static void LogRunStart(string input, string output, bool tight, bool includeSub)
    {
        Info("运行开始：输入=" + input + "\n输出=" + output + "\n贴边=" + tight + "，并发调度=CPU 自适应，含子目录=" + includeSub);
    }

	public static void LogRunEnd(DateTime start, int ok, int fail, int cancel, bool cancelled)
	{
		double num = Math.Round((DateTime.Now - start).TotalSeconds, 1);
		string text = (cancelled ? "已停止" : "完成");
		Info("运行" + text + "：成功=" + ok + "，失败=" + fail + "，取消=" + cancel + "，用时=" + num + " 秒");
	}

	public static void LogFile(string file, string status, string reason)
	{
		string text = (string.IsNullOrEmpty(reason) ? "" : (" —— " + reason));
		Line((status == "F") ? "失败" : "信息", status + "  " + file + text);
	}

	public static string DetectEnvironment()
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			stringBuilder.AppendLine(string.Concat("操作系统: ", Environment.OSVersion, Environment.Is64BitOperatingSystem ? " (64位)" : " (32位)"));
		}
		catch
		{
		}
		try
		{
			stringBuilder.AppendLine(".NET: " + Environment.Version);
		}
		catch
		{
		}
		try
		{
			stringBuilder.AppendLine("程序路径: " + Environment.GetCommandLineArgs()[0]);
		}
		catch
		{
		}
		bool flag = false;
		try
		{
			using RegistryKey registryKey = Registry.ClassesRoot.OpenSubKey("AutoCAD.Application");
			flag = registryKey != null;
		}
		catch
		{
		}
		stringBuilder.AppendLine("AutoCAD COM 注册(AutoCAD.Application 脚本ID): " + (flag ? "已检测到" : "未检测到 —— 工具将无法启动 AutoCAD！"));
		bool flag2 = false;
		try
		{
			object activeObject = Marshal.GetActiveObject("AutoCAD.Application");
			if (activeObject != null)
			{
				flag2 = true;
				Marshal.FinalReleaseComObject(activeObject);
			}
		}
		catch
		{
		}
		stringBuilder.AppendLine("已有 AutoCAD 实例在运行: " + (flag2 ? "是" : "否"));
		stringBuilder.AppendLine("注册表中的 AutoCAD 版本:");
		string value = ScanAutoCadRegistry();
		if (string.IsNullOrEmpty(value))
		{
			stringBuilder.AppendLine("  （未在注册表 HKCU/HKLM 找到 AutoCAD 版本条目）");
		}
		else
		{
			stringBuilder.AppendLine(value);
		}
		try
		{
			stringBuilder.AppendLine("AutoCAD Core Console(accoreconsole.exe): " +
				(CoreConsoleLocator.FindAccoreConsole() ?? "未找到 —— 批量出图将回退 COM 通道"));
		}
		catch
		{
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static string ScanAutoCadRegistry()
	{
		StringBuilder stringBuilder = new StringBuilder();
		string[] array = new string[2] { "SOFTWARE\\Autodesk\\AutoCAD", "SOFTWARE\\WOW6432Node\\Autodesk\\AutoCAD" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			try
			{
				using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(text);
				if (registryKey == null)
				{
					continue;
				}
				string[] subKeyNames = registryKey.GetSubKeyNames();
				string[] array3 = subKeyNames;
				foreach (string text2 in array3)
				{
					if (text2.StartsWith("R", StringComparison.OrdinalIgnoreCase))
					{
						stringBuilder.AppendLine("  HKLM\\" + text + "\\" + text2);
					}
				}
			}
			catch
			{
			}
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Autodesk\\AutoCAD");
			if (registryKey != null)
			{
				string[] subKeyNames = registryKey.GetSubKeyNames();
				array2 = subKeyNames;
				foreach (string text2 in array2)
				{
					if (text2.StartsWith("R", StringComparison.OrdinalIgnoreCase))
					{
						stringBuilder.AppendLine("  HKCU\\Software\\Autodesk\\AutoCAD\\" + text2);
					}
				}
			}
		}
		catch
		{
		}
		return stringBuilder.ToString().Trim();
	}
}
