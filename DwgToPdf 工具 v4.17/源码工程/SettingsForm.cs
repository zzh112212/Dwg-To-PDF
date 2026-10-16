using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

/// <summary>v4.5 设置窗口（独立 Form）。
/// v4.17 修订七（C 档·§6）：原 3 个 TabPage（质量审查 / 块图框 / 打印与输出）改为
/// 「左 132px 分类列表 + 右内容区」左右分栏（窗口 560×400），消除 TabPage 下半部的大量留白；
/// 说明文字色统一为 UiTheme.TextMuted(#5F5E5A)（字号沿用窗体字体 9pt = 12px，无需另设）。
/// 启动时从 AppSettings.DefaultPath 加载，关闭/确定时保存到同一路径。
/// 字段值通过 ResultSettings 属性传回 MainForm，MainForm 启动转换时注入到 PrintOptions。
/// v4.5 变更：块名改为"输入框+添加按钮+列表+删除选中"逐条管理（原多行文本框因窗口
/// AcceptButton=确定 导致按回车无法换行）；说明文字缩短并允许换行修复截断；
/// 新增 PDF 输出结构模式（按原文件夹结构 / 全部放在同一文件夹）。</summary>
public class SettingsForm : Form
{
    private readonly AppSettings _original;
    // v4.17 修订七（C 档·§6）：3 个 TabPage 改为「左分类列表 + 右内容区」左右分栏。
    // 只切换内容面板的 Visible，控件实例始终存活（切换分类不会丢未保存的编辑值）。
    private ListBox _lstCategories;
    private Panel _contentHost;
    private Panel _pageQuality, _pageBlock, _pagePrint;
    private Button _btnOk, _btnCancel, _btnReset;

    // Tab 1 质量审查
    private NumericUpDown _numMinFileSize; // 单位 KB

    // Tab 2 块图框（v4.5：输入框 + 添加按钮 + 列表 + 删除选中）
    private TextBox _txtBlockNameInput;
    private ListBox _lstBlocks;

    // Tab 3 打印/输出
    private ComboBox _cmbPdfLayout;        // 0=按原文件夹结构（默认） 1=全部放在同一文件夹
    // v4.8：大文件阈值（MB，0=不限制；v4.12 起=超过则退回待转换）
    private NumericUpDown _numSkipLargeMb;
    // v4.12：单张转换超时秒数（0=不限制；超时中止该张并退回待处理）
    private NumericUpDown _numConvertTimeoutSec;
    private TextBox _txtPdfFolder; // v4.17 修订三：PDF 子文件夹名称模板
    // （v4.15 修订：原 _numHQThreshold"大于1MB采用HQ"设置已删除——HQ 设备为负优化，见手册第28章）
    // v4.3：高质量打印阈值（MB，NumericUpDown）
    // v4.16：_numHQThreshold 控件已随设置项一并删除（HQ 停用，见上方注释）
    // v4.15：含光栅图片图纸的高清出图 dpi（下拉，索引对应 PrintQuality.RasterDpiChoices）
    private ComboBox _cmbRasterDpi;

    /// <summary>用户点"确定"后返回的设置对象；"取消"时为 null。</summary>
    public AppSettings ResultSettings { get; private set; }

    public SettingsForm(AppSettings current)
    {
        _original = current ?? new AppSettings();
        InitializeUI();
        LoadToUI(_original);
    }

    private void InitializeUI()
    {
        Text = "设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        // v4.17 修订七（C 档·§6）：3 个 TabPage 下半部大量留白 → 改左右分栏
        // （左 132px 分类列表 + 右内容区），窗口 480×350 → 560×400。
        ClientSize = new Size(560, 400);
        Font = UiTheme.FontBody;
        BackColor = UiTheme.PageBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.PageBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        Controls.Add(root);

        // ---------- 左右分栏：左分类列表（132px 固定宽）+ 右内容区 ----------
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiTheme.CardBackground
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // 分类列表底色取页面底色，与右侧白色内容区自然分界（无需额外分隔线）。
        _lstCategories = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 34,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            BackColor = UiTheme.PageBackground,
            ForeColor = UiTheme.Text,
            Font = UiTheme.FontBody,
            TabIndex = 0
        };
        _lstCategories.DrawItem += OnDrawCategory;
        _lstCategories.SelectedIndexChanged += OnCategoryChanged;
        body.Controls.Add(_lstCategories, 0, 0);

        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
        _pageQuality = BuildQualityPage();
        _pageBlock = BuildBlockPage();
        _pagePrint = BuildPrintPage();
        // 三个内容面板同为 Dock=Fill：始终只显示一个（切 Visible），故互不干扰。
        _contentHost.Controls.Add(_pageQuality);
        _contentHost.Controls.Add(_pageBlock);
        _contentHost.Controls.Add(_pagePrint);
        body.Controls.Add(_contentHost, 1, 0);
        root.Controls.Add(body, 0, 0);

        // 底部按钮
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 12, 0)
        };
        _btnOk = new Button { Text = "确定", Width = 78, Height = 28 };
        _btnCancel = new Button { Text = "取消", Width = 78, Height = 28 };
        _btnReset = new Button { Text = "恢复默认", Width = 84, Height = 28 };
        _btnOk.Click += OnOk;
        _btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        _btnReset.Click += OnReset;
        btnPanel.Controls.Add(_btnOk);
        btnPanel.Controls.Add(_btnCancel);
        btnPanel.Controls.Add(_btnReset);
        root.Controls.Add(btnPanel, 0, 1);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        _lstCategories.Items.Add("质量审查");
        _lstCategories.Items.Add("识别块图框");
        _lstCategories.Items.Add("打印输出");
        // 默认选中第一项；赋值即触发 OnCategoryChanged，一次性定好三个面板的可见性。
        _lstCategories.SelectedIndex = 0;
    }

    /// <summary>v4.17 修订七（C 档·§6）：左侧分类列表自绘项——
    /// 选中项 PrimaryLight(#E6F1FB) 底 + PrimaryDark(#0C447C) 字，未选中项取页面底色。</summary>
    private void OnDrawCategory(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstCategories.Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color back = selected ? UiTheme.PrimaryLight : UiTheme.PageBackground;
        Color fore = selected ? UiTheme.PrimaryDark : UiTheme.Text;
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
        Rectangle textRect = new Rectangle(e.Bounds.X + 14, e.Bounds.Y,
            Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, _lstCategories.Items[e.Index].ToString(),
            _lstCategories.Font, textRect, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>v4.17 修订七（C 档·§6）：切换分类——只切内容面板可见性，控件实例保持不变。</summary>
    private void OnCategoryChanged(object sender, EventArgs e)
    {
        if (_pageQuality == null || _pageBlock == null || _pagePrint == null) return;
        int i = _lstCategories.SelectedIndex;
        _pageQuality.Visible = (i == 0);
        _pageBlock.Visible = (i == 1);
        _pagePrint.Visible = (i == 2);
        _lstCategories.Invalidate();
    }

    private Panel BuildQualityPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12, 16, 12, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(new Label
        {
            Text = "PDF 最小文件大小（KB）：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _numMinFileSize = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = 1,
            Maximum = 1024 * 10, // 10MB 上限（防呆）
            Value = 10
        };
        layout.Controls.Add(_numMinFileSize, 1, 0);

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简（用户要求：简短、正式）
            Text = "说明：PDF 小于该值即判定内容缺失。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 1);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 1), 2);

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简
            Text = "另含页尺寸、内容流字节数综合判定。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 2);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 2), 2);

        page.Controls.Add(layout);
        return page;
    }

    /// <summary>v4.5：块名逐条管理——单行输入框回车或点"添加"入列表（回车被拦截执行添加，
    /// 不再触发窗口"确定"），列表中选中后可删除。说明文字缩短并 AutoSize 防截断。</summary>
    private Panel BuildBlockPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12, 12, 12, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));               // 说明（可换行）
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));         // 输入行
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));         // 列表
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));         // 删除按钮行

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简（用户要求：简短、正式）
            Text = "图框为块时启用。以下为额外补充的块名：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false
        }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0), 2);

        _txtBlockNameInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F)
        };
        _txtBlockNameInput.KeyDown += OnBlockInputKeyDown;
        layout.Controls.Add(_txtBlockNameInput, 0, 1);

        var btnAdd = new Button { Text = "添加", Dock = DockStyle.Fill, Height = 26 };
        btnAdd.Click += delegate { AddBlockName(); };
        layout.Controls.Add(btnAdd, 1, 1);

        _lstBlocks = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        layout.Controls.Add(_lstBlocks, 0, 2);
        layout.SetColumnSpan(_lstBlocks, 2);

        var btnRemove = new Button { Text = "删除选中", Width = 90, Height = 26, Anchor = AnchorStyles.Left };
        btnRemove.Click += delegate
        {
            if (_lstBlocks.SelectedItem != null) _lstBlocks.Items.Remove(_lstBlocks.SelectedItem);
        };
        layout.Controls.Add(btnRemove, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    /// <summary>回车在块名输入框内执行"添加"，不触发窗口 AcceptButton（确定）。</summary>
    private void OnBlockInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            AddBlockName();
        }
    }

    /// <summary>把输入框中的块名加入列表（去重、忽略大小写；分隔符字符不允许）。</summary>
    private void AddBlockName()
    {
        string name = _txtBlockNameInput.Text.Trim();
        if (name.Length == 0) return;
        if (name.Contains(',') || name.Contains(';'))
        {
            MessageBox.Show(this, "块名不能包含逗号或分号。", "设置",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        bool exists = _lstBlocks.Items.Cast<string>()
            .Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        if (!exists) _lstBlocks.Items.Add(name);
        _txtBlockNameInput.Text = "";
        _txtBlockNameInput.Focus();
    }

    /// <summary>v4.5：打印 Tab 更名"打印/输出"，新增 PDF 输出结构模式（用户可选）。
    /// v4.8：新增大文件阈值（MB，0=不限制）。
    /// v4.12：新增"单张转换超时秒数"（超时中止该张并退回待处理）；删除"长时转换提醒"。
    /// v4.17（2026-10-14，用户反馈两项）：
    ///   ① 删除"高质量打印：已停用…"整行——该功能已退役（见 PrintQuality.IsHighQuality），
    ///      留一行灰字"已停用"没有意义，用户要求不要出现；行索引整体上移，RowCount 9。
    ///   ② 说明文案统一精简为一句、正式表述（原长句在 480 宽窗口易截断）。</summary>
    private Panel BuildPrintPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBackground };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 11,
            Padding = new Padding(12, 16, 12, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));  // 0 PDF 输出结构
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 1 说明
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));  // 2 PDF 子文件夹名称（v4.17 修订三）
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 3 说明（v4.17 修订三）
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));  // 4 大文件阈值（超限退回待转换）
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 5 说明
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));  // 6 单张转换超时（退回待处理）
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 7 说明
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));  // 8 含图片图纸高清出图
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));  // 9 说明
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // 10 填充

        // v4.17：原"高质量打印：已停用（固定使用 DWG TO PDF.pc3…）"占位行已删除
        // （用户要求：已删除的功能不要再出现在设置里）。HQ pc3 停用依据见 PrintQuality.IsHighQuality。

        layout.Controls.Add(new Label
        {
            Text = "PDF 输出结构：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _cmbPdfLayout = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Left,
            Width = 220
        };
        _cmbPdfLayout.Items.Add("按原文件夹结构（默认）");
        _cmbPdfLayout.Items.Add("全部放在同一文件夹");
        layout.Controls.Add(_cmbPdfLayout, 1, 0);

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简
            Text = "说明：同目录输出时同名图纸自动加子目录前缀。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 1);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 1), 2);

        // v4.17 修订三：PDF 子文件夹名称（模板）——每批 PDF 归入独立文件夹
        layout.Controls.Add(new Label
        {
            Text = "PDF 子文件夹名称：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 2);
        _txtPdfFolder = new TextBox
        {
            Dock = DockStyle.Left,
            Width = 220,
            MaxLength = 64
        };
        layout.Controls.Add(_txtPdfFolder, 1, 2);

        layout.Controls.Add(new Label
        {
            // v4.17 修订三：占位符说明（时钟精度不够时可用日期）
            Text = "可用 HH(时) mm(分) ss(秒) 与 yyyy/MM/dd(日期)；时间分隔符用全角冒号：，留空=默认 PDF-HH:mm:ss。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 3);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 3), 2);

        // v4.8 需求② / v4.12：大文件阈值（超限退回待转换）
        layout.Controls.Add(new Label
        {
            Text = "文件超过多少 MB 时退回待转换：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 4);
        _numSkipLargeMb = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = 0m,
            Maximum = 100000m,
            Increment = 10m,
            Value = 0m
        };
        layout.Controls.Add(_numSkipLargeMb, 1, 4);

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简
            Text = "0 表示不限制；超限文件退回「待处理」栏。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 5);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 5), 2);

        // v4.12：单张转换超时 —— 超过该秒数则中止该张并退回「待处理」（取代原"长时提醒"）
        layout.Controls.Add(new Label
        {
            Text = "单张转换超过多少秒时退回待处理：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 6);
        _numConvertTimeoutSec = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = 0m,
            Maximum = 36000m,
            Increment = 10m,
            Value = 0m
        };
        layout.Controls.Add(_numConvertTimeoutSec, 1, 6);

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简（原"并发下单张正常 10~30 秒…需多轮补转"过长）
            Text = "0 表示不限制；设置过小会大量退回。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 7);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 7), 2);

        // v4.15：含光栅图片图纸的高清出图（仅对含 IMAGE 实体的图纸生效；这类图纸改走 COM 出图）
        layout.Controls.Add(new Label
        {
            Text = "含图片图纸的高清出图：",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 8);
        _cmbRasterDpi = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Left,
            Width = 220
        };
        _cmbRasterDpi.Items.Add("关闭（按原方式出图）");
        _cmbRasterDpi.Items.Add("1200 dpi");
        _cmbRasterDpi.Items.Add("2400 dpi（推荐）");
        _cmbRasterDpi.Items.Add("4800 dpi（更慢，矢量更细）");
        layout.Controls.Add(_cmbRasterDpi, 1, 8);

        layout.Controls.Add(new Label
        {
            // v4.17：说明精简（实测依据见手册第26章）
            Text = "仅对含图片的图纸生效；改用打印通道，单张较慢。",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.TextMuted
        }, 0, 9);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 9), 2);

        page.Controls.Add(layout);
        return page;
    }

    private void LoadToUI(AppSettings s)
    {
        // KB 显示：bytes / 1024
        // v4.6：夹紧到控件 Min/Max——settings.json 手写越界值（如 0 或超上限）时打开设置窗口直接崩溃
        _numMinFileSize.Value = Math.Max(_numMinFileSize.Minimum,
            Math.Min(_numMinFileSize.Maximum, s.QualityMinFileSizeBytes / 1024));
        // v4.5：块名列表逐条装入 ListBox
        _lstBlocks.Items.Clear();
        if (s.ExtraBlockNames != null)
        {
            foreach (var n in s.ExtraBlockNames) _lstBlocks.Items.Add(n);
        }
        // v4.16：HQ 阈值控件已删除，不再回填（HQ pc3 为负优化，见 PrintQuality.IsHighQuality）
        _cmbPdfLayout.SelectedIndex = s.PdfFlatOutput ? 1 : 0;
        // v4.17 修订三：PDF 子文件夹名称模板（空白按默认值显示）
        _txtPdfFolder.Text = string.IsNullOrWhiteSpace(s.PdfFolderTemplate) ? AppSettings.PdfFolderTemplateDefault : s.PdfFolderTemplate;
        // v4.8：大文件阈值（MB 显示，夹紧防越界）
        decimal skipMb = (decimal)((double)Math.Max(0, s.MaxDwgSizeBytes) / 1024.0 / 1024.0);
        _numSkipLargeMb.Value = Math.Max(_numSkipLargeMb.Minimum, Math.Min(_numSkipLargeMb.Maximum, skipMb));
        // v4.12：单张转换超时秒数（夹紧防越界）
        _numConvertTimeoutSec.Value = Math.Max(_numConvertTimeoutSec.Minimum,
            Math.Min(_numConvertTimeoutSec.Maximum, s.ConvertTimeoutSeconds));
        // v4.15：含图片图纸高清出图 dpi（按选项表找索引；异常值回落到推荐值 4800）
        int rIdx = Array.IndexOf(PrintQuality.RasterDpiChoices, s.RasterImageDpi);
        if (rIdx < 0) rIdx = Array.IndexOf(PrintQuality.RasterDpiChoices, PrintQuality.RasterDpiDefault);
        _cmbRasterDpi.SelectedIndex = rIdx < 0 ? 0 : rIdx;
    }

    private void OnOk(object sender, EventArgs e)
    {
        var s = new AppSettings
        {
            QualityMinFileSizeBytes = (int)_numMinFileSize.Value * 1024,
            ExtraBlockNames = _lstBlocks.Items.Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            // v4.16：HighQualityThresholdBytes 已退役（保留属性仅为兼容旧配置读回）
            PdfFlatOutput = _cmbPdfLayout.SelectedIndex == 1,
            // v4.17 修订三：PDF 子文件夹名称模板（Trim；空串交给引擎回落默认值）
            PdfFolderTemplate = (_txtPdfFolder.Text ?? "").Trim(),
            // v4.8：大文件阈值（0=不限制；v4.12 起=超过则退回待转换）
            MaxDwgSizeBytes = (long)(_numSkipLargeMb.Value * 1024m * 1024m),
            // v4.12：单张转换超时秒数（0=不限制；超时中止该张并退回待处理）
            ConvertTimeoutSeconds = (int)_numConvertTimeoutSec.Value,
            // v4.15：含图片图纸的高清出图 dpi（下拉索引 → dpi；0=关闭）
            RasterImageDpi = PrintQuality.RasterDpiChoices[
                (_cmbRasterDpi.SelectedIndex >= 0 && _cmbRasterDpi.SelectedIndex < PrintQuality.RasterDpiChoices.Length)
                    ? _cmbRasterDpi.SelectedIndex : 3]
        };
        s.Save(); // 立即持久化
        ResultSettings = s;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnReset(object sender, EventArgs e)
    {
        // 填回默认，但不立即保存（需点"确定"才写文件）
        var def = new AppSettings();
        LoadToUI(def);
    }
}
