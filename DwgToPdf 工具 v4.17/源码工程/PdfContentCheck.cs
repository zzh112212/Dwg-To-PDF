using System;
using System.IO;
using System.Text;

namespace DwgToPdf
{
    /// <summary>v4.3：质量审查多元化（不仅看文件大小）。
    /// PDF 仅文件小但内容可能全空（acoreconsole 静默产生空白 PDF），单纯大小阈值会被漏判。
    /// 本检查综合五个维度（任何一项不达标即视为内容异常）：
    ///   ① 文件大小 &lt; 纸型估算最小值（settings QualityMinFileSizeBytes 优先，默认 10KB）→ 失败（"PDF 体积过小"）
    ///   ② 无 MediaBox，或 MediaBox 长边/短边与期望纸型偏差 &gt; 5mm → 失败（"页尺寸异常"，内容被裁切/缺角/纸型错）
    ///   ③ 所有 /Contents 流累计字节数 &lt; 纸型估算最小内容流大小（每平方米 1KB，最少 2KB）→ 失败（"PDF 内容流过空"）
    ///   ④ 页数 &gt; 1 → 失败（"页数异常"，期望单页出图）
    ///   ⑤ 文件尾缺少 %%EOF → 失败（"PDF 不完整"，写盘被中断/文件被截断）
    /// 失败分类 reason 帮助日志/UI 区分具体原因（原版只有"内容缺失"一种）。
    ///
    /// v4.17 加严（补漏判）：
    ///   · ③ 原条件 "contentLen &gt; 0 &amp;&amp; contentLen &lt; minContent" 在 contentLen==0（完全无内容流）时
    ///     **跳过**检查 → 空 PDF 反而放行。现改为 contentLen &lt; minContent（去掉 contentLen &gt; 0），
    ///     并仅当存在 ≥5MB 的大流（嵌入大图/字体，必然有内容）时才豁免，避免大光栅图误判。
    ///   · ② 原实现只比对**长边**：纸型长短边比例失真（如 1189×400mm）时短边严重不符却仍放行。
    ///     现同时校验长边与短边；且解析成功却找不到 MediaBox 时直接判失败（文件损坏）。
    ///   · 新增 ⑤ 尾部 %%EOF 校验，捕获写盘中断产生的截断 PDF。
    /// </summary>
    internal static class PdfContentCheck
    {
        /// <summary>大流豁免阈值：单个流 ≥ 此值视为嵌入大图/字体（必然含内容），③ 不再按"内容流过空"判失败。</summary>
        private const long LargeStreamBytes = 5 * 1024 * 1024;

        /// <summary>页尺寸比对容差（mm）。</summary>
        private const double SizeToleranceMm = 5.0;

        /// <summary>综合检查。返回 (ok=true 通过；ok=false 失败，reason 描述具体原因)。
        /// v4.3：minFileSize 优先使用 settings 的 QualityMinFileSizeBytes（默认 10KB）；
        /// 若 &lt;=0 则回退为纸型估算的旧规则（每平方米 1KB，最少 2KB）。</summary>
        public static bool Check(string pdfPath, double paperLongMm, double paperShortMm, out string reason, int minFileSize = 0)
        {
            reason = null;
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                reason = "PDF 文件不存在";
                return false;
            }
            long fileSize = new FileInfo(pdfPath).Length;
            long minSize = minFileSize > 0 ? minFileSize : QualityThresholdBytes(paperLongMm, paperShortMm);
            if (fileSize < minSize)
            {
                reason = "PDF 体积过小（" + fileSize + " bytes < 阈值 " + minSize + " bytes）";
                return false;
            }
            // 读 PDF 二进制，提取 MediaBox 与 /Contents 流长度
            long contentLen = 0;          // 小流（< LargeStreamBytes）累计字节
            bool hasLargeStream = false;  // 是否存在 ≥ LargeStreamBytes 的流
            int lengthTokenCount = 0;     // /Length 命中数（0 → 完全无内容流）
            double? mediaLong = null, mediaShort = null;
            int pageCount = 0;
            bool parsed = false;
            try
            {
                byte[] bytes = File.ReadAllBytes(pdfPath);
                string pdfText = ExtractAscii(bytes);
                // MediaBox [w h] 寻找
                int idx = 0;
                while ((idx = pdfText.IndexOf("/MediaBox", idx, StringComparison.Ordinal)) >= 0)
                {
                    int lb = pdfText.IndexOf('[', idx);
                    if (lb < 0) { idx += 9; continue; }
                    int rb = pdfText.IndexOf(']', lb);
                    if (rb < 0) break;
                    string inside = pdfText.Substring(lb + 1, rb - lb - 1).Trim();
                    // 分隔符传 null = 按全部空白字符切分（空格/Tab/换行都覆盖，PDF 数组元素间允许任意空白）
                    string[] parts = inside.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double w) &&
                        double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double h))
                    {
                        // MediaBox 单位为 pt（1 pt = 1/72 inch = 25.4/72 mm）
                        double wMm = w * 25.4 / 72.0;
                        double hMm = h * 25.4 / 72.0;
                        // 取第一张 MediaBox 作为 page 尺寸
                        if (!mediaLong.HasValue)
                        {
                            mediaLong = Math.Max(wMm, hMm);
                            mediaShort = Math.Min(wMm, hMm);
                        }
                        pageCount++;
                    }
                    idx = rb + 1;
                }
                // /Length 后跟数字（流字节数）累加（/Contents 流）。
                // 实测 DWG To PDF 输出的 /Length 恒为直接数字（非 "n 0 R" 间接引用）。
                idx = 0;
                while ((idx = pdfText.IndexOf("/Length", idx, StringComparison.Ordinal)) >= 0)
                {
                    int after = idx + 7;
                    int end = after;
                    while (end < pdfText.Length && (char.IsWhiteSpace(pdfText[end]) || char.IsDigit(pdfText[end]))) end++;
                    string numStr = pdfText.Substring(after, end - after).Trim();
                    if (long.TryParse(numStr, out long len) && len > 0)
                    {
                        lengthTokenCount++;
                        if (len < LargeStreamBytes) contentLen += len;
                        else hasLargeStream = true;
                    }
                    idx = end;
                }
                parsed = true;
            }
            catch (Exception)
            {
                // 解析失败不直接判失败——文件大小已通过；只跳过 MediaBox/Contents 检查
                reason = null;
            }
            if (parsed)
            {
                // ② 页尺寸：无 MediaBox（文件损坏）或长/短边任一与期望纸型偏差 > 5mm → 失败
                if (!mediaLong.HasValue)
                {
                    reason = "页尺寸异常（未找到 MediaBox，疑似 PDF 损坏）";
                    return false;
                }
                bool longOk = Near(mediaLong.Value, paperLongMm) || Near(mediaLong.Value, paperShortMm);
                bool shortOk = Near(mediaShort.Value, paperLongMm) || Near(mediaShort.Value, paperShortMm);
                if (!longOk || !shortOk)
                {
                    reason = "页尺寸异常（MediaBox " + mediaLong.Value.ToString("0") + "×" + mediaShort.Value.ToString("0")
                        + "mm，期望 " + paperLongMm.ToString("0") + "×" + paperShortMm.ToString("0") + "mm）";
                    return false;
                }
                // ③ 内容流字节数：纸型 A0 内容流最少 ~5KB，A1~3KB，A2~2KB，<此值视为空
                // 简化为：每平方米纸面至少 1KB 内容。存在大流（嵌入大图）时豁免，避免大光栅图误判。
                double areaM2 = (paperLongMm / 1000.0) * (paperShortMm / 1000.0);
                long minContent = (long)Math.Max(2000, areaM2 * 1000); // 最少 2KB 或 1KB/m²
                if (!hasLargeStream && contentLen < minContent)
                {
                    reason = "PDF 内容流过空（累计 " + contentLen + " bytes，期望 ≥ " + minContent + " bytes"
                        + (lengthTokenCount == 0 ? "，未发现任何内容流" : "") + "，疑似内容缺失/裁切）";
                    return false;
                }
                // ④ 页数
                if (pageCount > 1)
                {
                    reason = "页数异常（" + pageCount + " 页，期望 1 页）";
                    return false;
                }
                // ⑤ 尾部 %%EOF（截断检测）——放最后：代价最高（可能重试等待写盘），前四项已过才做
                if (!HasEofMarker(pdfPath))
                {
                    reason = "PDF 不完整（文件尾缺少 %%EOF，疑似写盘中断/文件被截断）";
                    return false;
                }
            }
            return true;
        }

        /// <summary>尾部 %%EOF 标记检测（截断 PDF 判定）。写盘/落盘可能有延迟，
        /// 缺标记时短暂重试（300ms × 3）后再判失败，避免误杀正常文件；读取异常一律不判失败。</summary>
        private static bool HasEofMarker(string pdfPath)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        long take = Math.Min(4096L, fs.Length);
                        if (take <= 0) return false;
                        fs.Seek(-take, SeekOrigin.End);
                        byte[] buf = new byte[take];
                        int read = fs.Read(buf, 0, buf.Length);
                        if (ExtractAscii(buf, read).IndexOf("%%EOF", StringComparison.Ordinal) >= 0) return true;
                    }
                }
                catch
                {
                    return true; // 读取异常不因该维度判失败（保守放行）
                }
                if (attempt < 2) System.Threading.Thread.Sleep(300);
            }
            return false;
        }

        private static bool Near(double a, double b) => Math.Abs(a - b) <= SizeToleranceMm;

        /// <summary>从 PDF 二进制中提取可打印 ASCII（PDF 内对象常用 plain ASCII 文本，
        /// 二进制流会被 FlateDecode 压缩——我们用 ASCII 扫主要对象（MediaBox/Pages/Length）足够）。</summary>
        private static string ExtractAscii(byte[] bytes)
        {
            return ExtractAscii(bytes, bytes.Length);
        }

        private static string ExtractAscii(byte[] bytes, int count)
        {
            if (count > bytes.Length) count = bytes.Length;
            StringBuilder sb = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                byte b = bytes[i];
                if (b >= 0x20 && b < 0x7F) sb.Append((char)b);
                else if (b == '\n' || b == '\r' || b == '\t') sb.Append(' ');
            }
            return sb.ToString();
        }

        private static long QualityThresholdBytes(double paperLongMm, double paperShortMm)
        {
            // 与 ConverterEngine 现有规则一致：每平方米 1KB
            double areaM2 = (paperLongMm / 1000.0) * (paperShortMm / 1000.0);
            return (long)Math.Max(2000, areaM2 * 1000);
        }
    }
}
