using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

/// <summary>
/// v4.17 修订六（C 档）· 界面设计令牌：色板 / 间距 / 圆角 / 行高 / 字体 / DPI 换算。
/// 目的：把原先散落在 MainForm.cs、SettingsForm.cs 里的 Color.FromArgb / new Font(...) 常量
/// 集中到一处——改一处即全局生效，也便于审查"有没有冒出规范外的颜色"。
/// 约束：
///   ① 只做视觉，不改变任何出图 / 识别 / 输出行为；
///   ② 全部为浅色主题；正文对比度 ≥ 4.5:1（设计稿 §三 已逐色核算）；
///   ③ 字体只有 Regular 与 Bold 两档——WinForms 的 <see cref="Font"/> 不支持 500 字重，
///      设计稿的"400 / 500 两档"在此落地为"Regular / Bold 两档"，不出现其它字重。
/// 设计依据：`_测试输出\UI评审\UI优化设计稿-C档.md`（用户 2026-10-15 审批通过）。
/// </summary>
internal static class UiTheme
{
	// ============================================================
	//  一、色板（设计稿 §三）
	// ============================================================

	/// <summary>主色：主按钮底 / 进行中阶段胶囊 / 总进度条填充。白字对比度 7.4:1。</summary>
	public static readonly Color Primary = Rgb(0x18, 0x5F, 0xA5);
	/// <summary>主色浅：选中行底 / 信息条底（配 <see cref="PrimaryDark"/> 文字）。</summary>
	public static readonly Color PrimaryLight = Rgb(0xE6, 0xF1, 0xFB);
	/// <summary>主色深：<see cref="PrimaryLight"/> 底色上的文字色。</summary>
	public static readonly Color PrimaryDark = Rgb(0x0C, 0x44, 0x7C);
	/// <summary>浅主色：当前阶段进度条的填充——与总进度条的主色形成"一眼可分"的语义差异。</summary>
	public static readonly Color PrimarySoft = Rgb(0x85, 0xB7, 0xEB);

	/// <summary>成功色：成功计数 / 已完成阶段描边。</summary>
	public static readonly Color Success = Rgb(0x63, 0x99, 0x22);
	/// <summary>成功浅底：已完成阶段胶囊底。</summary>
	public static readonly Color SuccessLight = Rgb(0xEA, 0xF3, 0xDE);
	/// <summary>成功深字：<see cref="SuccessLight"/> 之上。</summary>
	public static readonly Color SuccessDark = Rgb(0x27, 0x50, 0x0A);

	/// <summary>失败色：失败计数 / 失败阶段描边。</summary>
	public static readonly Color Danger = Rgb(0xE2, 0x4B, 0x4A);
	/// <summary>失败浅底：失败行 / 失败阶段胶囊底。</summary>
	public static readonly Color DangerLight = Rgb(0xFC, 0xEB, 0xEB);
	/// <summary>失败行选中底（比 <see cref="DangerLight"/> 略深，用于列表选中态）。</summary>
	public static readonly Color DangerLightSelected = Rgb(0xF8, 0xD4, 0xD2);
	/// <summary>失败深字：<see cref="DangerLight"/> 之上。</summary>
	public static readonly Color DangerDark = Rgb(0x79, 0x1F, 0x1F);

	/// <summary>警告色：跳过 / 需注意。</summary>
	public static readonly Color Warning = Rgb(0xBA, 0x75, 0x17);

	/// <summary>正文色（对比度 14.6:1）。</summary>
	public static readonly Color Text = Rgb(0x2C, 0x2C, 0x2A);
	/// <summary>次要文字（对比度 6.4:1）——取代 <c>Color.DimGray</c> / <c>Color.Gray</c>（仅 3.6:1）。</summary>
	public static readonly Color TextMuted = Rgb(0x5F, 0x5E, 0x5A);
	/// <summary>禁用态文字。</summary>
	public static readonly Color TextDisabled = Rgb(0x9A, 0x99, 0x94);

	/// <summary>统一描边色（0.5px 细线）——取代 <c>SystemColors.ControlDark</c>。</summary>
	public static readonly Color Border = Rgb(0xD3, 0xD1, 0xC7);
	/// <summary>页面底色（卡片之间的间隙）。</summary>
	public static readonly Color PageBackground = Rgb(0xF5, 0xF5, 0xF3);
	/// <summary>卡片底色。</summary>
	public static readonly Color CardBackground = Color.White;
	/// <summary>只读输入框底色（比卡片略深，表达"不可编辑"）。</summary>
	public static readonly Color ReadOnlyBackground = Rgb(0xF7, 0xF7, 0xF7);
	/// <summary>进度条轨道底色。</summary>
	public static readonly Color Track = Rgb(0xF1, 0xEF, 0xE8);
	/// <summary>日志面板底色。</summary>
	public static readonly Color LogBackground = Rgb(0xFA, 0xFA, 0xF8);

	// ============================================================
	//  二、间距（一律取 4 的倍数，设计稿 §四）
	// ============================================================
	public const int Space1 = 4;
	public const int Space2 = 8;
	public const int Space3 = 12;
	public const int Space4 = 16;
	public const int Space5 = 24;

	// ============================================================
	//  三、圆角（设计稿 §四）
	// ============================================================
	/// <summary>卡片圆角（逻辑像素）。</summary>
	public const int RadiusCard = 12;
	/// <summary>控件圆角（按钮 / 输入框 / 下拉框 / 徽标）。</summary>
	public const int RadiusControl = 6;

	// ============================================================
	//  四、行高（设计稿 §四）
	// ============================================================
	/// <summary>输入行高。</summary>
	public const int HeightInput = 28;
	/// <summary>按钮行高。</summary>
	public const int HeightButton = 30;
	/// <summary>列表行高。</summary>
	public const int HeightListRow = 32;
	/// <summary>进度条轨道高度（胶囊高）。</summary>
	public const int HeightProgressTrack = 10;
	/// <summary>阶段胶囊条高度。</summary>
	public const int HeightPhaseChip = 26;

	// ============================================================
	//  五、字体
	// ============================================================
	private const string UiFamily = "Microsoft YaHei UI";
	private const string MonoFamily = "Consolas";

	/// <summary>正文（约 13px）。</summary>
	public static readonly Font FontBody = new Font(UiFamily, 9f);
	/// <summary>分区标题 / 辅助文字（约 12px）——与正文同族同字号，靠颜色与容器区分层级。</summary>
	public static readonly Font FontSmall = new Font(UiFamily, 8.5f);
	/// <summary>主按钮（强调）。</summary>
	public static readonly Font FontButtonPrimary = new Font(UiFamily, 9f, FontStyle.Bold);
	/// <summary>日志等宽字体。</summary>
	public static readonly Font FontMono = new Font(MonoFamily, 9f);

	// ============================================================
	//  六、DPI 换算
	// ============================================================

	/// <summary>
	/// 把逻辑像素（96 DPI 下的设计值）换算为该控件当前的物理像素。
	/// 自绘控件内部的一切尺寸/描边都必须走这里，否则 125% / 150% 下会出现半像素描边、
	/// 文字截断、圆角失真（手册 5.8 的高 DPI 教训）。
	/// 注意：本工程已声明 dpiAwareness="PerMonitorV2"，且 WinForms 会对整个窗体做等比缩放，
	/// 因此控件的 ClientSize 本身已是物理像素；本方法用于**控件内部**的绘制尺寸。
	/// </summary>
	public static int Dpi(Control control, int logicalPx)
	{
		return (int)Math.Round(DpiF(control, logicalPx));
	}

	/// <summary>浮点版 DPI 换算——用于描边宽度等不足 1px 的场合。</summary>
	public static float DpiF(Control control, float logicalPx)
	{
		int dpi = 96;
		try
		{
			if (control != null) dpi = control.DeviceDpi;
		}
		catch
		{
			// 句柄未创建等极端情况下退回 96 DPI，宁可少缩放也不要抛异常
		}
		if (dpi <= 0) dpi = 96;
		return logicalPx * dpi / 96f;
	}

	/// <summary>取控件实际生效的背景色：沿父链上溯，跳过 Transparent，找不到则退回系统面色。
	/// 自绘控件用它填底，从而在"分组框内 / 白卡片内"两种容器下都自动贴合。</summary>
	public static Color EffectiveBackground(Control control)
	{
		Control c = control;
		while (c != null)
		{
			Color bg = c.BackColor;
			if (bg != Color.Transparent && bg != Color.Empty) return bg;
			c = c.Parent;
		}
		return SystemColors.Control;
	}

	/// <summary>构造圆角矩形路径。半径自动收敛到"不超过短边的一半"，因此
	/// 传 radius = 高度即可得到全圆角（胶囊/跑道形）。</summary>
	public static GraphicsPath RoundedRect(Rectangle r, int radius)
	{
		GraphicsPath path = new GraphicsPath();
		int d = radius * 2;
		if (d > r.Width) d = r.Width;
		if (d > r.Height) d = r.Height;
		if (d <= 1 || r.Width <= 0 || r.Height <= 0)
		{
			if (r.Width > 0 && r.Height > 0) path.AddRectangle(r);
			return path;
		}
		path.AddArc(r.X, r.Y, d, d, 180f, 90f);
		path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
		path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
		path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
		path.CloseFigure();
		return path;
	}

	private static Color Rgb(int r, int g, int b)
	{
		return Color.FromArgb(r, g, b);
	}
}
