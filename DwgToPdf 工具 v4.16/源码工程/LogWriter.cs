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

	/// <summary>v3.9：可选日志行回调（界面日志面板实时镜像）。先写文件再回调；
	/// 回调抛异常不影响文件写入。缺省 null=仅写文件（自测等旧调用不受影响）。</summary>
	public LogWriter(string path, Action<string> onLine = null)
	{
		_path = path;
		_onLine = onLine;
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		_w = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		_w.AutoFlush = true;
	}

	public void WriteLine(string msg)
	{
		lock (_lock)
		{
			string line = null;
			try
			{
				line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg;
				_w.WriteLine(line);
			}
			catch
			{
			}
			if (_onLine != null && line != null)
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
