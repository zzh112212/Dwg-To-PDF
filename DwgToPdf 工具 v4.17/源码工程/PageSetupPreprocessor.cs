using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DwgToPdf.Batch
{
    /// <summary>COM 批量设置图纸副本的 Model 布局页面设置（纸张/方向/样式），使 -EXPORT 继承正确纸张。
    /// 设计依据（2026-08-27 用户决策）：accoreconsole 的 -EXPORT 输出纸张恒为图纸默认页面设置，
    /// 会话内无法改变 → 出图前用 COM 把每张图纸副本的 Model 布局页面设置改为目标纸张，随后 -EXPORT 继承。</summary>
    internal sealed class PageSetupPreprocessor
    {
        /// <summary>
    /// 每文件：复制源图→副本，COM 打开副本→设置 Model 布局页面设置（CanonicalMediaName/PlotRotation/StyleSheet）→保存→关闭。
    /// 返回 源图绝对路径→副本绝对路径 映射；失败文件不包含（由调用方走 COM 兜底）。
    /// 多实例并行 + 动态任务池：每 worker 独立 COM 实例自启自关（仅关自启实例），共享原子计数器动态领取任务；
    /// 副本目录 prepDir\w{wi}（v4.17：由调用方下发，默认指向临时目录，不再放在输出文件夹 logs\_prep）。
    /// outputFolder：仅用于读取图框缓存 frame_cache.json。
    /// isCancelled：每任务开始前检查，已取消则停止再取新任务（当前文件 COM 操作无法安全中断，处理完当前文件后停止）。
    /// </summary>
    public Dictionary<string, string> Preprocess(List<string> files, string outputFolder, string prepDir,
        string ctb, double marginMm, bool tight, IProgress<ProgressUpdate> progress,
        LogWriter log, out string error, Func<bool> isCancelled = null, ConvertProfile profile = null,
        bool showLineWeight = false,
        Dictionary<string, string> nameOverride = null)
    {
        error = "";
        var copies = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (files == null || files.Count == 0) return new Dictionary<string, string>();
        if (string.IsNullOrEmpty(prepDir)) prepDir = Path.Combine(outputFolder, "logs", "_prep");
        // 幂等：建目录失败不影响后续（失败文件会走 COM 兜底）
        try { Directory.CreateDirectory(prepDir); } catch { }
        int w = SmartConcurrency.PrepWorkers(files.Count);
        int[] next = new int[1];
        int[] done = new int[1]; // v3.3 整改③：进度计数器提升为全体 worker 共享，保证"设置页面设置 n/total"单调递增
        var threads = new List<Thread>(w);
        var errors = new ConcurrentQueue<string>();
        for (int i = 0; i < w; i++)
        {
            int wi = i;
            var t = new Thread(() => PrepWorker(wi, next, done, files, prepDir, ctb, marginMm, tight,
                                                outputFolder, progress, log, copies, errors, isCancelled, showLineWeight, nameOverride));
            t.SetApartmentState(ApartmentState.STA); // COM 会话必须 STA（RunCore 所在线程为 MTA）
            t.IsBackground = true;
            threads.Add(t);
        }
        for (int i = 0; i < threads.Count; i++)
        {
            if (i > 0) Thread.Sleep(800); // v4.0：保持 800ms 错开（150ms 实测导致资源竞争，性能反而下降）
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();
        if (!errors.IsEmpty)
            error = "预处理阶段异常: " + string.Join(" | ", errors);
        else if (copies.Count < files.Count)
            error = "预处理完成 " + copies.Count + "/" + files.Count + "（未生成副本的文件将走 COM 兜底）";
        return new Dictionary<string, string>(copies);
    }

    private void PrepWorker(int wi, int[] next, int[] done, List<string> files, string prepDir, string ctb, double marginMm,
        bool tight, string outputFolder, IProgress<ProgressUpdate> progress, LogWriter log,
        ConcurrentDictionary<string, string> copies, ConcurrentQueue<string> errors, Func<bool> isCancelled,
        bool showLineWeight = false, Dictionary<string, string> nameOverride = null)
    {
        double m = tight ? 0.0 : marginMm;
        object app = null, doc = null;
        bool owns = false;
        HashSet<int> ownedPids = new HashSet<int>();
        Late docs = null, d = null, layouts = null, model = null;
        string dir = Path.Combine(prepDir, "w" + wi);
        // 幂等：建目录失败不影响后续（失败文件会走 COM 兜底）
        try { Directory.CreateDirectory(dir); } catch { }
        try
        {
            OleMsgFilter.Install();
            // 每 worker 自启实例并检测归属（复用运行实例则不关，与单实例版 OwnsInstance 语义一致）
            var before = ComStarter.AcadProcessIds();
            app = ComStarter.StartNewInstance();
            var appLate = new Late(app);
            // best-effort：预热 COM 调用失败不影响后续流程
            try { appLate.Get("Documents"); } catch { }
            // v3.4：登记工具自启实例 PID（停止时可强制结束；正常 Quit 后注销）
            ownedPids = ComStarter.AcquireAndRegisterNewAcadPids(before, 4000);
            owns = ownedPids.Count > 0;
            if (owns)
            {
                // best-effort：抑制弹窗/后台化属优化项，设置失败不影响转换主流程
                try { appLate.Set("Visible", false); } catch { }
            }
            docs = new Late(appLate.Get("Documents"));
        }
        catch (Exception ex)
        {
            errors.Enqueue("worker" + wi + " AutoCAD 启动失败: " + ex.Message);
            log.WriteLine("[失败] 预处理 worker" + wi + " 启动失败：" + ex.Message);
            return; // 其余 worker 走动态池接走任务；极端情况全部启动失败 → copies 为空 → 全部 COM 兜底
        }
        try
        {
            while (true)
            {
                if (isCancelled != null && isCancelled()) break; // 用户停止：不再取新任务
                int idx = Interlocked.Increment(ref next[0]) - 1;
                if (idx >= files.Count) break; // 任务池取空
                string src = files[idx];
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) continue;
                int n = Interlocked.Increment(ref done[0]);
                // best-effort：界面操作失败不影响功能
                try { progress?.Report(new ProgressUpdate { Index = 0, Total = files.Count, FileName = "", Status = ItemStatus.Running, Booting = true, Phase = RunPhase.PageSetup, PhaseIndex = n, PhaseTotal = files.Count, Note = "设置页面设置 " + n + "/" + files.Count }); } catch { }
                // v4.5：跨子目录同名源图的副本用引擎下发的"工作名"（含子目录标识），
                // 消除并发预处理时同名副本 File.Copy 互相覆盖、两 worker 打开同一副本的竞态
                string srcName = Path.GetFileName(src);
                if (nameOverride != null && nameOverride.TryGetValue(src, out string workBase))
                    srcName = workBase + Path.GetExtension(src);
                string copyPath = UniqueCopyPath(dir, srcName);
                try
                {
                    // 图框从输出文件夹缓存读取（阶段 1 已写入）；无缓存则无法定纸张 → 该文件走 COM 兜底
                    // v4.6：删除 cacheWaitMs 等待循环（唯一调用方恒传 0，从未生效）
                    if (!FrameCache.TryGet(src, outputFolder, out FrameCache.Entry entry))
                    {
                        log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 预处理跳过（无图框缓存），走 COM 兜底");
                        continue;
                    }
                    double frameW = entry.MaxX - entry.MinX;
                    double frameH = entry.MaxY - entry.MinY;
                    var ps = PageSpec.Resolve(frameW, frameH, m);
                    File.Copy(src, copyPath, true);
                    // best-effort：清只读失败不影响后续写出
                    try { var cfi = new FileInfo(copyPath); if (cfi.IsReadOnly) cfi.IsReadOnly = false; } catch { }
                    // 页面设置与单实例版完全一致（Model 布局；-EXPORT 继承）
                    doc = docs.Call("Open", copyPath, false);
                    d = new Late(doc);
                    layouts = new Late(d.Get("Layouts"));
                    model = new Late(layouts.GetAt("Item", "Model"));
                    d.Set("ActiveLayout", model.Raw);
                    // v4.17 修订十五：HQ pc3 已于 v4.16 退役，设备恒为标准 pc3、媒体名直接取 PageSpec
                    string plotter = PrintQuality.StandardPlotterName;
                    string media = ps.Media;
                    model.Set("ConfigName", plotter);
                    try { model.Set("StyleSheet", ctb); }
                    catch (Exception exSs)
                    {
                        log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 预处理 StyleSheet(" + ctb + ") 设置失败: " + exSs.Message + "（继续）");
                    }
                    try { model.Set("CanonicalMediaName", media); }
                    catch (Exception exMedia)
                    {
                        log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 预处理 CanonicalMediaName(" + media + ") 首次失败: " + exMedia.Message + "（补设 ConfigName 重试）");
                        try
                        {
                            model.Set("ConfigName", plotter);
                            model.Set("CanonicalMediaName", media);
                        }
                        catch (Exception exMedia2)
                        {
                            throw new InvalidOperationException("CanonicalMediaName(" + media + ") 重试仍失败: " + exMedia2.Message);
                        }
                    }
                    try { model.Set("PlotRotation", ps.Rotation); }
                    catch (Exception exRot)
                    {
                        log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 预处理 PlotRotation(" + ps.Rotation + ") 设置失败: " + exRot.Message + "（继续）");
                    }
                    // v3.8 修复：有边距时设置精确模式（窗口=图框本身 + SetCustomScale 固定比例 + CenterPlot 居中），
                    // 否则 -EXPORT 继承源图 ScaleToFit 取 min 比例，必然某方向贴边（上边线消失 bug）。
                    // 实验结论（2026-08-31 _调查/PageSetupLab）：
                    //   - CustomScale 属性不存在，正确 API 是 SetCustomScale(分子, 分母) 方法；
                    //   - 不能设 PlotOrigin=(0,0)（会禁用 CenterPlot → 内容贴左下角）；
                    //   - SetWindowToPlot/UseStandardScale 必须先于 PlotType 设置（v2.3.1 经验）。
                    // v3.8 方案 G（2026-08-31）：窗口=图框外扩 M（每边 10mm，即 frame+2M）。
                    // 根因：识别 frame 常小于真实图框外框（识别容差），窗口=frame 会裁掉
                    // 真实图框超出部分（11 张 H56E 图框显示不完全 bug）。
                    // 配套 PageSpec.s 分母已改为窗口尺寸，保证 (frame+2M)×s ≤ 纸-2M：
                    //   短边恰好留 M 边距（精确 10mm），长边更大但对称；真实图框完整显示。
                    // 注意：此处的 M 即 m（tight=0 / 有边距=marginMm），与 PageSpec.Resolve 的 margin 一致。
                    if (!tight)
                    {
                        try
                        {
                            // v4.17 修订三 修复（2026-10-15）：先复位 UCS 为世界坐标系再设打印窗口。
                            // 图框识别返回 WCS 坐标，SetWindowToPlot 按当前 UCS 解释；源图保存过
                            // 非世界 UCS 时窗口整体平移 → 出图裁切错位（问题图纸实测 UCSORG
                            // =(229.02,215.70) 恰等于识别图框左下角）。详见 PlotSession.BuildScript。
                            CadConverter.TryResetUcsToWorld(d, msg => log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— " + msg));
                            model.Call("SetWindowToPlot", new double[2] { entry.MinX - m, entry.MinY - m }, new double[2] { entry.MaxX + m, entry.MaxY + m });
                            model.Set("UseStandardScale", false);
                            model.Set("PlotType", 4); // acWindow
                            model.Call("SetCustomScale", ps.Scale, 1.0);
                            // v4.3 方案 A2：显式清零 PlotOrigin 抵消源图已有偏移（v3.8 注释说
                            // "PlotOrigin=(0,0) 会禁用 CenterPlot"——实测验证是否容忍共存）。
                            model.Set("PlotOrigin", new double[2] { 0.0, 0.0 });
                            model.Set("CenterPlot", true);
                            log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 精确模式 SetCustomScale(" + ps.Scale + ",1) 窗口=图框外扩" + m + "mm PlotOrigin=(0,0) CenterPlot=true");
                        }
                        catch (Exception exExact)
                        {
                            log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 预处理精确模式设置失败（将继承源图比例）: " + exExact.Message);
                        }
                    }
                    new Late(doc).Call("Save");
                    copies[src] = copyPath;
                    log.WriteLine("[信息] " + Path.GetFileNameWithoutExtension(src) + " —— 页面设置 " + media + "(标准pc3) " + "旋转 " + ps.Rotation);
                }
                catch (Exception ex)
                {
                    log.WriteLine("[失败] " + Path.GetFileNameWithoutExtension(src) + " —— 预处理失败：" + ex.Message + "（走 COM 兜底）");
                    // 幂等：先清旧产物，删除失败不影响后续写出
                    try { if (File.Exists(copyPath)) File.Delete(copyPath); } catch { }
                }
                finally
                {
                    if (doc != null)
                    {
                        // v4.11：Close 带硬超时（RPC 死锁时不再无限等待；超时后放弃，进程由看门狗/收尾兜底终结）
                        if (!new Late(doc).CallWithTimeout(60000, "Close", false))
                            log.WriteLine("[警告] " + Path.GetFileNameWithoutExtension(src) + " —— 关闭文档超时（疑似 COM 阻塞），跳过继续");
                    }
                    ReleaseCom(model?.Raw, layouts?.Raw, d?.Raw, doc);
                    doc = null; d = null; layouts = null; model = null;
                }
            }
        }
        catch (Exception ex)
        {
            errors.Enqueue("worker" + wi + ": " + ex.Message);
            log.WriteLine("[失败] 预处理 worker" + wi + " 整体异常：" + ex.Message);
        }
        finally
        {
            ReleaseCom(docs?.Raw);
            if (app != null)
            {
                if (owns)
                {
                    // best-effort：抑制弹窗/后台化属优化项，设置失败不影响转换主流程
                    try { new Late(app).Set("DisplayAlerts", false); } catch { }
                    // best-effort：抑制弹窗/后台化属优化项，设置失败不影响转换主流程
                    try { new Late(app).Set("DisplayFileAlerts", false); } catch { }
                    // v4.1：关闭所有文档且不保存，防止 Quit 时弹"是否保存 Drawing1.dwg"
                    try {
                        var docs2 = new Late(new Late(app).Get("Documents"));
                        while (true) {
                            int cnt; try { cnt = System.Convert.ToInt32(docs2.Get("Count")); } catch { cnt = 0; }
                            if (cnt <= 0) break;
                            object d2;                             try { d2 = docs2.GetAt("Item", 0); } catch { break; }
                            if (!new Late(d2).CallWithTimeout(30000, "Close", false)) break; // v4.11：硬超时
                        }
                    // best-effort：关闭文档失败不阻断 Quit 收尾
                    } catch { }
                    new Late(app).CallWithTimeout(30000, "Quit"); // v4.11：硬超时，防 Quit 挂死
                    // v3.4：实例已正常退出，从工具自启登记表注销
                    foreach (var pid in ownedPids) ComStarter.UnregisterOwnedPid(pid);
                }
                // best-effort：释放失败交由 GC 兜底，不影响后续流程
                try { Marshal.ReleaseComObject(app); } catch { }
            }
        }
    }

        /// <summary>逐个释放 COM RCW（try-catch 包裹，防止同一 RCW 重复释放抛 InvalidComObjectException；
        /// 风格与 CadConverter.ReleaseCom 一致）。</summary>
        private static void ReleaseCom(params object[] objs)
        {
            foreach (var o in objs)
            {
                try { if (o != null) Marshal.ReleaseComObject(o); }
                // best-effort：释放失败交由 GC 兜底，不影响后续流程
                catch { }
            }
        }

        /// <summary>副本命名：源文件名；重名冲突追加 "(n)"（不同子目录的同名源图共存于 _prep 目录）。</summary>
        private static string UniqueCopyPath(string dir, string fileName)
        {
            string ext = Path.GetExtension(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string p = Path.Combine(dir, fileName);
            int n = 2;
            while (File.Exists(p))
            {
                p = Path.Combine(dir, baseName + "(" + n + ")" + ext);
                n++;
            }
            return p;
        }
    }
}
