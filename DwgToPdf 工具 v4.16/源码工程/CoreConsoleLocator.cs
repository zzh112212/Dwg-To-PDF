using System;
using System.IO;
using Microsoft.Win32;

namespace DwgToPdf.Batch
{
    /// <summary>定位 accoreconsole.exe（AutoCAD Core Console，无界面引擎）。找不到返回 null。</summary>
    internal static class CoreConsoleLocator
    {
        public static string FindAccoreConsole()
        {
            foreach (var dir in AcadInstallDirs())
            {
                var p = FindInDir(dir);
                if (p != null) return p;
            }
            return FindInProgramFiles();
        }

        /// <summary>在单个目录下探测 accoreconsole.exe；目录无效返回 null。</summary>
        public static string FindInDir(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return null;
                string p = Path.Combine(dir, "accoreconsole.exe"); // Path.Combine 已正确处理尾部反斜杠
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>扫描注册表收集 AutoCAD 安装目录（AcadLocation 值）。</summary>
        private static string[] AcadInstallDirs()
        {
            var dirs = new System.Collections.Generic.List<string>();
            string[] roots = { @"SOFTWARE\Autodesk\AutoCAD", @"SOFTWARE\WOW6432Node\Autodesk\AutoCAD" };
            foreach (var root in roots)
            {
                try
                {
                    using (var hive = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (hive == null) continue;
                        foreach (var rv in hive.GetSubKeyNames())
                        {
                            if (!rv.StartsWith("R", StringComparison.OrdinalIgnoreCase)) continue;
                            using (var prod = hive.OpenSubKey(rv))
                            {
                                if (prod == null) continue;
                                try
                                {
                                    foreach (var acad in prod.GetSubKeyNames())
                                    {
                                        if (!acad.StartsWith("ACAD-", StringComparison.OrdinalIgnoreCase)) continue;
                                        using (var p = prod.OpenSubKey(acad))
                                        {
                                            if (p == null) continue;
                                            var loc = p.GetValue("AcadLocation") as string;
                                            if (!string.IsNullOrWhiteSpace(loc)) dirs.Add(loc);
                                        }
                                    }
                                }
                                catch { /* 单个子键因 ACL 读取失败则跳过，不影响整体遍历 */ }
                            }
                        }
                    }
                }
                catch { /* 注册表根不可读时跳过该 hive */ }
            }
            return dirs.ToArray();
        }

        /// <summary>Program Files 兜底探测（注册表未命中时）。版本列表为低概率兜底，升级 AutoCAD 后需同步更新。</summary>
        private static string FindInProgramFiles()
        {
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                foreach (var root in new[] { pf, pf86 })
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    string autodesk = Path.Combine(root, "Autodesk");
                    if (!Directory.Exists(autodesk)) continue;
                    foreach (var d in Directory.GetDirectories(autodesk, "*", SearchOption.TopDirectoryOnly))
                    {
                        var byVer = FindInDir(Path.Combine(d, "AutoCAD 2026"));
                        if (byVer == null) byVer = FindInDir(Path.Combine(d, "AutoCAD 2025"));
                        if (byVer == null) byVer = FindInDir(Path.Combine(d, "AutoCAD 2024"));
                        if (byVer == null) byVer = FindInDir(d);
                        if (byVer != null) return byVer;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
