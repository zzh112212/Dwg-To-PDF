using System;
using System.IO;
using System.Text;

namespace DwgToPdf
{
    /// <summary>v4.3：质量审查多元化（不仅看文件大小）。
    /// PDF 仅文件小但内容可能全空（acoreconsole 静默产生空白 PDF），单纯大小阈值会被漏判。
    /// 本检查综合四个维度（任何一项不达标即视为内容异常）：
    ///   ① 文件大小 < 纸型估算最小值（settings QualityMinFileSizeBytes 优先，默认 10KB）→ 失败（"PDF 体积过小"）
    ///   ② PDF MediaBox 与期望纸型偏差 > 5mm → 失败（"页尺寸异常"，可能内容被裁切/缺角）
    ///   ③ 所有 /Contents 流累计字节数 < 纸型估算最小内容流大小（每平方米 1KB，最少 2KB）→ 失败（"PDF 内容流过空"）
    ///   ④ 页数 > 1 → 失败（"页数异常"，期望单页出图）
    /// 失败分类 reason 帮助日志/UI 区分具体原因（原版只有"内容缺失"一种）。</summary>
    internal static class PdfContentCheck
    {
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
            long contentLen = 0;
            double? mediaLong = null, mediaShort = null;
            int pageCount = 0;
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
                    string[] parts = inside.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
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
                // /Length 后跟数字（流字节数）累加（/Contents 流）
                idx = 0;
                int matchCount = 0;
                while ((idx = pdfText.IndexOf("/Length", idx, StringComparison.Ordinal)) >= 0)
                {
                    int after = idx + 7;
                    int end = after;
                    while (end < pdfText.Length && (char.IsWhiteSpace(pdfText[end]) || char.IsDigit(pdfText[end]))) end++;
                    string numStr = pdfText.Substring(after, end - after).Trim();
                    if (long.TryParse(numStr, out long len))
                    {
                        // 过滤：流长度 < 5MB 的算"内容流"（图像/字体/内容）；累积
                        if (len > 0 && len < 5 * 1024 * 1024) contentLen += len;
                        matchCount++;
                    }
                    idx = end;
                }
            }
            catch (Exception ex)
            {
                // 解析失败不直接判失败——文件大小已通过；只跳过 MediaBox/Contents 检查
                reason = null;
            }
            // 纸型与 MediaBox 偏差检查（容差 5mm，A 系纸型宽度可能与多个候选纸型匹配，按最大值匹配）
            if (mediaLong.HasValue && Math.Abs(mediaLong.Value - paperLongMm) > 5.0 && Math.Abs(mediaLong.Value - paperShortMm) > 5.0)
            {
                reason = "页尺寸异常（MediaBox " + mediaLong.Value.ToString("0") + "×" + mediaShort.Value.ToString("0")
                    + "mm，期望 " + paperLongMm.ToString("0") + "×" + paperShortMm.ToString("0") + "mm）";
                return false;
            }
            // 内容流字节数估算：纸型 A0 内容流最少 ~5KB，A1~3KB，A2~2KB，<此值视为空
            // 简化为：每平方米纸面至少 1KB 内容
            double areaM2 = (paperLongMm / 1000.0) * (paperShortMm / 1000.0);
            long minContent = (long)Math.Max(2000, areaM2 * 1000); // 最少 2KB 或 1KB/m²
            if (contentLen > 0 && contentLen < minContent)
            {
                reason = "PDF 内容流过空（累计 " + contentLen + " bytes，期望 ≥ " + minContent + " bytes，疑似内容缺失/裁切）";
                return false;
            }
            if (pageCount > 1)
            {
                reason = "页数异常（" + pageCount + " 页，期望 1 页）";
                return false;
            }
            return true;
        }

        /// <summary>从 PDF 二进制中提取可打印 ASCII（PDF 内对象常用 plain ASCII 文本，
        /// 二进制流会被 FlateDecode 压缩——我们用 ASCII 扫主要对象（MediaBox/Pages/Length）足够）。</summary>
        private static string ExtractAscii(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
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
