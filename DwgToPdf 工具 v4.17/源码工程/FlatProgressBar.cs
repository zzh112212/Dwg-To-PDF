using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

/// <summary>
/// v4.17 修订六（C 档）· 扁平圆角进度条（自绘），替代系统经典绿块 <see cref="ProgressBar"/>。
/// 与 ProgressBar 的三点差异（设计稿 §5.2）：
///   ① 轨道 10px 高、全圆角胶囊形，底色 <see cref="UiTheme.Track"/>，不再是系统凹陷绿块；
///   ② 填充色可指定——总进度用主色 <see cref="UiTheme.Primary"/>、当前阶段用浅主色
///      <see cref="UiTheme.PrimarySoft"/>，两条进度条"一眼可分"（原实现两条长得完全一样）；
///   ③ 右侧固定 34px 显示百分比数字（原实现没有任何数字，用户看不出具体进度）。
/// 兼容性：<see cref="Minimum"/> / <see cref="Maximum"/> / <see cref="Value"/> 的语义与
/// <see cref="ProgressBar"/> 完全一致，因此 MainForm 里既有的 13 处
/// <c>_progressTotal.Value = ...</c> 调用**一行都不用改**。
/// 约束：内部尺寸一律经 <see cref="UiTheme.Dpi"/> 换算（手册 5.8 的高 DPI 教训）。
/// </summary>
internal sealed class FlatProgressBar : Control
{
	private int _minimum;
	private int _maximum = 100;
	private int _value;

	/// <summary>右侧百分比数字区宽度（逻辑像素）。</summary>
	private const int PercentWidthLogical = 34;

	private Color _barColor = UiTheme.Primary;

	public FlatProgressBar()
	{
		// 自绘控件必须自己处理擦除与绘制；双缓冲消除刷新闪烁
		SetStyle(ControlStyles.UserPaint
			| ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw
			| ControlStyles.SupportsTransparentBackColor, true);
		BackColor = Color.Transparent;
		Font = UiTheme.FontSmall;
	}

	/// <summary>填充色。总进度条用 <see cref="UiTheme.Primary"/>，阶段进度条用 <see cref="UiTheme.PrimarySoft"/>。</summary>
	[DefaultValue(typeof(Color), "24, 95, 165")]
	public Color BarColor
	{
		get { return _barColor; }
		set
		{
			if (_barColor == value) return;
			_barColor = value;
			Invalidate();
		}
	}

	[DefaultValue(0)]
	public int Minimum
	{
		get { return _minimum; }
		set
		{
			if (_minimum == value) return;
			_minimum = value;
			if (_maximum < _minimum) _maximum = _minimum;
			if (_value < _minimum) _value = _minimum;
			Invalidate();
		}
	}

	[DefaultValue(100)]
	public int Maximum
	{
		get { return _maximum; }
		set
		{
			int v = value < _minimum ? _minimum : value;
			if (_maximum == v) return;
			_maximum = v;
			if (_value > _maximum) _value = _maximum;
			Invalidate();
		}
	}

	[DefaultValue(0)]
	public int Value
	{
		get { return _value; }
		set
		{
			int v = value;
			if (v < _minimum) v = _minimum;
			if (v > _maximum) v = _maximum;
			if (v == _value) return;
			_value = v;
			Invalidate();
		}
	}

	/// <summary>当前百分比（0..100 取整）。</summary>
	public int Percent
	{
		get
		{
			if (_maximum <= _minimum) return 0;
			double pct = (_value - _minimum) * 100.0 / (_maximum - _minimum);
			return (int)Math.Round(pct, MidpointRounding.AwayFromZero);
		}
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

		int pctWidth = UiTheme.Dpi(this, PercentWidthLogical);
		int trackHeight = Math.Max(2, UiTheme.Dpi(this, UiTheme.HeightProgressTrack));

		// 垂直居中（控件被拉高时轨道仍居中，而不是贴顶）
		int top = (ClientSize.Height - trackHeight) / 2;
		if (top < 0) top = 0;

		int trackWidth = ClientSize.Width - pctWidth - UiTheme.Dpi(this, UiTheme.Space1);
		if (trackWidth < 2) trackWidth = 2;

		Rectangle track = new Rectangle(0, top, trackWidth, trackHeight);

		using (GraphicsPath path = UiTheme.RoundedRect(track, trackHeight))
		using (SolidBrush br = new SolidBrush(UiTheme.Track))
		{
			g.FillPath(br, path);
		}

		int percent = Percent;
		if (percent > 0)
		{
			int fillWidth = (int)Math.Round(trackWidth * percent / 100.0);
			// 哪怕 1% 也要画出一个完整的圆头，否则看起来像"没开始"
			if (fillWidth < trackHeight) fillWidth = Math.Min(trackHeight, trackWidth);
			Rectangle fill = new Rectangle(0, top, fillWidth, trackHeight);
			using (GraphicsPath path = UiTheme.RoundedRect(fill, trackHeight))
			using (SolidBrush br = new SolidBrush(_barColor))
			{
				g.FillPath(br, path);
			}
		}

		// 右侧百分比：右对齐 + 垂直居中
		Rectangle percentRect = new Rectangle(ClientSize.Width - pctWidth, 0, pctWidth, ClientSize.Height);
		TextRenderer.DrawText(g, percent + "%", Font, percentRect, UiTheme.TextMuted,
			TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}
}
