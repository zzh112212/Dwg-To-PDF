namespace DwgToPdf.Batch;

/// <summary>打印设备与光栅增强策略。
///
/// v4.16（2026-10-07，用户实测反馈"PISCES 这批用默认 pc3 效果和速度都比 HQ.pc3 好"）
/// —— **"按文件大小切换 HQ pc3"的功能已退役**，设备恒为 <see cref="StandardPlotterName"/>。
/// 退役依据（可复现，40 张公司版 + 8 张 PISCES 实测）：
///   · 凡是 dwg &gt; 1MB（旧阈值 HighQualityThresholdBytes）走 HQ pc3 的产物，**全部是整页光栅**
///     （2A0 的 PISCES 120 个图块铺满 441840x1080px @393dpi；4A0 的 Y1563451/3453 各 7 个图块）；
///   · 凡是 &lt; 1MB 走标准 DWG To PDF.pc3 的产物，除含内嵌位图的几张外**全是矢量**
///     （矢量内容流 0.79~1.81MB、0~1 个图块），覆盖 A2/A1/A0/2A0/4A0 各种幅面；
///   · 速度：用户连跑同 4 张，耗时 93.7s / **450.9s** / 217.1s，5 倍波动对应是否落在 HQ 路径上。
/// ⇒ HQ pc3 对本项目是**负优化**。
///
/// v4.17 修订十五（2026-10-19）：原 IsHighQuality / PlotterFor / MapMedia 三个恒假/恒等方法、
/// 以及 HighQualityThresholdBytes 从 settings 到出图的多层传递链，**已全部删除**；
/// 调用点直接使用 <see cref="StandardPlotterName"/> 与 PageSpec 给出的媒体名。
///
/// v4.15 调研结论（重要，勿重复尝试）：**不存在"提高 PDF 中光栅图片质量"的可用开关**。
/// 实测（2026-09-15，见手册第26章）：
///   · 改 pc3 的 Raster_Limit / Monochrome_Raster_Limit（600→1200/2400，含直接改本机 HQ pc3）；
///   · 出图会话内设 RASTERDPI=300/1200/2400；
///   · 含图片图纸 vs 不含图片图纸（标准 pc3 vs HQ pc3）；
///   三组对照的 PDF **逐字节相同**、PDF 内嵌图片像素尺寸完全相同。
/// ⇒ AutoCAD PDF 驱动把已附加的光栅图像按**原始像素**嵌入，清晰度只取决于源图片本身。
/// 因此本类不含任何光栅质量开关（含图片图纸的高清出图走 RasterPlotterFactory + COM 路径）。</summary>
internal static class PrintQuality
{
    public const string StandardPlotterName = "DWG TO PDF.pc3";

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
}
