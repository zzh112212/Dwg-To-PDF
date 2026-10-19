using System;
using System.Collections.Generic;

namespace DwgToPdf.Batch;

public enum ItemStatus
{
	Pending,
	Running,
	Success,
	Failed,
	Cancelled
}

/// <summary>v4.17 修订三 需求①：本轮"待转换清单"的挑选规则。
/// 放在这里（而不是 MainForm）是因为它是**领域规则**而非界面逻辑：SelfTest 的编译清单
/// 不含 WinForms 的 MainForm.cs，放在 MainForm 里就无法做语义回归。
/// 规则本身由用户明确拍板：默认跳过，不加设置项。</summary>
public static class RunSelection
{
	/// <summary>从待转换清单里挑出本轮真正要转换的文件 —— **已成功的跳过**。
	/// 用户明确要求"默认跳过，且不需要在设置里面添加"：正常只转换"待转化"列表里的文件，
	/// 上一批已成功的图纸不重复参与打印（修复"再次点击转换时上一批又重打一遍"）。
	/// 只跳过 Success；Failed / Cancelled / Pending / 未知状态一律保留，保证失败图纸补得回来。</summary>
	public static List<string> FilterTodoFiles(IEnumerable<string> files, Func<string, ItemStatus> statusOf, out int alreadyDone)
	{
		var todo = new List<string>();
		alreadyDone = 0;
		if (files != null)
		{
			foreach (string path in files)
			{
				if (path == null) continue;
				if (statusOf != null && statusOf(path) == ItemStatus.Success)
				{
					alreadyDone++;
					continue;
				}
				todo.Add(path);
			}
		}
		return todo;
	}
}
