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
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			_w = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			_w.AutoFlush = true;
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
				catch
				{
				}
			}
			if (_onLine != null)
			{
				try { _onLine(line); }
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
			catch
			{
			}
			try
			{
				_w.Close();
			}
			catch
			{
			}
		}
	}
}
