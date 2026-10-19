using System;
using System.Drawing;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

/// <summary>
/// v4.17 修订六（C 档·§5.4）· 三栏列表的栏标题：状态色圆点 + 标题文字 + 计数徽标（胶囊）。
/// 取代原先"一个 AutoSize Label + 右侧按钮"的写法——圆点与徽标让"这栏是什么状态、有几条"
/// 一眼可读，不再需要读文字。
/// 自绘控件四条纪律：
///   ① 与旧 Label 同名同义（<see cref="Title"/> ≈ Label.Text）；
///   ② 显式 SetStyle（见构造函数）；
///   ③ 一切绘制尺寸走 UiTheme.Dpi；
///   ④ 右对齐的"合并…"按钮仍可用 Dock=Right 正常停靠在本 Panel 内。
/// </summary>
internal sealed class ColumnHeader : Panel
{
	private string _title = string.Empty;
	private Color _dotColor = UiTheme.TextDisabled;
	private int _count = -1;
	private Color _badgeBack = UiTheme.Track;
	private Color _badgeFore = UiTheme.TextMuted;

	private const int HeightLogical = 24;
	private const int DotLogical = 8;
	private const int DotLeftLogical = 2;
	private const int GapLogical = 6;
	private const int BadgePadXLogical = 7;
	private const int BadgeHeightLogical = 16;
	private const int BadgeMaxShown = 999;

	public ColumnHeader()
	{
		// 四条纪律②：自绘控件必须显式开这组样式
		SetStyle(ControlStyles.UserPaint
			| ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw
			| ControlStyles.SupportsTransparentBackColor, true);
		BackColor = Color.Transparent;
		Font = UiTheme.FontSmall;
		Height = HeightLogical;
	}

	/// <summary>栏名（≈ 旧 Label.Text）。</summary>
	public string Title
	{
		get { return _title; }
		set
		{
			string v = value ?? string.Empty;
			if (v == _title) return;
			_title = v;
			Invalidate();
		}
	}

	/// <summary>状态色圆点：待处理=灰 / 成功=绿 / 失败=红。</summary>
	public Color DotColor
	{
		get { return _dotColor; }
		set { if (value == _dotColor) return; _dotColor = value; Invalidate(); }
	}

	/// <summary>计数徽标数值；&lt; 0 表示不显示徽标。</summary>
	public int Count
	{
		get { return _count; }
		set { if (value == _count) return; _count = value; Invalidate(); }
	}

	/// <summary>徽标底色（成功=浅绿 / 失败=浅红 / 待处理=轨道灰）。</summary>
	public Color BadgeBack
	{
		get { return _badgeBack; }
		set { if (value == _badgeBack) return; _badgeBack = value; Invalidate(); }
	}

	/// <summary>徽标文字色（成功=深绿 / 失败=深红 / 待处理=次要文字色）。</summary>
	public Color BadgeFore
	{
		get { return _badgeFore; }
		set { if (value == _badgeFore) return; _badgeFore = value; Invalidate(); }
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		using (var brush = new SolidBrush(UiTheme.EffectiveBackground(this)))
		{
			e.Graphics.FillRectangle(brush, ClientRectangle);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		int h = ClientSize.Height;

		// 状态色圆点
		int d = UiTheme.Dpi(this, DotLogical);
		int x = UiTheme.Dpi(this, DotLeftLogical);
		int cy = h / 2;
		using (var brush = new SolidBrush(_dotColor))
		{
			g.FillEllipse(brush, x, cy - d / 2, d, d);
		}

		// 栏名（圆点右侧）
		int tx = x + d + UiTheme.Dpi(this, GapLogical);
		Size ts = TextRenderer.MeasureText(_title, Font, Size.Empty, TextFormatFlags.NoPadding);
		int ty = (h - ts.Height) / 2;
		TextRenderer.DrawText(g, _title, Font, new Point(tx, ty), UiTheme.Text,
			TextFormatFlags.NoPadding);

		// 计数徽标（栏名右侧的胶囊）
		if (_count < 0) return;
		string s = _count > BadgeMaxShown ? BadgeMaxShown + "+" : _count.ToString();
		Size bs = TextRenderer.MeasureText(s, Font, Size.Empty, TextFormatFlags.NoPadding);
		int padX = UiTheme.Dpi(this, BadgePadXLogical);
		int bw = bs.Width + padX * 2;
		int bh = UiTheme.Dpi(this, BadgeHeightLogical);
		int bx = tx + ts.Width + UiTheme.Dpi(this, GapLogical);
		int by = (h - bh) / 2;
		if (bx + bw > ClientSize.Width) return; // 空间不足则不画，避免压到右侧按钮

		using (var path = UiTheme.RoundedRect(new Rectangle(bx, by, bw, bh), bh))
		using (var brush = new SolidBrush(_badgeBack))
		{
			g.FillPath(brush, path);
		}
		TextRenderer.DrawText(g, s, Font, new Rectangle(bx, by, bw, bh), _badgeFore,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}
}
