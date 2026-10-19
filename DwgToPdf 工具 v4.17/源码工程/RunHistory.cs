using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DwgToPdf.Batch
{
    /// <summary>v4.15：运行历史与**语料指纹**（状态存 %LOCALAPPDATA%\DwgToPdf\run_history.txt，
    /// 不写入输出目录——用户已明确不要报告文件）。
    ///
    /// 动机（2026-09-16 实测教训）：语料被悄悄改动（用户删除"PISCES按摩总成图纸 - 0826"目录，
    /// 183 → 166 张）时，若不对比语料，很容易把"结果差异"误判为代码回归；本次将该对比
    /// **自动化进日志**：每次运行打印语料指纹 + 张数，并与上次比较给出"变化/一致"结论。
    ///
    /// 记录格式（每行一次运行，保留最近 20 行）：
    ///   日期(yyyy-MM-dd HH:mm:ss)|张数|成功|失败|秒|指纹</summary>
    internal static class RunHistory
    {
        private static string DirPath
        {
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "DwgToPdf");
            }
        }

        private static string FilePath => Path.Combine(DirPath, "run_history.txt");

        internal sealed class Snapshot
        {
            public string Date;
            public int Files;
            public int Ok;
            public int Fail;
            public double Seconds;
            public string Fingerprint;
        }

        /// <summary>语料指纹：对排序后的文件名清单取 MD5 前 8 位（只认文件名，不认路径/大小）。</summary>
        public static string Fingerprint(IEnumerable<string> files)
        {
            try
            {
                if (files == null) return "--------";
                var names = files.Where(f => !string.IsNullOrEmpty(f))
                                 .Select(Path.GetFileName)
                                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                 .ToList();
                using (var md5 = MD5.Create())
                {
                    byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", names)));
                    return BitConverter.ToString(h).Replace("-", "").Substring(0, 8);
                }
            }
            catch { return "--------"; }
        }

        /// <summary>日志输出：语料指纹 + 与上次运行的对比（"语料漂移"哨兵）。
        ///
        /// v4.15：**默认不打印**（用户反馈这类信息属于开发/诊断噪音，不该出现在转换日志里）——
        /// 运行历史仍**静默记录**（`run_history.txt`），需要排查时用
        /// `DWGTOPDF_SHOW_CORPUS=1` 打开本行输出即可。</summary>
        public static void LogComparison(LogWriter log, string fingerprint, int count)
        {
            if (log == null) return;
            try
            {
                if (Environment.GetEnvironmentVariable("DWGTOPDF_SHOW_CORPUS") != "1") return;
                log.WriteLine("语料指纹: " + fingerprint + "（" + count + " 张）");
                var prev = Load();
                if (prev == null)
                {
                    log.WriteLine("[提示] 首次记录语料基线（下次运行会自动对比语料张数与结果）");
                    return;
                }
                string prevInfo = "上次 " + prev.Date + "：成功 " + prev.Ok + " / 失败 " + prev.Fail
                                + "，用时 " + prev.Seconds.ToString("0.0") + "s";
                if (string.Equals(prev.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    log.WriteLine("语料与" + prevInfo + "（指纹一致，可直接对比）");
                }
                else
                {
                    log.WriteLine("[提示] **语料相对上次已变化**：" + prev.Files + " 张 → " + count + " 张"
                        + "（指纹 " + prev.Fingerprint + " → " + fingerprint + "）；" + prevInfo + "。");
                    log.WriteLine("       语料变化时，结果差异可能来自语料本身——请先核对语料再判断是否为代码回归。");
                }
            }
            // best-effort：本段（统计/收尾/镜像）失败不影响已完成产出
            catch { }
        }

        /// <summary>追加本次运行记录（保留最近 20 行）。</summary>
        public static void Save(string fingerprint, int count, int ok, int fail, double seconds)
        {
            try
            {
                Directory.CreateDirectory(DirPath);
                var lines = new List<string>();
                if (File.Exists(FilePath))
                {
                    try { lines.AddRange(File.ReadAllLines(FilePath, Encoding.UTF8)); }
                    // v4.17 修订十（CQ-06）：读取失败不再静默
                    catch (Exception ex) { BgLogger.Warn("运行历史读取失败：" + ex.GetType().Name + ": " + ex.Message); }
                }
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "|" + count + "|" + ok + "|" + fail
                          + "|" + seconds.ToString("0.0") + "|" + (fingerprint ?? "--------"));
                int skip = Math.Max(0, lines.Count - 20);
                File.WriteAllLines(FilePath, lines.Skip(skip).ToArray(), Encoding.UTF8);
            }
            // v4.17 修订十（CQ-06）：保存失败不再静默
            catch (Exception ex) { BgLogger.Warn("运行历史保存失败：" + ex.GetType().Name + ": " + ex.Message); }
        }

        private static Snapshot Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string[] p = lines[i].Split('|');
                    if (p.Length < 6) continue;
                    int files, ok, fail; double sec;
                    if (!int.TryParse(p[1], out files)) continue;
                    if (!int.TryParse(p[2], out ok)) continue;
                    if (!int.TryParse(p[3], out fail)) continue;
                    if (!double.TryParse(p[4], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out sec)) continue;
                    return new Snapshot { Date = p[0], Files = files, Ok = ok, Fail = fail, Seconds = sec, Fingerprint = p[5] };
                }
                return null;
            }
            // best-effort：读不到历史按无历史处理
            catch { return null; }
        }
    }
}
