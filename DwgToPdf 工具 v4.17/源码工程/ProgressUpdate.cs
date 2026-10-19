namespace DwgToPdf.Batch;

/// <summary>v3.9：转换阶段总览。固定 5 段：准备 → 图框识别 → 页面设置 → 出图 → 收尾。
/// Done 仅在整批流程结束时由 UI 侧标记（阶段条全部落定），引擎不发。</summary>
public enum RunPhase
{
	Prepare,
	FrameExtract,
	PageSetup,
	Plot,
	Finalize,
	Done
}

public sealed class ProgressUpdate
{
	public int Index;

	public int Total;

	public string FileName;

	public ItemStatus Status;

	public string Note;

	public bool Booting;

	/// <summary>v3.9：当前所处阶段（驱动界面阶段条）。</summary>
	public RunPhase Phase;

	/// <summary>v3.9：本阶段被跳过（缓存全命中 / 整批回退 COM），阶段条显示"跳过"。</summary>
	public bool PhaseSkipped;

	/// <summary>v3.9（双进度条）：当前阶段内已完成数（识别 x/y、设置页面 x/y、出图 x/y）。0 表示未提供。</summary>
	public int PhaseIndex;

	/// <summary>v3.9（双进度条）：当前阶段内总数。0 表示未提供。</summary>
	public int PhaseTotal;

	/// <summary>v3.9：日志行（界面日志面板实时镜像；null=非日志消息）。</summary>
	public string LogLine;
}
