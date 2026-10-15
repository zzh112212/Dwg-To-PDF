using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace DwgToPdf.Batch
{
    /// <summary>结果实体：图框范围。</summary>
    internal sealed class FrameBox
    {
        public double MinX, MinY, MaxX, MaxY;
        public string Layer;
        public string Source;
        /// <summary>v4.10：疑似图框块提示（"块名 x1 y1 x2 y2"多项以"；"连接）。
        /// 非空时 ConverterEngine 在日志/失败原因中提示用户开启"识别块图框"重转。</summary>
        public string BlockHint;

        /// <summary>v4.12 修订：全图范围（zoom-e 范围，来自 lsp 的 EXTENT 行）= minx/miny/maxx/maxy。
        /// 用于识别框完整性交叉校验：识别框没覆盖全图范围时，lsp 已按全图范围修正（Source="EXTENT"）。</summary>
        public double[] Extent;

        /// <summary>v4.12 修订：识别框没覆盖全图范围，且全图范围本身不可信（散线撑大/比例异常）→
        /// 保持原识别框出图，但出图可能缺内容，ConverterEngine 会写日志提示人工确认。</summary>
        public bool ExtentOut;

        /// <summary>v4.15：Model 空间光栅图片（IMAGE）实体数（来自 lsp 的 IMG 行）。
        /// &gt;0 时出图阶段改用"光栅分辨率增强"的打印配置（默认 1200dpi）；
        /// 否则小图带图片会走标准 pc3（光栅 150dpi）→ 图片发糊（用户实测反馈）。</summary>
        public int ImageCount;

        /// <summary>v4.15：真光栅 IMAGE 实体数 / 其中"外部图文件缺失"的个数（来自 lsp 的 IMGR 行）。
        /// 光栅增强出图只对**真光栅且无缺失外部图**的图纸启用：
        ///   · 外部图缺失的图纸走 COM 打印会输出**空白页**（accoreconsole 的 -EXPORT 正常，
        ///     2026-09-16 全量 183 张实测：6 张 H77 双面胶的图片路径指向他人桌面）；
        ///   · OLE 嵌入图片 / PDF-DWF 参考底图不算光栅（全量里 35 张被判"含图片"，真含
        ///     IMAGE 的仅 8 张；其余白走 COM，单张最慢 932s）。</summary>
        public int RasterCount;
        public int RasterMissingCount;

        /// <summary>v4.16：Model 布局"着色打印"组码 76 值（来自 lsp 的 G76 行；缺失=0）。
        /// ≠0（如 4=渲染）时出图阶段改用 COM 通道——accoreconsole 的 -EXPORT 会把整页
        /// 按渲染光栅化，线条被烘成位图 → 视觉"打印发浅"（用户 8/8 命中、12 张对照 0/12）。</summary>
        public int G76;
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    /// <summary>v4.3：图框合理性校验。
    /// 用户场景：H47/H77 这类图纸图框由 4 条独立 LINE 围成（非 polyline、非块），
    /// extframe.lsp 通用版按 layer 合并 bbox 时，被同层散 LINE 撑大到 5534×841 / 1995×1368 等异常范围，
    /// 选出的 frame 与真实图框偏差 4.7~18.6 倍（参考 PDF 内容 291×410），出图后图框线/内容被裁。
    /// 用户决定：识别结果不可信时直接判失败，标注"图框问题"，由人工在 CAD 中处理。
    /// 规则：company（公司标准库快扫）放行；BLOCK:*（块识别命中，勾选时）放行；
    /// legacy（通用版"图层图框"层，v4.6.1 起不再无条件信任）与 auto（通用版按 layer bbox）均需满足：
    /// A 系列标准比例（≤3）+ 短边 ≤2100mm + 长边 ≤3000mm；
    /// 大图框区间（短边>1600mm，v4.8.1 放宽放行 2A0/2A0+ 级别）额外要求 √2 比例 ±10%，
    /// 防止散线撑大恰好落入大尺寸区间被误放。
    /// 否则 <see cref="IsAcceptable"/> 返回 false，FailReason 描述原因。</summary>
    internal static class FrameValidator
    {
        // 阈值（mm）：auto 模式识别 frame 的合理范围上限
        public const double MaxAspectRatio = 3.0;       // 宽/高比例上限
        // v4.8.1：1600→2100。探针实测（2026-09-08 11 个失败文件）8 个为真实大图框：
        // 2378×1682（2A0/4A0）与 2835×2006（2A0+），短边 1682/2006 均超旧上限 1600 被误判失败
        public const double MaxShortEdge = 2100.0;      // 短边上限（A0=841, A0+=1189, 2A0=1682, 2A0+≈2006）
        public const double MaxLongEdge = 3000.0;       // 长边上限（2A0=2378, 2A0+≈2836；拦截更大异常范围）
        // 大图框区间起点与 √2 比例容差：短边>1600 的 frame 必须是 A 系列 √2 比例（±10%）
        public const double LargeFrameShortEdge = 1600.0;
        public const double LargeFrameRatioMin = 1.27;  // √2-10%
        public const double LargeFrameRatioMax = 1.56;  // √2+10%
        // v4.12 修订：EXTENT（全图范围）可用的最大尺寸 = 最大全出血纸 4A0（1682×2378）减去两侧 M=5mm。
        // 超过则不采用全图范围（保留识别框结果）。
        // v4.17 注：原注释称"超过则 PageSpec 走超大图框兜底取最小纸→整张被压到 A4"——
        // 该 A4 兜底缺陷已在 PageSpec.Resolve 修复（现兜底到最大纸型 4A0）。此闸门保留，
        // 目的改为：① 避免超过 4A0 的全图范围被大幅缩小出图；② 拦截"散线撑大"的异常全图范围。
        public const double ExtentMaxLongEdge = 2378.0 - 10.0;   // 2368
        public const double ExtentMaxShortEdge = 1682.0 - 10.0;  // 1672

        public static bool IsAcceptable(FrameBox box, out string reason)
        {
            reason = null;
            if (box == null) return false;
            string src = box.Source ?? "";
            // v4.10：ERR = lsp 主结果为失败哨兵（未识别到任何图框），仅可能携带 BlockHint 提示；
            // 直接走失败路径（是否附加块提示由 ConverterEngine 决定）。
            if (src == "ERR")
            {
                reason = "未识别到任何图框";
                return false;
            }
            // 公司版（company = 公司标准库图框层快扫）→ 可信
            if (src.IndexOf("company", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // v4.12 修订：EXTENT = 识别框未覆盖全图范围，lsp 已核查"合理幅面 + 块佐证 + 不超最大纸"，
            // 此处复核一次（同口径），避免两端判据漂移。
            if (src == "EXTENT")
            {
                double ew = Math.Abs(box.Width);
                double eh = Math.Abs(box.Height);
                double es = Math.Min(ew, eh);
                double el = Math.Max(ew, eh);
                // 必须能被最大纸型 4A0（1682×2378 全出血）容纳（窗口再外扩 M=5mm 两侧）
                if (el > ExtentMaxLongEdge || es > ExtentMaxShortEdge)
                {
                    reason = $"全图范围超出最大纸型（{ew:0}×{eh:0}mm > 4A0 可用 {ExtentMaxLongEdge:0}×{ExtentMaxShortEdge:0}mm）";
                    return false;
                }
                if (es < 100.0 || es > MaxShortEdge || el > MaxLongEdge)
                {
                    reason = $"全图范围超出合理图幅（{ew:0}×{eh:0}mm），疑似散线撑大";
                    return false;
                }
                if (el / es > MaxAspectRatio)
                {
                    reason = $"全图范围比例异常（{ew:0}×{eh:0}mm，比例 {el / es:0.0} > {MaxAspectRatio:0.0}）";
                    return false;
                }
                if (es > LargeFrameShortEdge && (el / es < LargeFrameRatioMin || el / es > LargeFrameRatioMax))
                {
                    reason = $"全图范围比例异常（{ew:0}×{eh:0}mm，比例 {el / es:0.00} 不在 √2±10% 内）";
                    return false;
                }
                return true;
            }
            // v4.6.1：legacy（通用版命中"图层图框"层）不再无条件信任——实测该层可能只有
            // 微小/局部实体（2.5×2.5mm、277×131mm 等，非真图框），同样执行下方尺寸/比例校验。
            // lsp 端已同步加校验（短边命中 A 系列才直接采信 legacy，否则继续 auto 评分），此处双保险。
            // 块识别命中 = 用户主动勾选，扩展块框 → 可信
            if (src.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase))
                return true;
            // 通用版 auto：按尺寸/比例校验
            double w = Math.Abs(box.Width);
            double h = Math.Abs(box.Height);
            double shortE = Math.Min(w, h);
            double longE = Math.Max(w, h);
            // 无效 bbox
            if (shortE < 1.0 || longE < 1.0)
            {
                reason = "图框为空（识别无有效 bbox）";
                return false;
            }
            // 极小图框（早前 22×15 二维码那种）→ 极可能是误识
            if (shortE < 100.0)
            {
                reason = $"图框过小（短边 {shortE:0}mm < 100mm，疑似局部图元误识）";
                return false;
            }
            // v4.9：RECT4 = lsp 检测到的真实矩形图框（4 条散 LINE 闭合矩形 / 闭合 LWPOLYLINE 矩形），
            // 且全图范围 ≈ 矩形（图框外无离群实体，等价 zoom e 范围）→ 真图框可信。
            // 放行长条/非A系列矩形（用户确认：L42P 矩形图框、H47/H77 四条散线图框均应识别），
            // 不再受比例/√2/上限约束，仅保留极端护栏。
            if (src == "RECT4")
            {
                if (longE > 15000.0)
                {
                    reason = $"图框长边过大（{longE:0}mm > 15000mm，疑似误识矩形）";
                    return false;
                }
                return true;
            }
            // v4.9：RECT4_OUT = 已识别出矩形图框，但图框外检测到其他实体（全图范围大于矩形）→
            // 出图范围不可信。按用户要求（2026-09-09）提示"疑似存在离群实体"，由人工确认。
            if (src == "RECT4_OUT")
            {
                // v4.17：区分两种成因，提示更精确（用户要求"多图幅目前不管，但提示要说准"）：
                //   · 图框外零散离群实体（原提示）；
                //   · 模型空间**并排绘制了多张图纸**——实测 H77 两张：
                //       H77_RC100C01A0 全图 3910×845mm 而矩形仅 2541×841mm；
                //       H77_RE100C01A0 全图 3805×849mm 而矩形仅 1315×841mm。
                //     特征：全图短边与矩形短边相当（≤1.15 倍，即"同高并排"），
                //     而全图长边 ≥ 矩形长边 1.4 倍（实测两例为 1.54 倍 / 2.89 倍）。
                double exLong = 0.0, exShort = 0.0;
                if (box.Extent != null && box.Extent.Length >= 4)
                {
                    double exW = Math.Abs(box.Extent[2] - box.Extent[0]);
                    double exH = Math.Abs(box.Extent[3] - box.Extent[1]);
                    exLong = Math.Max(exW, exH);
                    exShort = Math.Min(exW, exH);
                }
                if (exLong > 0.0 && longE > 0.0 && exLong >= longE * 1.4 && exShort <= shortE * 1.15)
                {
                    reason = $"图框外存在大范围图形（全图 {exLong:0}×{exShort:0}mm vs 识别图框 {w:0}×{h:0}mm，约 "
                        + $"{exLong / longE:0.0} 倍），疑似模型空间并排绘制了多张图纸；本工具只支持单张图纸，请先拆分为单张后重转";
                    return false;
                }
                reason = "疑似存在离群实体（已识别图框矩形，但图框外检测到其他实体，请人工确认）";
                return false;
            }
            // 宽高比异常（散线拉伸典型：RE100 5534×841 比例 6.6）
            if (longE / shortE > MaxAspectRatio)
            {
                reason = $"图框比例异常（{w:0}×{h:0}mm，比例 {longE / shortE:0.0} > {MaxAspectRatio:0.0}，疑似散 LINE 撑大）";
                return false;
            }
            // v4.8.1：大图框区间（短边>1600，2A0/2A0+/4A0 级别）必须满足 A 系列 √2 比例 ±10%。
            // 散线撑大即使尺寸落入大图框区间，比例也几乎不会恰好 √2 → 比例兜底防误放
            if (shortE > LargeFrameShortEdge)
            {
                double ratio = longE / shortE;
                if (ratio < LargeFrameRatioMin || ratio > LargeFrameRatioMax)
                {
                    reason = $"大图框比例异常（{w:0}×{h:0}mm，比例 {ratio:0.00} 不在 √2±10% 内，疑似散线撑大范围）";
                    return false;
                }
            }
            // 短边超上限（v4.8.1 放宽：1600→2100，放行 2A0=1682 / 2A0+≈2006 实测真实大图框）
            if (shortE > MaxShortEdge)
            {
                reason = $"图框短边过大（{shortE:0}mm > {MaxShortEdge:0}mm，疑似识别到整图散线范围，可尝试勾选\"识别块图框\"）";
                return false;
            }
            // 长边超上限（v4.8.1 放宽：2500→3000，放行 2A0=2378 / 2A0+≈2836）
            if (longE > MaxLongEdge)
            {
                reason = $"图框长边过大（{longE:0}mm > {MaxLongEdge:0}mm，疑似识别到整图散线范围）";
                return false;
            }
            return true;
        }
    }

    /// <summary>经 accoreconsole 运行 extframe.lsp 逐文件求图框。</summary>
    internal sealed class FrameExtractor
    {
        public const int DefaultTimeoutMs = 180000;

        /// <summary>v3.4：识别结果写出稳定后，等待进程自行退出的宽限时间；超时则主动结束进程并采纳结果。</summary>
        public const int HarvestGraceMs = 3000;

        // 探针校准结论（v3.0 Task 1）：accoreconsole 按 ANSI 代码页解析 .scr 与无 BOM 的 .lsp；
        // 中文路径/中文字符串必须用 GB2312(936) 编码写入，否则乱码导致 load 失败。
        private static readonly Encoding Gb2312 = Encoding.GetEncoding("GB2312");

        // v3.8.1 修复：accoreconsole 2021 的 (load) 命令无法加载外部 .lsp 文件（报"无法找到文件"，
        // 即使路径纯英文且文件存在），导致图框识别函数未定义、40/40 全失败、全部走 COM 兜底极慢。
        // 改为把 LISP 内容内联到 .scr 脚本中，完全规避 load 命令。AutoCAD 2026 下 load 正常，
        // 内联方式同样兼容。
        // v4.17 修订（2026-10-14 实测，5 次交错 A/B）：当年 (load) 失败的真因是**反斜杠路径**
        // （accoreconsole 把 "\" 当转义/续行），改用正斜杠路径后 (load) 在 2021 下完全可用，
        // 且比内联更快——内联脚本 32014 字节需 accoreconsole 逐字节解析，实测：
        //   内联平均 3983ms / (load) 平均 3564ms ⇒ 每张省 ~420ms（约 10%）。
        // 故 v4.17 默认改回 (load)（LISP 先复制到临时目录，规避中文路径）；
        // 一旦某次会话未产出有效结果，则**本进程内永久回退内联**并重试（只付一次代价），
        // 兼顾"环境确实不支持 load"的极端情况。
        private static readonly object _lspCacheLock = new object();
        private static readonly Dictionary<string, string> _lspCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>v4.17：本进程内是否强制内联 LISP（(load) 失败后置位，后续文件不再尝试 load）。</summary>
        private volatile bool _useInlineLisp;

        private static readonly object _lspCopyLock = new object();

        /// <summary>读取并缓存 LISP 文件内容（GB2312 编码），用于内联到 .scr 脚本。</summary>
        private static string ReadLspContent(string path)
        {
            lock (_lspCacheLock)
            {
                if (_lspCache.TryGetValue(path, out var cached)) return cached;
                var content = File.ReadAllText(path, Gb2312);
                _lspCache[path] = content;
                return content;
            }
        }

        /// <summary>v4.17：把 LISP 复制到临时目录（ASCII 路径）供 (load) 使用。
        /// accoreconsole 不支持中文路径，故不直接 load 安装目录下的 .lsp。
        /// 已存在且长度一致则跳过复制；失败返回 null（调用方回退内联）。</summary>
        private static string EnsureLispInTemp(string lspPath, string tempDir)
        {
            try
            {
                string dst = Path.Combine(tempDir, Path.GetFileName(lspPath));
                lock (_lspCopyLock)
                {
                    var d = new FileInfo(dst);
                    var s = new FileInfo(lspPath);
                    if (!d.Exists || d.Length != s.Length) File.Copy(lspPath, dst, true);
                }
                return dst;
            }
            catch { return null; }
        }

        /// <summary>v4.17：写识别脚本。preferLoad=true 时用 (load "ASCII路径") 外部加载 LISP（快）；
        /// 否则把 LISP 内容内联进 .scr（兼容 load 不可用的环境）。返回 true = 实际用了 (load)。
        /// 脚本尾部显式答 Y（放弃修改），防止会话结束写回源图（见 PlotSession.BuildScript 注释）。</summary>
        private bool WriteExtractScript(string lspPath, string tempDir, string callLine, string tag,
                                        bool preferLoad, out string scr)
        {
            string head = null;
            bool usedLoad = false;
            if (preferLoad)
            {
                string tempLsp = EnsureLispInTemp(lspPath, tempDir);
                if (tempLsp != null)
                {
                    head = "(load \"" + tempLsp.Replace("\\", "/") + "\")\n";
                    usedLoad = true;
                }
            }
            if (head == null) head = ReadLspContent(lspPath) + "\n";
            try { Directory.CreateDirectory(tempDir); } catch { }   // v4.11：外部清理可能删掉临时目录
            scr = Path.Combine(tempDir, tag + "_" + Guid.NewGuid().ToString("N") + ".scr");
            File.WriteAllText(scr, head + callLine + "QUIT\nY\n", Gb2312);
            return usedLoad;
        }

        private readonly string _accoreConsole;

        public FrameExtractor(string accoreConsole) { _accoreConsole = accoreConsole; }

        /// <summary>块图框识别开关（v4.3 新增）：开启时 extframe.lsp 展开 INSERT 块定义
        /// 收集图框端点（应对图框是块引用 EQWE 等的图纸）。默认关闭——开启会拖慢识别
        /// （每张 dwg 遍历 INSERT 块），仅在确认图纸图框是块的场景手动勾选。</summary>
        public bool RecognizeBlockFrame { get; set; } = false;

        /// <summary>v4.3：用户自定义 INSERT 块名（来自 settings.json）。
        /// 在块识别过程中，块名在此列表中 → 强制视为图框块（跳过特征过滤）；
        /// 不在列表中 → 需块内含图框特征（闭合 LWPOLYLINE 且尺寸合理）才参与候选。</summary>
        public System.Collections.Generic.List<string> ExtraBlockNames { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>v4.14：识别并发上限（0=用 SmartConcurrency 默认）。流水线模式下与出图池并行，
        /// 需主动压低识别路数（如 6），避免总会话数超出 CPU 承载（手册第4章：并发过高反而更慢）。</summary>
        public int MaxWorkers { get; set; } = 0;

        /// <summary>动态任务池：并发由 SmartConcurrency 决定（上限于 RecognitionMax），
        /// 共享原子计数器动态取任务，为每文件生成临时识别脚本并执行；返回 path→FrameBox（失败文件不含）。
        /// isCancelled：每文件开始前与等待期间检查，已取消则终止当前会话并停止后续。</summary>
        public Dictionary<string, FrameBox> RunExtract(List<string> files, string tempDir,
                                                       Action<string, bool> onFileDone = null,
                                                       Func<bool> isCancelled = null)
        {
            var result = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
            if (files == null || files.Count == 0) return result;
            int w = MaxWorkers > 0 ? Math.Max(1, Math.Min(MaxWorkers, files.Count))
                                   : SmartConcurrency.RecognitionWorkers(files.Count);
            var local = new Dictionary<string, FrameBox>[w];
            var threads = new List<Thread>(w);
            int[] next = new int[1];
            for (int i = 0; i < w; i++)
            {
                int wi = i;
                local[wi] = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
                var t = new Thread(() => ExtractPool(wi, next, files, tempDir, local[wi], onFileDone, isCancelled));
                t.IsBackground = true;
                threads.Add(t);
            }
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();
            foreach (var d in local)
                foreach (var kv in d)
                    result[kv.Key] = kv.Value;
            return result;
        }

        private void ExtractPool(int wi, int[] next, List<string> files, string tempDir, Dictionary<string, FrameBox> local,
                                 Action<string, bool> onFileDone, Func<bool> isCancelled)
        {
            while (true)
            {
                if (isCancelled != null && isCancelled()) break;   // 用户停止：不再处理剩余文件
                int idx = Interlocked.Increment(ref next[0]) - 1;
                if (idx >= files.Count) break;
                string f = files[idx];
                FrameBox box = null;
                string outTxt = null;
                try
                {
                    string lsp = LspPath();
                    string blockFlag = RecognizeBlockFrame ? "1" : "0";
                    // v4.3：把用户自定义额外块名以逗号分隔字符串传给 LISP（"0"=无）；
                    // 块名内可能含中文/特殊字符，需 JSON 转义后嵌入 .scr（accoreconsole 读 .scr 为 GB2312 解析，
                    // ASCII 安全的 JSON 字符串是稳妥做法：仅字母数字_$\\）
                    string extraBlocksFlag = "0";
                    if (ExtraBlockNames != null && ExtraBlockNames.Count > 0)
                    {
                        var sb = new StringBuilder();
                        foreach (var n in ExtraBlockNames)
                        {
                            if (string.IsNullOrWhiteSpace(n)) continue;
                            // 仅允许 ASCII 安全字符；中文块名需用户在 settings.json 中确认
                            // 为简化这里做最小过滤：非 ASCII 字符替换为 ?，避免 .scr 编码歧义
                            var safe = new StringBuilder();
                            foreach (char c in n.Trim())
                            {
                                safe.Append(c < 0x80 ? c : '?');
                            }
                            if (sb.Length > 0) sb.Append(",");
                            sb.Append(safe);
                        }
                        if (sb.Length > 0) extraBlocksFlag = sb.ToString();
                    }
                    outTxt = Path.Combine(tempDir, "ext_" + wi + "_" + Guid.NewGuid().ToString("N") + ".out");
                    string callLine = "(extframe \"" + outTxt.Replace("\\", "/") + "\" \"" + blockFlag + "\" \"" + extraBlocksFlag + "\")\n";
                    string isoDir = Path.Combine(tempDir, "iso_" + wi);
                    Directory.CreateDirectory(isoDir);
                    // v4.17：默认走 (load) 外部加载（快 ~420ms/张）；失败则回退内联
                    string scr;
                    bool usedLoad = WriteExtractScript(lsp, tempDir, callLine, "ext_" + wi, !_useInlineLisp, out scr);
                    RunSession(f, scr, outTxt, isoDir, isCancelled);
                    box = ParseResult(outTxt, f);
                    // v4.17：(load) 未产出有效结果（如环境不支持 load）→ 本进程内永久回退内联并重试一次。
                    // 只付一次代价，其余文件直接走内联，不会每张都慢一倍。
                    if (box == null && usedLoad)
                    {
                        _useInlineLisp = true;
                        try { if (File.Exists(outTxt)) File.Delete(outTxt); } catch { }
                        string scr2;
                        WriteExtractScript(lsp, tempDir, callLine, "exti_" + wi, false, out scr2);
                        RunSession(f, scr2, outTxt, isoDir, isCancelled);
                        box = ParseResult(outTxt, f);
                    }
                }
                catch (OperationCanceledException) { break; }      // 用户停止：终止当前会话后停止
                catch (TimeoutException)
                {
                    // v3.3 整改①b：个别图纸会让 accoreconsole 完成识别后进程退出阶段挂起，
                    // RunSession 超时强杀。此时识别结果往往已写入 .out 文件（557单品 MF171 实测），
                    // 若结果有效仍采纳，避免把好图纸误判为识别失败进而走 COM 兜底产出空白 PDF。
                    box = ParseResult(outTxt, f);
                }
                catch { box = null; }
                try { onFileDone?.Invoke(f, box != null); } catch { }
                if (box != null) local[f] = box;
            }
        }

        private void RunSession(string dwg, string scr, string outTxt, string isoDir, Func<bool> isCancelled)
        {
            // 先删旧结果，防止读到上次残留
            try { if (File.Exists(outTxt)) File.Delete(outTxt); } catch { }
            // v3.8.1：去掉 /isolate（同 PlotSession），accoreconsole 2021 /isolate 不可用。
            var psi = new ProcessStartInfo(_accoreConsole,
                " /i \"" + dwg + "\" /s \"" + scr + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("启动 accoreconsole 失败");
                ComStarter.RegisterAccore(p.Id);   // v4.11：登记，收尾收割孤儿
                // v3.4 整改②：提前收割。个别图纸会让 accoreconsole 完成识别并写出结果后，
                // 进程退出阶段挂起（实测等满 180s 超时），导致批量进度"卡在最后一个文件"。
                // 现在轮询 .out 文件：一旦写出且大小稳定（间隔 500ms 两次一致），再宽限
                // HarvestGraceMs 让 lsp 收尾，即主动结束进程并按正常完成处理。
                // DefaultTimeoutMs 仅作为最终防线保留。
                int waitedMs = 0;
                long lastLen = -1;
                int stableMs = 0;
                bool harvested = false;
                while (!p.WaitForExit(200))
                {
                    waitedMs += 200;
                    if (isCancelled != null && isCancelled())
                    {
                        try { p.Kill(); } catch { }
                        throw new OperationCanceledException("已停止");
                    }
                    if (!harvested && File.Exists(outTxt))
                    {
                        long len = -1;
                        try { len = new FileInfo(outTxt).Length; } catch { }
                        if (len > 0 && len == lastLen)
                        {
                            stableMs += 200;
                            if (stableMs >= HarvestGraceMs)
                            {
                                try { p.Kill(); } catch { }
                                p.WaitForExit(3000);
                                harvested = true;   // 结果已写出，按完成处理（ExitCode 不再可取）
                            }
                        }
                        else
                        {
                            stableMs = 0;
                            lastLen = len;
                        }
                    }
                    if (!harvested && waitedMs >= DefaultTimeoutMs)
                    {
                        try { p.Kill(); } catch { }
                        throw new TimeoutException("识别会话超时(>/" + (DefaultTimeoutMs / 1000) + "s)");
                    }
                }
                if (!harvested && p.ExitCode != 0) throw new InvalidOperationException("accoreconsole 退出码=" + p.ExitCode);
            }
        }

        internal static string LspPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p = Path.Combine(baseDir, "extframe.lsp");
            return File.Exists(p) ? p : Path.Combine(Path.GetDirectoryName(typeof(FrameExtractor).Assembly.Location), "extframe.lsp");
        }

        /// <summary>公司版识别：动态任务池，复用 extframe_company.lsp（仅已知图框层求盒）。返回 path→FrameBox。</summary>
        public Dictionary<string, FrameBox> RunExtractCompany(List<string> files, string tempDir,
                                                               Action<string, bool> onFileDone = null, Func<bool> isCancelled = null)
        {
            var result = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
            if (files == null || files.Count == 0) return result;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string lspPath = Path.Combine(baseDir, "extframe_company.lsp");
            if (!File.Exists(lspPath))
                lspPath = Path.Combine(Path.GetDirectoryName(typeof(FrameExtractor).Assembly.Location), "extframe_company.lsp");
            int w = MaxWorkers > 0 ? Math.Max(1, Math.Min(MaxWorkers, files.Count))
                                   : SmartConcurrency.RecognitionWorkers(files.Count);
            var local = new Dictionary<string, FrameBox>[w];
            var threads = new List<Thread>(w);
            int[] next = new int[1];
            for (int i = 0; i < w; i++)
            {
                int wi = i;
                local[wi] = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
                var t = new Thread(() => ExtractPoolCompany(wi, next, files, tempDir, lspPath, local[wi], onFileDone, isCancelled));
                t.IsBackground = true;
                threads.Add(t);
            }
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();
            foreach (var d in local) foreach (var kv in d) result[kv.Key] = kv.Value;
            return result;
        }

        private void ExtractPoolCompany(int wi, int[] next, List<string> files, string tempDir, string lspPath,
                                        Dictionary<string, FrameBox> local, Action<string, bool> onFileDone, Func<bool> isCancelled)
        {
            while (true)
            {
                if (isCancelled != null && isCancelled()) break;
                int idx = Interlocked.Increment(ref next[0]) - 1;
                if (idx >= files.Count) break;
                string f = files[idx];
                FrameBox box = null;
                try
                {
                    string outTxt2 = Path.Combine(tempDir, "cext_" + wi + "_" + Guid.NewGuid().ToString("N") + ".out");
                    string callLine = "(extframe-company \"" + outTxt2.Replace("\\", "/") + "\")\n";
                    string isoDir = Path.Combine(tempDir, "iso_" + wi);
                    Directory.CreateDirectory(isoDir);
                    // v4.17：默认走 (load) 外部加载（快）；失败则本进程内回退内联重试一次
                    string scr;
                    bool usedLoad = WriteExtractScript(lspPath, tempDir, callLine, "cext_" + wi, !_useInlineLisp, out scr);
                    RunSession(f, scr, outTxt2, isoDir, isCancelled);
                    box = ParseResult(outTxt2, f);
                    if (box == null && usedLoad)
                    {
                        _useInlineLisp = true;
                        try { if (File.Exists(outTxt2)) File.Delete(outTxt2); } catch { }
                        string scr2;
                        WriteExtractScript(lspPath, tempDir, callLine, "cexti_" + wi, false, out scr2);
                        RunSession(f, scr2, outTxt2, isoDir, isCancelled);
                        box = ParseResult(outTxt2, f);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { box = null; }
                try { onFileDone?.Invoke(f, box != null); } catch { }
                if (box != null) local[f] = box;
            }
        }

        /// <summary>解析结果文件（v4.10 改为逐行）：
        /// 第一条非 BLOCKHINT 行 = 主结果 "minx miny maxx maxy|layer|src"；"-1|" 哨兵 = 主结果失败。
        /// 其余 "BLOCKHINT|块名|minx miny maxx maxy" 行收集进 FrameBox.BlockHint（多项"；"连接）。
        /// 主结果失败但存在提示 → 返回 Source="ERR" 的 FrameBox（仅携带提示，校验必拒）；
        /// 主结果失败且无提示 → 返回 null（走重试）。格式错/文件不存在返回 null。</summary>
        internal static FrameBox ParseResult(string outTxt, string dwgPath)
        {
            try
            {
                if (!File.Exists(outTxt)) return null;
                string[] lines = File.ReadAllLines(outTxt, Gb2312);
                FrameBox box = null;
                var hints = new List<string>();
                double[] extent = null;      // v4.12 修订：EXTENT 行（全图范围）
                bool extentOut = false;      // v4.12 修订：WARN|extent-out（图框外仍有内容）
                int imageCount = 0;          // v4.15：IMG|n（图片/嵌入对象/参考底图总数）
                int rasterCount = 0;         // v4.15：IMGR|a|b（真光栅 IMAGE 数 | 外部图缺失数）
                int rasterMissing = 0;
                int shadePlot = 0;           // v4.16：G76|n（Model 布局着色打印组码 76；缺失=0）
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("BLOCKHINT|", StringComparison.Ordinal))
                    {
                        string[] hp = line.Split('|');
                        if (hp.Length != 3) continue;
                        string[] hc = hp[2].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        double hx0, hy0, hx1, hy1;
                        if (hc.Length == 4 &&
                            double.TryParse(hc[0], NumberStyles.Float, CultureInfo.InvariantCulture, out hx0) &&
                            double.TryParse(hc[1], NumberStyles.Float, CultureInfo.InvariantCulture, out hy0) &&
                            double.TryParse(hc[2], NumberStyles.Float, CultureInfo.InvariantCulture, out hx1) &&
                            double.TryParse(hc[3], NumberStyles.Float, CultureInfo.InvariantCulture, out hy1))
                            hints.Add(hp[1] + " " + hc[0] + " " + hc[1] + " " + hc[2] + " " + hc[3]);
                        continue;
                    }
                    // v4.12 修订：全图范围 / 越界告警 / 修正前候选框（辅助行，含 0 或 1 个"|"，不参与主结果解析）
                    if (line.StartsWith("EXTENT|", StringComparison.Ordinal))
                    {
                        double[] eb;
                        if (TryParseBox(line.Substring("EXTENT|".Length), out eb)) extent = eb;
                        continue;
                    }
                    if (line.StartsWith("WARN|", StringComparison.Ordinal))
                    {
                        if (line.IndexOf("extent-out", StringComparison.OrdinalIgnoreCase) >= 0) extentOut = true;
                        continue;
                    }
                    if (line.StartsWith("CANDIDATE|", StringComparison.Ordinal)) continue;
                    // v4.15：光栅图片计数（辅助行）
                    if (line.StartsWith("IMG|", StringComparison.Ordinal))
                    {
                        int iv;
                        if (int.TryParse(line.Substring("IMG|".Length).Trim(), out iv) && iv >= 0) imageCount = iv;
                        continue;
                    }
                    // v4.15：真光栅 IMAGE 计数（IMGR|a|b）
                    if (line.StartsWith("IMGR|", StringComparison.Ordinal))
                    {
                        string[] rp = line.Split('|');
                        int av, bv;
                        if (rp.Length >= 3 && int.TryParse(rp[1].Trim(), out av) && av >= 0) rasterCount = av;
                        if (rp.Length >= 3 && int.TryParse(rp[2].Trim(), out bv) && bv >= 0) rasterMissing = bv;
                        continue;
                    }
                    // v4.16：Model 布局"着色打印"组码 76（辅助行）
                    if (line.StartsWith("G76|", StringComparison.Ordinal))
                    {
                        int gv;
                        if (int.TryParse(line.Substring("G76|".Length).Trim(), out gv) && gv >= 0) shadePlot = gv;
                        continue;
                    }
                    if (box != null) continue;
                    if (line.StartsWith("-1|", StringComparison.Ordinal)) continue;   // 失败哨兵，后面可能还有 BLOCKHINT
                    string[] parts = line.Split('|');
                    if (parts.Length != 3) continue;
                    string[] c = parts[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (c.Length != 4) continue;
                    double d0 = double.Parse(c[0], CultureInfo.InvariantCulture);
                    double d1 = double.Parse(c[1], CultureInfo.InvariantCulture);
                    double d2 = double.Parse(c[2], CultureInfo.InvariantCulture);
                    double d3 = double.Parse(c[3], CultureInfo.InvariantCulture);
                    box = new FrameBox
                    {
                        MinX = d0, MinY = d1, MaxX = d2, MaxY = d3,
                        Layer = parts[1], Source = parts[2],
                    };
                }
                if (box == null)
                {
                    if (hints.Count == 0) return null;
                    return new FrameBox { Source = "ERR", BlockHint = string.Join("；", hints), Extent = extent, ExtentOut = extentOut, ImageCount = imageCount, RasterCount = rasterCount, RasterMissingCount = rasterMissing, G76 = shadePlot };
                }
                if (hints.Count > 0) box.BlockHint = string.Join("；", hints);
                box.Extent = extent;
                box.ExtentOut = extentOut;
                box.ImageCount = imageCount;
                box.RasterCount = rasterCount;
                box.RasterMissingCount = rasterMissing;
                box.G76 = shadePlot;   // v4.16：Model 布局着色打印组码 76（≠0 → 出图改走 COM）
                return box;
            }
            catch { return null; }
        }

        /// <summary>v4.12 修订：解析 "x0 y0 x1 y1" 形式的四数值盒（EXTENT 等辅助行用）。</summary>
        internal static bool TryParseBox(string text, out double[] box)
        {
            box = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] c = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (c.Length != 4) return false;
            var v = new double[4];
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(c[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return false;
            box = v;
            return true;
        }
    }
}
