using System;
using System.Runtime.InteropServices;

namespace DwgToPdf.Batch;

internal static class OleMsgFilter
{
	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("00000016-0000-0000-C000-000000000046")]
	private interface IMessageFilter
	{
		[PreserveSig]
		int HandleInComingCall(uint dwCallType, IntPtr hTaskCaller, uint dwTickCount, IntPtr lpInterfaceInfo);

		[PreserveSig]
		int RetryRejectedCall(IntPtr hTaskCallee, uint dwTickCount, uint dwRejectType);

		[PreserveSig]
		int MessagePending(IntPtr hTaskCallee, uint dwTickCount, uint dwPendingType);
	}

	private sealed class Impl : IMessageFilter
	{
		public int HandleInComingCall(uint a, IntPtr b, uint c, IntPtr d)
		{
			return 0;
		}

		public int RetryRejectedCall(IntPtr h, uint tick, uint rejectType)
		{
			return 100;
		}

		public int MessagePending(IntPtr h, uint tick, uint pending)
		{
			return 2;
		}
	}

	[ThreadStatic]
	private static bool _installed;

	[DllImport("ole32.dll")]
	private static extern int CoRegisterMessageFilter(IMessageFilter lpMessageFilter, out IMessageFilter lplpOldFilter);

	public static void Install()
	{
		if (!_installed && CoRegisterMessageFilter(new Impl(), out var _) == 0)
		{
			_installed = true;
		}
	}
}
