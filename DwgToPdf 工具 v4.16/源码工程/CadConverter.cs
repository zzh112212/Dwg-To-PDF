using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DwgToPdf.Batch;

/// <summary>
/// 与 AutoCAD COM 交互并进行批量出图的转换器。
/// 核心改进：图框所在图层不再依赖硬编码名称，而是自动识别。
/// </summary>
internal sealed partial class CadConverter
{
    // 向后兼容：若图纸恰好存在名为"图层图框"的图层，仍优先使用它（与旧工具行为一致）。
    private const string LegacyFrameLayerName = "图层图框";

    internal const string PlotterName = "DWG To PDF.pc3";
    private const string DefaultStyleSheetName = "monochrome.ctb";

    // v4.16：COM 出图前用 LISP entmod 把 Model 布局的"着色打印"(AcDbPlotSettings 组码 76) 置 0（按显示）。
    // 目的：PISCES 这批 76=4(渲染) 的图纸会被 PDF 驱动整页光栅化（线条被烘进位图 → 视觉"发浅"）。
    // Layout 对象没有 76 的 COM 属性，只能走 LISP；而 SendCommand 对约 300 字符长行会静默截断，
    // 故把 LISP 落盘再 (load)。目录必须纯 ASCII（LISP 的 (load)/(open) 对非 ASCII 路径不稳）。
    private const string ComLispDir = @"C:\Temp\dwgtopdf_com";

    /// <summary>v4.16：写入布局"着色打印=按显示"(组码 76=0) 的 LISP（落盘后 (load)，见 TrySetShadePlotToDisplay）。
    /// 用"有则替换、无则追加"——有的图纸布局记录里没有 76，subst 找不到旧值会原样返回（不报错也不写入），
    /// 必须用 append 追加。与 PlotSession.BuildPageSetupScript 的 76 写法一致。</summary>
    private const string ShadePlotLisp =
        "(defun c:dwg2pdfshade0 ( / lay le f )\n" +
        "  (setq lay (cdr (assoc 350 (member (cons 3 \"Model\") (dictsearch (namedobjdict) \"ACAD_LAYOUT\")))))\n" +
        "  (setq le (entget lay))\n" +
        "  (if (assoc 76 le)\n" +
        "      (setq le (subst (cons 76 0) (assoc 76 le) le))\n" +
        "      (setq le (append le (list (cons 76 0)))))\n" +
        "  (entmod le)\n" +
        "  (entupd lay)\n" +
        "  (setq f (open \"<MARKER_PATH>\" \"w\"))\n" +
        "  (write-line (itoa (cdr (assoc 76 (entget lay)))) f)\n" +
        "  (close f)\n" +
        "  (princ)\n" +
        ")\n";

    private const int AcWindow = 4;
    private const int AcScaleToFit = 0;

    private readonly object _app;
    private readonly Late _appLate;
    private readonly object _docs;
    private readonly bool _ownsInstance;
    // v3.4：本实例对应的工具自启 acad 进程 PID（用于停止时强制结束 / 正常关闭后注销）
    private HashSet<int> _ownedAcadPids = new HashSet<int>();

    /// <summary>该实例是否由本工具启动（true=转换结束后应 Shutdown）；附加的用户实例=false。</summary>
    public bool OwnsInstance => _ownsInstance;

    public CadConverter()
        : this(null, attachToRunning: false)
    {
    }

    public static CadConverter Attach(object runningApp) => new CadConverter(runningApp, attachToRunning: true);

    private CadConverter(object runningApp, bool attachToRunning)
    {
        _ownsInstance = !attachToRunning;
        if (attachToRunning) _app = runningApp;
        else
        {
            var before = ComStarter.AcadProcessIds();
            _app = ComStarter.StartNewInstance();
            // v3.4：登记工具自启实例 PID（用户点"停止"时可强制结束；正常 Shutdown 后注销）
            _ownedAcadPids = ComStarter.AcquireAndRegisterNewAcadPids(before, 4000);
            if (_ownedAcadPids.Count == 0) _ownsInstance = false;
        }

        _appLate = new Late(_app);
        _docs = _appLate.Get("Documents");

        if (_ownsInstance)
        {
            // v2.3.1：仅对本工具自启的新实例设置后台运行（不弹 AutoCAD 窗口）；
            // 附加/复用了用户已开的实例时绝不隐藏窗口，避免打扰用户并防止误关。
            try { _appLate.Set("Visible", false); }
            catch { }
        }
    }

    private static void ReleaseCom(params object[] objs)
    {
        foreach (var o in objs)
        {
            try { if (o != null) Marshal.ReleaseComObject(o); }
            catch { }
        }
    }

    /// <summary>v4.15：可传入"已识别的图框范围"（识别缓存里的 minx/miny/maxx/maxy）。
    /// 传入时跳过 TryGetFrameExtents 的**逐实体 COM 遍历**——实测 10 万实体图纸该遍历耗时
    /// 约 390 秒（含图片图纸走 COM 高清出图的瓶颈即此），而图框范围识别阶段已有。</summary>
    public CadResult Convert(string dwgPath, string pdfPath, Action<string> progress, bool fitTight, PrintOptions print, string plotter,
                             double[] knownLl = null, double[] knownUr = null)
        => ConvertImpl(dwgPath, pdfPath, progress, fitTight, print, plotter, knownLl, knownUr);

    private CadResult ConvertImpl(string dwgPath, string pdfPath, Action<string> progress, bool fitTight, PrintOptions print, string plotter,
                                  double[] knownLl, double[] knownUr)
    {
        print ??= new PrintOptions();
        var result = new CadResult();
        // v4.1：抑制对话框，防止 Quit 时弹"是否保存 Drawing1.dwg"
        try { _appLate.Set("DisplayAlerts", false); } catch { }
        try { _appLate.Set("DisplayFileAlerts", false); } catch { }
        var docs = new Late(_docs);
        object doc = null;
        try
        {
            var comSw = System.Diagnostics.Stopwatch.StartNew();   // v4.15：COM 出图分段计时
            doc = docs.Call("Open", dwgPath, false);
            Late d = new Late(doc);
            progress?.Invoke("[计时] COM 打开图纸: " + comSw.Elapsed.TotalSeconds.ToString("0.0") + "s");

            double[] ll, ur;
            string err, source;
            if (knownLl != null && knownUr != null)
            {
                ll = knownLl; ur = knownUr; err = ""; source = "识别缓存";
                progress?.Invoke("[计时] COM 跳过图框遍历（用识别缓存 " + ll[0].ToString("0.#") + "," + ll[1].ToString("0.#")
                    + " → " + ur[0].ToString("0.#") + "," + ur[1].ToString("0.#") + "）");
            }
            else if (!TryGetFrameExtents(d, out ll, out ur, out err, out source))
            {
                result.Ok = false;
                result.Message = err;
                return result;
            }

            result.FrameWidth = ur[0] - ll[0];
            result.FrameHeight = ur[1] - ll[1];

            var layouts = new Late(d.Get("Layouts"));
            var model = new Late(layouts.GetAt("Item", "Model"));
            d.Set("ActiveLayout", model.Raw);

            model.Set("ConfigName", string.IsNullOrWhiteSpace(plotter) ? PlotterName : plotter);
            string styleSheet = string.IsNullOrWhiteSpace(print.StyleSheetName) ? DefaultStyleSheetName : print.StyleSheetName;
            try { model.Set("StyleSheet", styleSheet); }
            catch (Exception ex)
            {
                progress?.Invoke("提示：设置打印样式表 '" + styleSheet + "' 失败（" + ex.Message + "），可能仍按当前样式输出");
            }

            double M = fitTight ? 0.0 : (print.EdgeMarginMm ?? 0.0);
            var ps = DwgToPdf.Batch.PageSpec.Resolve(result.FrameWidth, result.FrameHeight, M);
            string media = ps.Media;
            int rotation = ps.Rotation;
            try { model.Set("CanonicalMediaName", media); }
            catch (Exception ex2)
            {
                progress?.Invoke("提示：设置图纸尺寸 '" + media + "' 失败（" + ex2.Message + "），改用当前默认图纸");
            }
            // v4.16 修复（2026-10-07 实测）：Layout 对象**没有** PlotLineweights 属性，正确名字是
            //   PlotWithLineweights（另有 ScaleLineweights = LWDSCALE 缩放开关）。
            //   旧代码 model.Set("PlotLineweights", ...) 自 v4.1 起每次都抛异常并被下面的
            //   catch 吞掉 ⇒ 界面「显示线宽」设置从来没有生效过（与 PISCES "打印发浅" 排查
            //   过程中发现的根因之一相关）。
            //   实测依据（Get-Member 枚举 Layout 属性）：PlotWithLineweights / ScaleLineweights
            //   / PlotWithPlotStyles / PlotHidden / PlotViewportBorders / PlotViewportsFirst。
            // DXF 组码对应关系（实测：COM 把 PlotWithLineweights 置 true 后 Model 布局
            //   组码 70 由 11828 → 11956，即 +0x80），accore 通道据此设置。
            try { model.Set("PlotWithLineweights", print.ShowLineWeight); }
            catch (Exception exLw) { progress?.Invoke("提示：设置线宽显示失败（" + exLw.Message + "），继续"); }

            bool plotExact = false;
            if (!fitTight && M > 0.0)
            {
                try
                {
                    double s = ps.Scale;
                    if (s > 0.0)
                    {
                        // v2.3.1：重排 COM 调用顺序——SetWindowToPlot、UseStandardScale 必须先于
                        // PlotType=AcWindow 设置，否则 PISCES 等图纸会抛"输入无效"导致转换失败
                        // （见诊断工程_v2.3.1 阶段C日志，Mode F 顺序验证通过）。
                        // v3.8 修复（2026-08-31 控制变量实验结论）：
                        //   1) CustomScale 属性名不存在（Layout 对象无此成员）→ 正确 API 是
                        //      SetCustomScale(分子, 分母) 方法；旧代码 Set("CustomScale", s) 一直
                        //      抛 DISP_E_UNKNOWNNAME → 精确模式从未生效、静默回退 ScaleToFit 贴边。
                        //   2) 显式设置 PlotOrigin=(0,0) 会禁用 CenterPlot → 内容贴左下角；
                        //      必须不设 PlotOrigin，CenterPlot 才生效（B4/COM3 验证）。
                        //   3) v3.8 方案 G：窗口=图框外扩 M（frame±M），与主通道预处理一致。
                        //      识别 frame 常小于真实图框外框，窗口=frame 会裁掉真实图框
                        //      （11 张 H56E 图框显示不完全 bug）；PageSpec.s 分母已同步为窗口尺寸。
                        model.Set("PlotRotation", rotation);
                        model.Call("SetWindowToPlot", new double[2] { ll[0] - M, ll[1] - M }, new double[2] { ur[0] + M, ur[1] + M });
                        model.Set("UseStandardScale", false);
                        model.Set("PlotType", AcWindow);
                        model.Call("SetCustomScale", s, 1.0);
                        model.Set("CenterPlot", true);
                        plotExact = true;
                    }
                }
                catch (Exception exExact)
                {
                    progress?.Invoke("提示：精确边距(自定义比例)设置失败，自动回退为近似边距（" + exExact.Message + "）");
                }
            }

            if (!plotExact)
            {
                // 近似模式（含贴边/0 边距）：窗口外扩 M，适应纸张并居中。
                // v2.3.1：同样将 SetWindowToPlot / UseStandardScale 提前到 PlotType 之前，避免"输入无效"。
                model.Set("PlotRotation", rotation);
                double[] wll = { ll[0] - M, ll[1] - M };
                double[] wur = { ur[0] + M, ur[1] + M };
                model.Call("SetWindowToPlot", wll, wur);
                model.Set("UseStandardScale", true);
                model.Set("PlotType", AcWindow);
                model.Set("StandardScale", AcScaleToFit);
                model.Set("CenterPlot", true);
            }

            // v4.10.1 修复（后台打印导致 COM 兜底"假失败"）：
            // 源图若开启了后台打印（BACKGROUNDPLOT=2），PlotToFile 会把任务提交给后台打印
            // 队列后立即返回，导致此处检查时 PDF 尚未写盘 → 误判"未检测到 PDF 文件"，
            // 且关闭文档时 AutoCAD 弹"取消后台打印"模态对话框（极端情况阻塞收尾）。
            // 实测案例：PISCES_7807757_LD002APIA2（BACKGROUNDPLOT=2，PDF 稍后由后台打印写出）。
            // 对策①：出图前把当前文档 BACKGROUNDPLOT 强制为 0，改为前台同步出图。
            try { d.Call("SetVariable", "BACKGROUNDPLOT", 0); } catch { }
            // v4.15：抑制"出图后用系统默认程序打开产出文件"——实测（2026-09-17，受控复现）：
            // COM 打印完成的同一秒会用默认程序打开 PDF（本机默认=Edge，一次拉起 ~18 个进程/0.6GB，
            // 抢焦点且拖慢批量）。PLQUIET=1（静默出图）+ FILEOPEN? 均不影响出图结果，仅抑制弹窗/打开。
            try { d.Call("SetVariable", "PLQUIET", 1); } catch { }

            // v4.16（2026-10-07，"打印发浅"排查 + 用户验收"深浅可以"的组合之一）：
            //   COM 出图前把 Model 布局的"着色打印"(组码 76) 置 0（按显示）。
            //   缺 76 的图纸 subst 不报错也不写入，故 LISP 用"有则替换、无则追加"。
            //   兜底通道：任何失败只提示、绝不阻断出图。
            TrySetShadePlotToDisplay(d, progress);

            progress?.Invoke("[计时] COM 页面设置: " + comSw.Elapsed.TotalSeconds.ToString("0.0") + "s");
            var plotSw = System.Diagnostics.Stopwatch.StartNew();
            new Late(d.Get("Plot")).Call("PlotToFile", pdfPath);
            progress?.Invoke("[计时] COM 出图调用返回: " + plotSw.Elapsed.TotalSeconds.ToString("0.0") + "s");

            // 对策②：写盘兜底轮询——等待 PDF 出现且大小稳定（最多 30 秒），
            // 避免把"写盘迟到/边写边查"误判为失败或空白（BACKGROUNDPLOT 设置失败时的双保险）。
            bool exists;
            long pdfLen = 0;
            int waitedMs = 0;
            long lastLen = -1;
            while (true)
            {
                exists = File.Exists(pdfPath);
                long len = 0;
                if (exists)
                {
                    try { len = new FileInfo(pdfPath).Length; } catch { len = 0; }
                }
                // 文件已出现且两次采样大小一致 → 视为写完
                if (exists && len == lastLen) { pdfLen = len; break; }
                lastLen = len;
                if (waitedMs >= 30000) { pdfLen = exists ? len : 0; break; }
                System.Threading.Thread.Sleep(500);
                waitedMs += 500;
            }

            progress?.Invoke("[计时] COM 等待 PDF 落盘: " + comSw.Elapsed.TotalSeconds.ToString("0.0") + "s（本张）");
            // 出图后校验：若生成的 PDF 极小（通常为空白页），视为空白，避免静默产出空文件。
            bool isEmpty = exists && pdfLen < 3000;
            if (isEmpty)
            {
                // v3.3 整改②：空白 PDF 立即删除，避免残留在输出目录误导使用者
                // 诊断开关（默认关闭）：DWGTOPDF_KEEP_BLANK=1 → 保留空白 PDF 供排查
                if (Environment.GetEnvironmentVariable("DWGTOPDF_KEEP_BLANK") == "1")
                    progress?.Invoke("[诊断] 空白 PDF 保留（" + pdfLen + " bytes）: " + pdfPath);
                else
                    try { File.Delete(pdfPath); } catch { }
                result.Ok = false;
                result.Message = "已生成 PDF 但内容为空白（该图纸可能无法按当前方式出图，请人工检查）";
                return result;
            }
            result.Ok = exists;
            result.Message = result.Ok ? "成功" : "命令执行完毕但未检测到 PDF 文件";
            return result;
        }
        catch (TargetInvocationException ex3)
        {
            result.Ok = false;
            result.Message = ex3.InnerException != null ? ex3.InnerException.Message : ex3.Message;
            return result;
        }
        catch (Exception ex4)
        {
            result.Ok = false;
            result.Message = ex4.Message;
            return result;
        }
        finally
        {
            // v4.11：Close 带硬超时（RPC 死锁时不再无限等待；超时后放弃，进程由收尾兜底终结）
            if (doc != null) new Late(doc).CallWithTimeout(60000, "Close", false);
            ReleaseCom(doc);
        }
    }

    /// <summary>v4.16：COM 出图前把当前文档 Model 布局的"着色打印"(AcDbPlotSettings 组码 76) 置 0（按显示），
    /// 防止 76=4(渲染) 的图纸被 PDF 驱动整页光栅化（线条被烘进位图 → 打印"发浅"）。
    /// 实现：LISP entmod（Layout 无对应 COM 属性）→ LISP 落到纯 ASCII 目录再 (load)，
    /// 用标记文件与异步的 SendCommand 握手确认（不能靠 sleep 猜）。
    /// **任何一步失败都只提示一次并返回 false，绝不阻断后续 PlotToFile**（本通道是兜底，宁可不写 76 也不能不出图）。
    /// 同一文档重复调用幂等（每次先删旧标记文件）。</summary>
    private static bool TrySetShadePlotToDisplay(Late d, Action<string> progress)
    {
        string fail = null;
        try
        {
            Directory.CreateDirectory(ComLispDir);
            string lspPath = Path.Combine(ComLispDir, "dwg2pdf_shade0.lsp");
            string markerPath = Path.Combine(ComLispDir, "shade_marker.txt");
            string lspFwd = lspPath.Replace('\\', '/');
            string markerFwd = markerPath.Replace('\\', '/');

            try { if (File.Exists(markerPath)) File.Delete(markerPath); } catch { }   // 幂等：先清旧标记
            // 全 ASCII，用 ASCII 编码写出（不加 BOM）
            File.WriteAllText(lspPath, ShadePlotLisp.Replace("<MARKER_PATH>", markerFwd), Encoding.ASCII);

            // SendCommand 是异步的（命令排队后立即返回），必须靠标记文件握手。
            d.Call("SendCommand", "(load \"" + lspFwd + "\") ");
            d.Call("SendCommand", "(c:dwg2pdfshade0) ");

            int waited = 0;
            while (waited < 10000)   // 最多约 10 秒
            {
                if (File.Exists(markerPath))
                {
                    string val = null;
                    try { val = File.ReadAllText(markerPath).Trim(); } catch { }
                    try { File.Delete(markerPath); } catch { }
                    if (val == "0") return true;
                    fail = "布局着色打印读回值=" + (val ?? "(空)") + "（期望 0）";
                    break;
                }
                System.Threading.Thread.Sleep(100);
                waited += 100;
            }
            if (fail == null) fail = "等待 LISP 执行超时(10s)";
        }
        catch (Exception ex) { fail = ex.Message; }

        progress?.Invoke("提示：未能把布局着色打印改为\"按显示\"(76=0)（" + fail + "），继续出图");
        return false;
    }

    public void Shutdown()
    {
        // v2.3.1：只允许关闭本工具自启的实例；附加/复用的用户实例绝不 Quit，防止关闭用户正在
        // 使用的 AutoCAD、弹出退出提示（WorkerLoop 已按 OwnsInstance 判断，这里再保险一次）。
        if (!_ownsInstance) return;

        try
        {
            var docs = new Late(_docs);
            while (true)
            {
                int num;
                try { num = System.Convert.ToInt32(docs.Get("Count")); }
                catch { num = 0; }
                if (num <= 0) break;

                object d;
                try { d = docs.GetAt("Item", 0); }
                catch { break; }

                if (!new Late(d).CallWithTimeout(30000, "Close", false)) break; // v4.11：硬超时
            }
        }
        catch { }

        _appLate.CallWithTimeout(30000, "Quit"); // v4.11：硬超时，防 Quit 挂死
        try { Marshal.ReleaseComObject(_app); }
        catch { }
        // v3.4：实例已正常退出，从工具自启登记表中注销
        foreach (var pid in _ownedAcadPids) ComStarter.UnregisterOwnedPid(pid);
        _ownedAcadPids.Clear();
    }

    /// <summary>单个实体的包围盒：多段线直接读顶点坐标（最快），其余用 GetBoundingBox。</summary>
    internal static bool TryGetEntityExtents(Late ent, out double[] ll, out double[] ur)
    {
        string objectName = "";
        try { objectName = System.Convert.ToString(ent.Get("ObjectName")); }
        catch { }

        if (objectName.Equals("AcDbPolyline", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (ent.Get("Coordinates") is Array coords && coords.Length >= 4 && (coords.Length & 1) == 0)
                {
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    for (int k = 0; k + 1 < coords.Length; k += 2)
                    {
                        double x = System.Convert.ToDouble(coords.GetValue(k), CultureInfo.InvariantCulture);
                        double y = System.Convert.ToDouble(coords.GetValue(k + 1), CultureInfo.InvariantCulture);
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                    if (minX <= maxX && minY <= maxY)
                    {
                        ll = new double[2] { minX, minY };
                        ur = new double[2] { maxX, maxY };
                        return true;
                    }
                }
            }
            catch { }
        }

        return TryGetBoundingBox(ent, out ll, out ur);
    }

    /// <summary>
    /// 自动确定当前文档的图框范围（用于窗选出图窗口）。
    /// 策略：优先使用名为"图层图框"的图层（向后兼容）；
    /// 否则在所有图层中取"实体合并包围盒面积最大"的一层作为图框层——
    /// 因为图框通常是覆盖范围最大、最靠外的外层轮廓。
    /// </summary>
    internal static bool TryGetFrameExtents(Late doc, out double[] ll, out double[] ur, out string err, out string source)
    {
        ll = null; ur = null; err = ""; source = "";
        try
        {
            var ms = new Late(doc.Get("ModelSpace"));
            int count;
            try { count = System.Convert.ToInt32(ms.Get("Count")); }
            catch { count = 0; }

            var legacy = new LayerStat(LegacyFrameLayerName);
            var layers = new Dictionary<string, LayerStat>(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                object at;
                try { at = ms.GetAt("Item", i); }
                catch { continue; }

                Late ent = new Late(at);
                string layer;
                try { layer = System.Convert.ToString(ent.Get("Layer")); }
                catch { layer = "(无图层)"; }

                if (!TryGetEntityExtents(ent, out double[] e0, out double[] e1)) continue;

                if (!layers.TryGetValue(layer, out var st)) { st = new LayerStat(layer); layers[layer] = st; }
                st.Merge(e0, e1);

                if (string.Equals(layer, LegacyFrameLayerName, StringComparison.Ordinal))
                    legacy.Merge(e0, e1);
            }

            // 1) 向后兼容：存在"图层图框"层且有实体。
            if (legacy.HasBox)
            {
                ll = legacy.Ll; ur = legacy.Ur;
                source = "图层【" + LegacyFrameLayerName + "】(兼容旧模式)";
                return true;
            }

            // 2) 自动识别：取实体合并包围盒面积最大的图层作为图框层。
            LayerStat best = null;
            foreach (var st in layers.Values)
            {
                if (!st.HasBox) continue;
                if (best == null || st.Area > best.Area) best = st;
            }

            if (best != null)
            {
                ll = best.Ll; ur = best.Ur;
                source = "自动识别图层【" + best.Name + "】";
                return true;
            }

            err = "未识别到任何可用的图框范围（模型空间无有效实体）";
            source = "";
            return false;
        }
        catch (Exception ex)
        {
            err = "自动识别图框失败：" + ex.Message;
            return false;
        }
    }

    internal static bool TryGetBoundingBox(Late ent, out double[] b0, out double[] b1)
    {
        b0 = null; b1 = null;
        try
        {
            dynamic raw = ent.Raw;
            object lo = null, hi = null;
            raw.GetBoundingBox(lo, hi);
            if (lo is Array a && hi is Array b)
            {
                b0 = ToDoubles(a);
                b1 = ToDoubles(b);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static double[] ToDoubles(object v)
    {
        if (v == null || !(v is Array { Length: > 0 } arr)) return null;
        var d = new double[arr.Length];
        for (int i = 0; i < arr.Length; i++)
            d[i] = System.Convert.ToDouble(arr.GetValue(i), CultureInfo.InvariantCulture);
        return d;
    }

    /// <summary>记录一个图层所有实体合并后的包围盒范围。</summary>
    private sealed class LayerStat
    {
        public readonly string Name;
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;
        public bool HasBox;

        public LayerStat(string name) => Name = name;

        public void Merge(double[] ll, double[] ur)
        {
            if (ll[0] < MinX) MinX = ll[0];
            if (ll[1] < MinY) MinY = ll[1];
            if (ur[0] > MaxX) MaxX = ur[0];
            if (ur[1] > MaxY) MaxY = ur[1];
            HasBox = true;
        }

        public double[] Ll => new double[2] { MinX, MinY };
        public double[] Ur => new double[2] { MaxX, MaxY };
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Area => Math.Max(0.0, Width) * Math.Max(0.0, Height);
    }
}
