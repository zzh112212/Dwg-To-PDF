using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十二：从 MainForm.cs 拆分出「设置/持久化」方法（纯移动，不改逻辑）：
// LoadAppSettings / ApplyUiState / SaveUiState / OpenSettings
internal sealed partial class MainForm
{
	// v4.3：从 %APPDATA%\DwgToPdf\settings.json 加载用户设置；首次启动自动生成默认
	/// <summary>从 %APPDATA%\DwgToPdf\settings.json 加载用户设置到 _appSettings（首次启动会自动生成默认文件）；读取失败时退回全新默认设置、不抛出。由构造函数最先调用。</summary>
	private void LoadAppSettings()
	{
		try { _appSettings = AppSettings.Load(); }
		catch { _appSettings = new AppSettings(); }
	}

	/// <summary>v4.6：启动时恢复上次使用状态——输出文件夹、主界面选项（边距/配色/模式/
	/// 显示线宽/质量核查/识别块图框）、窗口大小与位置（含最大化）。恢复失败静默走默认。</summary>
	private void ApplyUiState()
	{
		var s = _appSettings;
		if (s == null) return;
		try
		{
			if (!string.IsNullOrEmpty(s.LastOutputDir) && Directory.Exists(s.LastOutputDir))
			{
				_txtOutput.Text = s.LastOutputDir;
			}
			_rbMarginOn.Checked = s.MarginOn;
			_rbMarginOff.Checked = !s.MarginOn;
			if (s.ColorIndex >= 0 && s.ColorIndex < _cmbPrint.Items.Count) _cmbPrint.SelectedIndex = s.ColorIndex;
			if (s.ModeIndex >= 0 && s.ModeIndex < _cmbMode.Items.Count) _cmbMode.SelectedIndex = s.ModeIndex;
			_chkLineWeight.Checked = s.LineWeight;
			_chkQualityCheck.Checked = s.QualityCheck;
			_chkBlockFrame.Checked = s.BlockFrame;
			if (s.WinLeft.HasValue && s.WinTop.HasValue && s.WinWidth.HasValue && s.WinHeight.HasValue)
			{
				var r = new Rectangle(s.WinLeft.Value, s.WinTop.Value, s.WinWidth.Value, s.WinHeight.Value);
				// 防呆：尺寸过小或完全在虚拟屏幕外（换了显示器/断投屏）则不恢复，保持默认居中
				var vs = SystemInformation.VirtualScreen;
				if (r.Width >= 400 && r.Height >= 300 && r.IntersectsWith(vs))
				{
					StartPosition = FormStartPosition.Manual;
					Bounds = r;
				}
			}
			if (s.WinMaximized) WindowState = FormWindowState.Maximized;
		}
		// best-effort：界面操作失败不影响功能
		catch { }
	}

	/// <summary>v4.6：关闭窗口时把当前界面状态写回 settings.json（下次启动自动恢复）。
	/// 最大化时记录 RestoreBounds（还原后的正常位置），并单独记录最大化标志。</summary>
	private void SaveUiState()
	{
		var s = _appSettings ?? new AppSettings();
		_appSettings = s;
		try
		{
			s.LastOutputDir = _txtOutput.Text.Trim();
			s.MarginOn = _rbMarginOn.Checked;
			s.ColorIndex = _cmbPrint.SelectedIndex;
			s.ModeIndex = _cmbMode.SelectedIndex;
			s.LineWeight = _chkLineWeight.Checked;
			s.QualityCheck = _chkQualityCheck.Checked;
			s.BlockFrame = _chkBlockFrame.Checked;
			Rectangle r = (WindowState == FormWindowState.Normal) ? Bounds : RestoreBounds;
			s.WinLeft = r.Left;
			s.WinTop = r.Top;
			s.WinWidth = r.Width;
			s.WinHeight = r.Height;
			s.WinMaximized = (WindowState == FormWindowState.Maximized);
			// v4.17 修订十·补（CQ-06 B-4）：Save 内部已 catch 并回 false、且记后台日志——
			// 此处按返回值坐实失败（原 catch 对 Save 是死路径），状态栏如实提示
			bool ok2 = s.Save();
			if (!ok2) _lblStatus.Text = "设置保存失败（详见后台日志）";
		}
		catch (Exception ex)
		{
			// 防御性兜底：字段读取/赋值段异常同样不静默（Save 内部吞异常后此路径极少触发）
			BgLogger.Warn("设置保存失败：" + ex.GetType().Name + ": " + ex.Message);
		}
	}

	// v4.3：打开设置窗口，编辑后保存到 settings.json 并立即影响下次转换
	/// <summary>打开设置窗口：以当前设置为初值弹出 SettingsForm，用户确定且返回了结果时替换 _appSettings 并在状态栏提示已保存（新设置在下次转换生效）；取消则不改动。</summary>
	private void OpenSettings(object sender, EventArgs e)
	{
		using (var dlg = new SettingsForm(_appSettings ?? new AppSettings()))
		{
			if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultSettings != null)
			{
				_appSettings = dlg.ResultSettings;
				_lblStatus.Text = "设置已保存：" + AppSettings.DefaultPath;
			}
		}
	}
}
