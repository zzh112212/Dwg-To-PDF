using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace DwgToPdf.Batch;

internal sealed class Late
{
	private const int RpcRejected = -2147418111;

	private readonly object _obj;

	private readonly Type _type;

	public object Raw => _obj;

	public Late(object obj)
	{
		if (obj == null)
		{
			throw new ArgumentNullException("obj");
		}
		_obj = obj;
		_type = obj.GetType();
	}

	public object Get(string name)
	{
		return Exec(() => _type.InvokeMember(name, BindingFlags.GetProperty, null, _obj, null));
	}

	public object GetAt(string name, object index)
	{
		try
		{
			return Exec(() => _type.InvokeMember(name, BindingFlags.InvokeMethod, null, _obj, new object[1] { index }));
		}
		catch (COMException)
		{
			return Exec(() => _type.InvokeMember(name, BindingFlags.GetProperty, null, _obj, new object[1] { index }));
		}
		catch (MissingMemberException)
		{
			return Exec(() => _type.InvokeMember(name, BindingFlags.GetProperty, null, _obj, new object[1] { index }));
		}
	}

	public void Set(string name, object value)
	{
		Exec(() => _type.InvokeMember(name, BindingFlags.SetProperty, null, _obj, new object[1] { value }));
	}

	public object Call(string name, params object[] args)
	{
		return Exec(() => _type.InvokeMember(name, BindingFlags.InvokeMethod, null, _obj, args));
	}

	/// <summary>v4.11：带硬超时的调用，用于收尾 Quit/Close 这类可能被 RPC 死锁卡住的 COM 调用。
	/// 调用投递到线程池执行（AutoCAD 为本地 COM 服务器，跨单元封送安全），超时后放弃等待并
	/// 返回 false——底层调用仍在后台，由调用方随后 ComStarter.KillAllOwned() 兜底终结进程。
	/// 正常路径与 Call() 完全一致（异常同样吞掉返回 false）。</summary>
	public bool CallWithTimeout(int timeoutMs, string name, params object[] args)
	{
		try
		{
			var t = System.Threading.Tasks.Task.Run(() => Call(name, args));
			return t.Wait(timeoutMs);
		}
		catch { return false; }
	}

	private T Exec<T>(Func<T> fn)
	{
		int num = 6;
		for (int i = 0; i < num; i++)
		{
			try
			{
				return fn();
			}
			catch (TargetInvocationException ex)
			{
				if (!(ex.InnerException is COMException { HResult: -2147418111 }))
				{
					throw ex.InnerException ?? ex;
				}
				Thread.Sleep(300 * (i + 1));
			}
		}
		return fn();
	}
}
