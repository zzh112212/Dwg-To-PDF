using System;

namespace DwgToPdf.Batch;

/// <summary>
/// 打印预设参数。v2.1 新增：把原本硬编码的黑白样式表（monochrome.ctb）
/// 外置为可选的出图配色模式，界面勾选后整批生效。
/// </summary>
internal sealed class PrintOptions
{
    /// <summary>图框距纸边空白距离(mm)。v3.7 起为二选一：有边距=固定 10.0（居中），无边距=0.0（贴边）。</summary>
    public double? EdgeMarginMm { get; set; }

    /// <summary>出图配色模式（对应 AutoCAD 打印样式表 ctb）。</summary>
    public ColorMode Mode { get; set; } = ColorMode.Monochrome;

    /// <summary>v4.1：是否显示/打印线宽（默认关，由界面"显示线宽"勾选）。
    /// 注：COM 通道经 Layout.PlotWithLineweights 生效；accore 的 -EXPORT 不认 0x80 该位。
    /// v4.16：含图片/嵌入对象的图纸改走 COM 时会被**强制置 true**（见 ConverterEngine.Plot.cs），
    /// 以免这批"打印发浅"的图纸必须手动勾选才有效果。此处默认值保持不变。</summary>
    public bool ShowLineWeight { get; set; } = false;

    /// <summary>v4.1：质量核查（默认开启）。出图后检查 PDF 文件大小，
    /// 低于纸张对应阈值判定为失败（空白/内容缺失），走 COM 兜底或标记失败。</summary>
    public bool QualityCheck { get; set; } = true;

    /// <summary>v4.3：识别块图框（默认关闭，UI 手动勾选开启）。开启时 extframe.lsp
    /// 展开 INSERT 块定义收集图框端点，应对图框是块引用（如 EQWE）的图纸；
    /// 关闭时保持旧行为（只认 LWPOLYLINE/LINE）。开启会拖慢识别阶段。</summary>
    public bool RecognizeBlockFrame { get; set; } = false;

    /// <summary>v4.3：用户自定义 INSERT 块名（额外补充模式——默认识别所有 INSERT 块；这里只追加）。
    /// 来自 settings.json 的「设置」窗口输入。空表示不追加。</summary>
    public System.Collections.Generic.List<string> ExtraBlockNames { get; set; } = new System.Collections.Generic.List<string>();

    /// <summary>v4.3：质量审查 PDF 最小文件大小（字节）。来自 settings.json，默认 10KB。
    /// PdfContentCheck 用此项替代之前的纸型估算硬编码。</summary>
    public int QualityMinFileSizeBytes { get; set; } = 10 * 1024;

    /// <summary>v4.3：打印高质量阈值（字节）。来自 settings.json，默认 1MB。
    /// dwg 文件大小 &gt; 此值时 PrintQuality 选择 HQ pc3。回退 v3.8.1 行为（1MB 阈值）。</summary>
    public long HighQualityThresholdBytes { get; set; } = 1024 * 1024;

    /// <summary>v4.15：含图片/嵌入对象图纸的高清出图 dpi（只调"光栅图像质量"）。
    /// 0=关闭；1200/2400/4800=用 RasterPlotterFactory 生成的 DwgToPdf_Quality&lt;dpi&gt;.pc3，
    /// 且这类图纸改走 COM（打印）路径——accoreconsole 的 -EXPORT 不认设备/质量。
    /// 默认 2400（实测 2400 与 4800 的图片像素完全相同，2400 更快）。</summary>
    public int RasterImageDpi { get; set; } = 2400;

    /// <summary>v4.5：PDF 输出结构。false=按原文件夹结构（默认，PDF 输出到 PDF文件\相对子目录\）；
    /// true=全部放在 PDF文件\ 根下（不同子目录同名图纸由 UniquePdfPath 加 (2)(3) 后缀）。
    /// 来自 settings.json 的「设置」→「打印/输出」选择。</summary>
    public bool FlatOutput { get; set; } = false;

    /// <summary>v4.8：dwg 大于该字节数时本轮不转换。0=不限制（默认）。
    /// v4.12：改为"退回待转换"——不再判失败，文件保留在 UI「待处理」栏，
    /// 用户可在其他图纸转换完成后再单独转换（「合并待转换」可把这一批复制出来）。
    /// 来自 settings.json 的「设置」→「打印/输出」。</summary>
    public long MaxDwgSizeBytes { get; set; } = 0;

    // v4.12：删除 "长时转换提醒"（原 v4.8 SlowRemindSeconds 设置项）——用户反馈该提醒无价值。

    /// <summary>v4.12：单张转换超时秒数（0=不限制，默认）。单张转换耗时超过该值时**中止该张**
    /// （杀掉其 accoreconsole 会话），并把该文件退回 UI「待处理」栏，用户可稍后单独转换。
    /// 取代原"长时提醒"（只提示不处理）。来自 settings.json 的「设置」→「打印/输出」。</summary>
    public int ConvertTimeoutSeconds { get; set; } = 0;

    /// <summary>v4.12：本轮忽略"大文件阈值"与"单张转换超时"限制。
    /// 由 UI 在**非首次运行**（用户再次点击"开始转换"，通常是补转上一轮退回待处理的文件）时置 true：
    /// 此时不再因文件大小跳过、也不因超时中止，用户可把这批文件直接转完。</summary>
    public bool IgnoreSizeTimeLimits { get; set; } = false;

    /// <summary>v4.14：有界流水线（默认开启）。开启且走"页面设置内联"快速路径时，把"图框识别"
    /// 与"出图"分块交错并行（识别块 i+1 ∥ 出图块 i），使总耗时接近 max(识别,出图) 而非两者之和。
    /// 关闭或设置环境变量 DWGTOPDF_SERIAL=1 时回退 v4.12 的串行两阶段（唯一回退手段）。
    /// 注：分块不改变"先小文件后大文件"的出图顺序（块按该顺序切分）。</summary>
    public bool PipelineMode { get; set; } = true;

    /// <summary>v4.12：页面设置内联（默认开启）。开启且"有边距(居中)"时，跳过阶段2 的 COM 预处理
    /// （不生成副本、不启动 AUTOCAD），改由 accoreconsole 出图会话内 entmod Model 布局页面设置——
    /// 与 COM 预处理路径实测逐字节等价（附录 24 章），但省掉整段 COM 开销且不依赖 AUTOCAD。
    /// 关闭或"贴边(M=0)"时回退原有 COM 预处理路径（兜底开关，正常无需关闭）。</summary>
    public bool AccorePageSetup { get; set; } = true;

    /// <summary>v4.17 修订三：PDF 子文件夹名称模板（来自 settings.json，默认 "PDF-HH:mm:ss"，全角冒号）。
    /// 由引擎在本轮开始时按 RunStartedAt 展开一次，产出本轮 PDF 子文件夹名。</summary>
    public string PdfFolderTemplate { get; set; } = "PDF-HH:mm:ss";

    /// <summary>v4.17 修订三：本轮转换的开始时刻（UI 点击"开始转换"时记录，DateTime.Now）。
    /// PDF 子文件夹名按此时刻展开 —— 与日志里的"开始时间"一致，便于用户按时间对上批次。</summary>
    public DateTime? RunStartedAt { get; set; }

    /// <summary>v4.16：浅拷贝。仅供"含图片/嵌入对象图纸强制开线宽"分支使用——
    /// PrintOptions 在多线程出图间共享，绝不可就地改 ShowLineWeight（会串到别的图纸）。</summary>
    public PrintOptions Clone() => (PrintOptions)MemberwiseClone();

    /// <summary>根据模式得到要设置的打印样式表文件名（ctb）。</summary>
    public string StyleSheetName => Mode switch
    {
        ColorMode.Grayscale => "grayscale.ctb",
        ColorMode.Color => "acad.ctb",
        _ => "monochrome.ctb",
    };

    /// <summary>界面显示用的说明文案。</summary>
    public static string Describe(ColorMode mode) => mode switch
    {
        ColorMode.Grayscale => "灰度",
        ColorMode.Color => "彩色",
        _ => "黑白",
    };
}

/// <summary>出图配色模式。</summary>
internal enum ColorMode
{
    /// <summary>黑白（默认，等同 v2.0 的 monochrome.ctb）。</summary>
    Monochrome,

    /// <summary>灰度（grayscale.ctb）。</summary>
    Grayscale,

    /// <summary>保留图纸原色（acad.ctb）。</summary>
    Color,
}