using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

internal sealed partial class MainForm : Form
{
	private TextBox _txtInput;

	private TextBox _txtOutput;

	private Label _lblPrint;

	private ComboBox _cmbPrint;
	private Label _lblMode;
	private ComboBox _cmbMode;

	// v4.1：第二行设置——显示线宽 + 质量核查
	// v4.3：+ 识别块图框（图框是块引用 INSERT 时的自动识别）
	// （并发设置界面方案已否决：并发参数 v4.0 实测最优 12/4/8，
	//  由 Concurrency.cs 硬编码内置、UI 不可调，无对应控件字段）
	private CheckBox _chkLineWeight;
	private CheckBox _chkQualityCheck;
	private CheckBox _chkBlockFrame;

	private RadioButton _rbMarginOn;

	private RadioButton _rbMarginOff;

	private Button _btnInput;

	private Button _btnInputFolder;

	private Button _btnOutput;

	private Button _btnStart;

	private Button _btnStop;

	// v4.3：设置按钮 + 用户设置（启动时 LoadAppSettings 从 %APPDATA%\DwgToPdf\settings.json 加载）
	private Button _btnSettings;
	private AppSettings _appSettings;

	// v4.17 修订六（C 档）：系统经典绿块 ProgressBar 换为自绘扁平圆角进度条。
	// 两者 Minimum/Maximum/Value 语义一致，故本文件内既有的 Value 赋值点无需改动。
	private FlatProgressBar _progressTotal;

	private FlatProgressBar _progressPhase;

	// v3.9（双进度条）：总进度按阶段权重推进。5 阶段权重（准备/识别/页面设置/出图/收尾），合计 100。
	private static readonly int[] _phaseWeights = { 10, 25, 30, 30, 5 };

	// v3.9（双进度条）：各阶段完成比例 0..1（推进总进度条）。
	private double[] _phaseDonePct = new double[5];

	private Button _btnOpenOut;

	private ToolStripStatusLabel _lblStatus;

	private DataGridView _gridPending;

	private DataGridView _gridSuccess;

	private DataGridView _gridFail;

	// v4.17 修订六（C 档·§5.4）：三栏栏标题由 Label 改为自绘 ColumnHeader（状态色圆点 + 计数徽标）。
	private ColumnHeader _hdrPending;

	private ColumnHeader _hdrSuccess;

	private ColumnHeader _hdrFail;

	// v4.17 修订七（C 档·§5.5）：失败原因内联展开——点失败行的「详情」链接，在失败栏下方展开
	// 只读详情面板（完整原因 + 日志尾部 20 行）。不弹窗（弹窗会打断批量操作节奏）。
	private Panel _failDetailPanel;

	private Label _lblFailDetailTitle;

	private TextBox _txtFailDetail;

	/// <summary>当前展开详情的失败文件路径（null = 未展开）。</summary>
	private string _failDetailPath;

	private SplitContainer _splitTop;

	private SplitContainer _splitBottom;

	// v4.17 修订六（C 档·P3-2）：原先 7 个同权重的 GroupBox（凹陷细边框 + 标题槽）视线无落点，
	// 主流程三卡改为自绘卡片 CardPanel（白底 + 12px 圆角 + 0.5px 描边，标题画在卡内左上）；
	// 列表与日志区降级为 Subtle 卡片（圆角 8px），视觉权重自然降低。
	private CardPanel _cardInput;

	private CardPanel _cardOptions;

	private CardPanel _cardProgress;

	private readonly Dictionary<string, ItemStatus> _statusByPath = new Dictionary<string, ItemStatus>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> _noteByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly List<string> _pendingFiles = new List<string>();

	private bool _converting;

	private Button _btnClear;

	private Button _btnCopyFailed;

	// v4.12：待处理栏的"合并待转换"（把待处理文件复制到一个文件夹，便于后续单独转换）
	private Button _btnCopyPending;

	private const int MaxUndo = 50;

	private readonly List<UndoEntry> _undoStack = new List<UndoEntry>();

	private readonly ConverterEngine _engine = new ConverterEngine();

	private DateTime _runStarted;

	/// <summary>v4.12：是否已经发起过转换。再次点击"开始转换"时置位 → 本轮忽略
	/// 文件大小阈值与单张转换超时，便于直接补转上一轮退回「待处理」的文件。</summary>
	private bool _hasRunBefore;

	private readonly System.Diagnostics.Stopwatch _runWatch = new System.Diagnostics.Stopwatch();

	private System.Windows.Forms.Timer _runTimer;

	private string _baseTitle;

	private int _completed;

	private int _okCount;

	private int _failCount;

	private int _cancelCount;

	// ================= v3.9：阶段条 + 日志面板 =================
	private static readonly RunPhase[] PhaseOrder = new[] { RunPhase.Prepare, RunPhase.FrameExtract, RunPhase.PageSetup, RunPhase.Plot, RunPhase.Finalize };

	private static readonly string[] PhaseNames = new[] { "1 准备", "2 图框识别", "3 页面设置", "4 出图", "5 收尾" };

	private static readonly string[] PhaseTooltips = new[]
	{
		"准备：定位 AutoCAD Core Console、创建输出/日志/临时目录、统计图框缓存命中",
		"图框识别：公司版模板识别 → 通用版补识别 → 失败重试（缓存命中自动跳过）",
		"页面设置：COM 批量给图纸副本设置 Model 页面（纸张/方向/边距/配色）",
		"出图：accoreconsole -EXPORT 副本，失败自动回退 COM 兜底",
		"收尾：清理 Activity Insights 残留进程、副本与临时文件、汇总日志"
	};

	// v4.17 修订六（C 档）：5 个"白底方框 Label"换为自绘胶囊条（PhaseChipBar.cs）。
	private PhaseChipBar _phaseBar;

	private Label _lblPhaseDetail;

	private SplitContainer _splitLog;

	// v4.17 修订六（C 档·§5.1）：列表 / 日志区降级为 Subtle 卡片（CardPanel，圆角 8px）。
	private CardPanel _listCard;

	private CardPanel _logCard;

	private RichTextBox _txtLog;

	// v4.15：日志"自动滚动到底部"开关（默认勾选；取消勾选或向上翻阅即暂停跟随）
	private CheckBox _chkAutoScroll;

	// v4.17 修订六（C 档·§5.1）：日志区改为 Subtle 卡片，标题由 CardPanel 绘制，
	// 原 _lblLogTitle 已删除（同一段文字只留一处，避免"卡片标题 + 面板标题"重复）。

	// v4.17：_btnOpenLog（"打开日志文件夹"）已删除——转换不再在输出文件夹写日志文件，
	// 输出文件夹里不会再有 logs\ 目录（用户要求）；主按钮区已有"打开输出文件夹"。

	private RunPhase _currentPhase = RunPhase.Prepare;

	private readonly bool[] _phaseSkippedFlags = new bool[5];

	private bool _initialLogSplitterSet;
	private bool _initialListSplitterSet;

	/// <summary>主窗体构造函数：设置标题 / 字体 / 底色与默认及最小窗口尺寸，依次完成「加载设置 → 构建界面 → 恢复上次界面状态」，挂接关闭时保存设置，并创建 500ms 的已用时刷新定时器（仅转换进行中更新标题）。</summary>
	public MainForm()
	{
		_baseTitle = "DWG 批量转 PDF（AutoCAD 驱动） " + Program.Version;
		Text = _baseTitle;
		Font = UiTheme.FontBody;
		// v4.17 修订六（C 档·§5.1）：页面底色 #F5F5F3，白卡片之间留 10px 间隙"浮"起来
		BackColor = UiTheme.PageBackground;
		// v4.17 修订四（P1-6）：原来未设最小尺寸，窗口可被拖到布局崩坏（三栏/按钮行挤成一团）。
		// v4.17 修订六（C 档·§5.1）：新增顶部标题条 40 + 卡片间隙 4×10 = 80 逻辑像素，
		// 原 760×680 / 最小 800×660 已装不下（日志卡片被挤出可视区，实测 _splitLog 需 ≥205），
		// 故默认与最小尺寸同步放大。所需客户区高度 = 453(顶部) + 22(状态栏) + 205(列表/日志) = 680。
		base.ClientSize = new Size(900, 760);
		MinimumSize = new Size(840, 730);
		base.StartPosition = FormStartPosition.CenterScreen;
		// v4.3：启动时从 settings.json 加载用户设置
		LoadAppSettings();
		BuildUi();
		// v4.6：恢复上次使用状态（输出文件夹/选项/窗口大小位置）+ 关闭时保存
		ApplyUiState();
		FormClosing += delegate { SaveUiState(); };
		_runTimer = new System.Windows.Forms.Timer
		{
			Interval = 500
		};
		_runTimer.Tick += delegate
		{
			if (_converting)
			{
				Text = _baseTitle + " — 已用时 " + FormatElapsed(_runWatch.Elapsed);
			}
		};
	}

	/// <summary>v4.17 修订七（C 档·§5.6）：Esc → 停止当前转换（仅在转换进行中且尚未下达停止时生效）。</summary>
	private void OnMainFormKeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Escape && _converting && _btnStop != null && _btnStop.Enabled)
		{
			StopRequested();
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
	}

	// ================= v4.17 修订六（C 档）：按钮层级 =================
	/// <summary>主按钮：实心主色底 + 白字 + 加粗。禁用时整体降级为浅主色底 + 灰字，
	/// 避免出现"实心蓝底 + 灰字"这种怪异组合（转换中"开始转换"会被禁用）。</summary>
	private static void ApplyPrimaryButton(Button b)
	{
		b.UseVisualStyleBackColor = false;
		b.FlatStyle = FlatStyle.Flat;
		b.FlatAppearance.BorderSize = 0;
		b.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 80, 139);
		b.FlatAppearance.MouseDownBackColor = UiTheme.PrimaryDark;
		b.Font = UiTheme.FontButtonPrimary;
		b.ForeColor = Color.White;
		b.BackColor = UiTheme.Primary;
		b.EnabledChanged += delegate
		{
			if (b.Enabled)
			{
				b.BackColor = UiTheme.Primary;
				b.ForeColor = Color.White;
			}
			else
			{
				b.BackColor = UiTheme.PrimaryLight;
				b.ForeColor = UiTheme.TextDisabled;
			}
		};
	}

	/// <summary>次按钮：白底 + 统一描边 + 正文色；悬停浅主色底。
	/// 禁用态虽然仍由系统置灰文字，但底色为白、描边可见，"禁用"这件事终于看得出来
	/// （原实现里"打开输出文件夹"的禁用态几乎与可用态无异）。</summary>
	private static void ApplySecondaryButton(Button b)
	{
		b.UseVisualStyleBackColor = false;
		b.FlatStyle = FlatStyle.Flat;
		b.FlatAppearance.BorderSize = 1;
		b.FlatAppearance.BorderColor = UiTheme.Border;
		b.FlatAppearance.MouseOverBackColor = UiTheme.PrimaryLight;
		b.FlatAppearance.MouseDownBackColor = Color.FromArgb(206, 227, 247);
		b.Font = UiTheme.FontBody;
		b.ForeColor = UiTheme.Text;
		b.BackColor = UiTheme.CardBackground;
	}

	// ================= v4.17 修订六（C 档·§5.1）：卡片布局辅助 =================
	/// <summary>卡片之间的 10px 间隙（页面底色）——让白卡片"浮"在 #F5F5F3 上，不用阴影。</summary>
	private static Panel MakeCardGap()
	{
		return new Panel
		{
			Dock = DockStyle.Top,
			Height = 10,
			BackColor = UiTheme.PageBackground
		};
	}

	/// <summary>把"自动滚动"复选框贴到日志卡片标题条右端（位置依赖卡片宽度，Resize 时重算）。</summary>
	private void PlaceLogAutoScroll()
	{
		if (_logCard == null || _chkAutoScroll == null) return;
		int x = _logCard.ClientSize.Width - _chkAutoScroll.Width - UiTheme.Dpi(_logCard, 10);
		if (x < 0) x = 0;
		_chkAutoScroll.Location = new Point(x, 0);
	}

	/// <summary>v4.17 修订六（C 档·§5.4）：把三栏实际条数同步到栏标题的计数徽标。</summary>
	private void UpdateHeaderCounts()
	{
		if (_hdrPending != null && _gridPending != null) _hdrPending.Count = _gridPending.Rows.Count;
		if (_hdrSuccess != null && _gridSuccess != null) _hdrSuccess.Count = _gridSuccess.Rows.Count;
		if (_hdrFail != null && _gridFail != null) _hdrFail.Count = _gridFail.Rows.Count;
	}

	/// <summary>v3.4：Dock 布局已接管尺寸；此方法仅在窗口缩放时按用户拖拽比例恢复 SplitContainer 分割位置。</summary>
	private void UpdateListBounds()
	{
		if (_splitTop == null || _splitBottom == null) return;
		double fTop = _splitTop.Width > 0 ? (double)_splitTop.SplitterDistance / _splitTop.Width : 1.0 / 3;
		double fBottom = _splitBottom.Width > 0 ? (double)_splitBottom.SplitterDistance / _splitBottom.Width : 0.5;
		double fLog = (_splitLog != null && _splitLog.Height > 60) ? (double)_splitLog.SplitterDistance / _splitLog.Height : 0.8;
		BeginInvoke((MethodInvoker)delegate
		{
			try
			{
				if (_splitTop.Width > 20) _splitTop.SplitterDistance = (int)(_splitTop.Width * fTop);
				if (_splitBottom.Width > 20) _splitBottom.SplitterDistance = (int)Math.Round(_splitBottom.Width * fBottom);
				// v3.9：日志面板高度比例保持（用户拖拽后在缩放窗口时保留）
				if (_splitLog != null && _splitLog.Height > 100)
				{
					_splitLog.SplitterDistance = (int)Math.Round(_splitLog.Height * fLog);
					if (_splitLog.SplitterDistance < _splitLog.Panel1MinSize) _splitLog.SplitterDistance = _splitLog.Panel1MinSize;
				}
			}
			// best-effort：界面操作失败不影响功能
			catch { }
		});
	}

	/// <summary>三栏列表（待处理 / 已成功 / 失败）的统一构建工厂：只读网格 + 卡片底色 + 双向滚动；失败栏（isFail=true）整行浅红底、文件列改链接列（点击打开源图所在目录）并追加「详情」展开列；目录/原因列 Fill 撑满。BuildUi 处调用三次分别建三栏。</summary>
		private DataGridView BuildGrid(bool isFail)
	{
		DataGridView g = new DataGridView
		{
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			BackgroundColor = UiTheme.CardBackground,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
			// v3.9：三栏统一水平+垂直滚动，超长路径/原因可横拉查看（列头仍可拖宽）
			ScrollBars = ScrollBars.Both
		};
		// v4.17 修订六（C 档·§5.4）：表头/网格线配色对齐扁平卡片。
		// 只调色，不换控件、不改列宽策略、不动行高（设计稿 §5.4 明确）。
		g.EnableHeadersVisualStyles = false;
		g.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.PageBackground;
		g.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextMuted;
		g.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.PageBackground;
		g.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.TextMuted;
		g.GridColor = UiTheme.Border;
		// v4.6：失败栏整行浅红底（与"失败文件已在列表中标红"的提示文案一致）
		if (isFail)
		{
			g.DefaultCellStyle.BackColor = UiTheme.DangerLight;
			g.DefaultCellStyle.SelectionBackColor = UiTheme.DangerLightSelected;
		}
		if (isFail)
		{
			var nameCol = new DataGridViewLinkColumn
			{
				Name = "name",
				HeaderText = "文件",
				ActiveLinkColor = Color.Blue,
				LinkColor = Color.RoyalBlue,
				VisitedLinkColor = Color.Purple,
				// v4.17 修订七（C 档·§5.5）：170 → 140。失败栏最小窗口下仅约 264 逻辑像素宽，
				// 需容纳 name + note + detail 三列；170+70+62=302 会溢出并出横向滚动条，
				// 「详情」链接被滚出可视区（实测反射与截图双重确认）。
				Width = 140
			};
			g.Columns.Add(nameCol);
		}
		else
		{
			g.Columns.Add("name", "文件");
			g.Columns["name"].Width = 140;
		}
		g.Columns.Add("note", isFail ? "原因" : "目录");
		// v4.8：note 列改 Fill 撑满容器（用户反馈：固定 320 宽导致表格右侧大片空白）。
		// v4.12 修复：MinimumWidth 320 → 70。失败栏只占窗口 1/3 宽，220+320=540 的最小宽度
		// 超过栏宽时 Fill 列不会出横向滚动条 → 右侧内容被裁（用户反馈"失败一栏右边看不到"）。
		// v4.17 修订七（C 档·§5.5）：70 → 54，为新增的「详情」链接列腾出空间。
		g.Columns["note"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
		g.Columns["note"].MinimumWidth = 54;
		if (isFail)
		{
			// v4.17 修订七（C 档·§5.5）：note 列只放"截断摘要"，完整原因移入可展开的详情面板；
			// 新增窄链接列作展开入口（点击在「详情 ▸」/「收起 ▾」间切换，见 UpdateFailDetailLinkText）。
			g.Columns.Add(new DataGridViewLinkColumn
			{
				Name = "detail",
				HeaderText = "",
				Width = 50,
				MinimumWidth = 46,
				Resizable = DataGridViewTriState.False,
				LinkColor = UiTheme.PrimaryDark,
				ActiveLinkColor = UiTheme.Primary,
				VisitedLinkColor = UiTheme.PrimaryDark,
				TrackVisitedState = false
			});
		}
		// v4.17 修订四（P2-6）：空列表绘制"暂无文件"占位（DataGridView 无内置空状态）
		g.Paint += delegate (object sender, PaintEventArgs e)
		{
			if (g.Rows.Count > 0) return;
			Rectangle r = new Rectangle(0, g.ColumnHeadersHeight, g.ClientSize.Width,
				Math.Max(0, g.ClientSize.Height - g.ColumnHeadersHeight));
			TextRenderer.DrawText(e.Graphics, "暂无文件", g.Font, r, UiTheme.TextDisabled,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		};
		return g;
	}

	/// <summary>响应「停止」：请求引擎取消本轮转换（Cancel() 会强制结束自启的 AutoCAD 进程、让阻塞的 COM 调用尽快返回），并把停止按钮置灰、改文案、在状态栏提示正在收尾。由停止按钮与 Esc 触发。</summary>
	private void StopRequested()
	{
		_engine.Cancel();
		_btnStop.Enabled = false;
		_btnStop.Text = "正在停止…";
		_lblStatus.Text = "停止指令已下达：正在结束工具自启的 AutoCAD 进程，稍候…";
	}

	/// <summary>窗体级快捷键拦截：非转换中时处理 Ctrl+V（粘贴清单）与 Ctrl+Z（撤销上一步添加 / 清空），已处理返回 true 阻止继续传递；其余按键交回基类。转换进行中一律不响应，避免误操作。</summary>
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (!_converting)
		{
			if (keyData.HasFlag(Keys.Control))
			{
				if (keyData.HasFlag(Keys.V))
				{
					PasteFromClipboard();
					return true;
				}
				if (keyData.HasFlag(Keys.Z))
				{
					UndoLast();
					return true;
				}
			}
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	/// <summary>把一条可撤销操作压入撤销栈，超过 MaxUndo 上限时丢弃最旧一条（先进先出），保证栈不会无限增长。由「添加文件 / 清空列表」在改动清单后调用。</summary>
	private void PushUndo(UndoEntry e)
	{
		_undoStack.Add(e);
		if (_undoStack.Count > MaxUndo)
		{
			_undoStack.RemoveAt(0);
		}
	}

	/// <summary>撤销最近一次清单改动：弹出栈顶 UndoEntry——若记录的是「添加」则从待转换清单移除这些文件，若是「清空」则按原顺序恢复，随后重建列表。转换进行中或撤销栈为空时不动作。由 Ctrl+Z 触发。</summary>
	private void UndoLast()
	{
		if (_converting || _undoStack.Count == 0)
		{
			return;
		}
		UndoEntry e = _undoStack[_undoStack.Count - 1];
		_undoStack.RemoveAt(_undoStack.Count - 1);
		if (e.Added != null)
		{
			foreach (string p in e.Added)
			{
				_pendingFiles.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
			}
		}
		if (e.Cleared != null)
		{
			foreach (string p in e.Cleared)
			{
				if (!_pendingFiles.Contains(p, StringComparer.OrdinalIgnoreCase))
				{
					_pendingFiles.Add(p);
				}
			}
		}
		RefreshListUi();
	}

	private sealed class UndoEntry
	{
		public List<string> Added;

		public List<string> Cleared;
	}

	/// <summary>重建三个列表（待转换 / 成功 / 失败）的全部行：先清空三表并收起失败详情面板，再按 _pendingFiles 逐条 AddRow；同时刷新输入框提示文案（空清单提示 / 已添加 N 个）、两条进度条归零、两个「复制到文件夹」按钮的可用态，最后同步栏标题计数。由清单增删（添加 / 清空 / 撤销）与转换开始时在 UI 线程调用。</summary>
	private void RefreshListUi()
	{
		_gridPending.Rows.Clear();
		_gridSuccess.Rows.Clear();
		_gridFail.Rows.Clear();
		// v4.17 修订七（C 档·§5.5）：清单重建 → 详情面板所指的行已不存在，收起
		HideFailDetail();
		foreach (string path in _pendingFiles)
		{
			AddRow(path, StatusFor(path));
		}
		// v4.17 修订四（P2-5）：原操作提示塞在 GroupBox 标题里（22 字），标题瘦身后迁到空清单时的输入框内。
		bool inputEmpty = _pendingFiles.Count == 0;
		_txtInput.Text = inputEmpty
			? "支持拖拽文件到窗口、Ctrl+V 粘贴（Ctrl+Z 撤销）"
			: "已添加 " + _pendingFiles.Count + " 个 .dwg 文件";
		_txtInput.ForeColor = inputEmpty ? UiTheme.TextDisabled : UiTheme.Text;
		// v3.9（双进度条）：总/阶段进度条归零（Maximum 固定 100，总进度按阶段权重驱动，不再按文件数）
		if (_progressTotal != null) _progressTotal.Value = 0;
		if (_progressPhase != null) _progressPhase.Value = 0;
		if (_btnCopyFailed != null) _btnCopyFailed.Enabled = _gridFail.Rows.Count > 0;
		if (_btnCopyPending != null) _btnCopyPending.Enabled = _gridPending.Rows.Count > 0;
		UpdateHeaderCounts();
	}

	/// <summary>查某文件当前状态：在 _statusByPath 中命中则返回该状态，否则视为「待处理」。用于清单重建与「只转尚未成功文件」的筛选。</summary>
	private ItemStatus StatusFor(string path)
	{
		return _statusByPath.TryGetValue(path, out ItemStatus s) ? s : ItemStatus.Pending;
	}

	/// <summary>按状态决定文件应落在哪一栏：失败→失败栏，成功→成功栏，其余→待处理栏。</summary>
	private DataGridView GridFor(ItemStatus st)
	{
		if (st == ItemStatus.Failed) return _gridFail;
		if (st == ItemStatus.Success) return _gridSuccess;
		return _gridPending;
	}

	/// <summary>向 st 对应的栏（失败 / 成功 / 待处理）追加一行：行 Tag 存完整路径，文件名写入 name 列、状态说明写入 note 列，并初始化该行的失败详情入口。由 RefreshListUi 在重建清单时逐条调用。</summary>
	private void AddRow(string path, ItemStatus st)
	{
		DataGridView grid = GridFor(st);
		int idx = grid.Rows.Add();
		grid.Rows[idx].Tag = path;
		grid.Rows[idx].Cells["name"].Value = Path.GetFileName(path);
		grid.Rows[idx].Cells["note"].Value = NoteText(path, st, null);
		InitFailDetailCell(grid, idx, path);
	}

	/// <summary>根据新状态把该文件的行实时归位到对应栏（边转换边归位），并刷新该行显示。</summary>
	private void UpdateRow(string path, ItemStatus st, string note)
	{
		if (string.IsNullOrEmpty(path)) return;
		_statusByPath[path] = st;
		if (!string.IsNullOrEmpty(note)) _noteByPath[path] = note;
		foreach (DataGridView g in new[] { _gridPending, _gridSuccess, _gridFail })
		{
			int r = FindRowInGrid(g, path);
			if (r >= 0) g.Rows.RemoveAt(r);
		}
		DataGridView grid = GridFor(st);
		int row = grid.Rows.Add();
		grid.Rows[row].Tag = path;
		grid.Rows[row].Cells["name"].Value = Path.GetFileName(path);
		grid.Rows[row].Cells["note"].Value = NoteText(path, st, note);
		InitFailDetailCell(grid, row, path);
		grid.FirstDisplayedScrollingRowIndex = Math.Max(0, row);
		if (st == ItemStatus.Failed && _btnCopyFailed != null) _btnCopyFailed.Enabled = true;
		if (st == ItemStatus.Pending && _btnCopyPending != null) _btnCopyPending.Enabled = true;
		UpdateHeaderCounts();
		// v4.17 修订七（C 档·§5.5）：该行若已离开失败栏，则自动收起它对应的详情面板
		SyncFailDetail();
	}

	/// <summary>note 列取值：失败=失败原因（截断摘要），成功/待处理=源文件所在目录。
	/// v4.17 修订七（C 档·§5.5）：失败原因列只显示截断摘要，完整原文见「详情」面板
	/// （_noteByPath 仍保留全文，故失败原因分类统计不受影响）。
	/// 截断 32 字：note 列实际可显示约 4~6 个全角字，超出本就会被列宽裁剪，
	/// 摘要取 32 字让 UI 表现不变而单元格底层文本更短。</summary>
	private string NoteText(string path, ItemStatus st, string incomingNote)
	{
		if (st == ItemStatus.Failed)
		{
			string full = !string.IsNullOrEmpty(incomingNote)
				? incomingNote
				: (_noteByPath.TryGetValue(path, out string rev) ? rev : "");
			return Truncate(full, 32);
		}
		return Path.GetDirectoryName(path) ?? "";
	}

	/// <summary>按字符数截断并追加省略号（null/空原样返回）。</summary>
	private static string Truncate(string s, int max)
	{
		if (string.IsNullOrEmpty(s)) return s ?? "";
		return s.Length <= max ? s : s.Substring(0, max) + "…";
	}

	/// <summary>在指定栏中按完整路径（忽略大小写）查行号，命中返回下标、否则 -1。因「状态变化要换栏」需先在旧栏定位再删除，故用行 Tag（完整路径）而非显示名匹配。</summary>
	private int FindRowInGrid(DataGridView grid, string file)
	{
		for (int i = 0; i < grid.Rows.Count; i++)
		{
			if (grid.Rows[i].Tag is string s && string.Equals(s, file, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	// ================= v4.17 修订七（C 档·§5.5）：失败原因内联展开 =================
	/// <summary>展开/收起指定失败行的详情面板：同行再点＝收起；点另一行＝换行显示。</summary>
	private void ToggleFailDetail(int rowIndex)
	{
		if (_failDetailPanel == null || rowIndex < 0 || rowIndex >= _gridFail.Rows.Count) return;
		if (!(_gridFail.Rows[rowIndex].Tag is string path) || path.Length == 0) return;
		bool sameRow = _failDetailPanel.Visible
			&& string.Equals(_failDetailPath, path, StringComparison.OrdinalIgnoreCase);
		if (sameRow) { HideFailDetail(); return; }
		_failDetailPath = path;
		_lblFailDetailTitle.Text = "失败详情：" + Path.GetFileName(path);
		_txtFailDetail.Text = BuildFailDetailText(path);
		_txtFailDetail.SelectionStart = 0;
		_txtFailDetail.SelectionLength = 0;
		// 详情面板标称 132px，但失败栏（_splitBottom.Panel2）被窗口高度/拖拽压矮时，
		// 固定 132 会把失败表格和面板一起挤出容器：实测最小窗口下失败栏仅约 126px
		// （表头 24px + 表格 102px），面板 132 > 102 → grid 高被压成 0、面板 Y=-18 溢出。
		// 改为按失败栏剩余空间收缩（最低保 44px：约 2 行小字 + 内边距），收起即恢复原高度。
		int p2H = _splitBottom != null ? _splitBottom.Panel2.Height : 0;
		int hdrH = _hdrFail != null ? _hdrFail.Height : 0;
		int avail = p2H - hdrH - 2;
		if (avail > 0) _failDetailPanel.Height = Math.Max(44, Math.Min(132, avail));
		_failDetailPanel.Visible = true;
		UpdateFailDetailLinkText();
	}

	/// <summary>收起详情面板并复位链接文案。</summary>
	private void HideFailDetail()
	{
		if (_failDetailPanel == null) return;
		_failDetailPath = null;
		_failDetailPanel.Visible = false;
		UpdateFailDetailLinkText();
	}

	/// <summary>行被移走/清空后，若详情面板所指的行已不在失败栏则自动收起。</summary>
	private void SyncFailDetail()
	{
		if (_failDetailPanel == null || !_failDetailPanel.Visible) return;
		if (string.IsNullOrEmpty(_failDetailPath) || FindRowInGrid(_gridFail, _failDetailPath) < 0)
		{
			HideFailDetail();
		}
	}

	/// <summary>把链接文案刷成与当前展开状态一致（未展开的行一律「详情 ▸」）。</summary>
	private void UpdateFailDetailLinkText()
	{
		if (_gridFail == null || !_gridFail.Columns.Contains("detail")) return;
		for (int i = 0; i < _gridFail.Rows.Count; i++)
		{
			bool open = _failDetailPanel != null && _failDetailPanel.Visible
				&& _gridFail.Rows[i].Tag is string p
				&& string.Equals(p, _failDetailPath, StringComparison.OrdinalIgnoreCase);
			_gridFail.Rows[i].Cells["detail"].Value = open ? "收起 ▾" : "详情 ▸";
		}
	}

	/// <summary>失败栏新增行时补上「详情」链接文案（其它栏无此列，直接跳过）。</summary>
	private void InitFailDetailCell(DataGridView grid, int rowIndex, string path)
	{
		if (grid == null || !grid.Columns.Contains("detail")) return;
		bool open = _failDetailPanel != null && _failDetailPanel.Visible
			&& string.Equals(path, _failDetailPath, StringComparison.OrdinalIgnoreCase);
		grid.Rows[rowIndex].Cells["detail"].Value = open ? "收起 ▾" : "详情 ▸";
	}

	/// <summary>详情正文 = 完整路径 + 完整失败原因 + 日志尾部 20 行。</summary>
	private string BuildFailDetailText(string path)
	{
		_noteByPath.TryGetValue(path, out string reason);
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("文件：" + path);
		sb.AppendLine();
		sb.AppendLine("失败原因：");
		sb.AppendLine(string.IsNullOrWhiteSpace(reason) ? "（无记录）" : reason);
		sb.AppendLine();
		sb.AppendLine("—— 日志尾部 20 行 ——");
		sb.Append(LogTail(20));
		return sb.ToString();
	}

	/// <summary>取日志控件文本的最后 n 行（不足 n 行则全取）。</summary>
	private string LogTail(int n)
	{
		if (_txtLog == null || n <= 0) return "";
		string[] lines = _txtLog.Text.Replace("\r\n", "\n").Split('\n');
		int start = Math.Max(0, lines.Length - n);
		return string.Join(Environment.NewLine, lines, start, lines.Length - start);
	}

	/// <summary>通用文件夹选择：弹出以 desc 为说明的文件夹浏览器，用户确定后把所选路径写回 box（取消则不动）。供输入 / 输出文件夹两个选择按钮复用。</summary>
	private void PickFolder(TextBox box, string desc)
	{
		// v4.6：反编译风格双变量简化为单变量（行为不变）
		using var dialog = new FolderBrowserDialog
		{
			Description = desc,
			ShowNewFolderButton = true
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			box.Text = dialog.SelectedPath;
		}
	}

	/// <summary>把失败文件（复制，保留原件）合并到一个新文件夹，并打开它。</summary>
	private void CopyFailedToFolder()
	{
		CopyGridToFolder(_gridFail, "失败文件_");
	}

	/// <summary>v4.12：把"待处理"栏的文件（含因超限退回的）合并到一个新文件夹，便于后续单独转换。</summary>
	private void CopyPendingToFolder()
	{
		CopyGridToFolder(_gridPending, "待转换文件_");
	}

	/// <summary>把指定栏里的源图复制到 输出文件夹\&lt;前缀&gt;yyyyMMdd_HHmmss\ 并打开该文件夹（保留原件）。</summary>
	private void CopyGridToFolder(DataGridView grid, string folderPrefix)
	{
		var paths = new List<string>();
		for (int i = 0; i < grid.Rows.Count; i++)
		{
			if (grid.Rows[i].Tag is string p) paths.Add(p);
		}
		if (paths.Count == 0)
		{
			_lblStatus.Text = "当前没有可合并的文件";
			return;
		}
		string outDir = _txtOutput.Text.Trim();
		if (string.IsNullOrEmpty(outDir))
		{
			_lblStatus.Text = "请先在输入框里指定输出文件夹";
			return;
		}
		string folder = Path.Combine(outDir, folderPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		try
		{
			Directory.CreateDirectory(folder);
			int copied = 0;
			foreach (string p in paths)
			{
				string dest = Path.Combine(folder, Path.GetFileName(p));
				string baseName = Path.GetFileNameWithoutExtension(dest);
				string ext = Path.GetExtension(dest);
				int k = 2;
				while (File.Exists(dest)) dest = Path.Combine(folder, baseName + "_" + (k++) + ext);
				File.Copy(p, dest);
				copied++;
			}
			_lblStatus.Text = "已复制 " + copied + " 个文件到 " + folder;
			// best-effort：界面操作失败不影响功能
			try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch { }
		}
		catch (Exception ex)
		{
			_lblStatus.Text = "复制失败：" + ex.Message;
		}
	}

	/// <summary>v4.17 修订三（2026-10-15 用户反馈）：开始转换只转换**尚未成功**的文件
	/// （待处理 + 失败 + 已停止）。旧行为把清单里所有文件（含已成功）一起重导，
	/// 于是"上一批打印完 → 添加新图 → 再点开始转换"时，上一批又被完整重打一遍：
	/// 既白花十几分钟，又因 UniquePdfPath 加后缀而产出"名称(2).pdf"重复文件。
	/// 需要重转已成功的文件时，先"清空列表"再重新添加（状态随之清空）即可。</summary>

	/// <summary>「开始转换」：先用领域规则 RunSelection.FilterTodoFiles 过滤出尚未成功的文件（已成功的默认跳过，done 为跳过数）；若无可转文件则在状态栏与日志提示，否则以 isContinue:false 启动转换。</summary>
	private void Start()
	{
		// v4.17 修订三 需求①：只转换尚未成功的文件（已成功的默认跳过，不加设置项）。
		// 规则本体在 RunSelection.FilterTodoFiles（领域规则，可被自测回归）。
		List<string> todo = RunSelection.FilterTodoFiles(_pendingFiles, StatusFor, out int done);
		if (todo.Count == 0)
		{
			_lblStatus.Text = done > 0
				? "全部 " + done + " 个文件都已转换完成，本轮没有要转换的文件；如需重转，请先“清空列表”再重新添加。"
				: "请先添加要转换的 dwg 文件（拖入窗口、Ctrl+V 粘贴，或点“添加文件…”）。";
			AppendLog(done > 0
				? "本轮跳过 " + done + " 个已转换成功的文件（无待转换文件）。"
				: "");
			return;
		}
		if (done > 0)
		{
			AppendLog("本轮跳过 " + done + " 个已转换成功的文件，仅转换 " + todo.Count + " 个文件。");
		}
		RunCore(todo, isContinue: false);
	}

	/// <summary>继续转换：只导出停止后仍未处理的文件（未处理 = 状态仍为待处理或已停止）。</summary>
	private void Continue()
	{
		List<string> unfinished = UnfinishedFiles();
		if (unfinished.Count == 0)
		{
			// v3.9：提示改状态栏（不再弹窗）
			_lblStatus.Text = "没有待继续处理的文件。";
			return;
		}
		RunCore(unfinished, isContinue: true);
	}

	/// <summary>v4.5：按失败栏行统计失败原因分类（与日志摘要同一分类口径）。
	/// 返回"图框问题 2、质量核查不通过 1"式摘要；无失败返回空串。</summary>
	private string FailureBreakdown()
	{
		var counts = new Dictionary<string, int>();
		for (int i = 0; i < _gridFail.Rows.Count; i++)
		{
			if (!(_gridFail.Rows[i].Tag is string p)) continue;
			_noteByPath.TryGetValue(p, out string note);
			string cat = ConvertReport.ClassifyReason(note);
			if (string.IsNullOrEmpty(cat)) cat = "出图失败";
			counts.TryGetValue(cat, out int c);
			counts[cat] = c + 1;
		}
		if (counts.Count == 0) return "";
		return string.Join("、", counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value));
	}

	/// <summary>取出所有尚未处理完的文件（仅未到达成功/失败终态者）。</summary>
	private List<string> UnfinishedFiles()
	{
		List<string> list = new List<string>();
		foreach (string path in _pendingFiles)
		{
			if (!_statusByPath.TryGetValue(path, out ItemStatus st) || st == ItemStatus.Pending || st == ItemStatus.Cancelled)
			{
				list.Add(path);
			}
		}
		return list;
	}

	/// <summary>转换总调度（Start / Continue 的公共实现）：校验非转换中且已选输出文件夹后，锁住输入类控件、复位计数与进度、把界面选项与用户设置组装成 PrintOptions / ConvertProfile，启动已用时定时器，再在后台线程调用引擎 RunFiles。线程结束或异常时经 SynchronizationContext 回到 UI 线程恢复按钮与拖放、落定进度条、汇总成功 / 失败 / 取消数并写日志。</summary>
	private void RunCore(List<string> snapshot, bool isContinue)
	{
		if (_converting)
		{
			return;
		}
		string output = _txtOutput.Text.Trim();
		if (snapshot.Count == 0)
		{
			MessageBox.Show(this, "请先添加要转换的 dwg 文件（拖入窗口、Ctrl+V 粘贴，或点“添加文件…”）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		if (string.IsNullOrEmpty(output))
		{
			MessageBox.Show(this, "请先选择输出文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		_converting = true;
		_btnInput.Enabled = false;
		_btnInputFolder.Enabled = false;
		_btnClear.Enabled = false;
		AllowDrop = false;
		RefreshListUi();
		SetPhaseBarIdle(); // v3.9（双进度条）：阶段条全灰 + 两进度条归零 + 阶段完成比例清零
		_completed = 0;
		// v4.6：级联赋值拆开（可读性）
		_cancelCount = 0;
		_failCount = 0;
		_okCount = 0;
		_btnStart.Enabled = false;
		_btnStop.Enabled = true;
		_btnStop.Text = "停止";
		_btnOpenOut.Enabled = false;
		_lblStatus.Text = "正在准备…";
		_engine.ResetCancel();
		SynchronizationContext sync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
		IProgress<ProgressUpdate> progress = new Progress<ProgressUpdate>(ApplyUpdate);
		bool tight = !_rbMarginOn.Checked;
		double margin = tight ? 0.0 : 10.0;
		// v4.3：从 settings.json 注入用户自定义（打印阈值/质量审查最小文件大小/块名列表）
		// v4.5：+ PDF 输出结构模式
		var settings = _appSettings ?? new AppSettings();
		var print = new PrintOptions
		{
			Mode = (_cmbPrint.SelectedIndex == 1) ? ColorMode.Color : ColorMode.Monochrome,
			EdgeMarginMm = margin,
				ShowLineWeight = _chkLineWeight.Checked,
			QualityCheck = _chkQualityCheck.Checked,
			RecognizeBlockFrame = _chkBlockFrame.Checked,
			QualityMinFileSizeBytes = settings.QualityMinFileSizeBytes,
			ExtraBlockNames = settings.ExtraBlockNames,
			FlatOutput = settings.PdfFlatOutput,
			// v4.15：含图片图纸的高清出图 dpi（0=关闭）
			RasterImageDpi = settings.RasterImageDpi,
			// v4.8：大文件阈值（0=不限制；v4.12 起=超过则退回待转换）
			MaxDwgSizeBytes = settings.MaxDwgSizeBytes,
			// v4.12：单张转换超时秒数（0=不限制；超时中止该张并退回待处理）
			ConvertTimeoutSeconds = settings.ConvertTimeoutSeconds,
			// v4.17 修订三：PDF 子文件夹名称模板 + 本轮开始时刻（每轮展开一次，例 PDF-09：42：30）
			PdfFolderTemplate = settings.PdfFolderTemplate,
			RunStartedAt = _runStarted,
			// v4.12：非首次运行（用户再次点"开始转换"，通常是补转退回待处理的文件）
			// → 本轮忽略文件大小与转换时间限制，让这批文件可以转完
			IgnoreSizeTimeLimits = _hasRunBefore
		};
		_hasRunBefore = true;
		var profile = new ConvertProfile { Mode = (_cmbMode.SelectedIndex == 1) ? ConvertMode.Company : ConvertMode.General };
		_runStarted = DateTime.Now;
		_runWatch.Restart();
		_runTimer.Start();
		Text = _baseTitle + " — 已用时 00:00";
		BgLogger.LogRunStart("文件清单 " + snapshot.Count + " 个", output, tight, false);
		Task.Run(delegate
		{
			try
			{
				_engine.RunFiles(snapshot, output, progress, tight, print, profile);
			}
			catch (Exception ex)
			{
				// v4.6：引擎内未捕获异常兜底——此前异常静默冒泡出 Task，sync.Post 永不执行，
				// _converting 恒 true、按钮/拖放永不恢复（UI 假死）。此处恢复 UI + 记日志 + 状态栏提示。
				BgLogger.Error("转换线程异常终止: " + ex);
				sync.Post(delegate
				{
					_runTimer.Stop();
					Text = _baseTitle;
					_converting = false;
					_btnStart.Enabled = true;
					_btnStop.Enabled = false;
					_btnStop.Text = "停止";
					_btnInput.Enabled = true;
					_btnInputFolder.Enabled = true;
					_btnClear.Enabled = true;
					AllowDrop = true;
					_btnOpenOut.Enabled = true;
					_lblStatus.Text = "转换异常终止：" + ex.Message + "（详见日志报告）";
					_lblPhaseDetail.Text = "转换异常终止。";
					AppendLog("==== 转换异常终止: " + ex.Message + " ====");
					_progressTotal.Value = 0;
					_progressPhase.Value = 0;
					Array.Clear(_phaseDonePct, 0, _phaseDonePct.Length);
					_completed = 0;
				}, null);
				return;
			}
			sync.Post(delegate
			{
				_runTimer.Stop();
				Text = _baseTitle;
				_converting = false;
				_btnInput.Enabled = true;
				_btnInputFolder.Enabled = true;
				_btnClear.Enabled = true;
				AllowDrop = true;
				if (!_engine.IsCancelled)
				{
					_progressTotal.Value = _progressTotal.Maximum;
					_progressPhase.Value = _progressPhase.Maximum;
				}
				_btnStart.Enabled = true;
				bool hasUnfinished = UnfinishedFiles().Count > 0;
				_btnStop.Enabled = hasUnfinished;
				_btnStop.Text = hasUnfinished ? "继续" : "停止";
				_btnOpenOut.Enabled = true;
				// v3.9：转换结果不再弹窗，改状态栏 + 阶段条落定 + 日志总结
				string sum = (_engine.IsCancelled ? "已停止" : "已完成") + "：成功 " + _okCount + " 张，失败 " + _failCount
					+ ((_cancelCount > 0) ? ("，取消 " + _cancelCount + " 张") : "") + "，总用时 " + FormatElapsed(_runWatch.Elapsed)
					+ (_failCount > 0 ? "（失败文件已在上方列表中标红）" : "");
				// v4.4：失败原因分类摘要（如"图框问题 2、质量核查不通过 1"）——进弹窗与日志；
				// 状态栏/阶段行保持单行原文案（Label 多行会撑高布局，v3.9 已固定行高）
				string breakdown = FailureBreakdown();
				_lblStatus.Text = sum;
				_lblPhaseDetail.Text = _engine.IsCancelled ? "转换已停止。" : "转换完成。" + sum;
				// v3.9：正常完成才落定全蓝；中途停止保留"进行到哪一步"的状态
				// v4.1：转换完成弹窗提醒
				// v4.6：两层 if(!_engine.IsCancelled) 合并为一层
				if (!_engine.IsCancelled)
				{
					SetPhaseBarDone();
					string msg = "转换完成！成功 " + _okCount + " 张，失败 " + _failCount + " 张，总用时 " + FormatElapsed(_runWatch.Elapsed);
					if (!string.IsNullOrEmpty(breakdown)) msg += "\n\n失败原因: " + breakdown;
					if (_failCount > 0) msg += "\n\n失败文件已在列表中标红，可点「合并失败文件」集中复制。";
					MessageBox.Show(msg, "转换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				BgLogger.LogRunEnd(_runStarted, _okCount, _failCount, _cancelCount, _engine.IsCancelled);
				AppendLog("==== " + sum + " ====");
				if (!string.IsNullOrEmpty(breakdown)) AppendLog("失败原因: " + breakdown);
				if (_failCount > 0) AppendLog("提示：失败文件已在上方列表中标红，可点“合并失败文件”集中复制。");
				// v3.9（双进度条）：本轮结束归零（阶段条保留完成/停止态供查看，进度条为下一轮准备）
				_progressTotal.Value = 0;
				_progressPhase.Value = 0;
				Array.Clear(_phaseDonePct, 0, _phaseDonePct.Length);
				_completed = 0;
				// v4.6：级联赋值拆开（可读性）
				_cancelCount = 0;
				_failCount = 0;
				_okCount = 0;
			}, null);
		});
	}

	/// <summary>把耗时格式化为可读文本：不足 1 小时用「分:秒」（mm:ss），达到 1 小时用「时:分:秒」（h:mm:ss）。用于标题栏已用时与转换完成汇总。</summary>
	private static string FormatElapsed(TimeSpan t)
	{
		int h = (int)t.TotalHours;
		if (h > 0)
		{
			return string.Format("{0}:{1:00}:{2:00}", h, t.Minutes, t.Seconds);
		}
		return string.Format("{0:00}:{1:00}", t.Minutes, t.Seconds);
	}

	// v3.9：移除原 ShowBootDialog/UpdateBootDialog/CloseBootDialog（"正在准备…"弹窗）。
	// 阶段信息全部改在界面内展示：阶段条芯片着色 + 当前阶段详情 Label（_lblPhaseDetail）。

	/// <summary>转换进度回调（在 UI 线程执行）：把日志行镜像到界面日志面板；按 RunPhase 只进不退地推进阶段条与阶段详情；对文件级更新累计成功 / 失败 / 取消数、刷新状态栏计数与阶段进度，并把该文件实时归位到对应栏。由引擎经 Progress 回调。</summary>
	private void ApplyUpdate(ProgressUpdate u)
	{
		// v3.9：日志行实时追加到界面日志面板（与文件日志同源镜像）
		if (u.LogLine != null) AppendLog(u.LogLine);
		// v3.9：阶段推进（只前进不后退；RunPhase 枚举顺序即流程顺序，
		// 缓存全命中/整批回退走 PhaseSkipped 置灰不打断顺序）
		if (u.Phase > _currentPhase && u.Phase != RunPhase.Done) AdvancePhase(u.Phase, u.PhaseSkipped);
		// v3.9：阶段内提示（识别 x/y、设置页面 x/y 等）刷新到阶段详情行，不再弹窗
		if (u.Booting)
		{
			if (!string.IsNullOrEmpty(u.Note)) _lblPhaseDetail.Text = u.Note;
			// v3.9（双进度条）：阶段内文件级进度（识别/补识别/重试/页面设置）驱动阶段进度条 + 总进度
			if (u.PhaseTotal > 0) UpdatePhaseProgress(u.Phase, u.PhaseIndex, u.PhaseTotal);
			return;
		}
		if (u.Total > 0)
		{
			if (u.Status == ItemStatus.Success || u.Status == ItemStatus.Failed || u.Status == ItemStatus.Cancelled)
			{
				_completed++;
				if (u.Status == ItemStatus.Success)
				{
					_okCount++;
					BgLogger.LogFile(u.FileName, "成功", "");
				}
				else if (u.Status == ItemStatus.Failed)
				{
					_failCount++;
					BgLogger.LogFile(u.FileName, "失败", u.Note);
				}
				else
				{
					_cancelCount++;
					BgLogger.LogFile(u.FileName, "已停止", u.Note);
				}
				// v3.9（双进度条）：出图阶段文件完成即阶段进度（驱动阶段进度条 + 总进度）
				UpdatePhaseProgress(RunPhase.Plot, _completed, u.Total);
			}
			_lblStatus.Text = "共 " + u.Total + " 张 · 成功 " + _okCount + "，失败 " + _failCount + ((_cancelCount > 0) ? ("，取消 " + _cancelCount) : "");
		}
		// v3.9：出图阶段的 x/y 进度也刷新阶段详情行
		if (u.Phase == RunPhase.Plot && !string.IsNullOrEmpty(u.Note))
		{
			_lblPhaseDetail.Text = u.Note;
		}
		UpdateRow(u.FileName, u.Status, u.Note);
		}

	/// <summary>打开输出文件夹：优先定位到最近创建的 PDF 批次子文件夹（LatestPdfSubFolder），找不到则退回输出根目录，再用资源管理器打开。由「打开输出文件夹」按钮触发。</summary>
	private void OpenOutputFolder()
	{
		string text = _txtOutput.Text.Trim();
		// v4.17 修订三：PDF 子文件夹改为按批次命名（默认 PDF-时.分.秒，同名再加 -2/-3）——
		// 打开时定位到**最新创建的那个批次文件夹**，多批次时不用用户自己找。
		// 找不到（或异常）则退回输出根目录，与 v4.17 行为兼容。
		string latest = LatestPdfSubFolder(text);
		if (!string.IsNullOrEmpty(latest)) text = latest;
		if (Directory.Exists(text))
		{
			try
			{
				Process.Start("explorer.exe", "\"" + text + "\"");
				return;
			}
			catch
			{
				return;
			}
		}
		MessageBox.Show(this, "输出文件夹不存在。", "提示");
	}

	/// <summary>v4.17 修订三：在输出根目录下找出最新创建的 PDF 批次子文件夹。
	/// 排序键 = CreationTimeUtc（并列再用 LastWriteTimeUtc）；无子目录/异常返回 null。
	/// 只认目录，且按模板前缀优先匹配（模板默认 "PDF-HH:mm:ss" → 前缀 "PDF-"），
	/// 以免命中用户自建的无关子目录；前缀无匹配时退化为"任意子目录取最新"。</summary>
	private static string LatestPdfSubFolder(string outputRoot)
	{
		try
		{
			if (string.IsNullOrEmpty(outputRoot) || !Directory.Exists(outputRoot)) return null;
			string[] dirs = Directory.GetDirectories(outputRoot);
			if (dirs.Length == 0) return null;
			var settings = AppSettings.Load();
			string prefix = (settings.PdfFolderTemplate ?? "").Trim();
			// 从模板里截出字面前缀（**最靠前的那个占位符**之前）。
			// v4.17 修订三：原先按 token 列表顺序逐个截断，"yyyy/MM/dd/HH/mm/ss" 的固定优先级会让
			// 模板 "dd-MM" 被 "MM" 提前截成 "dd-"（错）。改为取所有占位符的**最小下标**，
			// 与模板书写顺序无关，恒等于"第一个占位符之前"。
			int cut = -1;
			foreach (string tok in new[] { "yyyy", "MM", "dd", "HH", "mm", "ss" })
			{
				int i = prefix.IndexOf(tok, StringComparison.Ordinal);
				if (i >= 0 && (cut < 0 || i < cut)) cut = i;
			}
			if (cut >= 0) prefix = prefix.Substring(0, cut);
			prefix = prefix.Trim();
			var picked = dirs
				.Where(d => prefix.Length == 0 || Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (picked.Count == 0) picked = new List<string>(dirs);
			return picked
				.OrderBy(d =>
				{
					try { return Directory.GetCreationTimeUtc(d); } catch { return DateTime.MinValue; }
				})
				.ThenBy(d =>
				{
					try { return Directory.GetLastWriteTimeUtc(d); } catch { return DateTime.MinValue; }
				})
				.Last();
		}
		catch
		{
			return null;
		}
	}

	/// <summary>首次使用时的 AutoCAD 环境自检：若本机此前已检查过则直接返回；否则弹「环境检查」模态框、
	/// 在 <b>STA 后台线程</b>跑 EnvironmentSelfCheck.Run（AutoCAD COM 要求 STA，Task.Run 的线程池线程恒为 MTA，
	/// v4.17 修订十六改正），期间用 marquee 进度条，结束后按结果（通过 / 自动修复后通过 / 失败）刷新状态栏、
	/// 写日志并按需弹框。通过或失败都会写标记，整个自检只在本机首次运行时执行一次；设置页「重新自检」可清除标记重来。</summary>
	private async System.Threading.Tasks.Task RunFirstUseSelfCheckAsync()
	{
		if (EnvironmentSelfCheck.CheckedBefore())
		{
			// v4.17 修订十六：上次未通过时不再重复自检，仅状态栏提示（转换主路径走 accoreconsole，不受影响）
			if (!EnvironmentSelfCheck.PassedBefore())
			{
				_lblStatus.Text = "上次环境自检未通过（转换仍可用；可在设置页「重新自检」）。";
			}
			return;
		}
		_lblStatus.Text = "首次使用，正在检查本机 AutoCAD 环境…";

		// v2.3.1：自检期间弹窗提示用户，避免误以为程序卡死或反复检查。
		EnvironmentSelfCheck.Result result;
		using (var dlg = new Form
		{
			Text = "环境检查",
			FormBorderStyle = FormBorderStyle.FixedDialog,
			ClientSize = new System.Drawing.Size(380, 96),
			StartPosition = FormStartPosition.CenterScreen,
			TopMost = true,
			ShowInTaskbar = false,
			ControlBox = false,
			MinimizeBox = false,
			MaximizeBox = false
		})
		{
			dlg.Controls.Add(new Label
			{
				Text = "正在检查本机 AutoCAD 环境，请稍候…（仅首次运行执行一次）",
				Location = new System.Drawing.Point(10, 10),
				Size = new System.Drawing.Size(360, 40),
				TextAlign = ContentAlignment.MiddleCenter,
				Font = new Font("Microsoft YaHei UI", 10f)
			});
			dlg.Controls.Add(new ProgressBar
			{
				Location = new System.Drawing.Point(12, 60),
				Size = new System.Drawing.Size(356, 22),
				Minimum = 0,
				Maximum = 100,
				Style = ProgressBarStyle.Marquee
			});
			dlg.Show(this);
			try
			{
				// v4.17 修订十六：Run() 只定位 accoreconsole（秒级、不启动 AutoCAD）
				result = await System.Threading.Tasks.Task.Run(EnvironmentSelfCheck.Run);
			}
			finally
			{
				// best-effort：界面操作失败不影响功能
				try { dlg.Close(); } catch { }
			}
		}
		switch (result)
		{
			case EnvironmentSelfCheck.Result.Ok:
				_lblStatus.Text = "环境自检通过。";
				BgLogger.Info("环境自检通过。");
				break;
			case EnvironmentSelfCheck.Result.Failed:
				_lblStatus.Text = "环境自检未通过（出图引擎不可用，详见运行日志）。";
				BgLogger.Error("环境自检失败：" + EnvironmentSelfCheck.ReportPath);
				MessageBox.Show(this,
					"未在本机找到出图引擎 AutoCAD Core Console（accoreconsole.exe），暂时无法转换。\n\n" +
					"详细原因见：\n" + EnvironmentSelfCheck.ReportPath +
					"\n\n本工具用它出图，不需要打开 CAD。按报告中的步骤处理后，" +
					"在「设置」页点「重新自检」即可再次检查。",
					"环境自检未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				break;
		}
	}
}
