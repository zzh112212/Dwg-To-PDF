using System;
using System.IO;

namespace DwgToPdf.Batch;

/// <summary>高质量打印策略：dwg &gt; 阈值（默认 1MB）自动换高分辨 pc3（AutoCAD PDF High Quality Print，矢量2400DPI/光栅600DPI），
/// &lt;= 阈值用 DWG To PDF.pc3（矢量400DPI/光栅150DPI）以提速。默认阈值 1MB 与 v3.8.1 一致；
/// v4.3 同时支持「设置」窗口中按用户自定义阈值动态调整（静态 ThresholdBytes 仅作默认/兜底）。
/// 媒体名映射仅当探针发现 HQ pc3 媒体名与 DWG To PDF.pc3 不一致时启用（缺省按原媒体名透传）。
///
/// v4.15 调研结论（重要，勿重复尝试）：**不存在"提高 PDF 中光栅图片质量"的可用开关**。
/// 实测（2026-09-15，见手册第26章）：
///   · 改 pc3 的 Raster_Limit / Monochrome_Raster_Limit（600→1200/2400，含直接改本机 HQ pc3）；
///   · 出图会话内设 RASTERDPI=300/1200/2400；
///   · 含图片图纸 vs 不含图片图纸（标准 pc3 vs HQ pc3）；
///   三组对照的 PDF **逐字节相同**、PDF 内嵌图片像素尺寸完全相同。
/// ⇒ AutoCAD PDF 驱动把已附加的光栅图像按**原始像素**嵌入，清晰度只取决于源图片本身。
/// 因此本类保持"按文件大小分级"的原设计，不含任何光栅质量开关。</summary>
internal static class PrintQuality
{
    public const long ThresholdBytes = 1L * 1024 * 1024; // 1MB（与 v3.8.1 一致；用户可在「设置」中覆盖）

    public const string StandardPlotterName = "DWG TO PDF.pc3";
    public const string HighQualityPlotterName = "AutoCAD PDF (High Quality Print).pc3";

    // === v4.15：含图片图纸的高清出图（COM 路径） ===
    /// <summary>关闭（含图片图纸按普通方式出图）。</summary>
    public const int RasterDpiOff = 0;
    /// <summary>默认高清 dpi（只提"光栅图像质量"；实测 2400 与 4800 的图片像素完全相同，
    /// 而 2400 更快；4800 仅使矢量更细且明显更慢，故默认 2400）。</summary>
    public const int RasterDpiDefault = 2400;
    /// <summary>设置界面可选值（0=关闭）。</summary>
    public static readonly int[] RasterDpiChoices = { 0, 1200, 2400, 4800 };

    /// <summary>高清 pc3 文件名（由 RasterPlotterFactory 生成到用户的 AutoCAD Plotters 目录）。</summary>
    public static string QualityPlotterName(int dpi) => "DwgToPdf_Quality" + dpi + ".pc3";

    /// <summary>该文件是否应走"COM + 高清 pc3"。
    /// v4.15 修订收紧为：含**真光栅 IMAGE 实体**（rasterCount&gt;0）且其中**没有外部图缺失**
    /// （rasterMissing==0）且功能开启（rasterDpi&gt;0）。
    /// 收紧依据（2026-09-16 全量 183 张实测）：
    ///   · 只含 OLE 嵌入图片/参考底图的图纸白走 COM（单张最慢 932s），图片并未变清晰；
    ///   · 光栅图片的外部文件缺失时，COM 打印会输出**空白页**（accoreconsole 正常）。</summary>
    public static bool UseRasterBoost(int rasterCount, int rasterMissing, int rasterDpi)
        => rasterCount > 0 && rasterMissing == 0 && rasterDpi > 0;

    /// <summary>dwg 文件是否超过高质量阈值（&gt;threshold 走 HQ pc3）。
    /// threshold &lt; 0 时用默认阈值（PrintOptions.HighQualityThresholdBytes 或静态兜底）。</summary>
    public static bool IsHighQuality(string dwgPath, long thresholdBytes = -1)
    {
        long t = thresholdBytes >= 0 ? thresholdBytes : ThresholdBytes;
        try { return new FileInfo(dwgPath).Length > t; }
        catch { return false; }
    }

    /// <summary>按文件大小选择 pc3 设备名（&gt;阈值=HQ pc3，否则标准 DWG To PDF.pc3）。</summary>
    public static string PlotterFor(string dwgPath, long thresholdBytes = -1)
        => IsHighQuality(dwgPath, thresholdBytes) ? HighQualityPlotterName : StandardPlotterName;

    /// <summary>媒体名映射：HQ pc3 与标准 pc3 的媒体名不一致时在此转换；默认恒等透传。</summary>
    public static string MapMedia(bool highQuality, string fullBleedMedia)
        => fullBleedMedia;
}
