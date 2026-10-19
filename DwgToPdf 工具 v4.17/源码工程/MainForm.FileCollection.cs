using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十二：从 MainForm.cs 拆分出「拖放与文件收集」方法（纯移动，不改逻辑）：
// OnDragEnter / OnDragDrop / PasteFromClipboard / AddFilesDialog /
// AddFolderDialog / AddFiles / AddToBatch / ClearList
internal sealed partial class MainForm
{
	/// <summary>拖入时的光标反馈：仅当未在转换中且拖的是文件（FileDrop）时显示「复制」，否则显示「禁止」，避免转换期间误拖入。</summary>
	private void OnDragEnter(object sender, DragEventArgs e)
	{
		if (_converting || !e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.None;
			return;
		}
		e.Effect = DragDropEffects.Copy;
	}

	/// <summary>放下文件时：转换中忽略；否则把拖入的文件路径交给 AddFiles 收集（文件夹会自动递归展开，见 AddFiles）。</summary>
	private void OnDragDrop(object sender, DragEventArgs e)
	{
		if (_converting)
		{
			return;
		}
		if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
		{
			AddFiles(files);
		}
	}

	/// <summary>粘贴清单：转换中忽略；优先读剪贴板的文件列表，否则把文本按行（CRLF / LF）拆成路径，交给 AddFiles；剪贴板竞争或格式异常记后台日志、不再静默。由 Ctrl+V 触发。</summary>
	private void PasteFromClipboard()
	{
		if (_converting)
		{
			return;
		}
		try
		{
			List<string> paths = new List<string>();
			if (Clipboard.ContainsFileDropList())
			{
				foreach (string s in Clipboard.GetFileDropList())
				{
					paths.Add(s);
				}
			}
			else if (Clipboard.ContainsText())
			{
				foreach (string line in Clipboard.GetText().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
				{
					paths.Add(line.Trim());
				}
			}
			AddFiles(paths);
		}
		catch (Exception ex)
		{
			// v4.17 修订十（CQ-06）：剪贴板竞争/格式异常不再静默
			BgLogger.Warn("剪贴板路径收集失败：" + ex.GetType().Name + ": " + ex.Message);
		}
	}

	/// <summary>「添加文件…」：转换中忽略；弹多选、限定 dwg 的文件对话框，确定后把所选文件交给 AddFiles。</summary>
	private void AddFilesDialog()
	{
		if (_converting)
		{
			return;
		}
		using OpenFileDialog ofd = new OpenFileDialog
		{
			Title = "选择 dwg 图纸",
			Filter = "DWG 图纸 (*.dwg)|*.dwg|所有文件 (*.*)|*.*",
			Multiselect = true
		};
		if (ofd.ShowDialog(this) == DialogResult.OK)
		{
			AddFiles(ofd.FileNames);
		}
	}

	/// <summary>选择整个文件夹，把其中（含所有子文件夹）的 dwg 一并加入清单。</summary>
	private void AddFolderDialog()
	{
		if (_converting)
		{
			return;
		}
		using FolderBrowserDialog fbd = new FolderBrowserDialog
		{
			Description = "选择包含 DWG 图纸的文件夹（含所有子文件夹）",
			ShowNewFolderButton = true
		};
		if (fbd.ShowDialog(this) == DialogResult.OK)
		{
			AddFiles(new[] { fbd.SelectedPath });
		}
	}

	/// <summary>统一的文件收集入口：逐条清洗路径（去空白、去首尾引号）——目录则递归枚举其中所有 *.dwg，文件则须以 .dwg 结尾；经 AddToBatch 去重后加入清单。若有新增，记一条可撤销操作并重建列表。拖放 / 粘贴 / 文件与文件夹对话框都汇聚到这里。</summary>
	private void AddFiles(IEnumerable<string> paths)
	{
		if (paths == null || _converting)
		{
			return;
		}
		List<string> batch = new List<string>();
		foreach (string raw in paths)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				continue;
			}
			string path = raw.Trim().Trim('"');
			if (Directory.Exists(path))
			{
				foreach (string f in Directory.EnumerateFiles(path, "*.dwg", SearchOption.AllDirectories))
				{
					AddToBatch(batch, f);
				}
			}
			else if (File.Exists(path) && path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
			{
				AddToBatch(batch, path);
			}
		}
		if (batch.Count > 0)
		{
			PushUndo(new UndoEntry { Added = batch });
			RefreshListUi();
		}
	}

	/// <summary>把单个文件加入清单（内部助手）：先转成绝对路径，若已在待转换清单或本批已收则跳过（忽略大小写去重），否则同时追加到 _pendingFiles 与本批 batch。</summary>
	private void AddToBatch(List<string> batch, string path)
	{
		if (_converting)
		{
			return;
		}
		string full = Path.GetFullPath(path);
		if (_pendingFiles.Contains(full, StringComparer.OrdinalIgnoreCase) || batch.Contains(full, StringComparer.OrdinalIgnoreCase))
		{
			return;
		}
		_pendingFiles.Add(full);
		batch.Add(full);
	}

	/// <summary>「清空列表」：转换进行中或清单已空时不动作；否则先压入一条可撤销记录（保存当前清单），再清空待转换清单及其状态 / 备注，最后重建列表。</summary>
	private void ClearList()
	{
		if (_converting || _pendingFiles.Count == 0)
		{
			return;
		}
		PushUndo(new UndoEntry { Cleared = new List<string>(_pendingFiles) });
		_pendingFiles.Clear();
		_statusByPath.Clear();
		_noteByPath.Clear();
		RefreshListUi();
	}
}
