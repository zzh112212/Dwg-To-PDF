using System;
using System.IO;
using System.Text;

namespace DwgToPdf.Batch;

/// <summary>首次使用环境自检：能否连接本地 AutoCAD。通过后写标记，后续不再自检。</summary>
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

    public static string MarkerPath => Path.Combine(ReportDir(), "自检已通过.txt");

    public static string ReportPath => Path.Combine(ReportDir(), "自检失败报告.txt");

    public static bool PassedBefore() => File.Exists(MarkerPath);

    public enum Result
    {
        Ok,       // 直接通过
        Repaired, // 自修复后通过
        Failed,   // 自修复后仍失败
    }

    /// <summary>执行自检：失败则尝试自修复(acad /regserver)，再失败则写报告。返回结果。</summary>
    public static Result Run()
    {
        if (TryCheck())
        {
            File.WriteAllText(MarkerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new UTF8Encoding(false));
            return Result.Ok;
        }

        // 尝试自修复
        bool repaired = false;
        string acad = ComStarter.LocateAcadExe();
        if (!string.IsNullOrEmpty(acad))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(acad, "/regserver")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    // best-effort：进程可能已自行退出，结束失败无需处理
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } }
                }
            }
            // best-effort：/regserver 修复失败按未修复处理，转入结果上报
            catch { }
            repaired = TryCheck();
            if (repaired) File.WriteAllText(MarkerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new UTF8Encoding(false));
        }

        if (repaired) return Result.Repaired;

        WriteReport(acad);
        return Result.Failed;
    }

    private static bool TryCheck()
    {
        try { return ComStarter.TryConnectAndQuit(); }
        catch { return false; }
    }

    private static void WriteReport(string acadPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== DwgToPdf 环境自检失败报告 ==========");
        sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        sb.AppendLine(BgLogger.DetectEnvironment());
        sb.AppendLine();
        sb.AppendLine("定位到 acad.exe: " + (string.IsNullOrEmpty(acadPath) ? "未找到" : acadPath));
        sb.AppendLine();
        sb.AppendLine("【人工修复方法】");
        sb.AppendLine(" 1) 先手动正常打开一次 AutoCAD，完成许可证确认后再关闭；");
        sb.AppendLine(" 2) 若仍失败，请以管理员身份运行：");
        sb.AppendLine("        " + (string.IsNullOrEmpty(acadPath) ? "\"C:\\Program Files\\Autodesk\\AutoCAD 20XX\\acad.exe\"" : "\"" + acadPath + "\"") + " /regserver");
        sb.AppendLine(" 3) 重新打开本工具即可自动再次自检。");
        // best-effort：自检报告写盘失败不影响自检结论
        try { File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
    }
}