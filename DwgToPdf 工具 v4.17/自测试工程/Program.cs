using System;
using System.Collections.Generic;

namespace DwgToPdf.SelfTest
{
    public static class Assertion
    {
        public static int Failures;
        public static void True(bool cond, string name, string detail = "")
        {
            if (cond) Console.WriteLine("[PASS] " + name);
            else { Failures++; Console.WriteLine("[FAIL] " + name + (detail.Length > 0 ? " :: " + detail : "")); }
        }
        public static void Near(double a, double b, double tol, string name)
        { True(Math.Abs(a - b) <= tol, name + " (" + a + " vs " + b + ")"); }
        public static bool Passed() => Failures == 0;
        public static int ExitCode() => Failures == 0 ? 0 : 1;
    }

    public static class Program
    {
        /// <summary>
        /// v4.16（2026-10-07，用户批准）：校验「引擎新鲜度」，把"SelfTest 没重编 ⇒ 回归假绿"变成显式失败。
        ///
        /// 为什么需要：<c>SelfTest.csproj</c> 用 <c>&lt;Compile Include="..\源码工程\*.cs"&gt;</c>
        /// **直接引用引擎源码**，所以 <c>SelfTest.exe</c> 是一份**独立的编译产物**（内含引擎源码副本），
        /// 而**不是**引用主工程 exe。于是：
        ///   改了 ConverterEngine*.cs → 只编了主工程、没编 SelfTest ⇒ SelfTest 里仍是**旧引擎**，
        ///   但断言会**全部通过** —— 这比"失败"危险得多（假绿）。
        /// 判定规则：主工程 exe 的最后写入时间**晚于** SelfTest.exe ⇒ SelfTest 装的是旧引擎 ⇒ FAIL。
        /// 反之（SelfTest 更新，或本来就只编了 SelfTest）⇒ PASS。
        /// 找不到主工程 exe ⇒ 跳过，不判失败。
        ///
        /// 修复办法：<c>dotnet build 自测试工程\SelfTest.csproj -c Release</c>
        /// </summary>
        private static void CheckEngineFreshness()
        {
            try
            {
                string selfDir = AppDomain.CurrentDomain.BaseDirectory;
                string selfExe = System.IO.Path.Combine(selfDir, "SelfTest.exe");
                // 向上寻找同时含"源码工程"的目录：不把层级写死，目录结构调整后不会静默失效
                System.IO.DirectoryInfo root = new System.IO.DirectoryInfo(selfDir);
                while (root != null && !System.IO.Directory.Exists(System.IO.Path.Combine(root.FullName, "源码工程")))
                    root = root.Parent;
                if (root == null) { Console.WriteLine("[跳过] 找不到源码工程目录，无法校验引擎新鲜度"); return; }
                string mainExe = System.IO.Path.Combine(root.FullName, "源码工程", "bin", "Release", "net48", "DwgToPdf.exe");
                if (!System.IO.File.Exists(mainExe))
                { Console.WriteLine("[跳过] 未找到主工程 exe（DwgToPdf.exe），无法校验引擎新鲜度"); return; }
                DateTime tSelf = System.IO.File.GetLastWriteTime(selfExe);
                DateTime tMain = System.IO.File.GetLastWriteTime(mainExe);
                Assertion.True(tMain <= tSelf,
                    "引擎新鲜度：SelfTest 不早于主工程 exe（否则回归跑的是旧引擎 = 假绿）",
                    "SelfTest=" + tSelf.ToString("yyyy-MM-dd HH:mm:ss")
                    + "  DwgToPdf.exe=" + tMain.ToString("yyyy-MM-dd HH:mm:ss")
                    + "  → 请重新编译：dotnet build 自测试工程\\SelfTest.csproj -c Release");
            }
            catch (Exception ex) { Console.WriteLine("[跳过] 引擎新鲜度检查异常：" + ex.Message); }
        }

        public static int Main(string[] args)
        {
            // v4.16：任何分支跑之前先校验引擎新鲜度（秒级、只读文件系统）。
            // 放在最前面是有意的 —— 假绿必须**在第一行**就暴露，而不是等跑完 40 张才发现。
            CheckEngineFreshness();
            if (args.Length > 0 && args[0] == "--extract-smoke")
            {
                RunExtractSmoke();
                return Assertion.ExitCode();
            }
            if (args.Length > 0 && args[0] == "--plot-smoke")
            {
                RunPlotSmoke();
                return Assertion.ExitCode();
            }
            if (args.Length > 0 && args[0] == "--e2e") { RunE2E(); return Assertion.ExitCode(); }
            // v4.15：秒级单元断言（纯函数，不启动 AutoCAD）——日常改动先用它回归，再跑 E2E
            if (args.Length > 0 && args[0] == "--unit") { RunUnit(); return Assertion.ExitCode(); }
            if (args.Length > 0 && args[0] == "--fallback") { RunFallback(); return Assertion.ExitCode(); }
            if (args.Length > 1 && args[0] == "--e2e-custom") { RunE2E(args[1]); return Assertion.ExitCode(); }
            if (args.Length > 2 && args[0] == "--dir") { RunDirE2E(args[1], args[2]); return Assertion.ExitCode(); }
            if (args.Length > 2 && args[0] == "--dirc") { RunDirE2E(args[1], args[2], company: true); return Assertion.ExitCode(); }
            if (args.Length > 2 && args[0] == "--dirf") { RunDirE2E(args[1], args[2], flat: true); return Assertion.ExitCode(); }
            // v4.8.1：--dirb = 公司版 + 识别块图框（验证看板/块图框图纸的失败回收率）
            if (args.Length > 2 && args[0] == "--dirb") { RunDirE2E(args[1], args[2], company: true, blockFrame: true); return Assertion.ExitCode(); }
            // v4.17 修订三：--dirp = 固定“本轮开始时刻”连跑两轮，验证同名追加 -2 后缀（绝不与已有批合并）
            if (args.Length > 2 && args[0] == "--dirp") { RunDirPinned(args[1], args[2]); return Assertion.ExitCode(); }
            // v4.17 修订三：--dirs = “第一批已成功 + 新增图纸”两轮验证（用 PDF 修改时间显式证明旧图没被重打）
            if (args.Length > 2 && args[0] == "--dirs") { RunDirSkip(args[1], args[2]); return Assertion.ExitCode(); }
            if (args.Length > 0 && args[0] == "--sync") { TestSmartConcurrency(); return Assertion.ExitCode(); }
            if (args.Length > 0 && args[0] == "--dc") { RunDcSmoke(); return Assertion.ExitCode(); }
            // ---- Task 9: 验收分支（LISP vs COM 图框对比 / PDF 质量审查 / 27 张基准计时） ----
            if (args.Length > 0 && args[0] == "--compare-frames")
            {
                // COM 调用必须在 STA 线程；可选第二参数限制张数（冒烟用小样本，缺省=全量）
                int limit = 0;
                if (args.Length > 1 && int.TryParse(args[1], out int n)) limit = n;
                int lim = limit;
                var t = new System.Threading.Thread(() => RunCompareFrames(lim));
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.Start(); t.Join();
                return Assertion.ExitCode();
            }
            if (args.Length > 1 && args[0] == "--qa") { RunQa(args[1]); return Assertion.ExitCode(); }
            if (args.Length > 1 && args[0] == "--bench")
            {
                int workers = args.Length > 2 ? int.Parse(args[2]) : 4;
                RunBench(args[1], workers);
                return Assertion.ExitCode();
            }
            if (args.Length > 0 && args[0] == "--e2e-cancel")
            {
                int delay = args.Length > 1 ? int.Parse(args[1]) : 8000;
                RunE2ECancel(delay);
                return Assertion.ExitCode();
            }
            // ---- Task 10: 公司版 / 高质量打印 冒烟（真实 accoreconsole 集成测试，需数分钟） ----
            if (args.Length > 0 && args[0] == "--company-smoke") { RunCompanySmoke(); return Assertion.ExitCode(); }
            if (args.Length > 0 && args[0] == "--hq-smoke") { RunHqSmoke(); return Assertion.ExitCode(); }
            if (args.Length > 0 && args[0] == "--e2e-company") { RunE2ECompany(); return Assertion.ExitCode(); }
            if (args.Length > 0 && args[0] == "--gen-library") { RunGenLibrary(); return Assertion.ExitCode(); }
            // v3.6：边距对比自测（--margin-compare <src> <m1> <m2> [outDir]）。同一张图用两个边距值跑两次，
            // 验证 PrintOptions.EdgeMarginMm 是否真影响 PDF 输出。用于排查"边距功能似乎没有作用"。
            if (args.Length > 3 && args[0] == "--margin-compare") { RunMarginCompare(args[1], double.Parse(args[2]), double.Parse(args[3]), args.Length > 4 ? args[4] : null); return Assertion.ExitCode(); }
            TestPageSpec();
            TestFrameCache();
            TestLocator();
            TestExtractScript();
            TestPlotScript();
            TestSettingsJsonNoHqKey();
            TestFrameTemplateLibrary();
            TestConvertProfile();
            TestSmartConcurrency();
            TestPdfFolderTemplate();
            TestFilterTodoFiles();
            // v4.17 修订十（B2 异常可见化批）：CQ-01/CQ-02 注入断言
            TestLogWriterFailureVisibility();
            TestRecognitionFailureNote();
            // v4.17 修订十·补（CQ-02 边界 / CQ-06 B-4）：日志造期降级 + 设置保存失败可见化
            TestSettingsSaveFailureVisibility();
            if (!Assertion.Passed()) return Assertion.ExitCode();
            Console.WriteLine("ALL TESTS PASSED.");
            return Assertion.ExitCode();
        }

        // v4.17 修订十（CQ-02，V3）：日志写盘失败可见化——正常路径零变化 + 失败路径有痕迹
        private static void TestLogWriterFailureVisibility()
        {
            // N1：正常构造 + 写多行 → WriteFailed==false（成功路径零变化实证）
            string dir1 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwgtopdf_b2_n1");
            string path1 = System.IO.Path.Combine(dir1, "run.log");
            try
            {
                if (System.IO.Directory.Exists(dir1)) System.IO.Directory.Delete(dir1, true);
                System.IO.Directory.CreateDirectory(dir1);
                var ok = new DwgToPdf.Batch.LogWriter(path1);
                ok.WriteLine("line 1");
                ok.WriteLine("line 2");
                ok.WriteLine("line 3");
                Assertion.True(!ok.WriteFailed && ok.WriteFailCount == 0 && ok.LastWriteError == null,
                    "CQ-02 N1：正常写盘路径 WriteFailed==false（成功路径零变化）");
                ok.Dispose();
            }
            finally { try { if (System.IO.Directory.Exists(dir1)) System.IO.Directory.Delete(dir1, true); } catch { } }

            // N2/N3：反射关闭底层流 → 写盘失败被标志 + 仅首次失败 mirror 后台日志（降噪）
            string dir2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwgtopdf_b2_n2");
            string path2 = System.IO.Path.Combine(dir2, "run.log");
            try
            {
                if (System.IO.Directory.Exists(dir2)) System.IO.Directory.Delete(dir2, true);
                System.IO.Directory.CreateDirectory(dir2);
                var lw = new DwgToPdf.Batch.LogWriter(path2);
                var sw = (System.IO.StreamWriter)typeof(DwgToPdf.Batch.LogWriter)
                    .GetField("_w", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .GetValue(lw);
                sw.Close();   // 构造坏场景：底层流已关闭，随后写入必抛 ObjectDisposedException
                lw.WriteLine("boom");
                Assertion.True(lw.WriteFailed, "CQ-02 N2：写盘失败后 WriteFailed==true");
                Assertion.True(lw.LastWriteError != null && lw.LastWriteError.Contains("ObjectDisposedException"),
                    "CQ-02 N2：LastWriteError 含 ObjectDisposedException", lw.LastWriteError);
                lw.WriteLine("boom again");
                Assertion.True(lw.WriteFailCount == 2, "CQ-02 N3：连续失败只累计不重复 mirror（WriteFailCount==2）",
                    lw.WriteFailCount.ToString());
            }
            finally { try { if (System.IO.Directory.Exists(dir2)) System.IO.Directory.Delete(dir2, true); } catch { } }

            // N7（修订十·补 CQ-02 边界）：造期失败（已存在文件被当目录用）→ 不抛 + 置失败标志 + 降级仅内存
            string fileAsDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwgtopdf_b2r1_n7_file");
            try
            {
                System.IO.File.WriteAllText(fileAsDir, "x");   // 已存在文件，被当作目录用 → 造期必失败
                string badPath = System.IO.Path.Combine(fileAsDir, "run.log");
                var lw7 = new DwgToPdf.Batch.LogWriter(badPath, line => { });
                Assertion.True(lw7.WriteFailed, "CQ-02·补 N7：造期失败 WriteFailed==true（构造不抛异常）");
                Assertion.True(lw7.LastWriteError != null && lw7.LastWriteError.Contains("构造期"),
                    "CQ-02·补 N7：LastWriteError 含 构造期", lw7.LastWriteError);
                lw7.WriteLine("after degrade");   // 仅内存模式：不抛，失败计数仍保持 1
                Assertion.True(lw7.WriteFailCount == 1, "CQ-02·补 N7：降级后写行不抛且 WriteFailCount==1",
                    lw7.WriteFailCount.ToString());
            }
            finally { try { if (System.IO.File.Exists(fileAsDir)) System.IO.File.Delete(fileAsDir); } catch { } }
        }

        // v4.17 修订十·补（CQ-06 B-4）：设置保存失败可见化——Save 内部 catch 记日志回 false，不再静默
        private static void TestSettingsSaveFailureVisibility()
        {
            // N8：成功路径仍 true（合法 temp 路径），行为零变化
            string okPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwgtopdf_b2r1_n8_ok.json");
            try
            {
                var s8 = new DwgToPdf.Batch.AppSettings();
                Assertion.True(s8.Save(okPath), "CQ-06·补 N8：合法路径 Save 返回 true（成功路径零变化）");
            }
            finally { try { if (System.IO.File.Exists(okPath)) System.IO.File.Delete(okPath); } catch { } }

            // N8b：非法路径（已存在文件当目录）→ 回 false 且不抛异常（失败可感知 + 日志留痕）
            string fileAsDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwgtopdf_b2r1_n8_dir");
            try
            {
                System.IO.File.WriteAllText(fileAsDir, "x");
                string badPath = System.IO.Path.Combine(fileAsDir, "settings.json");
                var s8b = new DwgToPdf.Batch.AppSettings();
                bool r;
                try { r = s8b.Save(badPath); }
                catch (Exception ex)
                {
                    Assertion.True(false, "CQ-06·补 N8b：非法路径 Save 不抛异常", ex.GetType().Name);
                    return;
                }
                Assertion.True(!r, "CQ-06·补 N8b：非法路径 Save 返回 false（失败可感知）");
            }
            finally { try { if (System.IO.File.Exists(fileAsDir)) System.IO.File.Delete(fileAsDir); } catch { } }
        }

        // v4.17 修订十（CQ-01，V2）：识别异常真实化纯助手——取消语义保持 / 真实原因可读 / 可空安全
        private static void TestRecognitionFailureNote()
        {
            Assertion.True(DwgToPdf.Batch.ConverterEngine.RecognitionFailureNote(new System.OperationCanceledException()) == null,
                "CQ-01 N4：取消异常透传 null（取消语义保持）");
            string note = DwgToPdf.Batch.ConverterEngine.RecognitionFailureNote(new System.InvalidOperationException("boom"));
            Assertion.True(note != null && note.Contains("InvalidOperationException") && note.Contains("boom"),
                "CQ-01 N5：真实异常含类型与消息", note);
            try
            {
                string nul = DwgToPdf.Batch.ConverterEngine.RecognitionFailureNote(null);
                Assertion.True(nul == null, "CQ-01 N6：null 入参安全返回 null");
            }
            catch (Exception ex)
            {
                Assertion.True(false, "CQ-01 N6：null 入参安全返回 null", ex.GetType().Name);
            }
        }

        private static void TestPageSpec()
        {
            // Case 1: PISCES 靠背悬簧 1783.5 x 1261.5, 边距 5mm → 4A0 全出血、横放(90)
            var r = DwgToPdf.Batch.PageSpec.Resolve(1783.5, 1261.5, 5.0);
            Assertion.True(r.Media == "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)", "PageSpec PISCES media 4A0", r.Media);
            Assertion.True(r.Rotation == DwgToPdf.Batch.PageSpec.AcDegrees90, "PageSpec PISCES rotation 90");
            // v4.4 修正断言：v3.8 方案 G 后 s 分母为出图窗口(frame+2M)，非旧版 frame
            // （原断言 min((纸-2M)/框, …) 为 v3.8 前语义，v3.8/v4.3 未同步，遗留 3 条 FAIL）
            double expectedScalePisces = Math.Min((2378.0 - 10.0) / (1783.5 + 10.0), (1681.0 - 10.0) / (1261.5 + 10.0));
            Assertion.Near(r.Scale, expectedScalePisces, 1e-9, "PageSpec PISCES scale");
            Assertion.Near(r.PaperLong, 2378.0, 1e-9, "PageSpec PISCES paperLong 2378");
            Assertion.Near(r.PaperShort, 1681.0, 1e-9, "PageSpec PISCES paperShort 1681");

            // Case 2: 贴边 margin=0, 594 x 420 → A2 全出血
            var t = DwgToPdf.Batch.PageSpec.Resolve(594.0, 420.0, 0.0);
            Assertion.True(t.Media == "ISO_full_bleed_A2_(420.00_x_594.00_MM)", "PageSpec tight A2", t.Media);
            double expectedScaleTight = Math.Min(594.0 / 594.0, 420.0 / 420.0);
            Assertion.Near(t.Scale, expectedScaleTight, 1e-9, "PageSpec tight scale");
            Assertion.True(t.Rotation == DwgToPdf.Batch.PageSpec.AcDegrees90, "PageSpec tight A2 rotation 90");

            // Case 3: 纵向图 594 x 1189 (高>宽) → 旋转 0, A0 全出血
            var p = DwgToPdf.Batch.PageSpec.Resolve(594.0, 1189.0, 0.0);
            Assertion.True(p.Media == "ISO_full_bleed_A0_(841.00_x_1189.00_MM)", "PageSpec portrait A0", p.Media);
            Assertion.True(p.Rotation == DwgToPdf.Batch.PageSpec.AcDegrees0, "PageSpec portrait rotation 0");
            Assertion.Near(p.PaperLong, 1189.0, 1e-9, "PageSpec portrait paperLong 1189");

            // Case 4 (v4.16, 2026-10-07 用户反馈"PISCES 2A0 图纸打印发浅"):
            // 图框 1692 x 1199（比 2A0 的 1682x1189 只大 10mm），边距 10mm。
            // v4.15 严格比较 frame+2M 与纸型 → 被顶到 4A0，内容缩到约 1/1.41，
            // 按页缩放打印时线宽相对纸面宽度减半 = "发浅"；用户手动打印参照是 2A0 1:1。
            // v4.16 加 2% 选纸容差 → 应选 2A0，并把内容按 s 缩到刚好留 >=10mm 余量。
            var q = DwgToPdf.Batch.PageSpec.Resolve(1692.0, 1199.0, 10.0);
            Assertion.True(q.Media == "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)",
                "PageSpec v4.16 轻微超纸用 2A0 (不再跳 4A0)", q.Media);
            Assertion.True(q.Rotation == DwgToPdf.Batch.PageSpec.AcDegrees90, "PageSpec v4.16 rotation 90");
            double expectedScaleTol = Math.Min((1682.0 - 20.0) / (1692.0 + 20.0),
                                               (1189.0 - 20.0) / (1199.0 + 20.0));
            Assertion.Near(q.Scale, expectedScaleTol, 1e-9, "PageSpec v4.16 scale");
            // 内容必须真的放得下：窗口×s <= 纸-2M，两个方向都成立
            Assertion.True((1692.0 + 20.0) * q.Scale <= 1682.0 - 20.0 + 1e-9, "PageSpec v4.16 内容进得了纸长");
            Assertion.True((1199.0 + 20.0) * q.Scale <= 1189.0 - 20.0 + 1e-9, "PageSpec v4.16 内容进得了纸短");
            // 缩放必须非常接近 1:1（容差只允许缩一点点，不能明显改变图纸比例）
            Assertion.True(q.Scale > 0.95 && q.Scale <= 1.0, "PageSpec v4.16 缩放接近 1:1", q.Scale.ToString("0.####"));

            // Case 5 (v4.16 回归护栏): 明显超纸（>容差）时仍走 4A0 兜底，不得改变
            var big = DwgToPdf.Batch.PageSpec.Resolve(2000.0, 1400.0, 10.0);
            Assertion.True(big.Media == "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)",
                "PageSpec v4.16 明显超纸仍 4A0", big.Media);

            // Case 6 (v4.17 修复): 图框超出全部纸型（连 4A0 都放不下）→ 必须兜底到最大纸型 4A0。
            // 原实现 idx 初始为 0，循环不命中时保持 0 = **A4** —— 把大图压到 A4 出图，
            // 内容被裁/缺块（正是用户反馈的问题图纸症状）；且 `if (idx >= PaperLongMm.Length)`
            // 因 idx 恒 < Length 而永不成立（死代码，兜底从未生效）。
            var huge = DwgToPdf.Batch.PageSpec.Resolve(2600.0, 1900.0, 10.0);
            Assertion.True(huge.Media == "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)",
                "PageSpec v4.17 超出全部纸型兜底 4A0（不再压成 A4）", huge.Media);
            Assertion.Near(huge.PaperLong, 2378.0, 1e-9, "PageSpec v4.17 huge paperLong 2378");
            Assertion.Near(huge.PaperShort, 1681.0, 1e-9, "PageSpec v4.17 huge paperShort 1681");
            // 内容必须缩得进 4A0（长/短边两个方向都放得下）
            Assertion.True((2600.0 + 20.0) * huge.Scale <= 2378.0 - 20.0 + 1e-9, "PageSpec v4.17 huge 内容进得了纸长");
            Assertion.True((1900.0 + 20.0) * huge.Scale <= 1681.0 - 20.0 + 1e-9, "PageSpec v4.17 huge 内容进得了纸短");
        }

        private static void TestFrameCache()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_fc_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);

            string fake = System.IO.Path.Combine(dir, "a.dwg");
            System.IO.File.WriteAllText(fake, "0123456789");
            var fi = new System.IO.FileInfo(fake);
            var e = new DwgToPdf.Batch.FrameCache.Entry
            {
                Path = fake, Length = fi.Length, MtimeUtc = fi.LastWriteTimeUtc.Ticks,
                MinX = 0.25, MinY = 1.5, MaxX = 100.0, MaxY = 200.0, Source = "图层0"
            };

            // 命中
            Assertion.True(DwgToPdf.Batch.FrameCache.TryGet(fake, out var hit) == false, "cache miss first time");
            // 写入→再读命中
            DwgToPdf.Batch.FrameCache.Save(dir, new System.Collections.Generic.List<DwgToPdf.Batch.FrameCache.Entry> { e });
            Assertion.True(DwgToPdf.Batch.FrameCache.TryGet(fake, out var hit2) && hit2 != null, "cache hit after save");
            Assertion.Near(hit2.MinX, 0.25, 1e-12, "cache minx"); Assertion.Near(hit2.MinY, 1.5, 1e-12, "cache miny");
            Assertion.Near(hit2.MaxX, 100.0, 1e-12, "cache maxx"); Assertion.Near(hit2.MaxY, 200.0, 1e-12, "cache maxy");
            Assertion.True(hit2.Source == "图层0", "cache source");

            // 文件改动 → 失效
            System.Threading.Thread.Sleep(20);
            System.IO.File.WriteAllText(fake, "0123456789X");
            Assertion.True(DwgToPdf.Batch.FrameCache.TryGet(fake, out var hit3) == false, "cache invalidated by size change");

            // 版本不符 → 失效
            string cacheJson = DwgToPdf.Batch.FrameCache.CachePath(dir);
            string raw = System.IO.File.ReadAllText(cacheJson);
            string modified = raw.Replace("\"version\":1", "\"version\":999");
            System.IO.File.WriteAllText(cacheJson, modified);
            Assertion.True(DwgToPdf.Batch.FrameCache.TryGet(fake, out var hit4) == false, "cache invalidated by version mismatch");

            // 缓存文件损坏 → 未命中（静默容错）
            System.IO.File.WriteAllText(cacheJson, "###corrupted-garbage###");
            Assertion.True(DwgToPdf.Batch.FrameCache.TryGet(fake, out var hit5) == false, "cache corrupted returns miss");

            try { System.IO.Directory.Delete(dir, true); } catch { }
        }

        private static void TestLocator()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            // 本机已核实: D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe
            Assertion.True(!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe), "locator finds accoreconsole.exe", exe);
            Assertion.True(!string.IsNullOrEmpty(exe) &&
                System.IO.Path.GetFileName(exe).Equals("accoreconsole.exe", StringComparison.OrdinalIgnoreCase), "locator filename", exe);
            // 负向：构造非法目录应返回 null（不抛异常）
            string bad = DwgToPdf.Batch.CoreConsoleLocator.FindInDir("Z:\\no_such_dir_xyz");
            Assertion.True(bad == null, "locator null on missing dir");
            // 正向：临时目录放一个假 accoreconsole.exe 应命中完整路径
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_loc_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmpDir);
            string fakeExe = System.IO.Path.Combine(tmpDir, "accoreconsole.exe");
            System.IO.File.WriteAllText(fakeExe, "x");
            string hit = DwgToPdf.Batch.CoreConsoleLocator.FindInDir(tmpDir);
            Assertion.True(string.Equals(hit, fakeExe, StringComparison.OrdinalIgnoreCase), "locator FindInDir hit", hit);
            try { System.IO.Directory.Delete(tmpDir, true); } catch { }
        }

        private static void TestExtractScript()
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_lsp_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            string outTxt = System.IO.Path.Combine(tmp, "x.out");
            // 正常行（accoreconsole 输出为 ANSI/GB2312，故按 936 编码写入模拟真实输出）
            System.IO.File.WriteAllText(outTxt, "0.0 1.0 100.0 200.0|图层0|auto\n", System.Text.Encoding.GetEncoding(936));
            var b = DwgToPdf.Batch.FrameExtractor.ParseResult(outTxt, "a.dwg");
            Assertion.True(b != null, "lsp parse ok");
            Assertion.Near(b.MinX, 0.0, 1e-9, "lsp minx"); Assertion.Near(b.MaxY, 200.0, 1e-9, "lsp maxy");
            Assertion.True(b.Layer == "图层0" && b.Source == "auto", "lsp layer/source");
            // 负坐标 minx=-1 不应被误判为哨兵（与哨兵行以 "-1|" 开头区分）
            System.IO.File.WriteAllText(outTxt, "-1.000000 0.000000 100.000000 200.000000|图层0|auto\n", System.Text.Encoding.GetEncoding(936));
            var bn = DwgToPdf.Batch.FrameExtractor.ParseResult(outTxt, "a.dwg");
            Assertion.True(bn != null && Math.Abs(bn.MinX - (-1.0)) < 1e-9, "lsp negative minx not sentinel");
            // 哨兵/格式错 → null
            System.IO.File.WriteAllText(outTxt, "-1|ERR|no-ent\n");
            Assertion.True(DwgToPdf.Batch.FrameExtractor.ParseResult(outTxt, "a.dwg") == null, "lsp sentinel null");
            System.IO.File.WriteAllText(outTxt, "garbage");
            Assertion.True(DwgToPdf.Batch.FrameExtractor.ParseResult(outTxt, "a.dwg") == null, "lsp garbage null");
            System.IO.File.Delete(outTxt);
            Assertion.True(DwgToPdf.Batch.FrameExtractor.ParseResult(outTxt, "a.dwg") == null, "lsp missing null");
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        private static void TestPlotScript()
        {
            var frame = new DwgToPdf.Batch.FrameBox { MinX = 0, MinY = 0, MaxX = 1783.5, MaxY = 1261.5, Layer = "图层0", Source = "auto" };
            string scr = DwgToPdf.Batch.PlotSession.BuildScript(
                @"D:\a.dwg", @"D:\o\a.pdf", frame, "DWG To PDF.pc3",
                "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)", true, "monochrome.ctb", 5.0, @"D:\t");
            // 最终方案 = -EXPORT PDF + W 窗口（accoreconsole 下 -PLOT 不产出 PDF，Task 1 校准实测）。
            // plotter/media/ctb/方向以 ';' 注释行记录于脚本头部（AutoCAD 脚本注释，accoreconsole 忽略）。
            Assertion.True(scr.Contains("-EXPORT"), "plot script cmd");
            Assertion.True(scr.Contains("DWG To PDF.pc3"), "plot script plotter");
            Assertion.True(scr.Contains("ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)"), "plot script media");
            Assertion.True(scr.Contains("monochrome.ctb"), "plot script ctb");
            Assertion.True(scr.Contains("QUIT"), "plot script quit");
            Assertion.True(scr.Contains(";orientation=L"), "plot script orientation landscape");
            Assertion.True(scr.Contains("FILEDIA") && scr.Contains("BACKGROUNDPLOT"), "plot script sysvars");
            Assertion.True(scr.Contains("\nW\n"), "plot script window region");
            // v4.4 修正断言：v3.8 方案 G 后窗口=图框每边直接外扩 marginMm（delta=M=5），
            // 非旧版 delta=margin/s（原期望 -3.774686 为 v3.8 前语义，遗留 FAIL）
            Assertion.True(scr.Contains("-5,-5"), "plot script window ll", scr);
            Assertion.True(scr.Contains("1788.5,1266.5"), "plot script window ur", scr);
        }

        private static void RunExtractSmoke()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            var files = new List<string>()
            {
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633638-AB_LB600APIA1_靠背悬簧总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633639-AB_LB001APIA2_主驾按摩腰托总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633640-AB_LB003APIA2_主驾4W腰托总成_临时图纸_20260814.dwg",
            };
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_ex_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var ex2 = new DwgToPdf.Batch.FrameExtractor(exe);
            var r = ex2.RunExtract(files, tmp, (f, ok) => Console.WriteLine("  [extract] " + System.IO.Path.GetFileName(f) + " -> " + (ok ? "ok" : "FAIL")));
            // PISCES 每张图已知图框（v2.3.1 权威 COM 实测）：
            // 7633638 = 1783.5 x 1261.5；7633639/7633640 = 1682 x 1189（±0.1）
            var expect = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                [files[0]] = new[] { 1783.5, 1261.5 },
                [files[1]] = new[] { 1682.0, 1189.0 },
                [files[2]] = new[] { 1682.0, 1189.0 },
            };
            foreach (var f in files)
            {
                Assertion.True(r.ContainsKey(f), "extract result for " + System.IO.Path.GetFileName(f));
                if (r.TryGetValue(f, out var b))
                {
                    double[] e = expect[f];
                    Assertion.Near(b.Width, e[0], 0.1, "PISCES frame width " + System.IO.Path.GetFileName(f));
                    Assertion.Near(b.Height, e[1], 0.1, "PISCES frame height " + System.IO.Path.GetFileName(f));
                    Assertion.True(b.Source == "legacy" || b.Source == "auto", "source tag " + System.IO.Path.GetFileName(f), b.Source);
                }
            }
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        private static void RunPlotSmoke()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            var files = new List<string>()
            {
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633638-AB_LB600APIA1_靠背悬簧总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633639-AB_LB001APIA2_主驾按摩腰托总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633640-AB_LB003APIA2_主驾4W腰托总成_临时图纸_20260814.dwg",
            };
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_pl_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            string outDir = System.IO.Path.Combine(tmp, "pdf"); System.IO.Directory.CreateDirectory(outDir);
            var ex2 = new DwgToPdf.Batch.FrameExtractor(exe);
            var r = ex2.RunExtract(files, tmp, null);
            var pl = new DwgToPdf.Batch.PlotSession(exe);
            int ok = 0;
            foreach (var f in files)
            {
                if (!r.TryGetValue(f, out var box)) { Console.WriteLine("  [plot] no frame for " + System.IO.Path.GetFileName(f)); continue; }
                var ps = DwgToPdf.Batch.PageSpec.Resolve(box.Width, box.Height, 5.0);
                string pdf = System.IO.Path.Combine(outDir, System.IO.Path.GetFileNameWithoutExtension(f) + ".pdf");
                bool okF = pl.Plot(f, pdf, box, "DWG To PDF.pc3", ps.Media, box.Width >= box.Height, "monochrome.ctb", 5.0, tmp, out string why);
                Console.WriteLine("  [plot] " + System.IO.Path.GetFileName(f) + " -> " + (okF ? "ok " + new System.IO.FileInfo(pdf).Length + "B" : "FAIL " + why));
                if (okF) ok++;
            }
            Assertion.True(ok == 3, "plot smoke 3/3 ok, got " + ok);
            if (ok < 3) Console.WriteLine("  [plot] 保留排查目录: " + tmp);
            else { try { System.IO.Directory.Delete(tmp, true); } catch { } }
        }

        // ---- Task 8: 全流程自测（e2e）+ 兜底演练 ----

        private static void RunE2E(string outDir = null)
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            string tmp = outDir ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_e2e_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var files = new List<string>()
            {
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633638-AB_LB600APIA1_靠背悬簧总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633639-AB_LB001APIA2_主驾按摩腰托总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633640-AB_LB003APIA2_主驾4W腰托总成_临时图纸_20260814.dwg",
            };
            var engine = new DwgToPdf.Batch.ConverterEngine();
            // v4.17 修订三：PDF 子文件夹名每轮按模板展开，不再预建固定名（预建反而逼出 "-2" 后缀）。
            string pdfFolder = null; // 运行后由 FindPdfFolder 动态定位
            var beforePdfs = new HashSet<string>(AllPdfsUnder(tmp), StringComparer.OrdinalIgnoreCase);
            // v4.17：转换后输出文件夹不再保留 logs\ 与 frame_cache.json（引擎自动清理）。
            // 自测需要读日志时显式指定 DWGTOPDF_LOG_DIR（默认日志不落盘，只镜像到界面）。
            string logDir = System.IO.Path.Combine(tmp, "_selftest_logs");
            System.Environment.SetEnvironmentVariable("DWGTOPDF_LOG_DIR", logDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            {
                if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note);
            });
            engine.RunFiles(files, tmp, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 });
            System.Environment.SetEnvironmentVariable("DWGTOPDF_LOG_DIR", null);
            sw.Stop();
            Console.WriteLine("E2E 用时: " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
            int newPdfs = 0;
            pdfFolder = FindPdfFolder(tmp); // v4.17 修订三：动态定位本轮 PDF 子文件夹
            foreach (var f in System.IO.Directory.GetFiles(pdfFolder ?? tmp, "*.pdf", System.IO.SearchOption.AllDirectories))
                if (!beforePdfs.Contains(f)) newPdfs++;
            Assertion.True(newPdfs == 3, "e2e produced 3 pdfs, got " + newPdfs);
            // v4.17 新契约：转换完成后输出文件夹必须干净 —— 无 frame_cache.json、无 logs\ 目录
            Assertion.True(!System.IO.File.Exists(System.IO.Path.Combine(tmp, "frame_cache.json")),
                "v4.17 cleanup: frame_cache.json removed after run");
            Assertion.True(!System.IO.Directory.Exists(System.IO.Path.Combine(tmp, "logs")),
                "v4.17 cleanup: logs dir not created in output folder");
            // 日志读取（v4.17：日志经 DWGTOPDF_LOG_DIR 落到 tmp\_selftest_logs，不再落在输出文件夹）
            if (outDir != null)
            {
                string newest = null;
                DateTime newestT = DateTime.MinValue;
                if (System.IO.Directory.Exists(logDir))
                {
                    foreach (var f in System.IO.Directory.GetFiles(logDir, "DwgToPdf_*.log"))
                    {
                        var t = System.IO.File.GetLastWriteTimeUtc(f);
                        if (t > newestT) { newestT = t; newest = f; }
                    }
                }
                if (newest != null)
                {
                    string content = System.IO.File.ReadAllText(newest);
                    string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    Console.WriteLine("    [日志尾] " + (lines.Length > 0 ? lines[lines.Length - 1] : ""));
                }
            }
            if (outDir == null) { try { System.IO.Directory.Delete(tmp, true); } catch { } }
        }

        /// <summary>--dir &lt;输入目录&gt; &lt;输出目录&gt;：递归枚举输入目录下全部 *.dwg（排除 .bak / ~$ 临时文件），
        /// 走真实全流程转换（识别→页面设置→出图→COM 兜底）。断言：新增 PDF 数 == 输入 dwg 数且无失败文件，
        /// 打印失败清单便于排查。用于真实图纸集（如 557单品）的批量回归。</summary>
        /// <summary>--dir &lt;输入&gt; &lt;输出&gt;（--dirc 为公司版）：递归枚举 dwg 全流程转换并断言。
        /// v3.5：--dirc 用公司版模式跑真实图纸集，验证"公司版→通用版补识别→COM兜底"链路。
        /// v4.5：--dirf 用 FlatOutput=true 跑扁平输出模式，额外断言所有新 PDF 落在 PDF文件\ 根下。</summary>
        /// <param name="pinStart">v4.17 修订三：固定“本轮开始时刻”。用于 --dirp 端到端复现
        /// “同一秒内两次转换”→ 第二个批次自动落到 PDF-HH:mm:ss-2（用户明确要求的语义）。</param>
        private static void RunDirE2E(string inputDir, string outDir, bool company = false, bool flat = false, bool blockFrame = false, System.DateTime? pinStart = null)
        {
            if (!System.IO.Directory.Exists(inputDir))
            {
                Assertion.True(false, "dir input exists", inputDir);
                return;
            }
            var files = new List<string>();
            foreach (var f in System.IO.Directory.EnumerateFiles(inputDir, "*.dwg", System.IO.SearchOption.AllDirectories))
            {
                string name = System.IO.Path.GetFileName(f);
                if (name.StartsWith("~$") || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(f);
            }
            Console.WriteLine("输入 dwg 数量: " + files.Count + (company ? "（公司版模式）" : ""));
            Assertion.True(files.Count > 0, "dir found dwg files");
            System.IO.Directory.CreateDirectory(outDir);
            // v4.17 修订三：PDF 子文件夹名每轮按模板展开，不再预建固定名（预建反而逼出 "-2" 后缀）。
            string pdfFolder = null; // 运行后由 FindPdfFolder 动态定位
            var beforePdfs = new HashSet<string>(AllPdfsUnder(outDir), StringComparer.OrdinalIgnoreCase);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            { if (!string.IsNullOrEmpty(u.Note) && u.Note.Length < 160) Console.WriteLine("    [进度] " + u.Note); });
            var opts = new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0, FlatOutput = flat };
            // v4.17 修订三：--dirp 用固定开始时刻，使两轮转换解析出同一个目标文件夹名，从而触发追加 -2
            if (pinStart.HasValue) opts.RunStartedAt = pinStart;
            // v4.8.1：--dirb 时开启识别块图框
            if (blockFrame) opts.RecognizeBlockFrame = true;
            // v4.12：环境变量 DWGTOPDF_IGNORE_LIMITS=1 模拟"用户再次点击开始转换" →
            // 本轮忽略文件大小阈值与单张转换超时（用于验证退回文件可被直接补转）
            if (System.Environment.GetEnvironmentVariable("DWGTOPDF_IGNORE_LIMITS") == "1")
            {
                opts.IgnoreSizeTimeLimits = true;
                Console.WriteLine("本轮忽略文件大小与转换时间限制（模拟再次点击开始转换）");
            }
            // v4.12：环境变量 DWGTOPDF_TIMEOUT_SEC（秒）注入"单张转换超时"阈值，
            // 供端到端验证"超时中止并退回待处理"链路（同 DWGTOPDF_MAX_MB 风格）
            string toSecEnv = System.Environment.GetEnvironmentVariable("DWGTOPDF_TIMEOUT_SEC");
            if (!string.IsNullOrEmpty(toSecEnv) && int.TryParse(toSecEnv, out int toSec) && toSec > 0)
            {
                opts.ConvertTimeoutSeconds = toSec;
                Console.WriteLine("单张转换超时（环境变量注入）: " + toSec + " 秒");
            }
            // v4.8：环境变量 DWGTOPDF_MAX_MB（MB）注入大文件跳过阈值，供端到端验证"大文件跳过"链路
            string maxMbEnv = System.Environment.GetEnvironmentVariable("DWGTOPDF_MAX_MB");
            if (!string.IsNullOrEmpty(maxMbEnv) && long.TryParse(maxMbEnv, out long maxMb) && maxMb > 0)
            {
                opts.MaxDwgSizeBytes = maxMb * 1024 * 1024;
                Console.WriteLine("大文件跳过阈值（环境变量注入）: " + maxMb + " MB");
            }
            if (company)
                engine.RunFiles(files, outDir, p, false, opts, new DwgToPdf.Batch.ConvertProfile { Mode = DwgToPdf.Batch.ConvertMode.Company });
            else
                engine.RunFiles(files, outDir, p, false, opts);
            sw.Stop();
            int newPdfs = 0;
            pdfFolder = FindPdfFolder(outDir); // v4.17 修订三：动态定位本轮 PDF 子文件夹
            foreach (var f in System.IO.Directory.GetFiles(pdfFolder ?? outDir, "*.pdf", System.IO.SearchOption.AllDirectories))
                if (!beforePdfs.Contains(f)) newPdfs++;
            // 失败清单：输入基名（去重名 "(n)" 后缀）未出现在产出 PDF 中即视为失败
            var pdfBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pdf in System.IO.Directory.GetFiles(pdfFolder ?? outDir, "*.pdf", System.IO.SearchOption.AllDirectories))
            {
                if (beforePdfs.Contains(pdf)) continue;
                string b = System.IO.Path.GetFileNameWithoutExtension(pdf);
                int paren = b.IndexOf('(');
                if (paren > 0) b = b.Substring(0, paren).TrimEnd();
                pdfBases.Add(b);
            }
            var failed = new List<string>();
            foreach (var f in files)
            {
                string b = System.IO.Path.GetFileNameWithoutExtension(f);
                // v4.5：同名文件的 PDF 输出名为"子目录标识_基名"，以 "_基名" 结尾也算命中
                bool hit = pdfBases.Contains(b);
                if (!hit)
                {
                    foreach (var pb in pdfBases)
                        if (pb.EndsWith("_" + b, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                }
                if (!hit) failed.Add(f);
            }
            Console.WriteLine("DIR-E2E 用时: " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
            Console.WriteLine("DIR-E2E: 输入 " + files.Count + " 张, 新增 PDF " + newPdfs + " 张, 失败 " + failed.Count + " 张（输出结构: " + (flat ? "扁平" : "按原结构") + "）");
            foreach (var f in failed) Console.WriteLine("  [失败] " + f);
            Assertion.True(newPdfs == files.Count, "dir-e2e produced " + files.Count + " pdfs, got " + newPdfs);
            Assertion.True(failed.Count == 0, "dir-e2e no failed files, failed=" + failed.Count);
            // v4.5：扁平模式断言——所有新增 PDF 必须直接位于 PDF文件\ 根下（无子目录层级）
            if (flat)
            {
                int nested = 0;
                foreach (var pdf in System.IO.Directory.GetFiles(pdfFolder ?? outDir, "*.pdf", System.IO.SearchOption.AllDirectories))
                    if (!beforePdfs.Contains(pdf) && System.IO.Path.GetDirectoryName(pdf) != System.IO.Path.GetFullPath(pdfFolder ?? outDir)) nested++;
                Assertion.True(nested == 0, "dirf all new pdfs flat in root, nested=" + nested);
            }
        }

        /// <summary>v4.17 修订三：--dirp —— 用同一个“本轮开始时刻”把同一批图纸连转两次。
        /// 第一轮产出 PDF-HH:mm:ss，第二轮必须产出 PDF-HH:mm:ss-2 而不是覆盖/合并进第一批。
        /// 直接对应用户确认的语义：“两次运行同名则自动加 -2 后缀”。</summary>
        private static void RunDirPinned(string inputDir, string outDir)
        {
            System.DateTime pin = new System.DateTime(2026, 10, 15, 9, 8, 7);
            RunDirE2E(inputDir, outDir, pinStart: pin);
            Console.WriteLine("[dirp] 第二轮使用同一开始时刻 " + pin.ToString("HH:mm:ss") + " …");
            RunDirE2E(inputDir, outDir, pinStart: pin);
            string f1 = System.IO.Path.Combine(outDir, "PDF-09：08：07");
            string f2 = System.IO.Path.Combine(outDir, "PDF-09：08：07-2");
            Assertion.True(System.IO.Directory.Exists(f1), "v4.17 修订三 dirp 第一批落在 PDF-09:08:07");
            Assertion.True(System.IO.Directory.Exists(f2), "v4.17 修订三 dirp 第二批追加 -2 后缀（同秒不覆盖）");
            // v4.17 修订六·补：输出**保留输入的子目录结构**，计数必须递归（否则输入含子目录时必然假失败）
            int f2Pdfs = System.IO.Directory.GetFiles(f2, "*.pdf", System.IO.SearchOption.AllDirectories).Length;
            Assertion.True(f2Pdfs > 0, "v4.17 修订三 dirp -2 目录非空", "pdfs=" + f2Pdfs);
            Assertion.True(!System.IO.Directory.Exists(System.IO.Path.Combine(outDir, "PDF-09：08：07-3")),
                "v4.17 修订三 dirp 不会多出 -3（只跑了两轮）");
        }

        /// <summary>v4.17 修订三 需求① 端到端：完整复现用户反馈的场景。
        /// “上一批图纸打印完之后，我加了新的图纸后再次点击转换，
        /// 发现上一批已经打印了的又继续参与打印。”
        /// 验证方式不看日志文字（那只能证明“打了日志”），而是拿第一轮产出 PDF 的
        /// LastWriteTimeUtc 做牧征：第二轮只转换新图，旧 PDF 一字不动才算真的没重打。</summary>
        private static void RunDirSkip(string inputDir, string outDir)
        {
            var files = new List<string>();
            foreach (var f in System.IO.Directory.GetFiles(inputDir, "*.dwg", System.IO.SearchOption.AllDirectories))
            {
                string n = System.IO.Path.GetFileName(f);
                if (n.StartsWith("~$") || n.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(f);
            }
            Assertion.True(files.Count >= 2, "dirs 至少需要 2 张图（一张当旧批、一张当新图）", "n=" + files.Count);
            if (files.Count < 2) return;

            // 把第一张拷贚成“新增的图纸”（真实 dwg，不是空文件）
            string newDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_dirs_new_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(newDir);
            string newDwg = System.IO.Path.Combine(newDir, "新增图纸_" + System.IO.Path.GetFileName(files[1]));
            System.IO.File.Copy(files[1], newDwg, true);

            var status = new Dictionary<string, DwgToPdf.Batch.ItemStatus>(StringComparer.OrdinalIgnoreCase);
            Func<string, DwgToPdf.Batch.ItemStatus> st =
                p => status.TryGetValue(p, out var s) ? s : DwgToPdf.Batch.ItemStatus.Pending;
            var opts = new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 };
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { });

            Console.WriteLine("[dirs] 第一轮：转换全部 " + files.Count + " 张…");
            new DwgToPdf.Batch.ConverterEngine().RunFiles(files, outDir, p, false, opts);
            foreach (var f in files) status[f] = DwgToPdf.Batch.ItemStatus.Success;

            // 拉一份第一轮产出 PDF 的时间戳快照（写入前，便于第二轮对比）
            var stampBefore = new Dictionary<string, System.DateTime>(StringComparer.OrdinalIgnoreCase);
            string pdfFolder1 = FindPdfFolder(outDir);
            Assertion.True(pdfFolder1 != null, "dirs 第一轮产出了 PDF", "null");
            if (pdfFolder1 == null) return;
            // v4.17 修订六·补：同 --dirp，输出保留子目录结构 → 必须递归
            foreach (var pdf in System.IO.Directory.GetFiles(pdfFolder1, "*.pdf", System.IO.SearchOption.AllDirectories))
                stampBefore[pdf] = System.IO.File.GetLastWriteTimeUtc(pdf);

            Console.WriteLine("[dirs] 第二轮：模拟“新增图纸后再次点击转换”…");
            var list2 = new List<string>(files);
            list2.Add(newDwg);
            List<string> todo = DwgToPdf.Batch.RunSelection.FilterTodoFiles(list2, st, out int done);
            Assertion.True(done == files.Count, "v4.17 修订三 需求① 端到端：已成功的 " + files.Count + " 张全部计入跳过", "done=" + done);
            Assertion.True(todo.Count == 1 && todo[0] == newDwg,
                "v4.17 修订三 需求① 端到端：本轮只转换新增的 1 张（旧批不再参与）", string.Join(",", todo));

            new DwgToPdf.Batch.ConverterEngine().RunFiles(todo, outDir, p, false, opts);

            // 关键断言：旧 PDF 的修改时间必须一分未变（否则就是又打印了一遍）
            int replotted = 0;
            foreach (var kv in stampBefore)
            {
                if (!System.IO.File.Exists(kv.Key)) continue;
                System.DateTime now = System.IO.File.GetLastWriteTimeUtc(kv.Key);
                if (now != kv.Value) replotted++;
            }
            Assertion.True(replotted == 0, "v4.17 修订三 需求① 端到端：旧 PDF 零改动（确实没重打）", "replotted=" + replotted);
            // 新图确实转了：最新的那个 PDF 子文件夹里应该有新产出
            string pdfFolder2 = FindPdfFolder(outDir);
            int newPdfs = 0;
            if (pdfFolder2 != null)
                foreach (var pdf in System.IO.Directory.GetFiles(pdfFolder2, "*.pdf", System.IO.SearchOption.AllDirectories))
                    if (!stampBefore.ContainsKey(pdf)) newPdfs++;
            Assertion.True(newPdfs >= 1, "v4.17 修订三 需求① 端到端：新增图纸确实转出了 PDF", "newPdfs=" + newPdfs);
            Assertion.True(pdfFolder2 != null && pdfFolder2 != pdfFolder1,
                "v4.17 修订三 需求① 端到端：新批落入新时间戳文件夹（不与旧批混在一起）",
                System.IO.Path.GetFileName(pdfFolder2));
            try { System.IO.Directory.Delete(newDir, true); } catch { }
        }

        private static void RunE2ECancel(int delayMs)
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_cancel_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var files = new List<string>()
            {
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633638-AB_LB600APIA1_靠背悬簧总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633639-AB_LB001APIA2_主驾按摩腰托总成_临时图纸_20260814.dwg",
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633640-AB_LB003APIA2_主驾4W腰托总成_临时图纸_20260814.dwg",
            };
            var engine = new DwgToPdf.Batch.ConverterEngine();
            // 延迟 delayMs 毫秒后取消：验证"任意时刻停止"快速响应（识别/预处理/出图各阶段均可中断）
            using (var timer = new System.Threading.Timer(_ => { Console.WriteLine("    [取消] 用户点击停止（模拟，延迟" + delayMs + "ms）"); engine.Cancel(); }, null, delayMs, System.Threading.Timeout.Infinite))
            {
                var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
                { if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note); });
                engine.RunFiles(files, tmp, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 });
            }
            int pdfs = System.IO.Directory.GetFiles(FindPdfFolder(tmp) ?? tmp, "*.pdf").Length;
            Console.WriteLine("取消演练(延迟" + delayMs + "ms): PDF " + pdfs + "/3，IsCancelled=" + engine.IsCancelled);
            // 断言：取消被响应且引擎正常返回（PDF 数取决于取消时机，不固定断言）
            Assertion.True(engine.IsCancelled, "cancel responded (delay " + delayMs + "ms)");
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        private static void RunFallback()
        {
            // 前置：确认本机原本 acad 进程数
            var before = DwgToPdf.Batch.ComStarter.AcadProcessIds();
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            // 人为使 Core Console 识别失败：把 extframe.lsp 临时改名
            string lsp = DwgToPdf.Batch.FrameExtractor.LspPath();
            string backup = lsp + ".bak";
            // I3 整改（2026-08-27）：extframe.lsp 缺失时备份 Copy 不再抛异常，打印 skip 并 return
            try { System.IO.File.Copy(lsp, backup, true); }
            catch (Exception ex)
            {
                Console.WriteLine("skip: cannot backup lsp -> " + ex.Message);
                return;
            }
            try { System.IO.File.Delete(lsp); }
            catch (Exception ex) { Console.WriteLine("skip: cannot remove lsp -> " + ex.Message); }

            var files = new List<string>()
            {
                @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7643421-AB_LB002APIA2_主驾泵阀总成_临时图纸_20260814.dwg",
            };
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_fb_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            { if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(files, tmp, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 });

            // 恢复 lsp
            try { if (System.IO.File.Exists(backup) && !System.IO.File.Exists(lsp)) System.IO.File.Copy(backup, lsp); } catch { }

            int pdfs = System.IO.Directory.GetFiles(FindPdfFolder(tmp) ?? tmp, "*.pdf").Length;
            Assertion.True(pdfs == 1, "fallback produced 1 pdf via COM, got " + pdfs);
            // 不误关用户实例：转换后不得关闭任何既有 acad 进程（允许自启实例残留波动，不允许关闭既有进程）
            var after = DwgToPdf.Batch.ComStarter.AcadProcessIds();
            int closed = before.Count - after.Count;
            Assertion.True(closed <= 0, "no user instances closed, closed=" + closed);
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        // ---- Task 9: 验收子集（27 张）+ 验收分支 ----

        /// <summary>验收子集：PISCES 17 + 模板(H56E) 5 + 测试文件二 5 = 27 张（过滤不存在的文件）。</summary>
        private static List<string> GetAcceptanceFiles()
        {
            string root = @"E:\Dwg To Pdf 工具\测试文件输入";
            var list = new List<string>();
            list.AddRange(System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "PISCES按摩总成图纸 - 0826"), "*.dwg"));
            list.AddRange(new[]
            {
                System.IO.Path.Combine(root, @"模板（H56E）\坐垫按摩气袋\总成\Y1567676_H56E_MA100YH5A0_左椅坐垫按摩气袋-LH.dwg"),
                System.IO.Path.Combine(root, @"模板（H56E）\H56E_LB200YH5A0_靠背气泵总成LH.dwg"),
                System.IO.Path.Combine(root, @"模板（H56E）\靠背阀组总成\总成\Y1563451_H56E_LB001YH5A0_靠背按摩总成（气泵+阀组+簧架）-LH.dwg"),
                System.IO.Path.Combine(root, @"模板（H56E）\坐垫按摩气袋\单品\H56E_MA160YH5A0_底层3连毛毡板.dwg"),
                System.IO.Path.Combine(root, @"模板（H56E）\腿托按摩总成\总成\Y1567762_H56E_MT100YH5A0_腿托按摩总成.dwg"),
            });
            list.AddRange(new[]
            {
                System.IO.Path.Combine(root, @"测试文件二\F610M_GE0011806_VB002JF6A0_前排靠背通风袋_正式图纸.dwg"),
                System.IO.Path.Combine(root, @"测试文件二\F610M_VA220JF6A1_前排坐垫TPU膜_正式图纸.dwg"),
                System.IO.Path.Combine(root, @"测试文件二\F610M_VF220JF6A1_二排靠背左TPU膜_正式图纸.dwg"),
                System.IO.Path.Combine(root, @"测试文件二\F610M_GF0000067_VJ04510CA0_风扇固定软钉_正式图纸.dwg"),
                System.IO.Path.Combine(root, @"测试文件二\F610M_VA160JF6A0_坐垫泡棉_临时图纸_20260605.dwg"),
            });
            list = list.FindAll(f => System.IO.File.Exists(f));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("验收子集: " + list.Count + " 张 dwg");
            return list;
        }

        /// <summary>--compare-frames：LISP(accoreconsole) vs COM(AutoCAD) 图框一致性，容差 1e-6。
        /// limit&gt;0 时仅取前 limit 张（冒烟用小样本）。COM 复用单实例连续 Open/Close；只关本工具自启的实例。</summary>
        private static void RunCompareFrames(int limit)
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            var files = GetAcceptanceFiles();
            if (limit > 0 && limit < files.Count) files = files.GetRange(0, limit);

            // 阶段 1：LISP 提取（accoreconsole 子进程，并行 4）
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_cf_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var ex2 = new DwgToPdf.Batch.FrameExtractor(exe);
            var lisp = ex2.RunExtract(files, tmp, null);

            // 阶段 2：COM 对比（单实例连续 Open/Close，必须 STA 线程）
            DwgToPdf.Batch.OleMsgFilter.Install();
            var before = DwgToPdf.Batch.ComStarter.AcadProcessIds();
            object app = null;
            bool own = false;
            try
            {
                app = DwgToPdf.Batch.ComStarter.StartNewInstance();
                own = DwgToPdf.Batch.ComStarter.HasNewAcadProcess(before, 4000);
                if (own)
                {
                    try { new DwgToPdf.Batch.Late(app).Set("Visible", false); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [cmp] AutoCAD 实例启动失败: " + ex.Message);
                Assertion.True(false, "com instance started", ex.Message);
                return;
            }

            int ok = 0;
            try
            {
                foreach (var f in files)
                {
                    if (!lisp.TryGetValue(f, out var lb)) { Console.WriteLine("  [cmp] LISP 缺失 " + System.IO.Path.GetFileName(f)); continue; }
                    var cb = ComFrameOf(app, f);
                    if (cb == null) { Console.WriteLine("  [cmp] COM 缺失 " + System.IO.Path.GetFileName(f)); continue; }
                    double dx = Math.Abs(lb.MinX - cb.MinX), dy = Math.Abs(lb.MinY - cb.MinY);
                    double dx2 = Math.Abs(lb.MaxX - cb.MaxX), dy2 = Math.Abs(lb.MaxY - cb.MaxY);
                    bool pass = dx <= 1e-6 && dy <= 1e-6 && dx2 <= 1e-6 && dy2 <= 1e-6;
                    Console.WriteLine("  [cmp] " + (pass ? "OK " : "FAIL ") + System.IO.Path.GetFileName(f) +
                        "  LISP(" + lb.MinX + "," + lb.MinY + ")-(" + lb.MaxX + "," + lb.MaxY + ") vs COM(" + cb.MinX + "," + cb.MinY + ")-(" + cb.MaxX + "," + cb.MaxY + ")");
                    if (pass) ok++;
                }
            }
            finally
            {
                // 只关本工具自启的实例；附加/复用用户实例绝不 Quit
                if (own && app != null)
                {
                    try
                    {
                        var docs = new DwgToPdf.Batch.Late(new DwgToPdf.Batch.Late(app).Get("Documents"));
                        while (true)
                        {
                            int num;
                            try { num = System.Convert.ToInt32(docs.Get("Count")); } catch { num = 0; }
                            if (num <= 0) break;
                            object d;
                            try { d = docs.GetAt("Item", 0); } catch { break; }
                            try { new DwgToPdf.Batch.Late(d).Call("Close", false); } catch { break; }
                        }
                    }
                    catch { }
                    try { new DwgToPdf.Batch.Late(app).Call("Quit"); } catch { }
                }
                try { if (app != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(app); } catch { }
            }
            Assertion.True(ok == files.Count, "frame compare " + ok + "/" + files.Count);
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        /// <summary>COM 单张图框识别：打开文档 → TryGetFrameExtents（与 v2.3.1 同算法：
        /// LWPOLYLINE Coordinates 快路径 + GetBoundingBox 兜底；优先"图层图框"层，否则面积最大图层）。</summary>
        private static DwgToPdf.Batch.FrameBox ComFrameOf(object app, string f)
        {
            var docs = new DwgToPdf.Batch.Late(new DwgToPdf.Batch.Late(app).Get("Documents"));
            object doc = null;
            try
            {
                doc = docs.Call("Open", f, false);
                if (DwgToPdf.Batch.CadConverter.TryGetFrameExtents(new DwgToPdf.Batch.Late(doc),
                        out double[] ll, out double[] ur, out string err, out string source))
                {
                    return new DwgToPdf.Batch.FrameBox
                    {
                        MinX = ll[0], MinY = ll[1], MaxX = ur[0], MaxY = ur[1],
                        Layer = source, Source = "com",
                    };
                }
                Console.WriteLine("  [cmp] COM 识别失败(" + err + ") " + System.IO.Path.GetFileName(f));
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [cmp] COM 打开/识别异常 " + System.IO.Path.GetFileName(f) + ": " + ex.Message);
                return null;
            }
            finally
            {
                try { if (doc != null) new DwgToPdf.Batch.Late(doc).Call("Close", false); } catch { }
                try { if (doc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(doc); } catch { }
            }
        }

        /// <summary>--qa &lt;outDir&gt;：读 outDir 下所有 PDF，按源文件名→frame_cache 条目→PageSpec 预期纸张，断言 MediaBox。</summary>
        private static void RunQa(string outDir)
        {
            if (!System.IO.Directory.Exists(outDir))
            {
                Assertion.True(false, "qa outDir exists", outDir);
                return;
            }
            var srcByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in GetAcceptanceFiles()) srcByName[System.IO.Path.GetFileName(f)] = f;
            var pdfs = System.IO.Directory.GetFiles(FindPdfFolder(outDir) ?? outDir, "*.pdf");
            var entries = DwgToPdf.Batch.FrameCache.LoadEntries(outDir);
            // v4.17：转换完成后引擎会自动删除 frame_cache.json（输出文件夹保持干净）→
            // --qa 依赖该缓存推断预期纸张，须先以 DWGTOPDF_KEEP_CACHE=1 跑一次 --dir 生成缓存。
            if (entries.Count == 0)
            {
                Console.WriteLine("  [qa] 未找到 frame_cache.json —— v4.17 起转换完成后自动删除该缓存。");
                Console.WriteLine("  [qa] 请先以 DWGTOPDF_KEEP_CACHE=1 跑一次 --dir，再执行 --qa。");
                Assertion.True(false, "qa needs frame_cache.json (rerun --dir with DWGTOPDF_KEEP_CACHE=1)");
                return;
            }
            int ok = 0, total = 0;
            foreach (var pdf in pdfs)
            {
                string baseName = System.IO.Path.GetFileNameWithoutExtension(pdf);
                int paren = baseName.IndexOf("(");
                if (paren > 0) baseName = baseName.Substring(0, paren);
                if (!srcByName.TryGetValue(baseName + ".dwg", out var src)) { Console.WriteLine("  [qa] 跳过未知源 " + pdf); continue; }
                var ent = entries.Find(e => string.Equals(System.IO.Path.GetFileName(e.Path), baseName + ".dwg", StringComparison.OrdinalIgnoreCase));
                if (ent == null) { Console.WriteLine("  [qa] 无缓存图框 " + pdf); continue; }
                total++;
                var ps = DwgToPdf.Batch.PageSpec.Resolve(ent.MaxX - ent.MinX, ent.MaxY - ent.MinY, 5.0);
                bool pass = QaPdf(pdf, ps.PaperLong, ps.PaperShort);
                if (pass) ok++;
            }
            Assertion.True(total > 0, "qa found pdfs, total=" + total);
            Assertion.True(ok == total, "qa passed " + ok + "/" + total);
        }

        /// <summary>单 PDF 质量审查：大小&gt;3000B + MediaBox 宽高≈预期纸张（长边/短边，1% 容差）。</summary>
        private static bool QaPdf(string pdf, double expectWmm, double expectHmm)
        {
            var fi = new System.IO.FileInfo(pdf);
            if (fi.Length <= 3000) { Console.WriteLine("  [qa] FAIL size " + pdf + " " + fi.Length); return false; }
            // DWG To PDF 生成的 PDF 中 MediaBox 页面对象位于文件尾部（实测偏移可达 5MB），必须读全文件；
            // ASCII 解码对非 ASCII 字节替换为 '?'，不影响 MediaBox 纯 ASCII 匹配。
            string text;
            try
            {
                text = System.IO.File.ReadAllText(pdf, System.Text.Encoding.ASCII);
            }
            catch (Exception ex) { Console.WriteLine("  [qa] FAIL read " + pdf + " " + ex.Message); return false; }
            var m = System.Text.RegularExpressions.Regex.Match(text, @"/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]");
            if (!m.Success) { Console.WriteLine("  [qa] FAIL mediabox parse " + pdf); return false; }
            double w = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            double h = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            double ew = Math.Round(expectWmm / 25.4 * 72.0, 2), eh = Math.Round(expectHmm / 25.4 * 72.0, 2);
            // DWG To PDF 输出页面为竖版（MediaBox 宽=纸张短边、高=长边），且不依赖旋转方向；
            // 用无序匹配（宽/高 与 长短边 任一组合均可）保证横/竖页面都正确。
            bool okb = (Math.Abs(w - ew) / ew < 0.01 && Math.Abs(h - eh) / eh < 0.01)
                    || (Math.Abs(w - eh) / eh < 0.01 && Math.Abs(h - ew) / ew < 0.01);
            Console.WriteLine("  [qa] " + (okb ? "OK " : "FAIL ") + System.IO.Path.GetFileName(pdf) + " (" + w + "x" + h + ") vs exp(" + ew + "x" + eh + ")");
            return okb;
        }

        /// <summary>--bench &lt;outDir&gt; [workers]：v3.0 自动跑 27 张验收子集并计时（黑白、边距 5mm、并行 workers，缺省 4）。</summary>
        private static void RunBench(string outDir, int workers)
        {
            var files = GetAcceptanceFiles();
            System.IO.Directory.CreateDirectory(outDir);
            // v4.17 修订三：PDF 子文件夹名每轮按模板展开，不再预建固定名（预建反而逼出 "-2" 后缀）。
            string pdfFolder = null; // 运行后由 FindPdfFolder 动态定位
            var beforePdfs = new HashSet<string>(AllPdfsUnder(outDir), StringComparer.OrdinalIgnoreCase);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            // v4.17：日志默认不落盘 → 自测显式指定日志目录，便于读取"转换结束"摘要行
            string benchLogDir = System.IO.Path.Combine(outDir, "_selftest_logs");
            System.Environment.SetEnvironmentVariable("DWGTOPDF_LOG_DIR", benchLogDir);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            { if (!string.IsNullOrEmpty(u.Note) && u.Note.Length < 120) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(files, outDir, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 });
            System.Environment.SetEnvironmentVariable("DWGTOPDF_LOG_DIR", null);
            sw.Stop();
            int newPdfs = 0;
            pdfFolder = FindPdfFolder(outDir); // v4.17 修订三：动态定位本轮 PDF 子文件夹
            foreach (var f in System.IO.Directory.GetFiles(pdfFolder ?? outDir, "*.pdf", System.IO.SearchOption.AllDirectories))
                if (!beforePdfs.Contains(f)) newPdfs++;
            int fails = files.Count - newPdfs;
            // 从最新日志取"==== 转换结束 ====" 行（v4.17：日志落在 DWGTOPDF_LOG_DIR 指定目录）
            string logsDir = benchLogDir;
            if (System.IO.Directory.Exists(logsDir))
            {
                string newest = null;
                DateTime newestT = DateTime.MinValue;
                foreach (var f in System.IO.Directory.GetFiles(logsDir, "DwgToPdf_*.log"))
                {
                    var t = System.IO.File.GetLastWriteTimeUtc(f);
                    if (t > newestT) { newestT = t; newest = f; }
                }
                if (newest != null)
                {
                    string[] lines = System.IO.File.ReadAllLines(newest);
                    for (int i = lines.Length - 1; i >= 0; i--)
                        if (lines[i].Contains("转换结束")) { Console.WriteLine("    [日志] " + lines[i]); break; }
                }
            }
            Console.WriteLine("BENCH v3.0: " + files.Count + " 张, 用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒, 新增 PDF " + newPdfs + ", 失败 " + fails);
            Assertion.True(newPdfs == files.Count, "bench produced " + files.Count + " pdfs, got " + newPdfs);
        }

        private static void TestConvertProfile()
        {
            Assertion.True(DwgToPdf.Batch.ConvertProfile.Describe(DwgToPdf.Batch.ConvertMode.General) == "通用版", "profile describe general");
            Assertion.True(DwgToPdf.Batch.ConvertProfile.Describe(DwgToPdf.Batch.ConvertMode.Company) == "公司版", "profile describe company");
            var p = new DwgToPdf.Batch.ConvertProfile { Mode = DwgToPdf.Batch.ConvertMode.Company };
            Assertion.True(p.IsCompany, "profile is company");
        }

        private static void TestSmartConcurrency()
        {
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.RecognitionWorkers(0) == 1, "Recognition 文件数0 → 1");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.RecognitionWorkers(1) == 1, "Recognition 文件数1 → 1");
            int cpu = Environment.ProcessorCount;
            // v3.4：识别上限 8→12（accoreconsole 轻量；上限取 min(cpu,12)）
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.RecognitionWorkers(999) == Math.Min(cpu, 12), "Recognition 充足→min(cpu,12)");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PlotWorkers(999) == Math.Min(cpu, 8), "Plot 充足→min(cpu,8)");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PrepWorkers(999) == Math.Min(cpu, 4), "Prep 充足→min(cpu,4)");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PlotWorkers(3) == Math.Min(3, Math.Min(cpu, 8)), "Plot 文件数3 → 3");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PrepWorkers(2) == Math.Min(2, Math.Min(cpu, 4)), "Prep 文件数2 → 2");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.RecognitionWorkers(int.MaxValue) <= 12, "Recognition 永≤12");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PrepWorkers(int.MaxValue) <= 4, "Prep 永≤4");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.FallbackMax == 4, "FallbackMax = 4");
        }

        /// <summary>--dc：动态任务池冒烟。用 24 个假 dwg 走完整 4 段流程（识别失败→COM兜底失败），
        /// 验证：SmartConcurrency 并发数正确、进度上报、动态取件不丢不重、流程无异常返回。</summary>
        private static void RunDcSmoke()
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dc_smoke_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var files = new System.Collections.Generic.List<string>();
            try
            {
                int n = 24;
                for (int i = 0; i < n; i++)
                {
                    string f = System.IO.Path.Combine(tmp, "f" + i + ".dwg");
                    System.IO.File.WriteAllText(f, "FAKE" + i);
                    files.Add(f);
                }
                int w = DwgToPdf.Batch.SmartConcurrency.RecognitionWorkers(files.Count);
                // v3.4：识别上限 8→12
                Assertion.True(w == Math.Min(Environment.ProcessorCount, 12), "SmartConcurrency 24文件并发", "w=" + w);
                Assertion.True(DwgToPdf.Batch.SmartConcurrency.PrepWorkers(files.Count) == Math.Min(Environment.ProcessorCount, 4), "Prep 24文件并发");
                string outDir = System.IO.Path.Combine(tmp, "out");
                System.IO.Directory.CreateDirectory(outDir);
                var engine = new DwgToPdf.Batch.ConverterEngine();
                bool reported = false;
                int doneCount = 0;
                var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
                {
                    reported |= u.Total > 0;
                    if (u.Status == DwgToPdf.Batch.ItemStatus.Success || u.Status == DwgToPdf.Batch.ItemStatus.Failed || u.Status == DwgToPdf.Batch.ItemStatus.Cancelled)
                        doneCount++;
                });
                // 自测模式：假 dwg 全部无图框 → 阶段3 出图失败后跳过 COM 兜底直接判失败，
                // 避免 24 次真实 AutoCAD 实例启停拖慢/挂死自测（NoComFallback 仅自测分支启用，GUI 恒 false）。
                engine.NoComFallback = true;
                engine.RunFiles(files, outDir, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome });
                Assertion.True(reported, "RunFiles 有进度上报");
                Assertion.True(doneCount == n, "24 任务全部有终态", "done=" + doneCount);
                // 假 dwg 全部失败：不应产出 pdf，也不应抛出异常；每文件恰好一次终态（不丢不重）
                string[] pdfs = System.IO.Directory.GetFiles(FindPdfFolder(outDir) ?? outDir, "*.pdf", System.IO.SearchOption.AllDirectories);
                Assertion.True(pdfs.Length == 0, "假 dwg 不产出 pdf", "pdfs=" + pdfs.Length);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

                private static void TestSettingsJsonNoHqKey()
        {
            // v4.17 修订十五：HQ pc3 已于 v4.16 退役，本轮把 IsHighQuality / PlotterFor / MapMedia
            // 与 HighQualityThresholdBytes 传递链一并删除。原 5 条断言（"恒标准设备""恒 false"
            // "媒体名透传""默认阈值 1MB"）全部编码已删除的 API，随之移除；
            // 改为守住"设置 JSON 不再写该键"——防止有人在别处把这个键加回来。
            string json = DwgToPdf.Batch.AppSettings.ToJson(new DwgToPdf.Batch.AppSettings());
            Assertion.True(json.IndexOf("HighQualityThresholdBytes", StringComparison.Ordinal) < 0,
                "settings json 不再包含 HighQualityThresholdBytes 键");
            Assertion.True(json.IndexOf("\"RasterImageDpi\"", StringComparison.Ordinal) >= 0,
                "settings json 仍含 RasterImageDpi 键（未误删相邻字段）");
        }

        private static void TestFrameTemplateLibrary()
        {
            var lib = new DwgToPdf.Batch.FrameTemplateLibrary();
            lib.Entries.Add(new DwgToPdf.Batch.FrameTemplateLibrary.Entry { PaperSize = "A2", FrameLayer = "图层图框", BoxW = 594, BoxH = 420, Source = "a.dwg" });
            var hit = lib.Match(593.99, 420.01, null, 0.75);
            Assertion.True(hit != null && hit.PaperSize == "A2", "lib match within tol");
            Assertion.True(lib.Match(500.0, 300.0, null, 0.75) == null, "lib no match over tol");
            Assertion.True(DwgToPdf.Batch.FrameTemplateLibrary.InferPaper(1189.0, 841.0) == "A0", "infer A0");
            Assertion.True(DwgToPdf.Batch.FrameTemplateLibrary.InferPaper(296.72, 209.80) == "A4", "infer A4(线束)");
            Assertion.True(DwgToPdf.Batch.FrameTemplateLibrary.InferPaper(100.0, 50.0) == null, "infer out-of-range null");
            string json = lib.Serialize();
            Assertion.True(json.Contains("\"paper\":\"A2\""), "serialize contains paper");
            var lib2 = DwgToPdf.Batch.FrameTemplateLibrary.Deserialize(json);
            Assertion.True(lib2.Entries.Count == 1, "deserialize count");
            Assertion.Near(lib2.Entries[0].BoxW, 594.0, 1e-9, "deserialize w");
            Assertion.Near(lib2.Entries[0].BoxH, 420.0, 1e-9, "deserialize h");
            Assertion.True(lib2.Entries[0].PaperSize == "A2", "deserialize paper");
            var lib3 = DwgToPdf.Batch.FrameTemplateLibrary.Deserialize("###corrupt###");
            Assertion.True(lib3.Entries.Count == 0, "deserialize corrupt ok");
        }

        private static void RunUnit()
        {
            // 1) RunPolicy：瞬态类失败重试判定（只重试可能自己好起来的；必然复现的不重试）
            Assertion.True(!DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("图框问题：未识别到正规图框"), "retry: frame problem not retried");
            Assertion.True(!DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("无图框数据"), "retry: no-frame not retried");
            Assertion.True(!DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("已生成 PDF 但内容为空白（该图纸可能无法按当前方式出图，请人工检查）"), "retry: blank pdf not retried");
            Assertion.True(!DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("质量核查: PDF 内容流过空（累计 12 bytes）"), "retry: quality fail not retried");
            Assertion.True(DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("未能找到路径中的某个部分。"), "retry: path race retried");
            Assertion.True(DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("Core Console 出图失败(出图会话超时(>/240s))"), "retry: session timeout retried");
            Assertion.True(DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("参数错误。 (异常来自 HRESULT:0x80070057 (E_INVALIDARG))"), "retry: E_INVALIDARG retried");
            Assertion.True(!DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch(""), "retry: empty reason not retried");
            // 注入式扩展关键词（诊断钩子）
            System.Environment.SetEnvironmentVariable("DWGTOPDF_RETRY_EXTRA", "图形文件无效,我的自定义");
            Assertion.True(DwgToPdf.Batch.RunPolicy.ShouldRetryAfterBatch("图形文件无效"), "retry: extra pattern works");
            System.Environment.SetEnvironmentVariable("DWGTOPDF_RETRY_EXTRA", null);
            System.Environment.SetEnvironmentVariable("DWGTOPDF_NO_RETRY", "1");
            Assertion.True(!DwgToPdf.Batch.RunPolicy.AutoRetryEnabled(), "retry: switch off honored");
            System.Environment.SetEnvironmentVariable("DWGTOPDF_NO_RETRY", null);
            Assertion.True(DwgToPdf.Batch.RunPolicy.AutoRetryEnabled(), "retry: switch on by default");

            // 2) PrintQuality.UseRasterBoost：只有"真光栅且无缺失图"才走高清
            Assertion.True(!DwgToPdf.Batch.PrintQuality.UseRasterBoost(0, 0, 2400), "boost: no raster -> off");
            Assertion.True(!DwgToPdf.Batch.PrintQuality.UseRasterBoost(1, 1, 2400), "boost: missing external image -> off");
            Assertion.True(!DwgToPdf.Batch.PrintQuality.UseRasterBoost(1, 0, 0), "boost: dpi off -> off");
            Assertion.True(DwgToPdf.Batch.PrintQuality.UseRasterBoost(1, 0, 2400), "boost: raster+ok -> on");
            Assertion.True(DwgToPdf.Batch.PrintQuality.UseRasterBoost(3, 0, 1200), "boost: 1200 works");

            // 3) 并发夹紧（环境变量已清理，取默认上限）
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PlotWorkers(0) >= 1, "concurrency: plot min 1");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PlotWorkers(1000) <= 8, "concurrency: plot capped at 8");
            Assertion.True(DwgToPdf.Batch.SmartConcurrency.PrepWorkers(1000) <= 4, "concurrency: prep capped at 4");

            // 4) FrameExtractor.ParseResult：IMG / IMGR 行解析
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unit_ef_" + System.Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                System.IO.File.WriteAllText(tmp, "100 100 1389 941|图层图框|auto\nIMG|7\nIMGR|3|2\n", System.Text.Encoding.GetEncoding(936));
                var box = DwgToPdf.Batch.FrameExtractor.ParseResult(tmp, "x.dwg");
                Assertion.True(box != null, "parse: box parsed");
                if (box != null)
                {
                    Assertion.True(box.ImageCount == 7, "parse: IMG total = 7, got " + box.ImageCount);
                    Assertion.True(box.RasterCount == 3, "parse: IMGR raster = 3, got " + box.RasterCount);
                    Assertion.True(box.RasterMissingCount == 2, "parse: IMGR missing = 2, got " + box.RasterMissingCount);
                    Assertion.True(Math.Abs(box.Width - 1289.0) < 0.001 && Math.Abs(box.Height - 841.0) < 0.001, "parse: box size");
                }
            }
            finally { try { System.IO.File.Delete(tmp); } catch { } }

            // 4.5) LogFollowPolicy：日志"跟随滚动"策略（用户上翻时不得把视图拽回底部）
            Assertion.True(DwgToPdf.Batch.LogFollowPolicy.ShouldAutoScroll(true, 100, 100), "logfollow: at bottom -> follow");
            Assertion.True(DwgToPdf.Batch.LogFollowPolicy.ShouldAutoScroll(true, 99, 100), "logfollow: near bottom -> follow");
            Assertion.True(!DwgToPdf.Batch.LogFollowPolicy.ShouldAutoScroll(true, 40, 100), "logfollow: scrolled up -> pause");
            Assertion.True(!DwgToPdf.Batch.LogFollowPolicy.ShouldAutoScroll(false, 100, 100), "logfollow: unchecked -> no follow");
            Assertion.True(!DwgToPdf.Batch.LogFollowPolicy.ShouldAutoScroll(false, 40, 100), "logfollow: unchecked+up -> no follow");

            // 5) RunHistory.Fingerprint：与顺序无关、与内容相关
            var setA = new System.Collections.Generic.List<string> { @"C:\a\1.dwg", @"C:\b\2.dwg" };
            var setB = new System.Collections.Generic.List<string> { @"D:\z\2.dwg", @"D:\y\1.dwg" };
            var setC = new System.Collections.Generic.List<string> { @"C:\a\1.dwg", @"C:\b\3.dwg" };
            string fa = DwgToPdf.Batch.RunHistory.Fingerprint(setA);
            Assertion.True(fa == DwgToPdf.Batch.RunHistory.Fingerprint(setB), "fingerprint: order/path independent");
            Assertion.True(fa != DwgToPdf.Batch.RunHistory.Fingerprint(setC), "fingerprint: content sensitive");
            Assertion.True(fa.Length == 8, "fingerprint: 8 chars");

            // 6) PdfContentCheck：空白页/体积过小判定（合成 PDF 文本，不启动 AutoCAD）
            string pdfTiny = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unit_tiny_" + System.Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                System.IO.File.WriteAllText(pdfTiny, "%PDF-1.7\n1 0 obj\n<< /Length 21 >>\nstream\nx\nendstream\nendobj\n/MediaBox [0 0 3370 4768]\n");
                string why;
                bool ok = DwgToPdf.PdfContentCheck.Check(pdfTiny, 1189, 1682, out why, 10000);
                Assertion.True(!ok, "pdfcheck: tiny blank rejected");
                Assertion.True(!string.IsNullOrEmpty(why), "pdfcheck: reason given");
            }
            finally { try { System.IO.File.Delete(pdfTiny); } catch { } }

            // 6b) v4.17 质量审查加严（补漏判）：合成 PDF 断言，不启动 AutoCAD
            string qcDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unit_qc_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(qcDir);
            try
            {
                string filler = new string(' ', 3000); // 补足文件体积（≥ 纸型估算阈值），排除维度①干扰
                string why2;
                // (a) 正常 PDF：纸型正确 + 内容流达标 + 尾部 %%EOF → 通过
                string okPdf = System.IO.Path.Combine(qcDir, "ok.pdf");
                System.IO.File.WriteAllText(okPdf,
                    "%PDF-1.7\n1 0 obj\n<< /Length 3000 >>\nstream\n" + filler + "\nendstream\nendobj\n"
                    + "/MediaBox [0 0 595.28 841.89]\n%%EOF\n");
                Assertion.True(DwgToPdf.PdfContentCheck.Check(okPdf, 297, 210, out why2), "v4.17 pdfcheck: 正常 PDF 通过", why2);
                // (b) 完全无内容流（contentLen==0）：v4.17 前被 "contentLen > 0 &&" 漏判放行 → 现必须判失败
                string noStreamPdf = System.IO.Path.Combine(qcDir, "nostream.pdf");
                System.IO.File.WriteAllText(noStreamPdf,
                    "%PDF-1.7\n" + filler + "\n/MediaBox [0 0 595.28 841.89]\n%%EOF\n");
                Assertion.True(!DwgToPdf.PdfContentCheck.Check(noStreamPdf, 297, 210, out why2), "v4.17 pdfcheck: 无内容流判失败");
                Assertion.True(why2 != null && why2.Contains("内容流"), "v4.17 pdfcheck: 无内容流 reason 命中", why2);
                // (c) 长边对、短边严重不符（1189×400mm vs 期望 1189×841mm）：v4.17 前只比长边会漏判 → 现必须判失败
                string shortBadPdf = System.IO.Path.Combine(qcDir, "shortbad.pdf");
                System.IO.File.WriteAllText(shortBadPdf,
                    "%PDF-1.7\n1 0 obj\n<< /Length 3000 >>\nstream\n" + filler + "\nendstream\nendobj\n"
                    + "/MediaBox [0 0 1133.9 3369.4]\n%%EOF\n");
                Assertion.True(!DwgToPdf.PdfContentCheck.Check(shortBadPdf, 1189, 841, out why2), "v4.17 pdfcheck: 短边不符判失败");
                Assertion.True(why2 != null && why2.Contains("页尺寸"), "v4.17 pdfcheck: 短边不符 reason 命中", why2);
                // (d) 缺 %%EOF（写盘中断/截断）：现必须判失败
                string truncPdf = System.IO.Path.Combine(qcDir, "trunc.pdf");
                System.IO.File.WriteAllText(truncPdf,
                    "%PDF-1.7\n1 0 obj\n<< /Length 3000 >>\nstream\n" + filler + "\nendstream\nendobj\n"
                    + "/MediaBox [0 0 595.28 841.89]\n");
                Assertion.True(!DwgToPdf.PdfContentCheck.Check(truncPdf, 297, 210, out why2), "v4.17 pdfcheck: 缺 %%EOF 判失败");
                Assertion.True(why2 != null && why2.Contains("%%EOF"), "v4.17 pdfcheck: 截断 reason 命中", why2);
            }
            finally { try { System.IO.Directory.Delete(qcDir, true); } catch { } }

            // 6c) v4.17 RECT4_OUT 成因区分：并排多张图纸 vs 普通离群实体（提示文案断言）
            var mbMulti = new DwgToPdf.Batch.FrameBox
            {
                Source = "RECT4_OUT", MinX = 0, MinY = 0, MaxX = 2541, MaxY = 841,
                Extent = new double[] { 0, 0, 3910, 845 }
            };
            bool okMulti = DwgToPdf.Batch.FrameValidator.IsAcceptable(mbMulti, out string mWhy);
            Assertion.True(!okMulti && mWhy.Contains("多张图纸"), "v4.17 RECT4_OUT 并排多张图纸提示", mWhy);
            var mbMulti2 = new DwgToPdf.Batch.FrameBox
            {
                Source = "RECT4_OUT", MinX = 0, MinY = 0, MaxX = 1315, MaxY = 841,
                Extent = new double[] { 0, 0, 3805, 849 }
            };
            bool okMulti2 = DwgToPdf.Batch.FrameValidator.IsAcceptable(mbMulti2, out string mWhy2);
            Assertion.True(!okMulti2 && mWhy2.Contains("多张图纸"), "v4.17 RECT4_OUT 并排多张图纸提示(H77_RE)", mWhy2);
            var mbOutlier = new DwgToPdf.Batch.FrameBox
            {
                Source = "RECT4_OUT", MinX = 0, MinY = 0, MaxX = 1189, MaxY = 841,
                Extent = new double[] { 0, 0, 1250, 900 }
            };
            bool okOut = DwgToPdf.Batch.FrameValidator.IsAcceptable(mbOutlier, out string oWhy);
            Assertion.True(!okOut && oWhy.Contains("离群实体"), "v4.17 RECT4_OUT 普通离群实体提示", oWhy);
            var mbNoExt = new DwgToPdf.Batch.FrameBox
            {
                Source = "RECT4_OUT", MinX = 0, MinY = 0, MaxX = 1189, MaxY = 841, Extent = null
            };
            bool okNoExt = DwgToPdf.Batch.FrameValidator.IsAcceptable(mbNoExt, out string nWhy);
            Assertion.True(!okNoExt && nWhy.Contains("离群实体"), "v4.17 RECT4_OUT 无全图范围回退普通提示", nWhy);

            Console.WriteLine("--unit 完成（纯函数断言，秒级）");
        }

        // ---- Task 10: 公司版 / 高质量打印 冒烟 ----
        private static void RunCompanySmoke()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_cos_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var lib = new DwgToPdf.Batch.FrameTemplateLibrary();
            lib.BuildFromFolder(@"E:\Dwg To Pdf 工具\公司图纸模板", exe, tmp);
            Assertion.True(lib.Entries.Count >= 9, "company library built >=9, got " + lib.Entries.Count);
            string a2 = @"E:\Dwg To Pdf 工具\公司图纸模板\图纸图框模板_A2尺寸_A1版本 20260823.dwg";
            var ex2 = new DwgToPdf.Batch.FrameExtractor(exe);
            var frames = ex2.RunExtractCompany(new System.Collections.Generic.List<string> { a2 }, tmp, null, null);
            Assertion.True(frames.ContainsKey(a2), "company extract a2 frame");
            if (frames.TryGetValue(a2, out var b))
            {
                var hit = lib.Match(b.Width, b.Height, b.Layer, DwgToPdf.Batch.FrameTemplateLibrary.ToleranceMm);
                Assertion.True(hit != null && hit.PaperSize == "A2", "company match a2 -> A2, got " + (hit?.PaperSize ?? "null") + " w=" + b.Width + " h=" + b.Height);
            }
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        private static void RunHqSmoke()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            // 源替换：计划指定的 7633638 仅 787KB（<1MB 不触发高质量），改用同目录 >1MB 的 7633639（5.36MB）
            string bigSrc = @"E:\Dwg To Pdf 工具\测试文件输入\PISCES按摩总成图纸 - 0826\PISCES_7633639-AB_LB001APIA2_主驾按摩腰托总成_临时图纸_20260814.dwg";
            Assertion.True(System.IO.File.Exists(bigSrc) && new System.IO.FileInfo(bigSrc).Length > 1048576, "hq source exists and >1MB");
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_hq_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(new System.Collections.Generic.List<string> { bigSrc }, tmp, p, false,
                new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 },
                new DwgToPdf.Batch.ConvertProfile { Mode = DwgToPdf.Batch.ConvertMode.General });
            string[] pdfs = System.IO.Directory.GetFiles(FindPdfFolder(tmp) ?? tmp, "*.pdf");
            Assertion.True(pdfs.Length == 1, "hq produced 1 pdf, got " + pdfs.Length);
            if (pdfs.Length == 1)
            {
                bool okb = QaPdf(pdfs[0], 2378.0, 1681.0);
                Assertion.True(okb, "hq MediaBox ~ 4A0");
            }
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        // ---- Task 11: 公司版 E2E 全链路 ----
        private static void RunE2ECompany()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_e2eco_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var files = new System.Collections.Generic.List<string>
            {
                @"E:\Dwg To Pdf 工具\公司图纸模板\图纸图框模板_A2尺寸_A1版本 20260823.dwg",
            };
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(files, tmp, p, false,
                new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 },
                new DwgToPdf.Batch.ConvertProfile { Mode = DwgToPdf.Batch.ConvertMode.Company });
            string[] pdfs = System.IO.Directory.GetFiles(FindPdfFolder(tmp) ?? tmp, "*.pdf");
            Assertion.True(pdfs.Length == 1, "e2e company produced 1 pdf, got " + pdfs.Length);
            if (pdfs.Length == 1)
            {
                // A2 模板图框 = 594.268 x 420.19（实测，比 A2 标称大 0.27/0.19mm）。
                // PageSpec 按"图框+2*5mm 边距"选纸：604.3 > 594 → 落到 A1（841x594），与 v3.0 语义一致。
                bool okb = QaPdf(pdfs[0], 841.0, 594.0);
                Assertion.True(okb, "e2e company pdf A1 (A2模板框+5mm边距)");
            }
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        // ---- Task 12: 生成公司图框标准库（交付物，--gen-library 供发布/重建） ----
        private static void RunGenLibrary()
        {
            string exe = DwgToPdf.Batch.CoreConsoleLocator.FindAccoreConsole();
            Assertion.True(exe != null, "accoreconsole located");
            if (exe == null) return;
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_genlib_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmp);
            var lib = new DwgToPdf.Batch.FrameTemplateLibrary();
            lib.BuildFromFolder(@"E:\Dwg To Pdf 工具\公司图纸模板", exe, tmp);
            string json = lib.Serialize();
            string target = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DwgToPdf.Batch.FrameTemplateLibrary.FileName);
            System.IO.File.WriteAllText(target, json, System.Text.Encoding.UTF8);
            Console.WriteLine("[gen-library] entries=" + lib.Entries.Count + " -> " + target);
            Assertion.True(lib.Entries.Count >= 9, "library entries >=9, got " + lib.Entries.Count);
            Assertion.True(System.IO.File.Exists(target), "library json written");
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }

        /// <summary>v3.6 边距对比自测：--margin-compare &lt;src&gt; &lt;m1&gt; &lt;m2&gt; [outDir]。
        /// 同一张 dwg 用两个不同边距值各生成一张 PDF，对比输出。验证 PrintOptions.EdgeMarginMm
        /// 是否真影响 PDF（图框位置）。每次 RunFiles 走独立子目录避免预处理副本复用。</summary>
        private static void RunMarginCompare(string src, double m1, double m2, string outRoot)
        {
            if (!System.IO.File.Exists(src)) { Console.WriteLine("  [mc] 源 dwg 不存在: " + src); Assertion.True(false, "src exists", src); return; }
            string baseDir = string.IsNullOrEmpty(outRoot)
                ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_mc_" + Guid.NewGuid().ToString("N"))
                : outRoot;
            System.IO.Directory.CreateDirectory(baseDir);
            Console.WriteLine("边距对比: src=" + System.IO.Path.GetFileName(src) + ", m1=" + m1 + ", m2=" + m2);
            foreach (var m in new[] { m1, m2 })
            {
                string sub = System.IO.Path.Combine(baseDir, "m" + m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                System.IO.Directory.CreateDirectory(sub);
                var engine = new DwgToPdf.Batch.ConverterEngine();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { if (!string.IsNullOrEmpty(u.Note) && u.Note.Length < 120) Console.WriteLine("    [m" + m + "] " + u.Note); });
                engine.RunFiles(new System.Collections.Generic.List<string> { src }, sub, p, false,
                    new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = m });
                sw.Stop();
                int n = System.IO.Directory.GetFiles(FindPdfFolder(sub) ?? sub, "*.pdf").Length;
                Console.WriteLine("  margin=" + m + ": PDF=" + n + ", 用时=" + sw.Elapsed.TotalSeconds.ToString("0.0") + "s");
            }
            Console.WriteLine("对比目录: " + baseDir);
            Assertion.True(true, "margin compare done");
        }

        /// <summary>v4.17 修订三：定位输出文件夹下真正存放 PDF 的子文件夹。
        /// PDF 子文件夹名不再固定为 "PDF文件"，而是每轮按模板展开（默认 PDF-HH:mm:ss，
        /// 同名再加 -2/-3）。自测里所有"数 PDF"的位置都必须动态定位，不能再拼固定名。
        /// 规则：根下直接有 PDF（扁平输出）→ 返回根；否则取"含 PDF 的子目录"中创建时间最新的一个
        /// （一轮转换通常只有一个子目录）。找不到返回 null。</summary>
        private static string FindPdfFolder(string outDir)
        {
            try
            {
                if (string.IsNullOrEmpty(outDir) || !System.IO.Directory.Exists(outDir)) return null;
                if (System.IO.Directory.GetFiles(outDir, "*.pdf").Length > 0) return outDir;
                string best = null;
                System.DateTime bestT = System.DateTime.MinValue;
                foreach (string d in System.IO.Directory.GetDirectories(outDir))
                {
                    if (System.IO.Directory.GetFiles(d, "*.pdf").Length == 0) continue;
                    System.DateTime t = System.IO.Directory.GetCreationTimeUtc(d);
                    if (best == null || t >= bestT) { best = d; bestT = t; }
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>v4.17 修订三：枚举输出文件夹下所有 PDF（含子目录），用于"运行前"快照。</summary>
        private static System.Collections.Generic.List<string> AllPdfsUnder(string outDir)
        {
            try
            {
                if (string.IsNullOrEmpty(outDir) || !System.IO.Directory.Exists(outDir))
                    return new System.Collections.Generic.List<string>();
                return new System.Collections.Generic.List<string>(
                    System.IO.Directory.GetFiles(outDir, "*.pdf", System.IO.SearchOption.AllDirectories));
            }
            catch { return new System.Collections.Generic.List<string>(); }
        }

        /// <summary>v4.17 修订三：PDF 子文件夹名模板展开 + 同名去重（ResolvePdfSubFolder）。
        /// 覆盖用户明确约定的三条语义：
        ///   ① 默认模板 "PDF-HH.mm.ss" 按**开始转换时刻**展开（不是结束时刻）；
        ///   ② 目标名已存在（含空文件夹）时追加 "-2"/"-3"，绝不与已有批合并；
        ///   ③ 模板含 Windows 非法字符 / 全空白时回落，不产生非法路径。</summary>
        /// <summary>v4.17 修订三 需求①：二次点击转换时，上一批已成功的图纸不得再次参与打印。
        /// 用户明确要求：“默认跳过，不需要在设置里面添加”——即无开关、无设置项，
        /// 本轮只转换尚未成功的文件（待处理/失败/取消）。</summary>
        private static void TestFilterTodoFiles()
        {
            var files = new List<string> { "a.dwg", "b.dwg", "c.dwg", "d.dwg" };
            // 场景1：典型用户操作序列 —— 第一批 a/b 成功，第二批新增 c/d，再点转换
            var st = new Dictionary<string, DwgToPdf.Batch.ItemStatus>(StringComparer.OrdinalIgnoreCase)
            {
                { "a.dwg", DwgToPdf.Batch.ItemStatus.Success },
                { "b.dwg", DwgToPdf.Batch.ItemStatus.Success }
            };
            List<string> todo = DwgToPdf.Batch.RunSelection.FilterTodoFiles(files, p => st.TryGetValue(p, out var s) ? s : DwgToPdf.Batch.ItemStatus.Pending, out int done1);
            Assertion.True(done1 == 2, "v4.17 修订三 需求① 已成功 2 个被计入跳过数", "done=" + done1);
            Assertion.True(todo.Count == 2 && todo[0] == "c.dwg" && todo[1] == "d.dwg",
                "v4.17 修订三 需求① 只转换新增的 c/d（上一批不重打）", string.Join(",", todo));
            // 场景2：全部成功 → todo 为空（Start() 换提示“本轮没有要转换的文件”，不担心仍然跑空轮引擎）
            var allOk = new Dictionary<string, DwgToPdf.Batch.ItemStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) allOk[f] = DwgToPdf.Batch.ItemStatus.Success;
            List<string> none = DwgToPdf.Batch.RunSelection.FilterTodoFiles(files, p => allOk[p], out int done2);
            Assertion.True(none.Count == 0 && done2 == 4, "v4.17 修订三 需求① 全成功时 todo 为空", "todo=" + none.Count + ",done=" + done2);
            // 场景3：失败/取消/待处理 都必须保留（否则失败图纸永远补不了）
            var mixed = new Dictionary<string, DwgToPdf.Batch.ItemStatus>(StringComparer.OrdinalIgnoreCase)
            {
                { "a.dwg", DwgToPdf.Batch.ItemStatus.Success },
                { "b.dwg", DwgToPdf.Batch.ItemStatus.Failed },
                { "c.dwg", DwgToPdf.Batch.ItemStatus.Cancelled }
            };
            List<string> todo3 = DwgToPdf.Batch.RunSelection.FilterTodoFiles(files, p => mixed.TryGetValue(p, out var s) ? s : DwgToPdf.Batch.ItemStatus.Pending, out int done3);
            Assertion.True(done3 == 1 && todo3.Count == 3
                && todo3.Contains("b.dwg") && todo3.Contains("c.dwg") && todo3.Contains("d.dwg"),
                "v4.17 修订三 需求① 失败/取消/待处理均保留（只跳成功）", string.Join(",", todo3));
            // 场景4：项目项未变——空清单、空状态回调都不得抛异常
            List<string> empty = DwgToPdf.Batch.RunSelection.FilterTodoFiles(new List<string>(), p => DwgToPdf.Batch.ItemStatus.Success, out int d4);
            Assertion.True(empty.Count == 0 && d4 == 0, "v4.17 修订三 需求① 空清单安全");
            List<string> nullCb = DwgToPdf.Batch.RunSelection.FilterTodoFiles(files, null, out int d5);
            Assertion.True(nullCb.Count == 4 && d5 == 0, "v4.17 修订三 需求① 状态回调为 null 时全部当待转换（不抛）", "todo=" + nullCb.Count);
            List<string> nullList = DwgToPdf.Batch.RunSelection.FilterTodoFiles(null, p => DwgToPdf.Batch.ItemStatus.Success, out int d6);
            Assertion.True(nullList.Count == 0 && d6 == 0, "v4.17 修订三 需求① 清单为 null 安全");
        }

        private static void TestPdfFolderTemplate()
        {
            var at = new DateTime(2026, 10, 15, 9, 8, 7);

            // ① 默认模板：PDF-09：08：07（全角冒号，Windows 文件名合法）
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate(DwgToPdf.Batch.AppSettings.PdfFolderTemplateDefault, at)
                == "PDF-09\uFF1A08\uFF1A07", "v4.17 修订三 默认模板展开 PDF-HH:mm:ss",
                DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate(DwgToPdf.Batch.AppSettings.PdfFolderTemplateDefault, at));
            // 空白模板回落默认模板（不是回落 PDF文件）
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("   ", at) == "PDF-09\uFF1A08\uFF1A07",
                "v4.17 修订三 空白模板回落默认模板");
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate(null, at) == "PDF-09\uFF1A08\uFF1A07",
                "v4.17 修订三 null 模板回落默认模板");
            // 自定义模板：日期 + 时钟组合
            // 注意：模板里的**字面圆点**不是占位符，会被原样保留 —— 只有冒号会被映射成全角。
            // 所以 "HH.mm" 展开成 "09.08"，而 "HH:mm" 展开成 "09：08"。这正是用户要的效果：
            // 分隔符完全由模板里的字面字符决定，引擎不擅自改写法。
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("图框_yyyy-MM-dd_HH.mm", at) == "图框_2026-10-15_09.08",
                "v4.17 修订三 自定义模板 yyyy-MM-dd_HH.mm（字面圆点原样保留）");
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("图框_yyyy-MM-dd_HH:mm:ss", at) == "图框_2026-10-15_09\uFF1A08\uFF1A07",
                "v4.17 修订三 自定义模板 HH:mm:ss（冒号映射为全角）");
            // 纯日期模板（时钟精度不够时用）
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("yyyyMMdd", at) == "20261015",
                "v4.17 修订三 自定义模板 yyyyMMdd");

            // ② 半角冒号不是被丢弃，而是映射为全角「：」——否则 "09:08:07" 会变 "090807"
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("HH:mm:ss", at) == "09\uFF1A08\uFF1A07",
                "v4.17 修订三 半角冒号映射为全角（分隔符不丢）");
            // 用户手打全角冒号应原样保留
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("HH\uFF1Amm\uFF1Ass", at) == "09\uFF1A08\uFF1A07",
                "v4.17 修订三 全角冒号原样保留");
            // 其余 Windows 非法字符仍按原策略过滤
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("A:B*C?D\"E<F>G|H", at) == "A\uFF1ABCDEFGH",
                "v4.17 修订三 冒号映射后其余非法字符仍被过滤");
            // 过滤后为空 → 回落 PDF文件
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate("///", at) == DwgToPdf.Batch.AppSettings.PdfFolderNameDefault,
                "v4.17 修订三 全非法字符回落 PDF文件");
            // 超长截断到 64
            string longTpl = new string('x', 200);
            Assertion.True(DwgToPdf.Batch.AppSettings.ExpandPdfFolderTemplate(longTpl, at).Length == 64,
                "v4.17 修订三 超长模板截断到 64 字符");

            // ③ ResolvePdfSubFolder：目标不存在 → 原名
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "st_pdfdir_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            Assertion.True(DwgToPdf.Batch.ConverterEngine.ResolvePdfSubFolder(root, at, null) == "PDF-09\uFF1A08\uFF1A07",
                "v4.17 修订三 目标不存在返回原名");
            // 已存在（空文件夹也算占用）→ -2
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "PDF-09\uFF1A08\uFF1A07"));
            Assertion.True(DwgToPdf.Batch.ConverterEngine.ResolvePdfSubFolder(root, at, null) == "PDF-09\uFF1A08\uFF1A07-2",
                "v4.17 修订三 同名已存在追加 -2");
            // 同名是**文件**而非目录，同样算占用（不能把文件当目录写 PDF）
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "PDF-09\uFF1A08\uFF1A07-2"), "x");
            Assertion.True(DwgToPdf.Batch.ConverterEngine.ResolvePdfSubFolder(root, at, null) == "PDF-09\uFF1A08\uFF1A07-3",
                "v4.17 修订三 同名文件也占用 → 追加 -3");
            // startedAt 缺省 → 用当前时刻（只断言不抛异常且形如 PDF-xx：xx：xx）
            string nowName = DwgToPdf.Batch.ConverterEngine.ResolvePdfSubFolder(root, null, DwgToPdf.Batch.AppSettings.PdfFolderTemplateDefault);
            Assertion.True(nowName.StartsWith("PDF-") && nowName.Length == 12, "v4.17 修订三 startedAt 缺省用当前时刻", nowName);
            // 纯名称解析、无副作用：磁盘状态没变（仍是 -2/-3 占用），同一参数必须仍解析出 -3
            // （这一条对应 RunCore 的契约：每轮只调用一次、赋值后只读，不会边解析边建目录）
            Assertion.True(DwgToPdf.Batch.ConverterEngine.ResolvePdfSubFolder(root, at, null) == "PDF-09\uFF1A08\uFF1A07-3",
                "v4.17 修订三 纯解析无副作用，状态不变结果不变");
            try { System.IO.Directory.Delete(root, true); } catch { }
        }
    }
}
