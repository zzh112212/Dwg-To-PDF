using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DwgToPdf.Batch;

/// <summary>v4.15：生成"高清 PDF 质量"打印配置（pc3）——用于含光栅图片图纸。
///
/// 背景（用户实测 + 本机复现，详见手册第26章）：
///   · 打印对话框的「PDF 选项」里有 质量：矢量质量 / 光栅图像质量（4800dpi 时图片清晰可见）；
///   · 该质量由 **pc3 的自定义特性** 决定（phys_resolution = 矢量质量，Raster_Limit = 光栅图像质量）；
///   · **accoreconsole 的 -EXPORT 路径不认设备/质量**（实测：改设备名、改 pc3、改 DefaultConfig
///     都不影响输出；图片被压到 ~26-44dpi）；
///   · **COM（AutoCAD 打印）路径认 pc3 质量**（实测同一张图：2400/600 → 图片 733×472px ≈40dpi；
///     4800/4800 → 2931×1886px ≈161dpi，PDF 1877KB → 2949KB）。
/// ⇒ 因此含图片图纸改用：COM 出图 + 本类生成的高清 pc3。
///
/// pc3 容器（逆向 2026-09-15）：首行 "PIAFILEVERSION_2.0,PC3VER1,compress" + 正文前缀
///   b"pmzlibcodec" + 4 字节魔数 + LE32(解压后长度) + LE32(压缩后长度) + zlib 流。
///   **两个长度字段必须与本次压缩结果一致**——否则 AutoCAD 静默忽略整个文件
///   （这正是"手工改 pc3 无效"的原因）。
///
/// 幂等与容错：目标已存在且矢量/光栅都等于目标 dpi → 直接返回；模板缺失/解析失败/写盘失败
/// → 返回 false + reason，调用方回退 HQ pc3（不影响出图流程）。</summary>
internal static class RasterPlotterFactory
{
    /// <summary>内置 HQ pc3 文件名（媒体名与它一致，故高清版可继续用同一批媒体名）。</summary>
    public const string TemplateFileName = "AutoCAD PDF (High Quality Print).pc3";

    /// <summary>ISO-8859-1：pc3 正文是单字节文本，用它与字节一一对应，保证"解压→改→写回"不损坏内容。</summary>
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private static readonly object _lock = new object();
    private static readonly HashSet<string> _done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>确保 DwgToPdf_Quality&lt;dpi&gt;.pc3 可用（在全部找到的 AutoCAD Plotters 目录里生成）。
    /// 返回生成的设备名（失败返回 null，reason 给出原因）。线程安全；同一进程内只做一次。</summary>
    public static string Ensure(int dpi, out string reason)
    {
        reason = "";
        if (dpi <= 0) { reason = "未开启高清出图"; return null; }
        string name = PrintQuality.QualityPlotterName(dpi);
        lock (_lock)
        {
            if (_done.Contains(name)) return name;
            if (_failed.TryGetValue(name, out var why)) { reason = why; return null; }
            try
            {
                var dirs = FindPlotterDirs();
                if (dirs.Count == 0)
                {
                    reason = "未找到 AutoCAD Plotters 目录（无法注册打印配置）";
                    _failed[name] = reason;
                    return null;
                }
                bool okAny = false;
                string lastErr = "";
                foreach (var dir in dirs)
                {
                    string tpl = Path.Combine(dir, TemplateFileName);
                    if (!File.Exists(tpl)) { lastErr = "缺少模板 " + tpl; continue; }
                    string target = Path.Combine(dir, name);
                    string err;
                    if (WriteIfNeeded(tpl, target, dpi, out err)) okAny = true;
                    else lastErr = err;
                }
                if (!okAny)
                {
                    reason = string.IsNullOrEmpty(lastErr) ? "生成失败" : lastErr;
                    _failed[name] = reason;
                    return null;
                }
                _done.Add(name);
                return name;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                _failed[name] = reason;
                return null;
            }
        }
    }

    /// <summary>用户级 + 安装级 Plotters 目录（AutoCAD 各版本/语言各一份；能命中时全部生成）。</summary>
    internal static List<string> FindPlotterDirs()
    {
        var dirs = new List<string>();
        try
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseRoam = Path.Combine(roaming, "Autodesk");
            if (Directory.Exists(baseRoam)) dirs.AddRange(SafeDirs(baseRoam, "Plotters", 4));
        }
        // best-effort 探测：注册表/目录不可读按未找到处理
        catch { }
        try
        {
            foreach (var pf in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string basePf = Path.Combine(pf, "Autodesk");
                if (Directory.Exists(basePf)) dirs.AddRange(SafeDirs(basePf, "Plotters", 3));
            }
        }
        // best-effort 探测：注册表/目录不可读按未找到处理
        catch { }
        var uniq = new List<string>();
        foreach (var d in dirs)
            if (!uniq.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase))) uniq.Add(d);
        return uniq;
    }

    private static IEnumerable<string> SafeDirs(string root, string leafName, int maxDepth)
    {
        var found = new List<string>();
        try
        {
            var stack = new Stack<Tuple<string, int>>();
            stack.Push(Tuple.Create(root, 0));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur.Item2 > maxDepth) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(cur.Item1); } catch { continue; }
                foreach (var s in subs)
                {
                    if (string.Equals(Path.GetFileName(s), leafName, StringComparison.OrdinalIgnoreCase)) found.Add(s);
                    stack.Push(Tuple.Create(s, cur.Item2 + 1));
                }
            }
        }
        // best-effort 探测：注册表/目录不可读按未找到处理
        catch { }
        return found;
    }

    /// <summary>模板 → 目标（幂等）。目标已存在且矢量/光栅质量都是目标 dpi 时直接 true。</summary>
    internal static bool WriteIfNeeded(string templatePath, string targetPath, int dpi, out string reason)
    {
        reason = "";
        try
        {
            if (File.Exists(targetPath))
            {
                int cur;
                if (TryReadQualityDpi(targetPath, out cur) && cur == dpi && TryReadViewNewFileOff(targetPath)) return true;
            }
            string text = ReadPlainText(templatePath, out reason);
            if (text == null) return false;
            string patched = PatchQuality(text, dpi, out reason);
            if (patched == null) return false;
            byte[] bytes = BuildContainer(patched, templatePath, out reason);
            if (bytes == null) return false;
            // 幂等：先清旧产物，删除失败不影响后续写出
            try { Directory.CreateDirectory(Path.GetDirectoryName(targetPath)); } catch { }
            string tmp = targetPath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tmp, targetPath);
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>读取 pc3 的正文文本（自动处理 compress/zlib）。</summary>
    internal static string ReadPlainText(string path, out string reason)
    {
        reason = "";
        try
        {
            byte[] raw = File.ReadAllBytes(path);
            int nl = Array.IndexOf(raw, (byte)'\n');
            if (nl < 0) { reason = "pc3 首行缺失"; return null; }
            string header = Latin1.GetString(raw, 0, nl);
            byte[] body = new byte[raw.Length - nl - 1];
            Array.Copy(raw, nl + 1, body, 0, body.Length);
            if (header.IndexOf("compress", StringComparison.OrdinalIgnoreCase) < 0)
                return Latin1.GetString(body);
            int off = FindZlibOffset(body);
            if (off < 0) { reason = "未找到 zlib 流"; return null; }
            int start = off + 2;
            bool fdict = ((body[off] << 8 | body[off + 1]) & 0x20) != 0;
            if (fdict) start += 4;
            using (var ms = new MemoryStream(body, start, body.Length - start))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                return Latin1.GetString(outMs.ToArray());
            }
        }
        catch (Exception ex)
        {
            reason = "解压失败：" + ex.Message;
            return null;
        }
    }

    private static int FindZlibOffset(byte[] body)
    {
        for (int i = 0; i + 1 < body.Length; i++)
        {
            if (body[i] == 0x78 && (body[i + 1] == 0x9C || body[i + 1] == 0x01 || body[i + 1] == 0xDA))
                return i;
        }
        return -1;
    }

    /// <summary>按模板的容器格式重新打包（前缀字节原样保留，长度字段按本次压缩结果重算）。</summary>
    internal static byte[] BuildContainer(string plainText, string templatePath, out string reason)
    {
        reason = "";
        try
        {
            byte[] raw = File.ReadAllBytes(templatePath);
            int nl = Array.IndexOf(raw, (byte)'\n');
            if (nl < 0) { reason = "模板首行缺失"; return null; }
            byte[] head = new byte[nl + 1];
            Array.Copy(raw, head, nl + 1);
            byte[] body = new byte[raw.Length - nl - 1];
            Array.Copy(raw, nl + 1, body, 0, body.Length);
            int off = FindZlibOffset(body);
            if (off < 0) { reason = "模板缺少 zlib 流"; return null; }
            byte[] prefix = new byte[off];
            Array.Copy(body, prefix, off);
            byte[] data = Latin1.GetBytes(plainText);
            using (var ms = new MemoryStream())
            {
                using (var zs = new DeflateStream(ms, CompressionLevel.Optimal, true))
                {
                    zs.Write(data, 0, data.Length);
                }
                // DeflateStream 产出裸 deflate；pc3 需要 zlib 包装（0x78 0x9C + deflate + adler32）
                byte[] deflate = ms.ToArray();
                byte[] payload = new byte[deflate.Length + 6];
                payload[0] = 0x78; payload[1] = 0x9C;
                Array.Copy(deflate, 0, payload, 2, deflate.Length);
                uint adler = Adler32(data);
                payload[payload.Length - 4] = (byte)((adler >> 24) & 0xFF);
                payload[payload.Length - 3] = (byte)((adler >> 16) & 0xFF);
                payload[payload.Length - 2] = (byte)((adler >> 8) & 0xFF);
                payload[payload.Length - 1] = (byte)(adler & 0xFF);

                // 前缀：pmzlibcodec + 4 字节魔数 + LE32(解压长度) + LE32(压缩长度)
                var outMs = new MemoryStream();
                outMs.Write(head, 0, head.Length);
                outMs.Write(prefix, 0, prefix.Length - 8);                       // 含 magic
                byte[] sizes = new byte[8];
                BitConverter.GetBytes(data.Length).CopyTo(sizes, 0);
                BitConverter.GetBytes(payload.Length).CopyTo(sizes, 4);
                outMs.Write(sizes, 0, 8);
                outMs.Write(payload, 0, payload.Length);
                return outMs.ToArray();
            }
        }
        catch (Exception ex)
        {
            reason = "打包失败：" + ex.Message;
            return null;
        }
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte t in data)
        {
            a = (a + t) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    /// <summary>把"光栅图像质量"（Raster_Limit / Monochrome_Raster_Limit）改成目标 dpi；
    /// 仅当目标 dpi 超过模板的矢量质量（HQ=2400）时才同步提高矢量质量
    /// —— AKN：光栅质量不能超过矢量质量。
    /// 实测（本机 2026-09-15，见手册第26章）：光栅 2400 与 4800 的 PDF 内图片像素**完全相同**
    /// （2931×1886 ≈161dpi），而 4800 只让矢量更细、出图明显更慢，故不需要抬矢量。</summary>
    internal static string PatchQuality(string text, int dpi, out string reason)
    {
        reason = "";
        int hits = 0;
        // 光栅图像质量：custom{Raster_Limit / Monochrome_Raster_Limit}
        // 注意：pc3 文本里字符串是"只开不闭"写法（name="Raster_Limit 后直接换行、无收尾引号）
        foreach (var key in new[] { "Raster_Limit", "Monochrome_Raster_Limit" })
        {
            var re = new Regex("(name=\"" + key + "\\s*\\r?\\n\\s*value=)(\\d+)");
            if (re.IsMatch(text)) { text = re.Replace(text, "${1}" + dpi, 1); hits++; }
        }
        if (hits == 0) { reason = "pc3 中未找到光栅质量字段（驱动版本可能不同）"; return null; }
        // 矢量质量：仅在目标 dpi 高于模板现值时提高（保持"光栅 ≤ 矢量"）
        foreach (var key in new[] { "phys_resolution_x", "phys_resolution_y", "effective_resolution_x", "effective_resolution_y" })
        {
            var re = new Regex("(" + key + "=)([0-9.]+)");
            var m = re.Match(text);
            if (!m.Success) continue;
            double cur;
            if (double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out cur) && cur < dpi)
                text = re.Replace(text, "${1}" + dpi + ".0", 1);
        }
        // v4.15：关闭"出图后用系统默认程序打开产出文件"。
        // 实测（2026-09-17，受控复现）：HQ 模板里自定义属性 #4 `name="View_New_File` value=TRUE，
        // AutoCAD 每次 COM 打印完成会用默认程序 ShellExecute 打开 PDF（本机默认=Edge，
        // 一次拉起 ~18 个进程/0.6GB，抢焦点 + 拖慢批量；父进程实测为 acad）。置 FALSE 即不再打开。
        // 注：标准 pc3 无此属性（缺省被 AutoCAD 视为 TRUE），故 COM 路径应优先用本工厂生成的 pc3。
        var reView = new Regex("(name=\"View_New_File\\s*\\r?\\n\\s*value=)(TRUE|true)");
        if (reView.IsMatch(text)) text = reView.Replace(text, "${1}FALSE", 1);
        return text;
    }

    /// <summary>读出 pc3 的 View_New_File 是否已关闭（幂等判据用；属性缺失返回 false=未关闭）。</summary>
    internal static bool TryReadViewNewFileOff(string path)
    {
        try
        {
            string err;
            string text = ReadPlainText(path, out err);
            if (text == null) return false;
            var m = Regex.Match(text, "name=\"View_New_File\\s*\\r?\\n\\s*value=(TRUE|FALSE)", RegexOptions.IgnoreCase);
            return m.Success && m.Groups[1].Value.Equals("FALSE", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>读出现有 pc3 的光栅质量（用于幂等判断/自检）。</summary>
    internal static bool TryReadQualityDpi(string path, out int dpi)
    {
        dpi = -1;
        string err;
        string text = ReadPlainText(path, out err);
        if (text == null) return false;
        var m = Regex.Match(text, "name=\"Raster_Limit\\s*\\r?\\n\\s*value=(\\d+)");
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, out dpi);
    }
}
