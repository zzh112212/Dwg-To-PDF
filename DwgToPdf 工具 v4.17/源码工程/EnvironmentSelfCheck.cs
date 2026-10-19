using System;
using System.IO;
using System.Text;

namespace DwgToPdf.Batch;

/// <summary>首次使用环境自检：能否在本机出图。检查过（通过或失败）即写标记，后续不再自检。
/// v4.17 修订十六修两处缺陷：
///   ① **判据本身错了**：原判据是"启动一次交互式 AutoCAD COM 并退出"，而本工具真正的出图引擎是
///      accoreconsole（AutoCAD Core Console）。交互式 AutoCAD 会把"启动它的那个程序的可执行文件
///      路径"当作临时文件候选目录，部署路径含中文时它直接弹「所有可能用来保存临时文件的目录都
///      不存在或只读」并拒绝初始化，COM 类工厂必然报 0x80080005。实测（2026-10-19）：exe 置于
///      中文路径下自检 64~96 秒必失败，拷到纯英文路径 10 秒即过；而同一环境下 accoreconsole
///      出图 3/3 完全正常。也就是说旧判据在本项目的实际部署形态下**永远不可能通过**，
///      还会连带弹出那个 AutoCAD 错误框并泄漏一个数百 MB 的 acad.exe。
///      故判据改为「能否定位 accoreconsole」——快、可靠、且正是出图所必需。
///   ② 失败也写标记：否则真失败时每次启动都要重跑一遍（旧实现还要白等 regserver 的 30 秒）。
///      改为失败提示一次后不再重复，设置页「重新自检」可清除标记重来。</summary>
internal static class EnvironmentSelfCheck
{
    public static string ReportDir()
    {
        string dir = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
        if (string.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
        string log = Path.Combine(dir, "运行日志");
        try { Directory.CreateDirectory(log); } catch { log = Path.GetTempPath(); }
        return log;
    }

    /// <summary>通过标记：存在即视为本机出图引擎已就绪。</summary>
    public static string MarkerPath => Path.Combine(ReportDir(), "自检已通过.txt");

    /// <summary>失败标记：存在即视为「已检查过但未通过」，不再每次启动重跑。</summary>
    public static string FailMarkerPath => Path.Combine(ReportDir(), "自检已失败.txt");

    public static string ReportPath => Path.Combine(ReportDir(), "自检失败报告.txt");

    /// <summary>此前是否已通过（通过标记存在）。</summary>
    public static bool PassedBefore() => File.Exists(MarkerPath);

    /// <summary>此前是否已检查过——通过或失败任一标记存在即为 true。</summary>
    public static bool CheckedBefore() => File.Exists(MarkerPath) || File.Exists(FailMarkerPath);

    /// <summary>清除通过 / 失败标记，使下次启动重新自检（设置页「重新自检」调用）。</summary>
    public static void ClearMarkers()
    {
        // best-effort：删标记失败只会退化为下次启动仍沿用旧结论，不影响功能
        try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
        try { if (File.Exists(FailMarkerPath)) File.Delete(FailMarkerPath); } catch { }
    }

    public enum Result
    {
        Ok,       // 直接通过
        Failed,   // 出图引擎不可用
    }

    /// <summary>执行自检：能定位 accoreconsole 即通过并写标记；否则写报告与失败标记。
    /// 秒级完成，不启动任何 AutoCAD 进程。</summary>
    public static Result Run()
    {
        string accore = CoreConsoleLocator.FindAccoreConsole();
        if (!string.IsNullOrEmpty(accore))
        {
            // best-effort：标记写盘失败只会退化为每次启动都自检，不影响功能
            try
            {
                File.WriteAllText(MarkerPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + accore,
                    new UTF8Encoding(false));
            }
            catch { }
            // 本次通过 → 清掉历史失败标记，避免状态栏残留「上次未通过」
            try { if (File.Exists(FailMarkerPath)) File.Delete(FailMarkerPath); } catch { }
            BgLogger.Info("环境自检通过（出图引擎 AutoCAD Core Console）：" + accore);
            return Result.Ok;
        }

        WriteFailMarker();
        WriteReport();
        BgLogger.Error("环境自检失败：未能定位出图引擎 AutoCAD Core Console（accoreconsole.exe）。");
        return Result.Failed;
    }

    /// <summary>写失败标记：后续启动据 CheckedBefore 跳过自检，不再重复弹提示；
    /// 设置页「重新自检」可清除。</summary>
    private static void WriteFailMarker()
    {
        // best-effort：标记写盘失败只会退化为每次启动都自检，不影响功能
        try
        {
            File.WriteAllText(FailMarkerPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                "未能定位 AutoCAD Core Console（accoreconsole.exe）。\r\n" +
                "详细原因见同目录「自检失败报告.txt」。\r\n",
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static void WriteReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== DwgToPdf 环境自检失败报告 ==========");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        sb.AppendLine(BgLogger.DetectEnvironment());
        sb.AppendLine();
        sb.AppendLine("【失败原因】");
        sb.AppendLine("  未能定位 AutoCAD Core Console（accoreconsole.exe）。");
        sb.AppendLine("  本工具用它出图，所以它不在就无法转换。");
        sb.AppendLine();
        sb.AppendLine("【人工修复方法】");
        sb.AppendLine("  1) 确认本机已安装 AutoCAD，且安装目录下存在 accoreconsole.exe；");
        sb.AppendLine("  2) 未安装则安装 AutoCAD（本工具只需要它，不必打开 CAD）；");
        sb.AppendLine("  3) 装了多个版本时，本工具会自动挑选；仍定位不到可重装/修复该版本。");
        sb.AppendLine();
        sb.AppendLine("【说明】");
        sb.AppendLine("  交互式 AutoCAD（acad.exe）本工具并不需要——它只用于个别图纸的页面设置辅助，");
        sb.AppendLine("  且在本工具所在路径含中文时那个辅助路径本来就不可用（AutoCAD 会把启动它的");
        sb.AppendLine("  程序路径当作临时文件目录，中文路径会令其拒绝启动）。这不影响正常出图。");
        // best-effort：自检报告写盘失败不影响自检结论
        try { File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
    }
}
