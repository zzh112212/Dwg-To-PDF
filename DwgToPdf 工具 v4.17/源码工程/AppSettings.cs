using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DwgToPdf.Batch;

/// <summary>v4.6 用户设置（持久化到 %APPDATA%\DwgToPdf\settings.json）。
/// 自定义项：质量审查最小文件大小、识别块图框用户自定义块名、打印高质量阈值、PDF 输出结构。
/// v4.6 新增界面状态记忆：输出文件夹、主界面选项、窗口大小/位置（启动自动恢复上次状态）。
/// 首次启动若文件不存在则自动创建默认值；保存失败不抛异常（写日志 + 用内存值）。
/// 注：手写 JSON 序列化（少量字段）以避免 .NET 4.8 缺少 System.Text.Json。</summary>
public class AppSettings
{
    /// <summary>默认：质量审查判定"PDF 过小"的下限（字节）。10KB。</summary>
    public int QualityMinFileSizeBytes { get; set; } = 10 * 1024;

    /// <summary>默认：用户自定义 INSERT 块名（额外补充模式——默认识别所有 INSERT 块；这里只追加）。
    /// 例：["EQWE", "MY_FRAME"]，逗号或换行分隔。</summary>
    public List<string> ExtraBlockNames { get; set; } = new List<string>();

    // v4.15 修订：原 HighQualityThresholdBytes（"大于1MB采用HQ"）停止参与决策——实测 HQ.pc3 为
    // 负优化（accore -EXPORT 不认设备；COM 路径 HQ 输出大 2.4 倍慢 2.9 倍），设备一律
    // DWG To PDF.pc3（手册第28章）。
    // v4.17 修订十五：该属性与 settings.json 里的同名键**已彻底删除**（连同 ToJson 重复写键的缺陷）。
    // 旧 settings.json 里残留的该键读取时自动忽略（ReadXxx 按名查找，未识别的键不参与）。

    /// <summary>v4.15：含图片/嵌入对象（IMAGE / OLE / 参考底图）图纸的高清出图 dpi（光栅图像质量）。
    /// 0=关闭；1200/2400/4800=该类图纸改走 COM + DwgToPdf_Quality&lt;dpi&gt;.pc3（更慢但清晰）。
    /// 默认 2400（实测 2400 与 4800 图片像素相同，2400 更快）。</summary>
    public int RasterImageDpi { get; set; } = 2400;

    /// <summary>v4.5：PDF 输出结构。false=按原文件夹结构（默认，PDF 随源目录层级存放）；
    /// true=全部放在输出文件夹\PDF文件\ 根下（不同子目录同名图纸工作名=子目录前缀，互不覆盖）。</summary>
    public bool PdfFlatOutput { get; set; } = false;

    /// <summary>v4.8：dwg 大于该字节数时本轮不转换。0=不限制（默认）。
    /// v4.12：改为"退回待转换"——不判失败，保留在「待处理」栏，可在其他图纸完成后继续转换。</summary>
    public long MaxDwgSizeBytes { get; set; } = 0;

    // v4.12：删除 "长时转换提醒"（原 v4.8 SlowFileRemindSeconds 设置项）——用户反馈该提醒无价值。
    // 旧 settings.json 里残留的该键读取时会被忽略（ReadXxx 按名查找，未识别的键不参与）。

    /// <summary>v4.12：单张转换超时秒数（0=不限制，默认）。超时则中止该张并退回「待处理」栏。
    /// 新键（不复用 SlowFileRemindSeconds），默认 0 避免旧配置 30 秒直接导致大量中止。</summary>
    public int ConvertTimeoutSeconds { get; set; } = 0;

    // === v4.6：界面状态记忆（记住上次使用状态，启动自动恢复） ===

    /// <summary>v4.6：上次使用的输出文件夹路径（空=未设置过）。</summary>
    public string LastOutputDir { get; set; } = "";

    /// <summary>v4.6：边距选项。true=有边距（居中，默认）；false=无边距（贴边）。</summary>
    public bool MarginOn { get; set; } = true;

    /// <summary>v4.6：配色下拉索引。0=黑白（默认）1=彩色。</summary>
    public int ColorIndex { get; set; } = 0;

    /// <summary>v4.6：模式下拉索引。0=通用版 1=公司版（默认）。</summary>
    public int ModeIndex { get; set; } = 1;

    /// <summary>v4.6：显示线宽选项（默认关）。</summary>
    public bool LineWeight { get; set; } = false;

    /// <summary>v4.6：质量核查选项（默认开）。</summary>
    public bool QualityCheck { get; set; } = true;

    /// <summary>v4.6：识别块图框选项。v4.11 起默认开（用户要求：默认识别块图框）。</summary>
    // v4.12 修订：默认由 true 改为 false —— 实测开启"识别块图框"会让识别阶段 +45%，
    // 而"整幅图框就是一个块"的图纸（如 H47 EQWE 总成）自 v4.12 修订起已能自动修正
    //（按全图范围出图，不依赖该开关）。仅在识别结果不完整、日志提示 BLOCKHINT 时才建议开启。
    public bool BlockFrame { get; set; } = false;

    /// <summary>v4.6：窗口位置/大小（启动恢复）。HasValue=false 表示未记录过，走默认居中。</summary>
    public int? WinLeft { get; set; }
    public int? WinTop { get; set; }
    public int? WinWidth { get; set; }
    public int? WinHeight { get; set; }
    public bool WinMaximized { get; set; } = false;

    /// <summary>v4.17 修订三（2026-10-15 用户需求）：PDF 子文件夹名称模板。
    /// 每次"开始转换"按**该轮开始时刻**展开一次，作为输出文件夹下的 PDF 子目录名，
    /// 使每批产出的 PDF 各自归入独立文件夹、互不覆盖。
    /// 可用占位符：HH=时、mm=分、ss=秒，另支持 yyyy / MM / dd（当时钟精度不够时可用日期）。
    /// 默认 "PDF-HH:mm:ss"（例：10:30:15 转换 → PDF-10：30：15，全角冒号：Windows 文件名不允许半角冒号）。
    /// 非法路径字符会被过滤；空白/过滤后为空回落默认值；同一秒内再次转换自动追加 -2、-3。</summary>
    public string PdfFolderTemplate { get; set; } = "PDF-HH:mm:ss";

    /// <summary>settings.json 路径：%APPDATA%\DwgToPdf\settings.json</summary>
    public static string DefaultPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DwgToPdf");
            return Path.Combine(dir, "settings.json");
        }
    }

    /// <summary>v4.17 修订三：PDF 子文件夹名称模板的占位符展开。
    /// 时间分隔符用全角冒号「：」（Windows 文件名不允许半角冒号，用户要求时间按 时:分:秒 书写）。
    /// 支持 HH（时）/ mm（分）/ ss（秒）/ yyyy / MM / dd —— 时钟精度不够时可用日期组合。
    /// 用户若手打半角冒号 ":"，会自动映射为全角「：」，避免被当非法字符静默丢掉。
    /// 展开后过滤 Windows 路径非法字符（\ / : * ? " &lt; &gt; | 与控制字符）并收尾去点号/空格；
    /// 空白模板、过滤后为空、或长度超限时回落默认模板。
    /// 注意：展开只做一次（每轮转换开始时按该时刻），不再二次替换。</summary>
    public static string ExpandPdfFolderTemplate(string template, DateTime startedAt)
    {
        string t = (template ?? "").Trim();
        if (t.Length == 0) t = PdfFolderTemplateDefault;
        string s = t
            .Replace("yyyy", startedAt.ToString("yyyy"))
            .Replace("MM", startedAt.ToString("MM"))
            .Replace("dd", startedAt.ToString("dd"))
            .Replace("HH", startedAt.ToString("HH"))
            .Replace("mm", startedAt.ToString("mm"))
            .Replace("ss", startedAt.ToString("ss"));
        // v4.17 修订三（用户要求）：时间分隔符改用冒号。半角冒号 ':' 是 Windows 文件名非法字符，
        // 直接过滤会把 "09:08:07" 变成 "090807"（分隔符全丢）——故先统一映射为全角「：」。
        s = s.Replace(':', '\uFF1A');
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' || c == '"'
                || c == '<' || c == '>' || c == '|' || char.IsControl(c)) continue;
            sb.Append(c);
        }
        s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length == 0) s = PdfFolderNameDefault;
        if (s.Length > 64) s = s.Substring(0, 64).Trim().TrimEnd('.', ' ');
        return s.Length == 0 ? PdfFolderNameDefault : s;
    }

    /// <summary>PDF 子文件夹默认名（v4.17 及以前的固定名），模板解析失败时用它兜底。</summary>
    public const string PdfFolderNameDefault = "PDF文件";

    /// <summary>PDF 子文件夹名称模板的默认值：PDF-时:分:秒（全角冒号，Windows 合法）。</summary>
    public const string PdfFolderTemplateDefault = "PDF-HH:mm:ss";

    /// <summary>从指定路径加载；文件不存在则创建并返回默认值。加载失败回退内存默认。</summary>
    public static AppSettings Load(string path = null)
    {
        if (string.IsNullOrEmpty(path)) path = DefaultPath;
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new AppSettings();
                fresh.Save(path);
                return fresh;
            }
            string json = File.ReadAllText(path, Encoding.UTF8);
            return Parse(json);
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>保存到指定路径（默认 DefaultPath）。失败记后台日志并回 false（v4.17 修订十·补 CQ-06 B-4）。</summary>
    public bool Save(string path = null)
    {
        if (string.IsNullOrEmpty(path)) path = DefaultPath;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson(this), new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            // v4.17 修订十·补（CQ-06 B-4）：保存失败不再静默——记后台日志后按既有契约回 false
            BgLogger.Warn("设置保存失败：" + path + "：" + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>将字节数格式化为最易读单位（KB/MB）。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.##") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0") + " KB";
        return bytes + " B";
    }

    // === 手写 JSON 序列化/反序列化（少量字段，避免 System.Text.Json 依赖）===

    public static string ToJson(AppSettings s)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"QualityMinFileSizeBytes\":").Append(s.QualityMinFileSizeBytes).Append(",");
        // v4.15：含图片图纸高清出图 dpi（0=关闭）
        sb.Append("\"RasterImageDpi\":").Append(s.RasterImageDpi).Append(",");
        sb.Append("\"PdfFlatOutput\":").Append(s.PdfFlatOutput ? "true" : "false").Append(",");
        // v4.8：大文件阈值（v4.12 起语义=超过则退回待转换）
        sb.Append("\"MaxDwgSizeBytes\":").Append(s.MaxDwgSizeBytes).Append(",");
        // v4.12：单张转换超时秒数（0=不限制）
        sb.Append("\"ConvertTimeoutSeconds\":").Append(s.ConvertTimeoutSeconds).Append(",");
        sb.Append("\"ExtraBlockNames\":[");
        if (s.ExtraBlockNames != null)
        {
            bool first = true;
            foreach (var n in s.ExtraBlockNames)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(EscapeJson(n)).Append("\"");
            }
        }
        sb.Append("],");
        // v4.6：界面状态记忆（窗口位置四值任一缺失即不恢复，保持居中默认）
        sb.Append("\"LastOutputDir\":\"").Append(EscapeJson(s.LastOutputDir ?? "")).Append("\",");
        sb.Append("\"MarginOn\":").Append(s.MarginOn ? "true" : "false").Append(",");
        sb.Append("\"ColorIndex\":").Append(s.ColorIndex).Append(",");
        sb.Append("\"ModeIndex\":").Append(s.ModeIndex).Append(",");
        sb.Append("\"LineWeight\":").Append(s.LineWeight ? "true" : "false").Append(",");
        sb.Append("\"QualityCheck\":").Append(s.QualityCheck ? "true" : "false").Append(",");
        sb.Append("\"BlockFrame\":").Append(s.BlockFrame ? "true" : "false").Append(",");
        sb.Append("\"WinLeft\":").Append(NullableJson(s.WinLeft)).Append(",");
        sb.Append("\"WinTop\":").Append(NullableJson(s.WinTop)).Append(",");
        sb.Append("\"WinWidth\":").Append(NullableJson(s.WinWidth)).Append(",");
        sb.Append("\"WinHeight\":").Append(NullableJson(s.WinHeight)).Append(",");
        sb.Append("\"WinMaximized\":").Append(s.WinMaximized ? "true" : "false").Append(",");
        // v4.17 修订三：PDF 子文件夹名称模板（默认 PDF-HH:mm:ss）
        sb.Append("\"PdfFolderTemplate\":\"").Append(EscapeJson(s.PdfFolderTemplate ?? "")).Append("\"");
        sb.Append("}");
        return sb.ToString();
    }

    /// <summary>v4.6：可空整数 JSON 表示（null 不带引号）。</summary>
    private static string NullableJson(int? v)
    {
        return v.HasValue ? v.Value.ToString() : "null";
    }

    /// <summary>极简 JSON 解析：键值 + 字符串数组。足够少量字段使用。</summary>
    public static AppSettings Parse(string json)
    {
        var s = new AppSettings();
        if (string.IsNullOrEmpty(json)) return s;
        // 提取整数值
        s.QualityMinFileSizeBytes = ReadIntField(json, "QualityMinFileSizeBytes", s.QualityMinFileSizeBytes);
        // v4.17 修订十五：HighQualityThresholdBytes 键已彻底移除（旧配置中的该键被忽略）
        // v4.15：含图片图纸高清出图 dpi（缺字段=默认 4800）
        s.RasterImageDpi = ReadIntField(json, "RasterImageDpi", s.RasterImageDpi);
        s.PdfFlatOutput = ReadBoolField(json, "PdfFlatOutput", s.PdfFlatOutput);
        // v4.8：大文件阈值（缺字段=保持默认值；v4.12 起语义=超过则退回待转换）
        s.MaxDwgSizeBytes = ReadLongField(json, "MaxDwgSizeBytes", s.MaxDwgSizeBytes);
        // v4.12：单张转换超时秒数（缺字段=0 不限制）
        s.ConvertTimeoutSeconds = ReadIntField(json, "ConvertTimeoutSeconds", s.ConvertTimeoutSeconds);
        s.ExtraBlockNames = ReadStringArrayField(json, "ExtraBlockNames");
        // v4.6：界面状态记忆（缺字段=未记录过，保持属性默认值）
        s.LastOutputDir = ReadStringField(json, "LastOutputDir", s.LastOutputDir);
        s.MarginOn = ReadBoolField(json, "MarginOn", s.MarginOn);
        s.ColorIndex = ReadIntField(json, "ColorIndex", s.ColorIndex);
        s.ModeIndex = ReadIntField(json, "ModeIndex", s.ModeIndex);
        s.LineWeight = ReadBoolField(json, "LineWeight", s.LineWeight);
        s.QualityCheck = ReadBoolField(json, "QualityCheck", s.QualityCheck);
        s.BlockFrame = ReadBoolField(json, "BlockFrame", s.BlockFrame);
        s.WinLeft = ReadNullableIntField(json, "WinLeft");
        s.WinTop = ReadNullableIntField(json, "WinTop");
        s.WinWidth = ReadNullableIntField(json, "WinWidth");
        s.WinHeight = ReadNullableIntField(json, "WinHeight");
        s.WinMaximized = ReadBoolField(json, "WinMaximized", s.WinMaximized);
        // v4.17 修订三：PDF 子文件夹名称模板（缺字段=默认 PDF-HH:mm:ss；空白串按默认值处理由调用方兜底）
        s.PdfFolderTemplate = ReadStringField(json, "PdfFolderTemplate", s.PdfFolderTemplate);
        return s;
    }

    /// <summary>v4.6：字符串字段解析（带引号转义还原；字段缺失返回默认值）。</summary>
    private static string ReadStringField(string json, string key, string def)
    {
        int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (idx < 0) return def;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return def;
        int q1 = json.IndexOf('"', colon + 1);
        if (q1 < 0) return def;
        int i = q1 + 1;
        var sb = new StringBuilder();
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '\\' && i + 1 < json.Length)
            {
                char n = json[i + 1];
                switch (n)
                {
                    case '\\': sb.Append('\\'); i += 2; continue;
                    case '"': sb.Append('"'); i += 2; continue;
                    case 'n': sb.Append('\n'); i += 2; continue;
                    case 'r': sb.Append('\r'); i += 2; continue;
                    case 't': sb.Append('\t'); i += 2; continue;
                    default: sb.Append(n); i += 2; continue;
                }
            }
            if (c == '"') return sb.ToString();
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>v4.6：可空整数字段解析（缺失或 null 返回 null）。</summary>
    private static int? ReadNullableIntField(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return null;
        int end = colon + 1;
        while (end < json.Length && (json[end] == ' ' || json[end] == '\t' || json[end] == '\n' || json[end] == '\r')) end++;
        if (json.IndexOf("null", end, StringComparison.Ordinal) == end) return null;
        int numStart = end;
        if (end < json.Length && (json[end] == '-' || char.IsDigit(json[end]))) end++;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        if (end == numStart) return null;
        int v;
        return int.TryParse(json.Substring(numStart, end - numStart), out v) ? v : (int?)null;
    }

    /// <summary>极简 JSON 布尔解析：true/false。缺省或解析失败用默认值。</summary>
    private static bool ReadBoolField(string json, string key, bool def)
    {
        int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (idx < 0) return def;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return def;
        int end = colon + 1;
        while (end < json.Length && (json[end] == ' ' || json[end] == '\t' || json[end] == '\n' || json[end] == '\r')) end++;
        if (json.IndexOf("true", end, StringComparison.Ordinal) == end) return true;
        if (json.IndexOf("false", end, StringComparison.Ordinal) == end) return false;
        return def;
    }

    private static int ReadIntField(string json, string key, int def)
    {
        int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (idx < 0) return def;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return def;
        int end = colon + 1;
        while (end < json.Length && (json[end] == ' ' || json[end] == '\t' || json[end] == '\n' || json[end] == '\r')) end++;
        int numStart = end;
        if (end < json.Length && (json[end] == '-' || char.IsDigit(json[end]))) end++;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        if (end == numStart) return def;
        int v;
        return int.TryParse(json.Substring(numStart, end - numStart), out v) ? v : def;
    }

    private static long ReadLongField(string json, string key, long def)
    {
        int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (idx < 0) return def;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return def;
        int end = colon + 1;
        while (end < json.Length && (json[end] == ' ' || json[end] == '\t' || json[end] == '\n' || json[end] == '\r')) end++;
        int numStart = end;
        if (end < json.Length && (json[end] == '-' || char.IsDigit(json[end]))) end++;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        if (end == numStart) return def;
        long v;
        return long.TryParse(json.Substring(numStart, end - numStart), out v) ? v : def;
    }

    private static List<string> ReadStringArrayField(string json, string key)
    {
        var result = new List<string>();
        int idx = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
        if (idx < 0) return result;
        int lb = json.IndexOf('[', idx);
        if (lb < 0) return result;
        int rb = json.IndexOf(']', lb);
        if (rb < 0) return result;
        string inside = json.Substring(lb + 1, rb - lb - 1);
        // 简易字符串提取：扫描双引号
        int i = 0;
        while (i < inside.Length)
        {
            int q1 = inside.IndexOf('"', i);
            if (q1 < 0) break;
            int q2 = inside.IndexOf('"', q1 + 1);
            if (q2 < 0) break;
            string s = UnescapeJson(inside.Substring(q1 + 1, q2 - q1 - 1));
            if (s.Length > 0) result.Add(s);
            i = q2 + 1;
        }
        return result;
    }

    private static string EscapeJson(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                switch (n)
                {
                    case '\\': sb.Append('\\'); i++; break;
                    case '"': sb.Append('"'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    default: sb.Append(n); i++; break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
