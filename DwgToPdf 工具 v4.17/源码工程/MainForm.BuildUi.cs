using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十三：从 MainForm.cs 拆分出「界面构建」方法（纯移动，不改逻辑）：
// BuildUi（665 行，MainForm.cs 中最大的单一方法）。
// 搬迁依据：CQ-08（MainForm.cs ≤1400 行）；本批后 MainForm.cs 1863 → 1197，达成验收线。
internal sealed partial class MainForm
{
	/// <summary>构建主窗体全部界面（由构造函数在 LoadAppSettings 之后调用）：依次搭出顶部「输入输出 / 选项 / 模式」三张卡片、中部三栏文件列表与日志面板、底部状态栏，并把各控件赋给字段、挂接事件。本方法约 665 行，B5（修订十三）已将其单独拆到本文件。</summary>
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
					// best-effort：界面操作失败不影响功能
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
				// best-effort：界面操作失败不影响功能
				try { _splitLog.SplitterDistance = _splitLog.Height - 130; } catch { }
				_initialLogSplitterSet = true;
			}
			// v4.8：三栏列表首次显示时三等分（待处理 1/3，成功/失败平分其余 2/3；
			// SplitContainer 默认各按 50% 分割，实际为 50%/25%/25%，用户反馈不统一）
			if (!_initialListSplitterSet && _splitTop != null && _splitTop.Width > 200)
			{
				// best-effort：界面操作失败不影响功能
				try { _splitTop.SplitterDistance = _splitTop.Width / 3; } catch { }
				// best-effort：界面操作失败不影响功能
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
}
