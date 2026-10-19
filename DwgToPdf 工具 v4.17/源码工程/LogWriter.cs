using System;
using System.IO;
using System.Text;

namespace DwgToPdf.Batch;

internal sealed class LogWriter : IDisposable
{
	private readonly object _lock = new object();

	private readonly StreamWriter _w;

	private readonly string _path;

	private readonly Action<string> _onLine;

	/// <summary>v4.17：最近一次写入的 UTC 时间（内存记录）。
	/// 阶段看门狗改用它判断"日志停滞"——不再依赖磁盘日志文件的修改时间。</summary>
	public DateTime LastWriteUtc { get; private set; }

	/// <summary>v4.17 修订十（CQ-02）：任一次写盘/收尾失败即为 true。成功路径恒 false。</summary>
	public bool WriteFailed { get; private set; }

	/// <summary>v4.17 修订十（CQ-02）：写盘失败累计次数（降噪：仅首次失败 mirror 后台日志）。</summary>
	public int WriteFailCount { get; private set; }

	/// <summary>v4.17 修订十（CQ-02）：最近一次失败原因（"异常类型: 消息"）。</summary>
	public string LastWriteError { get; private set; }

	/// <summary>v4.17：仅内存模式 —— 不写任何文件，日志行只经 onLine 回调镜像到界面。
	/// 这是主程序的默认模式：输出文件夹不再生成 logs\ 目录与 *.log 文件。</summary>
	public LogWriter(Action<string> onLine) : this(null, onLine)
	{
	}

	/// <summary>v4.17：path 为 null/空 → 仅内存模式（不落盘）；onLine 为 null → 仅落盘（自测等旧调用不受影响）。</summary>
	public LogWriter(string path, Action<string> onLine = null)
	{
		_path = path;
		_onLine = onLine;
		LastWriteUtc = DateTime.UtcNow;
		if (!string.IsNullOrEmpty(path))
		{
			try
			{
				string directoryName = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				_w = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				_w.AutoFlush = true;
			}
			catch (Exception ex)
			{
				// v4.17 修订十·补（CQ-02 边界）：造期失败（无效/不可写日志路径）降级仅内存模式，
				// 不阻塞转换（日志 best-effort 语义）；置失败标志供收尾警告行提示
				_w = null;
				WriteFailCount = 1;
				LastWriteError = "构造期：" + ex.GetType().Name + ": " + ex.Message;
				WriteFailed = true;
				BgLogger.Error("运行日志初始化失败（已降级为仅内存模式）：" + LastWriteError + "（路径 " + path + "）");
			}
		}
	}

	public void WriteLine(string msg)
	{
		lock (_lock)
		{
			string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
			LastWriteUtc = DateTime.UtcNow;
			if (_w != null)
			{
				try
				{
					_w.WriteLine(line);
				}
				catch (Exception ex)
				{
				WriteFailCount++;
				LastWriteError = ex.GetType().Name + ": " + ex.Message;
				WriteFailed = true;
				if (WriteFailCount == 1) BgLogger.Error("运行日志写盘失败：" + LastWriteError + "（路径 " + _path + "）");
				}
			}
			if (_onLine != null)
			{
				try { _onLine(line); }
				// best-effort：界面镜像失败不影响日志写出
				catch { }
			}
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			if (_w == null) return;
			try
			{
				_w.Flush();
			}
			catch (Exception ex)
			{
				WriteFailCount++;
				LastWriteError = ex.GetType().Name + ": " + ex.Message;
				WriteFailed = true;
				if (WriteFailCount == 1) BgLogger.Error("运行日志写盘失败：" + LastWriteError + "（路径 " + _path + "）");
			}
			try
			{
				_w.Close();
			}
			catch (Exception ex)
			{
				WriteFailCount++;
				LastWriteError = ex.GetType().Name + ": " + ex.Message;
				WriteFailed = true;
				if (WriteFailCount == 1) BgLogger.Error("运行日志写盘失败：" + LastWriteError + "（路径 " + _path + "）");
			}
		}
	}
}
