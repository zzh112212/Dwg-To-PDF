using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.6：从 MainForm.cs 拆分出阶段条与日志面板辅助方法（纯移动，不改逻辑）：
// IndexOfPhase / UpdatePhaseProgress / UpdateTotalProgress / SetPhaseBarIdle
// / AdvancePhase / ResetPhaseBar / SetPhaseBarDone / AppendLog
// v4.17 修订六（C 档）：阶段条由 Label[] 改为自绘 PhaseChipBar，SetPhaseBarIdle / ResetPhaseBar
//   只做"状态翻译"，具体配色下沉到 PhaseChipBar.Apply（视觉改动，行为不变）。
// v4.17：OpenLogFolder 已删除（输出文件夹不再写日志文件，无 logs\ 目录可打开）
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
	/// <summary>初始/空闲：阶段条全部回到"待执行"态。
	/// v4.17 修订六（C 档）：着色下沉到 PhaseChipBar.Apply，本方法不再直接操作控件颜色。</summary>
	private void SetPhaseBarIdle()
	{
		_currentPhase = RunPhase.Prepare;
		Array.Clear(_phaseSkippedFlags, 0, _phaseSkippedFlags.Length);
		Array.Clear(_phaseDonePct, 0, _phaseDonePct.Length);
		if (_progressTotal != null) _progressTotal.Value = 0;
		if (_progressPhase != null) _progressPhase.Value = 0;
		if (_phaseBar != null) _phaseBar.Apply(-1, null);
	}

	/// <summary>v3.9：阶段推进（只前进；skipped 置灰当前阶段并继续，不打断流程顺序）。</summary>
	private void AdvancePhase(RunPhase phase, bool skipped)
	{
		_currentPhase = phase;
		if (skipped && _phaseBar != null)
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

	/// <summary>按 _currentPhase 重着色：之前=已完成，当前=进行中（跳过则转警告色），之后=待执行。
	/// v4.17 修订六（C 档）：着色逻辑整体下沉到 PhaseChipBar.Apply——本方法只负责把
	/// <c>_currentPhase</c> 翻译成"当前阶段下标"（RunPhase.Done 等非阶段枚举值 → 全部落定为已完成）。</summary>
	private void ResetPhaseBar()
	{
		if (_phaseBar == null) return;
		int index = IndexOfPhase(_currentPhase);
		if (index < 0) index = PhaseOrder.Length;   // Done 等非阶段值：全部落定为已完成
		_phaseBar.Apply(index, _phaseSkippedFlags);
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

}
