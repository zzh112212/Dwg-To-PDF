using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

/// <summary>
/// v4.17 修订六（C 档）· 自绘卡片容器（设计稿 §5.1）。
/// 取代主窗口里"凹陷细边框 + 独立标题槽"的 GroupBox：白底 + 12px 圆角 + 0.5px 统一描边，
/// 标题以 12px 次要文字色画在卡片内左上（不再单占一条标题槽）。
///
/// 与 GroupBox 的语义对齐（自绘控件"四条纪律"之一：新控件必须同名同义实现旧控件的关键属性）：
///   · <see cref="Title"/> ≈ GroupBox.Text；
///   · <see cref="Padding"/> 生效；
///   · <see cref="DisplayRectangle"/> 把 Dock=Fill 的子内容自动推到标题下方——调用方
///     只需把原有 tblXxx(Dock=Fill) 照旧 Add 进来即可，无需自己算标题高度。
///
/// 层级：<see cref="Subtle"/>=true 用于列表 / 日志等次级区域（圆角收敛到 8px），
/// 视觉权重低于主流程卡片，但不换控件、不换配色，避免"两套卡片长得不一样"。
/// </summary>
internal sealed class CardPanel : Panel
{
	private string _title = string.Empty;
	private bool _subtle;

	// 标题区的逻辑尺寸（96 DPI 下的设计值，绘制时经 UiTheme.Dpi 换算）。
	// TitleHeightLogical 取 16，与 GroupBox 标题槽高度一致——这样把 GroupBox 换成 CardPanel 时
	// 各卡片 Height 可保持原值、内容区高度不变（调用方零改动，见手册 5.11 纪律①）。
	private const int TitleHeightLogical = 16;
	private const int TitlePadLeftLogical = 12;
	private const int TitlePadTopLogical = 4;
	private const int SubtleRadiusLogical = 8;

	public CardPanel()
	{
		// 四条纪律②：自绘控件必须显式开这组样式，否则重绘闪烁 / 背景不透明
		SetStyle(ControlStyles.UserPaint
			| ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw
			| ControlStyles.SupportsTransparentBackColor, true);
		// 必须先开 SupportsTransparentBackColor 再设 Transparent——顺序反了会被重置为不透明
		BackColor = Color.Transparent;
		Font = UiTheme.FontBody;
		// 默认内边距：左右 12（设计稿 §四），上下配合标题高度取小值
		Padding = new Padding(12, 4, 12, 6);
	}

	/// <summary>卡片标题（≈ GroupBox.Text）。为空时不占标题区。</summary>
	public string Title
	{
		get { return _title; }
		set
		{
			string v = value ?? string.Empty;
			if (v == _title) return;
			_title = v;
			PerformLayout(); // 标题高度变化 → Dock=Fill 子内容区随之变化
			Invalidate();
		}
	}

	/// <summary>次级卡片（列表 / 日志）：圆角收敛到 8px，视觉权重低于主流程卡片。</summary>
	public bool Subtle
	{
		get { return _subtle; }
		set
		{
			if (value == _subtle) return;
			_subtle = value;
			Invalidate();
		}
	}

	/// <summary>标题占用高度（物理像素；无标题为 0）。</summary>
	private int TitleHeight
	{
		get { return string.IsNullOrEmpty(_title) ? 0 : UiTheme.Dpi(this, TitleHeightLogical); }
	}

	/// <summary>
	/// 复刻 GroupBox 的语义：子内容可用矩形 = 客户区减去 Padding，再整体下移标题高度。
	/// 这样"把原 GroupBox 内的 tblXxx(Dock=Fill) 原样搬过来"就能正确落在标题下方。
	/// </summary>
	public override Rectangle DisplayRectangle
	{
		get
		{
			Rectangle r = ClientRectangle;
			int top = TitleHeight + Padding.Top;
			int w = r.Width - Padding.Left - Padding.Right;
			int h = r.Height - top - Padding.Bottom;
			if (w < 0) w = 0;
			if (h < 0) h = 0;
			return new Rectangle(r.X + Padding.Left, r.Y + top, w, h);
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		// 用父链上第一个非透明底色填底：卡片圆角之外的四个角因此与页面底色一致
		using (var brush = new SolidBrush(UiTheme.EffectiveBackground(this)))
		{
			e.Graphics.FillRectangle(brush, ClientRectangle);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;

		// 描边 0.5px，内缩半像素绘制，避免被客户区边缘裁掉一半（高 DPI 下更明显）
		float inset = UiTheme.DpiF(this, 0.5f);
		RectangleF rf = new RectangleF(
			inset, inset,
			ClientRectangle.Width - inset * 2f,
			ClientRectangle.Height - inset * 2f);
		if (rf.Width <= 1f || rf.Height <= 1f) return;

		int radius = UiTheme.Dpi(this, _subtle ? SubtleRadiusLogical : UiTheme.RadiusCard);
		using (GraphicsPath path = UiTheme.RoundedRect(Rectangle.Round(rf), radius))
		{
			using (var fill = new SolidBrush(UiTheme.CardBackground))
			{
				g.FillPath(fill, path);
			}
			using (var pen = new Pen(UiTheme.Border, UiTheme.DpiF(this, 0.5f)))
			{
				g.DrawPath(pen, path);
			}
		}

		if (!string.IsNullOrEmpty(_title))
		{
			// 文字走 Default（非 AA）渲染，避免小字号发虚
			g.SmoothingMode = SmoothingMode.Default;
			using (var brush = new SolidBrush(UiTheme.TextMuted))
			{
				float x = UiTheme.DpiF(this, TitlePadLeftLogical);
				float y = UiTheme.DpiF(this, TitlePadTopLogical);
				g.DrawString(_title, UiTheme.FontSmall, brush, x, y);
			}
		}
	}
}
