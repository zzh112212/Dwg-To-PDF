using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DwgToPdf.Batch
{
    /// <summary>公司版图框标准库：条目 = 标称纸张 + 图框层名 + 框盒尺寸 + 来源模板。
    /// 运行时识别由 extframe_company.lsp 对已知图框层（图层图框/图框图层）快扫承担，
    /// 本库与"公司图框标准库.json"作为模板特征基线，用于自测断言与文档核对。</summary>
    internal sealed class FrameTemplateLibrary
    {
        public const int LibraryVersion = 1;
        public const double ToleranceMm = 0.75;
        public const string FileName = "公司图框标准库.json";

        public sealed class Entry
        {
            public string PaperSize;      // A4 / A3 / A2 / A1 / A0 / 2A0
            public string FrameLayer;     // 图框所在图层名
            public double BoxW;           // 框盒宽 (mm)
            public double BoxH;           // 框盒高 (mm)
            public string Source;         // 来源模板文件名
            public double NominalLong;
            public double NominalShort;
        }

        public readonly List<Entry> Entries = new List<Entry>();

        /// <summary>解析公司模板文件夹所有 dwg，生成标准库条目并加入本库。</summary>
        public void BuildFromFolder(string templatesFolder, string accore, string tmpDir)
        {
            var files = new List<string>();
            if (Directory.Exists(templatesFolder))
                files.AddRange(Directory.GetFiles(templatesFolder, "*.dwg"));
            var ex = new FrameExtractor(accore);
            var frames = ex.RunExtract(files, tmpDir, null);
            foreach (var f in files)
            {
                if (!frames.TryGetValue(f, out var box)) continue;
                string paper = InferPaper(box.Width, box.Height);
                if (paper == null) continue;
                var nom = NominalFor(paper);
                Entries.Add(new Entry
                {
                    PaperSize = paper,
                    FrameLayer = string.IsNullOrEmpty(box.Layer) ? "" : box.Layer,
                    BoxW = box.Width, BoxH = box.Height,
                    Source = Path.GetFileName(f),
                    NominalLong = nom.Long, NominalShort = nom.Short,
                });
            }
        }

        /// <summary>按尺寸（容差 tol，正负各 tol）匹配标准库，返回偏差最小者；无匹配返回 null。</summary>
        public Entry Match(double frameW, double frameH, string frameLayer, double tol)
        {
            Entry best = null; double bestDev = double.MaxValue;
            foreach (var e in Entries)
            {
                double dw = Math.Abs(e.BoxW - frameW), dh = Math.Abs(e.BoxH - frameH);
                if (dw <= tol && dh <= tol)
                {
                    double dev = dw + dh;
                    if (dev < bestDev) { bestDev = dev; best = e; }
                }
            }
            return best;
        }

        /// <summary>由框盒尺寸推断标称纸张（A4..2A0），超范围返回 null。</summary>
        internal static string InferPaper(double w, double h)
        {
            double lon = Math.Max(w, h), sho = Math.Min(w, h);
            var cand = new[]
            {
                ("2A0", 1682.0, 1189.0), ("A0", 1189.0, 841.0), ("A1", 841.0, 594.0),
                ("A2", 594.0, 420.0), ("A3", 420.0, 297.0), ("A4", 297.0, 210.0),
            };
            foreach (var c in cand)
                if (Math.Abs(lon - c.Item2) <= 1.0 && Math.Abs(sho - c.Item3) <= 1.0) return c.Item1;
            return null;
        }

        private static readonly Dictionary<string, (double Long, double Short)> Nom = new Dictionary<string, (double, double)>
        {
            ["A4"] = (297.0, 210.0), ["A3"] = (420.0, 297.0), ["A2"] = (594.0, 420.0),
            ["A1"] = (841.0, 594.0), ["A0"] = (1189.0, 841.0), ["2A0"] = (1682.0, 1189.0),
        };
        internal static (double Long, double Short) NominalFor(string paper) => Nom.TryGetValue(paper, out var v) ? v : (0, 0);

        // ---- 序列化（UTF-8，版本化） ----
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":").Append(LibraryVersion).Append(",\"entries\":[");
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"paper\":\"").Append(e.PaperSize)
                  .Append("\",\"layer\":\"").Append(e.FrameLayer)
                  .Append("\",\"w\":").Append(e.BoxW.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append(",\"h\":").Append(e.BoxH.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append(",\"src\":\"").Append(JsonEsc(e.Source)).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static FrameTemplateLibrary Deserialize(string json)
        {
            var lib = new FrameTemplateLibrary();
            if (string.IsNullOrWhiteSpace(json)) return lib;
            try
            {
                int eIdx = json.IndexOf("\"entries\"", StringComparison.Ordinal);
                int arrStart = json.IndexOf('[', eIdx);
                int arrEnd = json.LastIndexOf(']');
                string body = json.Substring(arrStart, arrEnd - arrStart + 1);
                int i = 0;
                while (i < body.Length)
                {
                    int start = body.IndexOf('{', i);
                    if (start < 0) break;
                    int end = body.IndexOf('}', start);
                    if (end < 0) break;
                    lib.Entries.Add(ParseEntry(body.Substring(start + 1, end - start - 1)));
                    i = end + 1;
                }
            }
            catch { /* 损坏 → 空库，调用方重建 */ }
            return lib;
        }

        private static Entry ParseEntry(string s)
        {
            var e = new Entry();
            string Get(string k) { int a = s.IndexOf(k + "\":\"", StringComparison.Ordinal); if (a < 0) return ""; a += k.Length + 3; int b = s.IndexOf('"', a); return b < 0 ? "" : s.Substring(a, b - a); }
            string GetNum(string k) { int a = s.IndexOf(k + "\":", StringComparison.Ordinal); if (a < 0) return "0"; a += k.Length + 2; int b = a; while (b < s.Length && (char.IsDigit(s[b]) || s[b] == '.')) b++; return s.Substring(a, b - a); }
            e.PaperSize = Get("paper"); e.FrameLayer = Get("layer"); e.Source = Get("src");
            double.TryParse(GetNum("w"), NumberStyles.Float, CultureInfo.InvariantCulture, out e.BoxW);
            double.TryParse(GetNum("h"), NumberStyles.Float, CultureInfo.InvariantCulture, out e.BoxH);
            var nom = NominalFor(e.PaperSize); e.NominalLong = nom.Long; e.NominalShort = nom.Short;
            return e;
        }

        private static string JsonEsc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}