using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DwgToPdf.Batch
{
    /// <summary>经 accoreconsole 执行出图会话逐文件生成 PDF。应答序列依赖 Task 1 探针校准。</summary>
    internal sealed class PlotSession
    {
        // v4.11：出图超时由"固定 180s"改为"随图档大小自适应"——重图（大 dwg/复杂实体）
        // 单次 -EXPORT 常 >180s，旧值会误超时并回退更慢的 COM（实测 PISCES_7807757）。
        // 规则：base(180s) + 20s/MB，上限 600s；DefaultTimeoutMs 保留为基准值。
        public const int DefaultTimeoutMs = 180000;
        public const int PlotTimeoutPerMbMs = 20000;
        public const int PlotTimeoutMaxMs = 600000;

        /// <summary>v4.11：按图档大小给出该文件的出图超时（base + 20s/MB，上限 600s）。</summary>
        public static int TimeoutFor(string dwgPath)
        {
            long len = 0;
            try { var fi = new FileInfo(dwgPath); if (fi.Exists) len = fi.Length; } catch { }
            double mb = len / 1048576.0;
            long ms = DefaultTimeoutMs + (long)(PlotTimeoutPerMbMs * mb);
            if (ms > PlotTimeoutMaxMs) ms = PlotTimeoutMaxMs;
            return (int)ms;
        }

        // accoreconsole 按 ANSI 代码页解析 .scr；中文路径须 GB2312 写入（Task 1 探针结论）
        private static readonly Encoding Gb2312 = Encoding.GetEncoding("GB2312");

        private readonly string _accoreConsole;

        public PlotSession(string accoreConsole) { _accoreConsole = accoreConsole; }

        /// <summary>出图一张。成功返回 true；失败/超时/PDF 异常返回 false 且 reason 给出原因。
        /// isCancelled：会话等待期间检查，已取消则终止当前 accoreconsole 并抛出 OperationCanceledException。</summary>
        public bool Plot(string dwg, string pdfPath, FrameBox frame, string plotter, string media,
                         bool landscape, string ctb, double marginMm, string tempDir, out string reason,
                         Func<bool> isCancelled = null, PageSpec.Resolved psForSetup = null,
                         bool plotLineweights = false)
        {
            reason = "";
            try
            {
                if (frame == null) { reason = "无图框数据"; return false; }
                string scrText = BuildScript(dwg, pdfPath, frame, plotter, media, landscape, ctb, marginMm, tempDir, psForSetup, plotLineweights);
                // v4.11：外部清理可能在会话中途删掉临时目录（手册 8.7），写脚本前先补建，避免整批回退 COM
                try { Directory.CreateDirectory(tempDir); } catch { }
                string scrPath = Path.Combine(tempDir, "plot_" + Guid.NewGuid().ToString("N") + ".scr");
                File.WriteAllText(scrPath, scrText, Gb2312);
                string workDir = Path.Combine(tempDir, "plotwork_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);
                RunSession(dwg, scrPath, workDir, isCancelled);
                // -EXPORT 默认文件名输出到工作目录，名为 <图纸名>-Model.pdf；扫描最新 PDF 移动
                string[] got = Directory.GetFiles(workDir, "*.pdf", SearchOption.AllDirectories);
                if (got.Length > 0)
                {
                    var latest = got.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                    File.Move(latest, pdfPath);
                }
                if (!File.Exists(pdfPath)) { reason = "未生成 PDF（工作目录=" + workDir + "）"; return false; }
                // v3.3 整改②：空白 PDF 立即删除，避免残留在输出目录（COM 兜底会重新生成）
                if (new FileInfo(pdfPath).Length < 5000)
                {
                    try { File.Delete(pdfPath); } catch { }
                    reason = "PDF 内容为空";
                    return false;
                }
                return true;
            }
            catch (OperationCanceledException) { throw; }   // 用户停止：上抛由调用方按"取消"处理，不回退 COM
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>生成出图应答脚本（-EXPORT PDF 方案，accoreconsole 实测可用）。
        /// v3.8 方案 G：窗口=图框外扩 M（每边 marginMm，即 frame+2M），与预处理副本的
        /// SetWindowToPlot 一致（PageSetupPreprocessor 同步外扩）。
        /// 根因链（2026-08-31 _调查/PageSetupLab 控制变量实验 + 11 张 H56E 图框显示不完全）：
        ///   - 精确边距必须由预处理副本的 SetCustomScale(分子,分母) 固定比例实现（+CenterPlot 居中）；
        ///   - 若窗口=图框本身，识别 frame 常小于真实图框外框（识别容差），真实图框超出窗口被裁切
        ///     （11 张 H56E"图框显示不完全"bug，v3.8 初版）；
        ///   - 窗口=frame+2M 时，PageSpec.s 分母同步为窗口尺寸 → (frame+2M)×s ≤ 纸-2M，
        ///     短边恰好留 M 边距、长边对称留更大边距，真实图框完整显示；
        ///   - 无边距：M=0，窗口=图框，ScaleToFit 贴边（预期行为）。
        /// 其余实测结论（Task 1 + 冒烟，保留）：
        ///   - -PLOT 在 accoreconsole 下应答序列完整但不产出 PDF；且不更新 Model 布局页面设置（-EXPORT 不继承）。
        ///   - accoreconsole 不支持 ActiveX（vlax-get-acad-object 返回 nil），无法用 LISP 脚本化页面设置。
        ///   - 纸张/方向/样式由预处理副本的 Model 页面设置继承（PageSetupPreprocessor），-EXPORT 输出与 v2.3.1 一致。
        /// plotter/media/ctb/方向以 ';' 注释行记录于脚本头部（AutoCAD 脚本注释，供审计与测试断言）。</summary>
        internal static string BuildScript(string dwg, string pdfPath, FrameBox frame, string plotter, string media,
                                                  bool landscape, string ctb, double marginMm, string tempDir,
                                                  PageSpec.Resolved psForSetup = null, bool plotLineweights = false)
                {
            // 窗口=图框外扩 M（方案 G：补偿识别偏差，保证真实图框完整；M=0 时=图框本身）
            double m = marginMm;
            double llx = frame.MinX - m, lly = frame.MinY - m;
            double urx = frame.MaxX + m, ury = frame.MaxY + m;

            string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
            string ori = landscape ? "L" : "P";
            var lines = new List<string>
            {
                "FILEDIA", "0",
                "BACKGROUNDPLOT", "0",
                ";plotter=" + plotter,
                ";media=" + media,
                ";ctb=" + ctb,
                ";orientation=" + ori,
            };
            // v4.12：页面设置在会话内完成（entmod Model 布局）——无副本、无 COM 阶段2
            if (psForSetup != null)
            {
                lines.Add(";v412=页面设置内联(entmod)，跳过阶段2 COM 预处理");
                lines.AddRange(BuildPageSetupScript(psForSetup, frame, marginMm, plotter, media, landscape, ctb, plotLineweights));
            }
            lines.Add("-EXPORT");
            lines.Add("PDF");
            lines.Add("W");                        // 打印区域：窗口（与 COM 窗口语义一致）
            lines.Add(Fmt(llx) + "," + Fmt(lly));  // 窗口左下角
            lines.Add(Fmt(urx) + "," + Fmt(ury));  // 窗口右上角
            lines.Add("");                         // 是否保存后打印图纸空间的更改：空行=默认 N（不落盘、不修改源图）
            lines.Add("");                         // 输入输出文件名：空行=默认 <图纸名>-Model.pdf（规避中文路径截断，输出到工作目录后移动）
            // v4.12 修订（重要修复）：会话内 entmod 会让图纸处于"已修改"状态，而 accoreconsole
            // 在 QUIT 时提问的是**反向**问题：
            //     "是否确实要放弃对图形所做的全部修改？[是(Y)/否(N)] <否>"
            // 即 **答 N（或直接回车取默认）＝ 不放弃修改 ＝ 保存写回源图** ✗✗
            // 实测后果：源 dwg 被改写（哈希变化、体积增大约 14 万字节）并生成同名 .bak；
            // 且"只打开不做任何修改"的图纸也会被判为已修改（遇到自定义对象 + 重生成）→
            // 每次转换都会重写一遍源文件。
            // 正确应答是 Y（放弃修改）；(setvar "DBMOD" 0) 在 AutoCAD 2026 被拒绝执行，不可用。
            lines.Add("QUIT");
            lines.Add("Y");                        // 放弃修改：绝不让工具写回源图纸
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>v4.12：生成"会话内页面设置"脚本行（entmod Model 布局的 AcDbPlotSettings 字段）。
        /// 字段映射经基准 dump 探针逐项实测，与 COM 预处理（PageSetupPreprocessor → Layout 属性）
        /// 在 PDF 层面逐字节等价（附录 24 章；差异仅 XMP 时间戳）：
        ///   2=打印机  4=纸型名  7=打印样式表(ctb)
        ///   40~43=0（全出血纸无边距）        44/45=纸短边/长边（取纸型名实际尺寸）
        ///   46/47=居中原点 ((纸边 - 窗口边×s) / 2)   48/49/140/141=窗口(frame∓M)
        ///   70=原值清 0x10(UseStandardScale) + 置 0x04(CenterPlot)   72=旋转
        ///   73/74=1/4   142/143=s/1（自定义比例，s 必须用 PageSpec.Scale 精确值）
        ///   147 保持原值（实测不影响输出）；148/149=-窗口左下角
        /// 每行输出一条**完整** LISP 表达式：脚本单行有 255 字符上限，且便于失败时定位到具体字段。</summary>
        private static IEnumerable<string> BuildPageSetupScript(PageSpec.Resolved ps, FrameBox frame, double marginMm,
                                                               string plotter, string media, bool landscape, string ctb,
                                                               bool plotLineweights)
        {
            double m = marginMm;
            double llx = frame.MinX - m, lly = frame.MinY - m;
            double urx = frame.MaxX + m, ury = frame.MaxY + m;
            double winW = frame.MaxX - frame.MinX + 2.0 * m;
            double winH = frame.MaxY - frame.MinY + 2.0 * m;
            // 与纸张方向对齐：landscape 时旋转 90°，窗口长边沿纸长边
            double winLong = landscape ? winW : winH;
            double winShort = landscape ? winH : winW;
            // 44/45 取纸型名的真实纸宽高（PaperShort 是 round(长边/√2) 近似值，4A0 会差 1mm）
            double paperW, paperH;
            if (!PageSpec.TryMediaDims(media, out paperW, out paperH))
            {
                paperW = ps.PaperShort;
                paperH = ps.PaperLong;
            }
            double s = ps.Scale;
            double orgX = (paperW - winShort * s) / 2.0;
            double orgY = (paperH - winLong * s) / 2.0;

            Func<double, string> N = v => v.ToString("0.############", CultureInfo.InvariantCulture);
            Func<int, string, string> Set = (code, val) =>
                "(setq le (subst (cons " + code + " " + val + ") (assoc " + code + " le) le))(princ)";

            yield return "(setq d (dictsearch (namedobjdict) \"ACAD_LAYOUT\") lay (cdr (assoc 350 (member (cons 3 \"Model\") d))) le (entget lay))(princ)";
            yield return Set(2, "\"" + plotter + "\"");
            yield return Set(4, "\"" + media + "\"");
            yield return Set(7, "\"" + ctb + "\"");
            // v4.16（2026-10-07，用户实测"打印发浅"真根因）：布局记录的**组码 76 = 着色打印**
            // （位于 AcDbPlotSettings 段）。PISCES 这批图纸里它是 4（着色/渲染），
            // AutoCAD 的 PDF 驱动会把**整页光栅化**（实测：76=0/1 → 矢量内容流 1.9MB、
            // 只有 16 个 30x29px 的 OLE 小图；76=3/4 → 矢量内容流 4KB、整页 301dpi 位图），
            // 线条被烘进位图、线宽层次被抹平成单一 0.32mm —— 这才是"发浅"的成因。
            // 写 0（按显示），与用户手动打印（PLOT 对话框：着色打印=按显示）的参照一致。
            // ⚠ 不要再把组码 146 当成着色打印：146 在 **AcDbLayout** 段，是**标高**，
            //   手册第27/28 章早前把它标注为"146 着色打印"是错的（第29 章已更正）。
            // ⚠ 不能用上面的 Set() 写 76：它在 (assoc 76 le) 为 nil 时 **不报错但也不写入**
            //   （subst 找不到旧值就原样返回），而 PISCES 里7633639 / 7643421 的布局记录
            //   恰好没有 76 —— 实测这两张设了 76=0 仍是整页光栅（62 / 31 图块）。
            //   因此 76 单独用"有则替换、无则追加"的写法。
            // 只改内存中的副本，Close(0)/QUIT N 不保存 ⇒ 源图不受影响。
            yield return "(if (assoc 76 le) (setq le (subst (cons 76 0) (assoc 76 le) le)) (setq le (append le (list (cons 76 0)))))(princ)";
            yield return Set(40, "0.0");
            yield return Set(41, "0.0");
            yield return Set(42, "0.0");
            yield return Set(43, "0.0");
            yield return Set(44, N(paperW));
            yield return Set(45, N(paperH));
            yield return Set(46, N(orgX));
            yield return Set(47, N(orgY));
            yield return Set(48, N(llx));
            yield return Set(49, N(lly));
            yield return Set(140, N(urx));
            yield return Set(141, N(ury));
            // 70 = 打印布局标志位：-17 清 0x10(UseStandardScale) + 置 0x04(CenterPlot)；
            // v4.16 新增 0x80(128)="打印线宽" —— 实测该位由 COM Layout.PlotWithLineweights
            // 置 true 时 11828→11956（+0x80）确定，LWDISPLAY 仍为 0，说明出图引擎读的是这个位。
            // 注意：-EXPORT 通道实测**不认**该位（2026-10-07，同一图纸 LWDISPLAY/0x80/146
            // 四种组合的 PDF 内容流 md5 完全相同），此处仍按正确意图写入：语义正确，且
            // 一旦平台认它即刻生效。真正生效的"变深"手段是选纸（见 PageSpec.PaperFitTolerance）。
            int flags = 4 | (plotLineweights ? 128 : 0);
            yield return Set(70, string.Format(CultureInfo.InvariantCulture,
                "(logior (logand (cdr (assoc 70 le)) -17) {0})", flags));
            yield return Set(72, ps.Rotation.ToString(CultureInfo.InvariantCulture));
            yield return Set(73, "1");
            yield return Set(74, "4");
            yield return Set(142, N(s));
            yield return Set(143, "1.0");
            yield return Set(148, N(-llx));
            yield return Set(149, N(-lly));
            // v4.15 修订记录（2026-10-06，用户发现+三轮工具实验）：页面设置"着色打印=按显示"
            // （DXF 146=0）在 AutoCAD GUI 打印可解决线条发浅（用户实测）；但 accoreconsole
            // 无头环境下无论 146=0 配任何颜色方案（原色 / 改黑 / ACI 250 深灰），-EXPORT 输出
            // 均为**整页白**（2.9MB 内容全在但渲染不可见）——该渲染管线在无头环境不可用，
            // 属 accoreconsole 平台限制，非工具可修。同日亦验证：-EXPORT 通道内改图层线宽
            // （组码 370）同样全白。=> 发浅类图纸的出路 = COM 打印通道 / 源头治本 / 打印机调浓。
            // v4.15 修订记录（2026-10-06 用户发现 + 工具实验）：页面设置"着色打印=按显示"
            // （DXF 146=0）可解决打印发浅——**但仅在 AutoCAD GUI / COM（有显示上下文）有效**；
            // 在 accoreconsole（无头）里 entmod 146=0 后 -EXPORT 输出**整页白**（内容 2.9MB 全部
            // 渲染为白——7 号线按屏幕反色成白色，无头环境无背景替换处理），不可用，已回滚。
            // 结论："按显示"效果 = GUI 打印 / COM 通道专属；工具 accore 通道维持现状。
            yield return "(entmod le)(princ)";
        }

        private void RunSession(string dwg, string scr, string workDir, Func<bool> isCancelled)
        {
            int timeoutMs = TimeoutFor(dwg);   // v4.11：按图档大小自适应
            string logPath = Path.Combine(workDir, "session.log");
            // v3.8.1：去掉 /isolate。accoreconsole 2021 的 /isolate 报 "Copying folder failed
            // errno=3 / Failed to create registry keys"，导致退出码=1 全部回退 COM。
            // 批量出图为只读操作（打开 DWG→-EXPORT→不保存），共享默认配置无冲突。
            var psi = new ProcessStartInfo(_accoreConsole,
                " /i \"" + dwg + "\" /s \"" + scr + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workDir,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("启动 accoreconsole 失败");
                ComStarter.RegisterAccore(p.Id);   // v4.11：登记，收尾收割孤儿
                using (var log = new StreamWriter(logPath, false, Gb2312))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) { try { log.WriteLine(e.Data); } catch { } } };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) { try { log.WriteLine("[ERR] " + e.Data); } catch { } } };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    int waitedMs = 0;
                    while (!p.WaitForExit(200))
                    {
                        waitedMs += 200;
                        if (isCancelled != null && isCancelled())
                        {
                            try { p.Kill(); } catch { }
                            throw new OperationCanceledException("已停止");
                        }
                        if (waitedMs >= timeoutMs)
                        {
                            try { p.Kill(); } catch { }
                            throw new TimeoutException("出图会话超时(>/" + (timeoutMs / 1000) + "s)，日志=" + logPath);
                        }
                    }
                }
                if (p.ExitCode != 0) throw new InvalidOperationException("accoreconsole 退出码=" + p.ExitCode + "，日志=" + logPath);
            }
        }
    }
}
