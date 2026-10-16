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

private void BuildUi()
	{
		// ================= v3.4：Dock 布局（顶部三组 + 中部列表区 + 底部状态栏） =================
		// ---------- 1) 输入输出 ----------
		// v4.17 修订六（C 档·P3-2）：GroupBox → CardPanel（白卡片 + 12px 圆角 + 0.5px 描边）。
		// CardPanel 的标题高度与 GroupBox 标题槽一致，故 Height 保持 100 不变、内容区高度不变。
		_cardInput = new CardPanel
		{
			Title = "输入输出",
			Dock = DockStyle.Top,
			Height = 100,
			Padding = new Padding(12, 4, 12, 6)
		};
		var tblInput = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
		tblInput.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
		tblInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		tblInput.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tblInput.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tblInput.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
		tblInput.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

		var lblInput = new Label { Text = "待转换文件", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
		_txtInput = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5), BackColor = UiTheme.ReadOnlyBackground };
		_btnInput = new Button { Text = "添加文件…", Width = 96, Height = 28, Margin = new Padding(4, 6, 2, 6) };
		_btnInput.Click += delegate { AddFilesDialog(); };
		_btnInputFolder = new Button { Text = "添加文件夹…", Width = 110, Height = 28, Margin = new Padding(2, 6, 4, 6) };
		_btnInputFolder.Click += delegate { AddFolderDialog(); };

		var lblOutput = new Label { Text = "输出文件夹", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
		_txtOutput = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5), BackColor = UiTheme.ReadOnlyBackground };
		_btnOutput = new Button { Text = "浏览…", Width = 96, Height = 28, Margin = new Padding(4, 6, 4, 6) };
		_btnOutput.Click += delegate { PickFolder(_txtOutput, "选择放置生成的 PDF 的输出文件夹"); };

		tblInput.Controls.Add(lblInput, 0, 0);
		tblInput.Controls.Add(_txtInput, 1, 0);
		tblInput.Controls.Add(_btnInput, 2, 0);
		tblInput.Controls.Add(_btnInputFolder, 3, 0);
		tblInput.Controls.Add(lblOutput, 0, 1);
		tblInput.Controls.Add(_txtOutput, 1, 1);
		tblInput.Controls.Add(_btnOutput, 2, 1);
		_cardInput.Controls.Add(tblInput);

		// ---------- 2) 选项（单行对齐栅格） ----------
		_cardOptions = new CardPanel
		{
			Title = "选项",
			Dock = DockStyle.Top,
			Height = 92, // v4.8：80 不够容纳两行控件（含标题行约需 86px），第二行底部被裁剪
			Padding = new Padding(12, 4, 12, 6)
		};
		// v4.17 修订四（P1-5）：原两行共用一个 6 列 AutoSize 栅格，列宽取两行最大值 →
		// 行 1 的"配色："标签被行 2 的复选框撑开（与下拉框之间空出 100px），行 2 又因首列 Margin 整体右移 17px。
		// v4.17 修订五（回归修复）：仍用单个 6 列栅格，但行 2 不再独占列 —— 改为"跨 6 列的单格 + 横向流式容器"，
		// 既不参与列宽计算（100px 空档消失），又从栅格左缘起排（与行 1 左缘对齐）。
		// 【踩坑·务必保留】曾把两行拆成两个嵌套 TableLayoutPanel，且子表 Dock=Fill 落在 AutoSize 行内，
		// 实测子表被撑到 100 逻辑像素高，把行 2 顶出 92px 高的分组之外、整行不可见（详见手册 5.10）。
		var tblOptions = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 6,
			RowCount = 2,
			Margin = new Padding(0)
		};
		for (int i = 0; i < 6; i++) tblOptions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tblOptions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tblOptions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		ToolTip toolTip = new ToolTip();
		_rbMarginOn = new RadioButton
		{
			Text = "有边距（居中）",
			AutoSize = true,
			Checked = true,
			Margin = new Padding(0, 10, 14, 0)
		};
		toolTip.SetToolTip(_rbMarginOn, "图框按 10mm 边距居中打印。");

		_rbMarginOff = new RadioButton
		{
			Text = "无边距（贴边）",
			AutoSize = true,
			Margin = new Padding(0, 10, 18, 0)
		};
		toolTip.SetToolTip(_rbMarginOff, "图框贴纸边打印，不留边距。");

		_lblPrint = new Label { Text = "配色：", AutoSize = true, Margin = new Padding(0, 10, 2, 0) };
		_cmbPrint = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 96,
			Margin = new Padding(0, 8, 18, 0)
		};
		_cmbPrint.Items.Add("黑白");
		_cmbPrint.Items.Add("彩色");
		_cmbPrint.SelectedIndex = 0;
		toolTip.SetToolTip(_cmbPrint, "出图配色：黑白 / 彩色。");

		_lblMode = new Label { Text = "模式：", AutoSize = true, Margin = new Padding(0, 10, 2, 0) };
		_cmbMode = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 96,
			Margin = new Padding(0, 8, 4, 0)
		};
		_cmbMode.Items.Add("通用版");
		_cmbMode.Items.Add("公司版");
		_cmbMode.SelectedIndex = 1;
		toolTip.SetToolTip(_cmbMode, "通用版：逐层识别图框，通用性强。\r\n公司版：按公司模板库快速匹配，速度更快。");

		tblOptions.Controls.Add(_rbMarginOn, 0, 0);
		tblOptions.Controls.Add(_rbMarginOff, 1, 0);
		tblOptions.Controls.Add(_lblPrint, 2, 0);
		tblOptions.Controls.Add(_cmbPrint, 3, 0);
		tblOptions.Controls.Add(_lblMode, 4, 0);
		tblOptions.Controls.Add(_cmbMode, 5, 0);
		// v4.1：第二行——显示线宽 + 质量核查两个选项。
		// 并发参数沿用 v4.0 实测最优值（12/4/8），Concurrency.cs 硬编码内置、
		// UI 不可调（曾计划并发设置数值框 + settings.json，已评估否决未落地）
		_chkLineWeight = new CheckBox
		{
			Text = "显示线宽",
			AutoSize = true,
			Margin = new Padding(0, 6, 18, 0)
		};
		toolTip.SetToolTip(_chkLineWeight, "勾选后按图纸线宽显示；默认统一粗细。");

		_chkQualityCheck = new CheckBox
		{
			Text = "质量核查",
			AutoSize = true,
			Margin = new Padding(0, 6, 18, 0),
			Checked = true
		};
		toolTip.SetToolTip(_chkQualityCheck, "开启后自动检测 PDF 内容完整性；不达标判失败并回退重出。默认开启。");
		_chkBlockFrame = new CheckBox
		{
			Text = "识别块图框",
			AutoSize = true,
			Margin = new Padding(0, 6, 0, 0),
			// v4.12 修订：默认由勾选改为不勾选（识别块图框会明显拖慢识别，实测 +45%）；
			// 实际状态由 ApplyUiState 按 settings.json 恢复（本处仅为默认值）
			Checked = false
		};
		toolTip.SetToolTip(_chkBlockFrame, "图框为块（INSERT）的图纸可启用。");

		// v4.17 修订五：行 2 三个开关装进横向流式容器，整体跨 6 列放在第 2 行，
		// 从而不参与列宽计算、且从栅格左缘起排（与行 1 左缘对齐）。
		var optRow2 = new FlowLayoutPanel
		{
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Anchor = AnchorStyles.Left | AnchorStyles.Top,
			Margin = new Padding(0)
		};
		optRow2.Controls.Add(_chkLineWeight);
		optRow2.Controls.Add(_chkQualityCheck);
		optRow2.Controls.Add(_chkBlockFrame);
		tblOptions.Controls.Add(optRow2, 0, 1);
		tblOptions.SetColumnSpan(optRow2, 6);
		_cardOptions.Controls.Add(tblOptions);

		// ---------- 3) 进度与操作（v3.9：+阶段总览条 + 当前阶段详情） ----------
		_cardProgress = new CardPanel
		{
			Title = "进度",
			Dock = DockStyle.Top,
			// v4.17 修订四：按钮行移出后高度 165 → 135（扣掉按钮行 30）
			Height = 135,
			Padding = new Padding(12, 4, 12, 6)
		};
		// v3.9：阶段总览条（5 段横排：准备→图框识别→页面设置→出图→收尾）。
		// 运行前完整展示让用户知道后续步骤；运行中当前阶段高亮、完成变绿、跳过转警告色。
		// v4.17 修订六（C 档·P3-3）：由"5 个白底方框 Label"改为自绘胶囊条（PhaseChipBar）——
		// 方框造型被读成"5 个禁用输入框"，胶囊 + 连接线才读得出"流程进度"。
		_phaseBar = new PhaseChipBar(PhaseNames, PhaseTooltips)
		{
			Dock = DockStyle.Top,
			Height = UiTheme.HeightPhaseChip,
			Margin = new Padding(0, 0, 0, 4)
		};
		// v3.9：当前阶段详情（替代原"正在准备…"弹窗文字，随阶段实时刷新）
		_lblPhaseDetail = new Label
		{
			Dock = DockStyle.Top,
			Height = 18,
			Text = "就绪：添加 dwg 文件、选择输出文件夹后点击“开始转换”。",
			TextAlign = ContentAlignment.MiddleLeft,
			Font = UiTheme.FontSmall,
			ForeColor = UiTheme.TextMuted,
			Margin = new Padding(0, 1, 0, 0)
		};
		// v3.9（双进度条）：总进度条 + 阶段进度条，各带左侧语义标签。
		// 总进度=整个批次按阶段权重推进（用户感知整体走到哪）；阶段进度=当前阶段内 x/y。
		// v3.5.1：进度条 Top + 按钮行 Top/Left，避免 TableLayoutPanel Percent+AutoSize
		// 在 GroupBox 嵌套时被压缩导致按钮栏看不见的问题。
		// v4.17 修订六（C 档·P3-5/P3-8）：自绘扁平进度条。
		// 总进度 = 主色、阶段进度 = 浅主色 —— 两条"一眼可分"（原两条系统绿块长得完全一样）；
		// 右侧自带百分比数字（原实现没有任何数字，用户看不出具体进度）。
		_progressTotal = new FlatProgressBar
		{
			Dock = DockStyle.Fill,
			Minimum = 0,
			Maximum = 100,
			Margin = new Padding(0),
			BarColor = UiTheme.Primary
		};
		_progressPhase = new FlatProgressBar
		{
			Dock = DockStyle.Fill,
			Minimum = 0,
			Maximum = 100,
			Margin = new Padding(0),
			BarColor = UiTheme.PrimarySoft
		};
		var lblTotal = new Label
		{
			Text = "总进度",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = UiTheme.FontSmall,
			ForeColor = UiTheme.TextMuted,
			Margin = new Padding(0)
		};
		var lblPhase = new Label
		{
			Text = "阶段进度",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = UiTheme.FontSmall,
			ForeColor = UiTheme.TextMuted,
			Margin = new Padding(0)
		};
		var pctBar = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			// v4.17 修订四（P1-3）：48 → 44。两行各 22 正好；48 时 TLP 把多出的 4px 补给末行，
			// 实测两条进度条 28px vs 33px 不等高。
			Height = 44,
			ColumnCount = 2,
			RowCount = 2,
			Margin = new Padding(0, 2, 0, 2)
		};
		// v4.17 修订四（P1-2）：54 逻辑宽放不下 8.5pt 的"阶段进度"4 字（与进度条左缘重叠 1px 被裁）→ 64
		pctBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
		pctBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		pctBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
		pctBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
		pctBar.Controls.Add(lblTotal, 0, 0);
		pctBar.Controls.Add(_progressTotal, 1, 0);
		pctBar.Controls.Add(lblPhase, 0, 1);
		pctBar.Controls.Add(_progressPhase, 1, 1);
		// v4.17 修订四（P1-4 + B 档·主次区分）：五个按钮统一宽 120（原 105/113/130/150 参差）。
		// v4.17 修订六（C 档·P3-1/P3-7 收口）：改"实心主色 + 白底描边"两级按钮层级——
		//   主按钮（开始转换）= 实心主色底 + 白字 + 加粗；
		//   次按钮            = 白底 + 统一描边 + 正文色，禁用时文字转灰（原禁用态几乎看不出）。
		int btnW = 120, btnH = UiTheme.HeightButton, btnGap = UiTheme.Space2;
		// v4.17 修订七（C 档·§5.6）：主按钮带加速键 Alt+S（文案 "开始转换(S)"，S 带下划线）
		_btnStart = new Button { Text = "开始转换(&S)", Width = btnW, Height = btnH, Margin = new Padding(0, 0, btnGap, 0) };
		ApplyPrimaryButton(_btnStart);
		_btnStart.Click += delegate { Start(); };
		_btnStop = new Button { Text = "停止", Width = btnW, Height = btnH, Enabled = false, Margin = new Padding(0, 0, btnGap, 0) };
		ApplySecondaryButton(_btnStop);
		_btnStop.Click += delegate
		{
			if (_converting) { StopRequested(); }
			else { Continue(); }
		};
		_btnClear = new Button { Text = "清空清单", Width = btnW, Height = btnH, Margin = new Padding(0, 0, btnGap, 0) };
		ApplySecondaryButton(_btnClear);
		_btnClear.Click += delegate { ClearList(); };
		_btnOpenOut = new Button { Text = "打开输出文件夹", Width = btnW, Height = btnH, Enabled = false, Margin = new Padding(0, 0, btnGap, 0) };
		ApplySecondaryButton(_btnOpenOut);
		_btnOpenOut.Click += delegate { OpenOutputFolder(); };

		// v4.17 修订六（C 档·§5.7）："设置…"从操作条移出，改到顶部标题条右端（见下方 §6）。
		// 操作条只保留 4 个流程按钮——高频"开始转换"不再与低频配置同权重并列。
		var buttonsRow = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			// v4.5：移除 v4.4 的"重试失败项"按钮（用户反馈多余）。
			// v4.17 修订六：5 列 → 4 列（设置按钮已移出操作条）
			ColumnCount = 4,
			RowCount = 1,
			Height = 30,
			Margin = new Padding(0)
		};
		for (int i = 0; i < 4; i++) buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		buttonsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		buttonsRow.Controls.Add(_btnStart, 0, 0);
		buttonsRow.Controls.Add(_btnStop, 1, 0);
		buttonsRow.Controls.Add(_btnClear, 2, 0);
		buttonsRow.Controls.Add(_btnOpenOut, 3, 0);
		// v3.9 布局顺序（自上而下）：阶段条 → 双进度条 → 阶段详情。
		// WinForms Dock=Top：后 Add 的显示在最上（v3.8.1 buttonsRow→_progress 实测），故逆序 Add。
		// v4.17 修订四（B 档·信息层级）：按钮行从"进度"组移出，独立为紧邻其下的操作条——
		// "进度"组只承担展示语义（阶段条 + 双进度 + 阶段详情），操作按钮不再混在进度组内。
		_cardProgress.Controls.Add(_lblPhaseDetail);
		_cardProgress.Controls.Add(pctBar);
		_cardProgress.Controls.Add(_phaseBar);
		Panel actionBar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 46,
			BackColor = UiTheme.PageBackground,
			Padding = new Padding(10, 8, 10, 8)
		};
		buttonsRow.Dock = DockStyle.Left;
		buttonsRow.Width = 4 * 120 + 3 * 8;
		actionBar.Controls.Add(buttonsRow);

		// ---------- 4) 三栏列表区（填充剩余空间） ----------
		// v4.17 修订六（C 档·§5.4）：三栏栏标题由"Label"改为自绘 ColumnHeader——
		// 状态色圆点（待处理灰 / 成功绿 / 失败红）+ 计数徽标（胶囊），状态与条数一眼可读。
		// v4.8：三栏头部统一 24px 高（原成功/待处理 19px vs 失败栏 24px，表头错位）。
		_hdrPending = new ColumnHeader
		{
			Title = "待处理",
			DotColor = UiTheme.TextDisabled,
			BadgeBack = UiTheme.Track,
			BadgeFore = UiTheme.TextMuted,
			Dock = DockStyle.Top
		};
		// v4.12：新增"合并待转换"——与"合并失败文件"同样式：把待处理栏的文件复制到一个文件夹，
		// 便于在大文件超限退回、或分批复转时单独处理这一批。
		_btnCopyPending = new Button
		{
			Text = "合并待转换",
			// v4.12：按钮靠栏位右侧（用户要求），栏标题仍在左
			Dock = DockStyle.Right,
			AutoSize = true,
			Margin = new Padding(2, 0, 4, 0),
			Enabled = false
		};
		_btnCopyPending.Click += delegate { CopyPendingToFolder(); };
		_hdrPending.Controls.Add(_btnCopyPending);
		// v4.17 修订四（P1-1）：三栏统一为"Dock=Top 的标题条 + Dock=Fill 的表格"。
		// v4.17 修订六（C 档·§5.4）：标题条改自绘 ColumnHeader（圆点 + 计数徽标）。
		_hdrSuccess = new ColumnHeader
		{
			Title = "成功",
			DotColor = UiTheme.Success,
			BadgeBack = UiTheme.SuccessLight,
			BadgeFore = UiTheme.SuccessDark,
			Dock = DockStyle.Top
		};
		_hdrFail = new ColumnHeader
		{
			Title = "失败",
			DotColor = UiTheme.Danger,
			BadgeBack = UiTheme.DangerLight,
			BadgeFore = UiTheme.DangerDark,
			Dock = DockStyle.Top
		};
		_btnCopyFailed = new Button
		{
			Text = "合并失败文件",
			// v4.12：按钮靠栏位右侧（用户要求），栏标题仍在左
			Dock = DockStyle.Right,
			AutoSize = true,
			Margin = new Padding(2, 0, 4, 0),
			Enabled = false
		};
		_btnCopyFailed.Click += delegate { CopyFailedToFolder(); };
		_hdrFail.Controls.Add(_btnCopyFailed);
		_gridPending = BuildGrid(isFail: false);
		_gridSuccess = BuildGrid(isFail: false);
		_gridFail = BuildGrid(isFail: true);
		_gridPending.Dock = DockStyle.Fill;
		_gridSuccess.Dock = DockStyle.Fill;
		_gridFail.Dock = DockStyle.Fill;
		_gridFail.CellContentClick += (sender, e) =>
		{
			if (e.RowIndex < 0) return;
			if (e.ColumnIndex == _gridFail.Columns["name"].Index)
			{
				if (_gridFail.Rows[e.RowIndex].Tag is string p && File.Exists(p))
				{
					try { Process.Start("explorer.exe", "/select,\"" + p + "\""); }
					catch { }
				}
				return;
			}
			// v4.17 修订七（C 档·§5.5）：点「详情」链接 → 在失败栏下方展开/收起只读详情面板
			if (_gridFail.Columns.Contains("detail") && e.ColumnIndex == _gridFail.Columns["detail"].Index)
			{
				ToggleFailDetail(e.RowIndex);
			}
		};
		// v4.17 修订六：三栏容器底色统一为卡片白——ColumnHeader 用 EffectiveBackground 沿父链取底，
		// 父链上若还是系统 Control 灰，圆点/文字就会浮在灰块上（与修订四 P0-1 同族坑）。
		Panel pendingPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
		pendingPanel.Controls.Add(_gridPending);
		pendingPanel.Controls.Add(_hdrPending);
		Panel successPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
		successPanel.Controls.Add(_gridSuccess);
		successPanel.Controls.Add(_hdrSuccess);

		// v4.17 修订七（C 档·§5.5）：失败详情面板（默认隐藏，点失败行「详情」链接时展开）。
		// 贴在失败栏下方（Dock=Bottom），展开时只压缩上方失败表格，不影响其它两栏。
		_lblFailDetailTitle = new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			Font = UiTheme.FontSmall,
			ForeColor = UiTheme.DangerDark,
			Margin = new Padding(0)
		};
		var btnFailDetailClose = new Button
		{
			Text = "收起",
			Dock = DockStyle.Right,
			Width = 56,
			Height = 22,
			Margin = new Padding(4, 0, 0, 0),
			FlatStyle = FlatStyle.Flat
		};
		btnFailDetailClose.Click += delegate { HideFailDetail(); };
		var failDetailHeader = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = UiTheme.DangerLight };
		// Dock 按 z-order 逆序布局：Fill 的标题先 Add，否则会被 Right 的按钮挤没（手册 5.10）。
		failDetailHeader.Controls.Add(_lblFailDetailTitle);
		failDetailHeader.Controls.Add(btnFailDetailClose);

		_txtFailDetail = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			WordWrap = false,
			ScrollBars = ScrollBars.Both,
			BackColor = UiTheme.ReadOnlyBackground,
			Font = UiTheme.FontMono,
			BorderStyle = BorderStyle.FixedSingle,
			Margin = new Padding(0)
		};
		_failDetailPanel = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 132,
			BackColor = UiTheme.DangerLight,
			Padding = new Padding(8, 4, 8, 8),
			Visible = false
		};
		// Fill 的正文先 Add（布局时最后处理、拿到剩余高度），Top 的表头后 Add。
		_failDetailPanel.Controls.Add(_txtFailDetail);
		_failDetailPanel.Controls.Add(failDetailHeader);

		_splitBottom = new SplitContainer
		{
			Orientation = Orientation.Vertical,
			SplitterWidth = 4,
			Dock = DockStyle.Fill,
			// v4.17 修订六：分隔线取页面底色，在白色卡片上呈浅灰细线（原系统 Control 灰偏重）
			BackColor = UiTheme.PageBackground
		};
		_splitBottom.Panel1.BackColor = UiTheme.CardBackground;
		_splitBottom.Panel2.BackColor = UiTheme.CardBackground;
		_splitBottom.Panel1.Controls.Add(successPanel);
		// Add 顺序决定 Dock 布局次序（后 Add 的先占位）：_gridFail 先 Add → 最后布局、拿到 Fill 剩余区；
		// _failDetailPanel 次之 → Bottom；_hdrFail 最后 → Top。顺序错会让详情面板把表格挤没。
		_splitBottom.Panel2.Controls.Add(_gridFail);
		_splitBottom.Panel2.Controls.Add(_failDetailPanel);
		_splitBottom.Panel2.Controls.Add(_hdrFail);
		_splitTop = new SplitContainer
		{
			Orientation = Orientation.Vertical,
			SplitterWidth = 4,
			Dock = DockStyle.Fill,
			BackColor = UiTheme.PageBackground
		};
		_splitTop.Panel1.BackColor = UiTheme.CardBackground;
		_splitTop.Panel2.BackColor = UiTheme.CardBackground;
		_splitTop.Panel1.Controls.Add(pendingPanel);
		_splitTop.Panel2.Controls.Add(_splitBottom);

		// ---------- 5) v3.9：日志面板（列表区下方，可上下拖拽调整高度） ----------
		_txtLog = new RichTextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			BackColor = UiTheme.LogBackground,
			Font = UiTheme.FontMono,
			BorderStyle = BorderStyle.None,
			WordWrap = true
		};
		// v4.17：日志只在界面显示，不再写入输出文件夹（用户要求：去掉输出文件夹的 log 文件）
		_txtLog.AppendText("—— 转换日志 ——\r\n开始转换后，引擎日志将实时显示在这里。\r\n");
		// v4.15：日志跟随开关（默认开启；上翻日志时自动暂停，回到底部自动恢复）
		// v4.17 修订六（C 档·§5.1）：日志区改为 Subtle 卡片，标题由 CardPanel 绘制；
		// 复选框不再单独占一行表头，改手工定位到卡片标题条右端（Resize 时重算）。
		_chkAutoScroll = new CheckBox
		{
			Text = "自动滚动",
			Width = 86,
			// 高度须 ≤ 卡片标题带（CardPanel.TitleHeight=16 + Padding.Top=8 = 24），
			// 否则会压到下方的日志正文（实测 22 高时会与 RichTextBox 顶部重叠 7px）
			Height = 20,
			Checked = true,
			Margin = new Padding(0),
			TextAlign = ContentAlignment.MiddleLeft,
			Font = UiTheme.FontSmall,
			ForeColor = UiTheme.TextMuted
		};
		_logCard = new CardPanel
		{
			Title = "日志（实时显示）",
			Subtle = true,
			Dock = DockStyle.Fill,
			Padding = new Padding(10, 8, 10, 8)
		};
		_logCard.Controls.Add(_txtLog);
		_logCard.Controls.Add(_chkAutoScroll);
		// 复选框贴在卡片标题条右端：位置依赖卡片宽度，故在 Resize 时重算（幂等）
		_logCard.Resize += delegate { PlaceLogAutoScroll(); };
		// v4.17 修订六（C 档·§5.1）：列表区整体降级为 Subtle 卡片（标题"文件清单"）。
		_listCard = new CardPanel
		{
			Title = "文件清单",
			Subtle = true,
			Dock = DockStyle.Fill,
			Padding = new Padding(8, 2, 8, 8)
		};
		_listCard.Controls.Add(_splitTop);
		_splitLog = new SplitContainer
		{
			Orientation = Orientation.Horizontal,
			SplitterWidth = 5,
			Dock = DockStyle.Fill,
			Panel1MinSize = 140,
			Panel2MinSize = 60,
			BackColor = UiTheme.PageBackground
		};
		_splitLog.Panel1.BackColor = UiTheme.PageBackground;
		_splitLog.Panel2.BackColor = UiTheme.PageBackground;
		_splitLog.Panel1.Controls.Add(_listCard);
		_splitLog.Panel2.Controls.Add(_logCard);

		StatusStrip statusStrip = new StatusStrip();
		_lblStatus = new ToolStripStatusLabel { Text = "就绪" };
		statusStrip.Items.Add(_lblStatus);
		statusStrip.Dock = DockStyle.Bottom;

		// ---------- 6) v4.17 修订六（C 档·§5.7）：客户区顶部标题条 ----------
		// 低频配置（设置…）从操作条移到标题条右端（图标 + 文字，热区 ≥ 32×32）；
		// 操作条因此只留 4 个流程按钮，高频"开始转换"的权重不再被配置项稀释。
		_btnSettings = new Button
		{
			Text = "⚙ 设置",
			Width = 84,
			Height = UiTheme.HeightButton,
			Margin = new Padding(0),
			Dock = DockStyle.Right
		};
		ApplySecondaryButton(_btnSettings);
		_btnSettings.Click += OpenSettings;
		var lblAppTitle = new Label
		{
			Text = "DWG 批量转 PDF",
			Dock = DockStyle.Left,
			AutoSize = true,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = UiTheme.FontBody,
			ForeColor = UiTheme.Text,
			Margin = new Padding(0)
		};
		Panel headerBar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 40,
			BackColor = UiTheme.PageBackground,
			Padding = new Padding(12, 5, 12, 5)
		};
		// v4.17 修订四（P0-1）教训：同一容器内 Dock=Top 的标题 + Dock=Right 的按钮，
		// 必须标题先 Add（Dock 按 z-order 逆序布局，后 Add 的先占位）。
		headerBar.Controls.Add(lblAppTitle);
		headerBar.Controls.Add(_btnSettings);

		// Dock 布局：Fill 先加，顶部组依次，状态栏最后（占据最底部）。
		// v4.17 修订六（C 档·§5.1）：卡片之间插 10px 间隙 Panel——卡片浮在 #F5F5F3 页面底色上，
		// 靠"间隙 + 圆角"分层，不用阴影（设计稿 §二 明确不用阴影）。
		base.Controls.Add(_splitLog);
		base.Controls.Add(actionBar);
		base.Controls.Add(MakeCardGap());
		base.Controls.Add(_cardProgress);
		base.Controls.Add(MakeCardGap());
		base.Controls.Add(_cardOptions);
		base.Controls.Add(MakeCardGap());
		base.Controls.Add(_cardInput);
		base.Controls.Add(MakeCardGap());
		base.Controls.Add(headerBar);
		base.Controls.Add(statusStrip);
		// v3.9：日志区初始约 120px 高（面板1 三栏列表占剩余），随后可拖拽调整。
		// 注意：构造函数阶段窗体 Handle 尚未创建，禁止 BeginInvoke/Invoke，
		// 故延后到 Shown 事件；只在首次 Shown 时设固定距离，之后走 UpdateListBounds 按比例保持。
		AllowDrop = true;
		DragEnter += OnDragEnter;
		DragDrop += OnDragDrop;
		Resize += (senderForm, e) => UpdateListBounds();
		Shown += (senderForm, e) =>
		{
			if (!_initialLogSplitterSet && _splitLog != null && _splitLog.Height > 200)
			{
				try { _splitLog.SplitterDistance = _splitLog.Height - 130; } catch { }
				_initialLogSplitterSet = true;
			}
			// v4.8：三栏列表首次显示时三等分（待处理 1/3，成功/失败平分其余 2/3；
			// SplitContainer 默认各按 50% 分割，实际为 50%/25%/25%，用户反馈不统一）
			if (!_initialListSplitterSet && _splitTop != null && _splitTop.Width > 200)
			{
				try { _splitTop.SplitterDistance = _splitTop.Width / 3; } catch { }
				try { _splitBottom.SplitterDistance = _splitBottom.Width / 2; } catch { }
				_initialListSplitterSet = true;
			}
			UpdateListBounds();
			// v4.17 修订四：窗口打开时不预置焦点——否则"设置…"按钮会带蓝色焦点框，看起来像默认操作
			ActiveControl = null;
		};
		Shown += async (senderForm, e) => await RunFirstUseSelfCheckAsync();
		SetPhaseBarIdle();
		RefreshListUi();

		// ---------- v4.17 修订七（C 档·§5.6）：键盘可达性 ----------
		// 回车＝开始转换；Esc＝停止当前转换（KeyPreview 让窗体先于子控件收键，
		// 否则焦点落在列表/日志里时 Esc 会被子控件吞掉）。
		AcceptButton = _btnStart;
		KeyPreview = true;
		KeyDown += OnMainFormKeyDown;

		// 显式 TabIndex：输入区 → 选项区 → 操作条 → 设置 → 列表 → 日志。
		// 顶层容器必须显式设——默认 TabIndex 随 Add 顺序，而 Dock 布局是逆序 Add，
		// _splitLog 最先 Add → 默认会排到最前，与视觉顺序相反。
		_cardInput.TabIndex = 0;
		_cardOptions.TabIndex = 1;
		_cardProgress.TabIndex = 2;
		actionBar.TabIndex = 3;
		headerBar.TabIndex = 4;
		_splitLog.TabIndex = 5;
		// 输入区（容器内相对序号）
		_txtInput.TabIndex = 0;
		_btnInput.TabIndex = 1;
		_btnInputFolder.TabIndex = 2;
		_txtOutput.TabIndex = 3;
		_btnOutput.TabIndex = 4;
		// 选项区
		_rbMarginOn.TabIndex = 0;
		_rbMarginOff.TabIndex = 1;
		_cmbPrint.TabIndex = 2;
		_cmbMode.TabIndex = 3;
		optRow2.TabIndex = 4;
		_chkLineWeight.TabIndex = 0;
		_chkQualityCheck.TabIndex = 1;
		_chkBlockFrame.TabIndex = 2;
		// 操作条
		_btnStart.TabIndex = 0;
		_btnStop.TabIndex = 1;
		_btnClear.TabIndex = 2;
		_btnOpenOut.TabIndex = 3;
		// 标题条
		_btnSettings.TabIndex = 0;
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
			catch { }
		});
	}

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

	private void StopRequested()
	{
		_engine.Cancel();
		_btnStop.Enabled = false;
		_btnStop.Text = "正在停止…";
		_lblStatus.Text = "停止指令已下达：正在结束工具自启的 AutoCAD 进程，稍候…";
	}

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

	private void OnDragEnter(object sender, DragEventArgs e)
	{
		if (_converting || !e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.None;
			return;
		}
		e.Effect = DragDropEffects.Copy;
	}

	private void OnDragDrop(object sender, DragEventArgs e)
	{
		if (_converting)
		{
			return;
		}
		if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
		{
			AddFiles(files);
		}
	}

	private void PasteFromClipboard()
	{
		if (_converting)
		{
			return;
		}
		try
		{
			List<string> paths = new List<string>();
			if (Clipboard.ContainsFileDropList())
			{
				foreach (string s in Clipboard.GetFileDropList())
				{
					paths.Add(s);
				}
			}
			else if (Clipboard.ContainsText())
			{
				foreach (string line in Clipboard.GetText().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
				{
					paths.Add(line.Trim());
				}
			}
			AddFiles(paths);
		}
		catch
		{
		}
	}

	private void AddFilesDialog()
	{
		if (_converting)
		{
			return;
		}
		using OpenFileDialog ofd = new OpenFileDialog
		{
			Title = "选择 dwg 图纸",
			Filter = "DWG 图纸 (*.dwg)|*.dwg|所有文件 (*.*)|*.*",
			Multiselect = true
		};
		if (ofd.ShowDialog(this) == DialogResult.OK)
		{
			AddFiles(ofd.FileNames);
		}
	}

	/// <summary>选择整个文件夹，把其中（含所有子文件夹）的 dwg 一并加入清单。</summary>
	private void AddFolderDialog()
	{
		if (_converting)
		{
			return;
		}
		using FolderBrowserDialog fbd = new FolderBrowserDialog
		{
			Description = "选择包含 DWG 图纸的文件夹（含所有子文件夹）",
			ShowNewFolderButton = true
		};
		if (fbd.ShowDialog(this) == DialogResult.OK)
		{
			AddFiles(new[] { fbd.SelectedPath });
		}
	}

	private void AddFiles(IEnumerable<string> paths)
	{
		if (paths == null || _converting)
		{
			return;
		}
		List<string> batch = new List<string>();
		foreach (string raw in paths)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				continue;
			}
			string path = raw.Trim().Trim('"');
			if (Directory.Exists(path))
			{
				foreach (string f in Directory.EnumerateFiles(path, "*.dwg", SearchOption.AllDirectories))
				{
					AddToBatch(batch, f);
				}
			}
			else if (File.Exists(path) && path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
			{
				AddToBatch(batch, path);
			}
		}
		if (batch.Count > 0)
		{
			PushUndo(new UndoEntry { Added = batch });
			RefreshListUi();
		}
	}

	private void AddToBatch(List<string> batch, string path)
	{
		if (_converting)
		{
			return;
		}
		string full = Path.GetFullPath(path);
		if (_pendingFiles.Contains(full, StringComparer.OrdinalIgnoreCase) || batch.Contains(full, StringComparer.OrdinalIgnoreCase))
		{
			return;
		}
		_pendingFiles.Add(full);
		batch.Add(full);
	}

	private void ClearList()
	{
		if (_converting || _pendingFiles.Count == 0)
		{
			return;
		}
		PushUndo(new UndoEntry { Cleared = new List<string>(_pendingFiles) });
		_pendingFiles.Clear();
		_statusByPath.Clear();
		_noteByPath.Clear();
		RefreshListUi();
	}

	private void PushUndo(UndoEntry e)
	{
		_undoStack.Add(e);
		if (_undoStack.Count > MaxUndo)
		{
			_undoStack.RemoveAt(0);
		}
	}

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
			// v4.3：高质量打印阈值（>该值用 HQ pc3）
			HighQualityThresholdBytes = settings.HighQualityThresholdBytes,
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

		// v4.3：从 %APPDATA%\DwgToPdf\settings.json 加载用户设置；首次启动自动生成默认
		private void LoadAppSettings()
	{
		try { _appSettings = AppSettings.Load(); }
		catch { _appSettings = new AppSettings(); }
	}

	/// <summary>v4.6：启动时恢复上次使用状态——输出文件夹、主界面选项（边距/配色/模式/
	/// 显示线宽/质量核查/识别块图框）、窗口大小与位置（含最大化）。恢复失败静默走默认。</summary>
	private void ApplyUiState()
	{
		var s = _appSettings;
		if (s == null) return;
		try
		{
			if (!string.IsNullOrEmpty(s.LastOutputDir) && Directory.Exists(s.LastOutputDir))
			{
				_txtOutput.Text = s.LastOutputDir;
			}
			_rbMarginOn.Checked = s.MarginOn;
			_rbMarginOff.Checked = !s.MarginOn;
			if (s.ColorIndex >= 0 && s.ColorIndex < _cmbPrint.Items.Count) _cmbPrint.SelectedIndex = s.ColorIndex;
			if (s.ModeIndex >= 0 && s.ModeIndex < _cmbMode.Items.Count) _cmbMode.SelectedIndex = s.ModeIndex;
			_chkLineWeight.Checked = s.LineWeight;
			_chkQualityCheck.Checked = s.QualityCheck;
			_chkBlockFrame.Checked = s.BlockFrame;
			if (s.WinLeft.HasValue && s.WinTop.HasValue && s.WinWidth.HasValue && s.WinHeight.HasValue)
			{
				var r = new Rectangle(s.WinLeft.Value, s.WinTop.Value, s.WinWidth.Value, s.WinHeight.Value);
				// 防呆：尺寸过小或完全在虚拟屏幕外（换了显示器/断投屏）则不恢复，保持默认居中
				var vs = SystemInformation.VirtualScreen;
				if (r.Width >= 400 && r.Height >= 300 && r.IntersectsWith(vs))
				{
					StartPosition = FormStartPosition.Manual;
					Bounds = r;
				}
			}
			if (s.WinMaximized) WindowState = FormWindowState.Maximized;
		}
		catch { }
	}

	/// <summary>v4.6：关闭窗口时把当前界面状态写回 settings.json（下次启动自动恢复）。
	/// 最大化时记录 RestoreBounds（还原后的正常位置），并单独记录最大化标志。</summary>
	private void SaveUiState()
	{
		var s = _appSettings ?? new AppSettings();
		_appSettings = s;
		try
		{
			s.LastOutputDir = _txtOutput.Text.Trim();
			s.MarginOn = _rbMarginOn.Checked;
			s.ColorIndex = _cmbPrint.SelectedIndex;
			s.ModeIndex = _cmbMode.SelectedIndex;
			s.LineWeight = _chkLineWeight.Checked;
			s.QualityCheck = _chkQualityCheck.Checked;
			s.BlockFrame = _chkBlockFrame.Checked;
			Rectangle r = (WindowState == FormWindowState.Normal) ? Bounds : RestoreBounds;
			s.WinLeft = r.Left;
			s.WinTop = r.Top;
			s.WinWidth = r.Width;
			s.WinHeight = r.Height;
			s.WinMaximized = (WindowState == FormWindowState.Maximized);
			s.Save();
		}
		catch { }
	}

	// v4.3：打开设置窗口，编辑后保存到 settings.json 并立即影响下次转换
	private void OpenSettings(object sender, EventArgs e)
	{
		using (var dlg = new SettingsForm(_appSettings ?? new AppSettings()))
		{
			if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultSettings != null)
			{
				_appSettings = dlg.ResultSettings;
				_lblStatus.Text = "设置已保存：" + AppSettings.DefaultPath;
			}
		}
	}

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

	private async System.Threading.Tasks.Task RunFirstUseSelfCheckAsync()
	{
		if (EnvironmentSelfCheck.PassedBefore())
		{
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
				result = await System.Threading.Tasks.Task.Run(EnvironmentSelfCheck.Run);
			}
			finally
			{
				try { dlg.Close(); } catch { }
			}
		}
		switch (result)
		{
			case EnvironmentSelfCheck.Result.Ok:
				_lblStatus.Text = "环境自检通过。";
				BgLogger.Info("环境自检通过。");
				break;
			case EnvironmentSelfCheck.Result.Repaired:
				_lblStatus.Text = "环境自检自动修复后通过。";
				BgLogger.Info("环境自检失败后已自动 /regserver 修复并通过。");
				MessageBox.Show(this, "检测到本机 AutoCAD 连接异常，已自动修复并连接成功。", "环境自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
				break;
			case EnvironmentSelfCheck.Result.Failed:
				_lblStatus.Text = "环境自检失败（详见运行日志）。";
				BgLogger.Error("环境自检失败：" + EnvironmentSelfCheck.ReportPath);
				MessageBox.Show(this,
					"无法连接到本机 AutoCAD。\n\n" +
					"已尝试自动修复仍未成功，详细原因见：\n" + EnvironmentSelfCheck.ReportPath +
					"\n\n请按报告中的步骤修复（手动打开一次 AutoCAD，或管理员运行 acad.exe /regserver）后重试。",
					"环境自检失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
				break;
		}
	}
}
