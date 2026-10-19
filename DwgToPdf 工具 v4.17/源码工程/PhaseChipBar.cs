using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

/// <summary>阶段胶囊的五种视觉状态。
/// 设计稿 §5.3 定义了前四种；<see cref="Skipped"/> 是本工程领域里真实存在的第五种
/// （阶段被跳过，见 <c>MainForm._phaseSkippedFlags</c>），用警告色系表达，
/// 从而把原来"跳过"与"未开始"都用灰底区分不开的问题一并解决。</summary>
internal enum PhaseChipState
{
	/// <summary>待执行：白底 + 灰描边 + 次要文字色。</summary>
	Pending,
	/// <summary>进行中：实心主色 + 白字。</summary>
	Active,
	/// <summary>已完成：浅绿底 + 深绿字 + ✓。</summary>
	Done,
	/// <summary>失败：浅红底 + 深红字 + ×。（预留：当前引擎不单独上报阶段失败，C2/C3 需要时启用）</summary>
	Failed,
	/// <summary>已跳过：白底 + 警告色描边与文字 + "（跳过）"。</summary>
	Skipped
}

/// <summary>
/// v4.17 修订六（C 档）· 阶段总览条（自绘胶囊 + 连接线），替代原来 5 个
/// "白底 + FixedSingle 边框 + 灰字"的 <see cref="Label"/>——那 5 个方框看起来像 5 个禁用输入框，
/// 完全读不出"流程进度"的语义（设计稿 §5.3）。
/// 视觉契约：
///   · 待执行 = 白底 + <see cref="UiTheme.Border"/> 描边 + <see cref="UiTheme.TextMuted"/> 文字；
///   · 进行中 = 实心 <see cref="UiTheme.Primary"/> + 白字 + 加粗；
///   · 已完成 = <see cref="UiTheme.SuccessLight"/> 底 + <see cref="UiTheme.SuccessDark"/> 文字 + ✓；
///   · 失败   = <see cref="UiTheme.DangerLight"/> 底 + <see cref="UiTheme.DangerDark"/> 文字 + ×；
///   · 已跳过 = 白底 + <see cref="UiTheme.Warning"/> 描边与文字 + "（跳过）"；
///   · 胶囊之间的连接线颜色跟随**左端**胶囊（已完成=绿 / 进行中=蓝 / 其余=灰）。
/// 约束：内部尺寸一律经 <see cref="UiTheme.Dpi"/> 换算（手册 5.8 的高 DPI 教训）。
/// </summary>
internal sealed class PhaseChipBar : Control
{
	private readonly string[] _names;
	private readonly string[] _tooltips;
	private readonly PhaseChipState[] _states;
	private readonly ToolTip _tip = new ToolTip();
	private int _hoverIndex = -1;

	/// <summary>胶囊之间的连接线占位宽度（逻辑像素）。</summary>
	private const int GapLogical = 16;

	public PhaseChipBar(string[] names, string[] tooltips)
	{
		_names = names ?? new string[0];
		_tooltips = tooltips;
		_states = new PhaseChipState[_names.Length];
		for (int i = 0; i < _states.Length; i++) _states[i] = PhaseChipState.Pending;

		SetStyle(ControlStyles.UserPaint
			| ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw
			| ControlStyles.SupportsTransparentBackColor, true);
		BackColor = Color.Transparent;
		Font = UiTheme.FontSmall;
		Height = UiTheme.HeightPhaseChip;
	}

	/// <summary>胶囊数量（= 阶段数）。</summary>
	public int Count { get { return _names.Length; } }

	/// <summary>读取某一段的状态（供自测断言使用）。</summary>
	public PhaseChipState StateAt(int index)
	{
		if (index < 0 || index >= _states.Length) return PhaseChipState.Pending;
		return _states[index];
	}

	/// <summary>
	/// 按"当前阶段下标 + 跳过标记"整体重着色。语义与改造前的 ResetPhaseBar 完全一致：
	///   · activeIndex &lt; 0            → 全部待执行（空闲态）；
	///   · activeIndex &gt;= Count       → 全部已完成（转换结束）；
	///   · 其余                        → 下标小于 activeIndex 的为已完成，
	///                                   下标等于 activeIndex 的为进行中（或跳过），
	///                                   下标大于 activeIndex 的为待执行。
	/// </summary>
	public void Apply(int activeIndex, bool[] skippedFlags)
	{
		for (int i = 0; i < _states.Length; i++)
		{
			PhaseChipState st;
			if (activeIndex < 0) st = PhaseChipState.Pending;
			else if (i < activeIndex) st = PhaseChipState.Done;
			else if (i > activeIndex) st = PhaseChipState.Pending;
			else st = (skippedFlags != null && i < skippedFlags.Length && skippedFlags[i])
				? PhaseChipState.Skipped
				: PhaseChipState.Active;
			_states[i] = st;
		}
		Invalidate();
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
		using (SolidBrush br = new SolidBrush(UiTheme.EffectiveBackground(this)))
		{
			pevent.Graphics.FillRectangle(br, ClientRectangle);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;

		int n = _names.Length;
		if (n == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

		int gap = UiTheme.Dpi(this, GapLogical);
		int available = ClientSize.Width - gap * (n - 1);
		if (available < n) available = n;
		int chipWidth = available / n;
		int chipHeight = ClientSize.Height;
		float stroke = Math.Max(1f, UiTheme.DpiF(this, 1f));

		for (int i = 0; i < n; i++)
		{
			int x = i * (chipWidth + gap);
			Rectangle chip = new Rectangle(x, 0, chipWidth, chipHeight);
			DrawChip(g, chip, _states[i], _names[i], stroke);

			// 连接线（画在相邻两胶囊之间的空隙里）
			if (i >= n - 1) continue;
			int lineWidth = gap - UiTheme.Dpi(this, 4);
			if (lineWidth <= 0) continue;
			int lineX = x + chipWidth + UiTheme.Dpi(this, 2);
			int lineY = chipHeight / 2;
			using (Pen pen = new Pen(ConnectorColor(_states[i]), stroke))
			{
				g.DrawLine(pen, lineX, lineY, lineX + lineWidth, lineY);
			}
		}
	}

	private void DrawChip(Graphics g, Rectangle chip, PhaseChipState state, string name, float stroke)
	{
		Color fill, border, textColor;
		string label = name;
		bool bold = false;
		bool drawBorder = true;

		switch (state)
		{
			case PhaseChipState.Active:
				fill = UiTheme.Primary;
				border = UiTheme.Primary;
				textColor = Color.White;
				bold = true;
				drawBorder = false;
				break;
			case PhaseChipState.Done:
				fill = UiTheme.SuccessLight;
				border = UiTheme.SuccessLight;
				textColor = UiTheme.SuccessDark;
				label = "✓ " + name;
				drawBorder = false;
				break;
			case PhaseChipState.Failed:
				fill = UiTheme.DangerLight;
				border = UiTheme.DangerLight;
				textColor = UiTheme.DangerDark;
				label = "× " + name;
				drawBorder = false;
				break;
			case PhaseChipState.Skipped:
				fill = UiTheme.CardBackground;
				border = UiTheme.Warning;
				textColor = UiTheme.Warning;
				label = name + "（跳过）";
				break;
			default:
				fill = UiTheme.CardBackground;
				border = UiTheme.Border;
				textColor = UiTheme.TextMuted;
				break;
		}

		using (GraphicsPath path = UiTheme.RoundedRect(chip, chip.Height))
		{
			using (SolidBrush br = new SolidBrush(fill)) g.FillPath(br, path);
			if (drawBorder)
			{
				using (Pen pen = new Pen(border, stroke)) g.DrawPath(pen, path);
			}
		}

		Font font = bold ? UiTheme.FontButtonPrimary : Font;
		TextRenderer.DrawText(g, label, font, chip, textColor,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
			| TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
	}

	/// <summary>连接线颜色：跟随左端胶囊——已完成=绿、进行中=蓝、其余=灰。</summary>
	private static Color ConnectorColor(PhaseChipState left)
	{
		if (left == PhaseChipState.Done) return UiTheme.Success;
		if (left == PhaseChipState.Active) return UiTheme.Primary;
		return UiTheme.Border;
	}

	/// <summary>把鼠标横坐标换算为胶囊下标（-1 = 落在空隙或控件之外）。</summary>
	private int HitTestIndex(int x)
	{
		int n = _names.Length;
		if (n == 0) return -1;
		int gap = UiTheme.Dpi(this, GapLogical);
		int available = ClientSize.Width - gap * (n - 1);
		if (available < n) available = n;
		int chipWidth = available / n;
		if (chipWidth <= 0) return -1;
		for (int i = 0; i < n; i++)
		{
			int left = i * (chipWidth + gap);
			if (x >= left && x < left + chipWidth) return i;
		}
		return -1;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		int index = HitTestIndex(e.X);
		if (index == _hoverIndex) return;
		_hoverIndex = index;
		try
		{
			_tip.Hide(this);
			if (index >= 0 && _tooltips != null && index < _tooltips.Length && _tooltips[index] != null)
			{
				_tip.Show(_tooltips[index], this, e.X + UiTheme.Dpi(this, 12), e.Y + UiTheme.Dpi(this, 18), 10000);
			}
		}
		catch
		{
			// ToolTip 在控件尚未可见时可能抛异常，忽略即可（不影响功能）
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		_hoverIndex = -1;
		try { _tip.Hide(this); }
		// best-effort：界面操作失败不影响功能
		catch { }
	}
}
