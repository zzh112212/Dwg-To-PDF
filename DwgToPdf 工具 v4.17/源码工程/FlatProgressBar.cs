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
///
/// v4.17 修订六·补（2026-10-15，用户验收反馈）：**补上系统进度条自带的平滑动画**。
/// 用户原话："进度条不是流动状态，而是突然变化。分支前的就是流动的。"
/// 根因：`msctls_progress32` 在 SetPos 时会自己做一段过渡动画（旧→新位置滑动），
/// 换成本自绘控件后是"立刻跳到新值"，于是几十秒才变一次的粗粒度更新显得格外生硬。
/// 对策：保留 <see cref="Value"/> 作为**目标值**语义不变，另加一个只用于绘制的
/// <see cref="_displayValue"/>，用 UI 定时器做 ease-out 缓动逼近目标；
/// 百分比数字仍显示**目标值**（真实进度），只有填充长度在滑动。
/// </summary>
internal sealed class FlatProgressBar : Control
{
	private int _minimum;
	private int _maximum = 100;
	private int _value;

	// ---- 平滑动画（v4.17 修订六·补）----
	/// <summary>动画帧间隔（毫秒）。15ms ≈ 66fps，肉眼连续且开销可忽略。</summary>
	private const int AnimTickMs = 15;
	private readonly Timer _animTimer;
	/// <summary>当前**绘制用**的值（缓动中间值）。<see cref="_value"/> 是目标值。</summary>
	private double _displayValue;
	private double _animFrom;
	private double _animTo;
	private int _animStartTick;
	private int _animDurationMs;

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

		_animTimer = new Timer { Interval = AnimTickMs };
		_animTimer.Tick += OnAnimTick;
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
			if (_displayValue < _minimum) _displayValue = _minimum;
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
			if (_displayValue > _maximum) _displayValue = _maximum;
			Invalidate();
		}
	}

	/// <summary>目标值（语义与 <see cref="ProgressBar.Value"/> 一致）。
	/// 设置后填充会平滑滑向新值，而不是瞬间跳变。</summary>
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
			StartAnimation();
		}
	}

	/// <summary>当前百分比（0..100 取整，基于**目标值**——百分比数字显示真实进度，不跟着动画滞后）。</summary>
	public int Percent
	{
		get
		{
			if (_maximum <= _minimum) return 0;
			double pct = (_value - _minimum) * 100.0 / (_maximum - _minimum);
			return (int)Math.Round(pct, MidpointRounding.AwayFromZero);
		}
	}

	/// <summary>绘制用百分比（0..100，基于缓动中的显示值）。</summary>
	private double DisplayPercent
	{
		get
		{
			if (_maximum <= _minimum) return 0;
			double v = _displayValue;
			if (v < _minimum) v = _minimum;
			if (v > _maximum) v = _maximum;
			return (v - _minimum) * 100.0 / (_maximum - _minimum);
		}
	}

	// ================= 动画 =================

	/// <summary>启动一次"从当前显示值滑向目标值"的缓动。
	/// 句柄未创建或不在 UI 线程时直接落定——避免在错误线程上操作 Timer。</summary>
	private void StartAnimation()
	{
		if (!IsHandleCreated || InvokeRequired)
		{
			_displayValue = _value;
			return;
		}

		_animFrom = _displayValue;
		_animTo = _value;
		double delta = Math.Abs(_animTo - _animFrom);
		if (delta < 0.001)
		{
			_displayValue = _animTo;
			_animTimer.Stop();
			Invalidate();
			return;
		}

		// 位移越大滑得越久；夹在 140~420ms —— 有"流动感"又不拖沓（贴近系统进度条的观感）
		_animDurationMs = (int)Math.Min(420.0, Math.Max(140.0, 120.0 + delta * 2.5));
		_animStartTick = Environment.TickCount;
		_animTimer.Stop();
		_animTimer.Start();
	}

	private void OnAnimTick(object sender, EventArgs e)
	{
		int elapsed = unchecked(Environment.TickCount - _animStartTick);
		double p = _animDurationMs <= 0 ? 1.0 : (double)elapsed / _animDurationMs;
		if (p >= 1.0)
		{
			_displayValue = _animTo;
			_animTimer.Stop();
		}
		else
		{
			double eased = 1.0 - Math.Pow(1.0 - p, 3.0);   // ease-out cubic：快起慢收
			_displayValue = _animFrom + (_animTo - _animFrom) * eased;
		}
		Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _animTimer != null)
		{
			_animTimer.Stop();
			_animTimer.Tick -= OnAnimTick;
			_animTimer.Dispose();
		}
		base.Dispose(disposing);
	}

	// ================= 绘制 =================

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

		double displayPct = DisplayPercent;
		if (displayPct > 0.05)
		{
			int fillWidth = (int)Math.Round(trackWidth * displayPct / 100.0);
			// 哪怕不到 1% 也要画出一个完整的圆头，否则看起来像"没开始"
			if (fillWidth < trackHeight) fillWidth = Math.Min(trackHeight, trackWidth);
			if (fillWidth > trackWidth) fillWidth = trackWidth;
			Rectangle fill = new Rectangle(0, top, fillWidth, trackHeight);
			using (GraphicsPath path = UiTheme.RoundedRect(fill, trackHeight))
			using (SolidBrush br = new SolidBrush(_barColor))
			{
				g.FillPath(br, path);
			}
		}

		// 右侧百分比：右对齐 + 垂直居中（显示目标值 = 真实进度）
		Rectangle percentRect = new Rectangle(ClientSize.Width - pctWidth, 0, pctWidth, ClientSize.Height);
		TextRenderer.DrawText(g, Percent + "%", Font, percentRect, UiTheme.TextMuted,
			TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}
}
