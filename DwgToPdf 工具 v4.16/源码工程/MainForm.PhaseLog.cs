using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.6：从 MainForm.cs 拆分出阶段条与日志面板辅助方法（纯移动，不改逻辑）：
// IndexOfPhase / UpdatePhaseProgress / UpdateTotalProgress / SetPhaseBarIdle
// / AdvancePhase / ResetPhaseBar / SetPhaseBarDone / AppendLog / OpenLogFolder
internal sealed partial class MainForm
{
	// ================= v3.9（双进度条）：总进度 / 阶段进度辅助 =================
	/// <summary>阶段在 PhaseOrder 中的下标；不在 5 段序列（如 Done）返回 -1。</summary>
	private static int IndexOfPhase(RunPhase phase)
	{
		for (int i = 0; i < PhaseOrder.Length; i++)
			if (PhaseOrder[i] == phase) return i;
		return -1;
	}

	/// <summary>阶段内进度：更新 _phaseDonePct[阶段]=done/total（只增不减，重试轮不回跳），
	/// 刷新阶段进度条与总进度条。</summary>
	private void UpdatePhaseProgress(RunPhase phase, int done, int total)
	{
		int i = IndexOfPhase(phase);
		if (i < 0 || total <= 0) return;
		double pct = Math.Max(0, Math.Min(1.0, (double)done / total));
		if (pct > _phaseDonePct[i]) _phaseDonePct[i] = pct;
		_progressPhase.Value = (int)(_progressPhase.Maximum * pct);
		UpdateTotalProgress();
	}

	/// <summary>总进度 = Σ(阶段权重 × 阶段完成比例)。权重合计 100，故 Maximum=100 直接取值。</summary>
	private void UpdateTotalProgress()
	{
		double sum = 0;
		for (int i = 0; i < _phaseDonePct.Length && i < _phaseWeights.Length; i++)
			sum += _phaseWeights[i] * _phaseDonePct[i];
		_progressTotal.Value = (int)Math.Min(_progressTotal.Maximum, sum);
	}

	// ================= v3.9：阶段条辅助 =================
	/// <summary>初始/空闲：阶段条全灰。</summary>
	private void SetPhaseBarIdle()
	{
		_currentPhase = RunPhase.Prepare;
		Array.Clear(_phaseSkippedFlags, 0, _phaseSkippedFlags.Length);
		Array.Clear(_phaseDonePct, 0, _phaseDonePct.Length);
		if (_progressTotal != null) _progressTotal.Value = 0;
		if (_progressPhase != null) _progressPhase.Value = 0;
		if (_phaseChips == null) return;
		for (int i = 0; i < _phaseChips.Length; i++)
		{
			_phaseChips[i].BackColor = SystemColors.Control;
			_phaseChips[i].ForeColor = Color.DimGray;
		}
	}

	/// <summary>v3.9：阶段推进（只前进；skipped 置灰当前阶段并继续，不打断流程顺序）。</summary>
	private void AdvancePhase(RunPhase phase, bool skipped)
	{
		_currentPhase = phase;
		if (skipped && _phaseChips != null)
		{
			for (int i = 0; i < PhaseOrder.Length; i++)
				if (PhaseOrder[i] == phase) _phaseSkippedFlags[i] = true;
		}
		// v3.9（双进度条）：阶段切换结算。当前阶段及之前=已完成（跳过阶段也视为完成），之后=未开始
		int idx = IndexOfPhase(phase);
		if (idx >= 0)
		{
			for (int i = 0; i < _phaseDonePct.Length; i++)
				_phaseDonePct[i] = (i <= idx) ? 1.0 : 0.0;
			_progressPhase.Value = 0; // 新阶段内进度从 0 起算（跳过阶段无实际工作也显示 0）
			UpdateTotalProgress();
		}
		ResetPhaseBar();
	}

	/// <summary>按 _currentPhase 重着色：之前=完成（蓝底），当前=进行中（绿底），之后=未开始（灰）；跳过阶段灰底+"（跳过）"。</summary>
	private void ResetPhaseBar()
	{
		if (_phaseChips == null) return;
		bool reached = false;
		for (int i = 0; i < PhaseOrder.Length; i++)
		{
			Label chip = _phaseChips[i];
			if (PhaseOrder[i] == _currentPhase)
			{
				reached = true;
				if (_phaseSkippedFlags[i])
				{
					chip.BackColor = SystemColors.Control;
					chip.ForeColor = Color.DimGray;
					chip.Text = PhaseNames[i] + "（跳过）";
				}
				else
				{
					chip.BackColor = Color.FromArgb(223, 240, 216);
					chip.ForeColor = Color.FromArgb(46, 104, 57);
					chip.Text = PhaseNames[i];
				}
			}
			else if (!reached)
			{
				chip.BackColor = Color.FromArgb(221, 235, 247);
				chip.ForeColor = Color.FromArgb(31, 78, 121);
				chip.Text = PhaseNames[i];
			}
			else
			{
				chip.BackColor = SystemColors.Control;
				chip.ForeColor = Color.DimGray;
				chip.Text = PhaseNames[i];
			}
		}
	}

	/// <summary>转换结束/停止：阶段条全部落定为完成色。</summary>
	private void SetPhaseBarDone()
	{
		_currentPhase = RunPhase.Done;
		ResetPhaseBar();
	}

	// ================= v3.9：日志面板 =================
	/// <summary>追加一行到界面日志面板（UI 线程调用）。
	/// v4.15 修复：原实现每次追加后**无条件** ScrollToCaret——批量转换日志刷得很快，
	/// 用户向上翻阅历史时会被不断拽回底部（"日志不能上拉"）。现在改为**只在用户正处于底部时跟随**：
	/// 上翻即暂停跟随（此时把视口恢复回原来的位置），滚回底部或勾选"自动滚动"即恢复跟随。</summary>
	private void AppendLog(string line)
	{
		if (_txtLog == null || line == null) return;
		try
		{
			bool wantFollow = _chkAutoScroll == null || _chkAutoScroll.Checked;
			bool follow = LogFollowPolicy.ShouldAutoScroll(wantFollow, LastVisibleLine(_txtLog), _txtLog.GetLineFromCharIndex(_txtLog.TextLength));
			int keepChar = follow ? -1 : FirstVisibleChar(_txtLog);
			_txtLog.AppendText(line + "\r\n");   // 注意：AppendText 会把插入符移到末尾，需按需纠正视口
			if (follow)
			{
				_txtLog.SelectionStart = _txtLog.TextLength;
				_txtLog.ScrollToCaret();
			}
			else if (keepChar >= 0)
			{
				// 保持原视口：把插入符放回"原来的首个可见字符"再 ScrollToCaret（最小滚动 → 位置不变）
				_txtLog.SelectionStart = keepChar;
				_txtLog.ScrollToCaret();
			}
		}
		catch { }
	}

	/// <summary>当前日志面板可见的最后一行的行号（读取失败按"在底部"处理，保持旧行为）。</summary>
	private static int LastVisibleLine(RichTextBox rtb)
	{
		try
		{
			int x = Math.Max(1, rtb.ClientSize.Width / 2);
			int y = Math.Max(1, rtb.ClientSize.Height - 2);
			return rtb.GetLineFromCharIndex(rtb.GetCharIndexFromPosition(new Point(x, y)));
		}
		catch { return int.MaxValue; }
	}

	/// <summary>当前日志面板顶部可见字符的索引（用于暂停跟随时恢复视口；失败返回 -1=不处理）。</summary>
	private static int FirstVisibleChar(RichTextBox rtb)
	{
		try { return rtb.GetCharIndexFromPosition(new Point(2, 2)); }
		catch { return -1; }
	}

	private void OpenLogFolder()
	{
		string outDir = _txtOutput.Text.Trim();
		if (string.IsNullOrEmpty(outDir))
		{
			_lblStatus.Text = "请先选择输出文件夹。";
			return;
		}
		string logsDir = Path.Combine(outDir, "logs");
		if (Directory.Exists(logsDir))
		{
			try { Process.Start("explorer.exe", "\"" + logsDir + "\""); }
			catch { }
		}
		else
				{
					try { Process.Start("explorer.exe", "\"" + outDir + "\""); }
					catch { }
				}
		}
}
