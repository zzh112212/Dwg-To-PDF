using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DwgToPdf.Batch;

/// <summary>
/// 负责获取 AutoCAD COM 实例（启动新实例或附加到已运行实例）。
/// 修复 v2.1 的三处缺陷（详见《AutoCAD连接失败问题报告》）：
///   1. 缺少 AutoCAD 2021(R24.0) 等版本的 ProgID；
///   2. 不能优先附加已运行的 AutoCAD 实例；
///   3. 通用 ProgID 兜底注册损坏时会直接失败，且缺少可排查的报错信息。
/// </summary>
internal static class ComStarter
{
    // 覆盖 AutoCAD 2018 ~ 2026 全系版本，从高到低排列，最后才用通用 ProgID 兜底。
    // 注意：AutoCAD 版本 与 R 版本/ProgID 对应关系见 BGLogger.DetectEnvironment()。
    private static readonly string[] CandidateProgIds = new string[10]
    {
        "AutoCAD.Application.25.1", // AutoCAD 2026 (R25.1)
        "AutoCAD.Application.25.0", // AutoCAD 2025 (R25.0)
        "AutoCAD.Application.24.3", // AutoCAD 2024 (R24.3)
        "AutoCAD.Application.24.2", // AutoCAD 2023 (R24.2)
        "AutoCAD.Application.24.1", // AutoCAD 2022 (R24.1)
        "AutoCAD.Application.24.0", // AutoCAD 2021 (R24.0) —— v2.1 缺失的关键版本
        "AutoCAD.Application.23.1", // AutoCAD 2020 (R23.1)
        "AutoCAD.Application.23.0", // AutoCAD 2019 (R23.0)
        "AutoCAD.Application.22.0", // AutoCAD 2018 (R22.0)
        "AutoCAD.Application"       // 通用 ProgID，仅作最后兜底
    };

    /// <summary>
    /// 尝试从运行对象表(ROT)附加到已运行的 AutoCAD 实例。
    /// 能拿到实例说明 AutoCAD 已正常完成许可证与初始化，可完全绕过 COM 类工厂创建，
    /// 从而规避 80040154 启动失败的问题。
    /// </summary>
    public static bool TryGetRunningInstance(out object app)
    {
        app = null;
        try
        {
            object activeObject = Marshal.GetActiveObject("AutoCAD.Application");
            if (activeObject != null)
            {
                app = activeObject;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启动一个新的 AutoCAD 实例（不会附加到已运行的实例）。
    /// 逐版本尝试安装的 ProgID；全部失败时抛出带详细诊断的聚合错误，便于用户排查。
    /// </summary>
    public static object StartNewInstance()
    {
        var errors = new List<string>();
        for (int i = 0; i < CandidateProgIds.Length; i++)
        {
            string progId = CandidateProgIds[i];
            try
            {
                Type typeFromProgID = Type.GetTypeFromProgID(progId, throwOnError: false);
                if (typeFromProgID == null)
                {
                    errors.Add(progId + "：未注册");
                    continue;
                }
                return Activator.CreateInstance(typeFromProgID);
            }
            catch (Exception ex)
            {
                errors.Add(progId + "：注册存在但创建实例失败 -> " + ex.Message);
            }
        }
        throw new InvalidOperationException(
            "AutoCAD 启动失败：以上尝试的版本均无法创建实例。\n详细诊断：\n  " + string.Join("\n  ", errors) + "\n" +
            "建议：\n" +
            "  1) 先手动打开一次 AutoCAD（完成许可证确认），再运行本工具；\n" +
            "  2) 若仍启动失败，请以管理员身份运行 acad.exe /regserver 修复 COM 注册；\n" +
            "  3) 从上方候选列表中确认本机安装的版本对应的 ProgID 是否在候选列表中（详见程序日志的环境检查）。");
    }

    /// <summary>扫描注册表定位本机 acad.exe；找不到返回 null。</summary>
    public static string LocateAcadExe()
    {
        string[] baseKeys = new string[]
        {
            @"SOFTWARE\Autodesk\AutoCAD",
            @"SOFTWARE\WOW6432Node\Autodesk\AutoCAD",
            @"Software\Autodesk\AutoCAD"
        };
        try
        {
            using (var lm = Microsoft.Win32.Registry.LocalMachine)
            foreach (var baseKey in baseKeys)
            {
                var found = FindAcadInHive(lm, baseKey);
                if (found != null) return found;
            }
            foreach (var baseKey in baseKeys)
            {
                var found = FindAcadInHive(Microsoft.Win32.Registry.CurrentUser, baseKey.Replace("SOFTWARE\\WOW6432Node", "Software"));
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    private static string FindAcadInHive(Microsoft.Win32.RegistryKey root, string path)
    {
        try
        {
            using (Microsoft.Win32.RegistryKey rKey = root.OpenSubKey(path))
            {
                if (rKey == null) return null;
                foreach (string rVers in rKey.GetSubKeyNames())
                {
                    if (!rVers.StartsWith("R", StringComparison.OrdinalIgnoreCase)) continue;
                    using (Microsoft.Win32.RegistryKey rv = rKey.OpenSubKey(rVers))
                    {
                        if (rv == null) continue;
                        foreach (string prod in rv.GetSubKeyNames())
                        {
                            using (Microsoft.Win32.RegistryKey pk = rv.OpenSubKey(prod))
                            {
                                if (pk == null) continue;
                                object loc = pk.GetValue("AcadLocation");
                                if (loc is string dir && !string.IsNullOrWhiteSpace(dir))
                                {
                                    string exe = Path.Combine(dir.TrimEnd('\\'), "acad.exe");
                                    if (System.IO.File.Exists(exe)) return exe;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 启动一次临时实例并立即退出，用于环境自检。成功返回 true。
    /// v2.3.1 安全改进：
    ///   - 若已有 AutoCAD 进程在运行，直接判定连接可用，绝不启动/退出任何实例，
    ///     避免误关用户正在使用的 AutoCAD、弹出退出提示；
    ///   - 确为本工具新起的实例才调用 Quit。
    /// </summary>
    public static bool TryConnectAndQuit()
    {
        // 已有运行实例 → 环境必然可用（能连上即证明 COM 正常），不启动不退出。
        if (TryGetRunningInstance(out object existing))
        {
            try { Marshal.FinalReleaseComObject(existing); } catch { }
            return true;
        }

        object app = null;
        try
        {
            var before = AcadProcessIds();
            app = StartNewInstance();
            var l = new Late(app);
            try { l.Get("Documents"); } catch { }
            if (HasNewAcadProcess(before, 4000))
            {
                try { l.Call("Quit"); } catch { }
            }
            try { Marshal.ReleaseComObject(app); app = null; } catch { }
            return true;
        }
        catch
        {
            try { if (app != null) { Marshal.ReleaseComObject(app); } } catch { }
            return false;
        }
    }

    /// <summary>统计当前 acad.exe 进程 ID 集合（用于判断实例是否由本工具新起）。</summary>
    public static HashSet<int> AcadProcessIds()
    {
        var set = new HashSet<int>();
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("acad"))
            {
                try { set.Add(p.Id); } catch { }
            }
        }
        catch { }
        return set;
    }

    /// <summary>
    /// 在 timeoutMs 内等待是否出现新的 acad 进程（相对 before 而言）。
    /// AutoCAD 的 ActivateObject/CreateObject 存在复用运行实例的行为；若复用，
    /// 则不会出现新进程，此时绝不能关闭该实例（它属于用户）。
    /// </summary>
    public static bool HasNewAcadProcess(HashSet<int> before, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var after = AcadProcessIds();
            after.ExceptWith(before);
            if (after.Count > 0) return true;
            System.Threading.Thread.Sleep(300);
        }
        return false;
    }

    // ---- v3.4：工具自启 acad 进程登记表 ----
    // 仅登记确由本工具新起的实例（用户实例绝不登记）。用途：
    //   ① 用户点"停止"时强制结束自启实例，让阻塞中的 COM 调用快速失败返回（修复停止失效）；
    //   ② COM 兜底实例跨文件复用，收尾统一关闭（消除每文件冷启动）。
    // 并发启动时 diff 可能把他 worker 刚启动的实例也算进来——都是工具自启，过度登记无害。
    private static readonly object _ownedGate = new object();
    private static readonly HashSet<int> _ownedPids = new HashSet<int>();

    /// <summary>在 timeoutMs 内等待新出现的 acad 进程并登记为工具自启实例，返回登记的 PID 集合（可能为空）。</summary>
    public static HashSet<int> AcquireAndRegisterNewAcadPids(HashSet<int> before, int timeoutMs)
    {
        var found = new HashSet<int>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var after = AcadProcessIds();
            after.ExceptWith(before);
            if (after.Count > 0)
            {
                found = after;
                break;
            }
            System.Threading.Thread.Sleep(300);
        }
        if (found.Count > 0)
        {
            lock (_ownedGate)
            {
                foreach (var pid in found) _ownedPids.Add(pid);
            }
        }
        return found;
    }

    /// <summary>注销（实例已正常关闭时调用）。</summary>
    public static void UnregisterOwnedPid(int pid)
    {
        lock (_ownedGate) { _ownedPids.Remove(pid); }
    }

    /// <summary>强制结束全部工具自启的 acad 进程，返回结束的数量。绝不触碰未登记（用户）实例。</summary>
    public static int KillAllOwned()
    {
        int[] pids;
        lock (_ownedGate) { pids = new int[_ownedPids.Count]; _ownedPids.CopyTo(pids); }
        int killed = 0;
        foreach (var pid in pids)
        {
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(pid);
                if (p != null && !p.HasExited)
                {
                    p.Kill();
                    killed++;
                }
            }
            catch { }
            finally
            {
                lock (_ownedGate) { _ownedPids.Remove(pid); }
            }
        }
        return killed;
    }

    // ---- v4.11：accoreconsole 进程登记表（收尾收割孤儿） ----
    // 背景：accoreconsole "结果已产出但不退出"时由收割逻辑 Kill；若 Kill 失败或进程
    // 卡在退出阶段，会残留 100% CPU 的孤儿进程（实测一个转了 42 分钟）。登记 PID，
    // 转换结束时统一收割。收割前校验进程名，防 PID 复用误杀。
    private static readonly object _accoreGate = new object();
    private static readonly HashSet<int> _accorePids = new HashSet<int>();

    public static void RegisterAccore(int pid)
    {
        if (pid <= 0) return;
        lock (_accoreGate) { _accorePids.Add(pid); }
    }

    public static void UnregisterAccore(int pid)
    {
        lock (_accoreGate) { _accorePids.Remove(pid); }
    }

    /// <summary>收尾收割仍在登记表中的 accoreconsole（正常退出者已注销；仍在表中的即孤儿）。返回结束数量。</summary>
    public static int KillLeftoverAccore()
    {
        int[] pids;
        lock (_accoreGate) { pids = new int[_accorePids.Count]; _accorePids.CopyTo(pids); }
        int killed = 0;
        foreach (var pid in pids)
        {
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(pid);
                if (p != null && !p.HasExited && string.Equals(p.ProcessName, "accoreconsole", StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    killed++;
                }
            }
            catch { }
            finally { lock (_accoreGate) { _accorePids.Remove(pid); } }
        }
        return killed;
    }

    // ---- v3.6：AutoCAD Activity Insights 伴随进程清理 ----
    // 背景：AutoCAD 2024+ 启动时会拉起 AcEventSync.exe / AcQMod.exe（活动见解组件，
    // 安装目录 C:\Program Files\Autodesk\AutoCAD Activity Insights）。实测确认：
    // acad.exe 正常退出后它们不会随之退出（命令行 --idleDuration 3600，设计存活 1 小时），
    // 批量转换会残留大量此类进程。Autodesk 官方说明强制关闭它们不影响 AutoCAD 功能。
    // 策略：转换开始前快照已存在的 PID，结束时只杀"转换期间新出现"的，
    // 绝不触碰用户自己开 AutoCAD 时伴随的进程（与上方 acad 登记表同思路）。
    private static readonly string[] ActivityInsightProcessNames =
    {
        "AcEventSync",
        "AcQMod",
        "AcActivityInsights",
        "AcLiveInsights",
    };

    /// <summary>快照当前全部 Activity Insights 伴随进程 PID（用于 diff 出"转换期间新起"的）。</summary>
    public static HashSet<int> ActivityInsightsSnapshot()
    {
        var set = new HashSet<int>();
        foreach (var name in ActivityInsightProcessNames)
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    try { set.Add(p.Id); } catch { }
                }
            }
            catch { }
        }
        return set;
    }

    /// <summary>结束 before 快照之后新出现的 Activity Insights 进程，返回结束数量。
    /// before 为 null 或进程在快照中已存在 → 不动（属于转换前就在的，可能是用户 CAD 伴随进程）。</summary>
    public static int KillActivityInsightsSince(HashSet<int> before)
    {
        if (before == null) return 0;
        var now = ActivityInsightsSnapshot();
        int killed = 0;
        foreach (var pid in now)
        {
            if (before.Contains(pid)) continue;
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(pid);
                if (p != null && !p.HasExited)
                {
                    p.Kill();
                    killed++;
                }
            }
            catch { }
        }
        return killed;
    }
}