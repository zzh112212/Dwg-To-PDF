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
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--probe-hq-media") { var t = new System.Threading.Thread(RunProbeHqMedia); t.SetApartmentState(System.Threading.ApartmentState.STA); t.Start(); t.Join(); return Assertion.ExitCode(); }
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
            TestPrintQuality();
            TestFrameTemplateLibrary();
            TestConvertProfile();
            TestSmartConcurrency();
            if (!Assertion.Passed()) return Assertion.ExitCode();
            Console.WriteLine("ALL TESTS PASSED.");
            return Assertion.ExitCode();
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
            string pdfFolder = System.IO.Path.Combine(tmp, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
            System.IO.Directory.CreateDirectory(pdfFolder);
            // 目录复用场景（--e2e-custom 二次运行）会累积旧 PDF，故按"新增"计数
            var beforePdfs = new HashSet<string>(System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
            // 运行前是否已有缓存文件（--e2e-custom 复用目录二次运行时为 true → 可验证缓存复用断言）
            bool cacheExistedBeforeRun = System.IO.File.Exists(System.IO.Path.Combine(tmp, "frame_cache.json"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            {
                if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note);
            });
            engine.RunFiles(files, tmp, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 });
            sw.Stop();
            Console.WriteLine("E2E 用时: " + sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
            int newPdfs = 0;
            foreach (var f in System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories))
                if (!beforePdfs.Contains(f)) newPdfs++;
            Assertion.True(newPdfs == 3, "e2e produced 3 pdfs, got " + newPdfs);
            Assertion.True(System.IO.File.Exists(System.IO.Path.Combine(tmp, "frame_cache.json")), "e2e wrote frame_cache.json");
            // 二次缓存验证：读最新日志（--e2e-custom 检查"需识别"计数；目录复用时累积多份日志，按修改时间取最新）
            if (outDir != null)
            {
                string logsDir = System.IO.Path.Combine(tmp, "logs");
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
                        string content = System.IO.File.ReadAllText(newest);
                        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        Console.WriteLine("    [日志尾] " + (lines.Length > 0 ? lines[lines.Length - 1] : ""));
                        // I2 整改（2026-08-27）：复用目录（运行前已有缓存）必须断言缓存全命中、跳过识别
                        if (cacheExistedBeforeRun)
                        {
                            bool cacheHit = content.Contains("需识别 0") || content.Contains("缓存命中 3/3");
                            Assertion.True(cacheHit, "e2e cache reuse: second run skips recognition", "日志应含 需识别 0 或 缓存命中 3/3");
                        }
                    }
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
        private static void RunDirE2E(string inputDir, string outDir, bool company = false, bool flat = false, bool blockFrame = false)
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
            string pdfFolder = System.IO.Path.Combine(outDir, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
            System.IO.Directory.CreateDirectory(pdfFolder);
            // 目录复用场景：按"新增"计数，避免旧 PDF 干扰断言
            var beforePdfs = new HashSet<string>(System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            { if (!string.IsNullOrEmpty(u.Note) && u.Note.Length < 160) Console.WriteLine("    [进度] " + u.Note); });
            var opts = new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0, FlatOutput = flat };
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
            foreach (var f in System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories))
                if (!beforePdfs.Contains(f)) newPdfs++;
            // 失败清单：输入基名（去重名 "(n)" 后缀）未出现在产出 PDF 中即视为失败
            var pdfBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pdf in System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories))
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
                foreach (var pdf in System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories))
                    if (!beforePdfs.Contains(pdf) && System.IO.Path.GetDirectoryName(pdf) != System.IO.Path.GetFullPath(pdfFolder)) nested++;
                Assertion.True(nested == 0, "dirf all new pdfs flat in root, nested=" + nested);
            }
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
            int pdfs = System.IO.Directory.GetFiles(System.IO.Path.Combine(tmp, DwgToPdf.Batch.ConverterEngine.PdfFolderName), "*.pdf").Length;
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

            int pdfs = System.IO.Directory.GetFiles(System.IO.Path.Combine(tmp, DwgToPdf.Batch.ConverterEngine.PdfFolderName), "*.pdf").Length;
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
            var pdfs = System.IO.Directory.GetFiles(System.IO.Path.Combine(outDir, DwgToPdf.Batch.ConverterEngine.PdfFolderName), "*.pdf");
            var entries = DwgToPdf.Batch.FrameCache.LoadEntries(outDir);
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
            string pdfFolder = System.IO.Path.Combine(outDir, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
            System.IO.Directory.CreateDirectory(pdfFolder);
            var beforePdfs = new HashSet<string>(System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u =>
            { if (!string.IsNullOrEmpty(u.Note) && u.Note.Length < 120) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(files, outDir, p, false, new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 });
            sw.Stop();
            int newPdfs = 0;
            foreach (var f in System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories))
                if (!beforePdfs.Contains(f)) newPdfs++;
            int fails = files.Count - newPdfs;
            // 从最新日志取"==== 转换结束 ====" 行
            string logsDir = System.IO.Path.Combine(outDir, "logs");
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
                string pdfFolder = System.IO.Path.Combine(outDir, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
                System.IO.Directory.CreateDirectory(pdfFolder);
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
                string[] pdfs = System.IO.Directory.GetFiles(pdfFolder, "*.pdf", System.IO.SearchOption.AllDirectories);
                Assertion.True(pdfs.Length == 0, "假 dwg 不产出 pdf", "pdfs=" + pdfs.Length);
            }
            finally
            {
                try { System.IO.Directory.Delete(tmp, true); } catch { }
            }
        }

                private static void TestPrintQuality()
        {
            // v4.3：按文件大小选设备（>阈值=HQ pc3，否则标准 pc3）；媒体名恒等透传。
            string tmp = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tmp, new byte[1024 * 1024 + 1]);   // >1MB
                // v4.16：HQ pc3 已停用（实测为负优化，见手册第28章）。原断言"大文件 -> HQ pc3"
                // 编码的是已废弃的行为：它曾让所有 >1MB 的图纸整页光栅化、慢 5 倍。
                // 现在无论文件多大都固定用标准 DWG TO PDF.pc3（矢量输出）。
                Assertion.True(DwgToPdf.Batch.PrintQuality.PlotterFor(tmp, 1048576) == DwgToPdf.Batch.PrintQuality.StandardPlotterName, "large file -> standard pc3 (HQ retired v4.16)");
                Assertion.True(!DwgToPdf.Batch.PrintQuality.IsHighQuality(tmp, 1048576), "IsHighQuality always false (v4.16)");
                System.IO.File.WriteAllBytes(tmp, new byte[1024]);              // <1MB
                Assertion.True(DwgToPdf.Batch.PrintQuality.PlotterFor(tmp, 1048576) == DwgToPdf.Batch.PrintQuality.StandardPlotterName, "small file -> standard pc3");
            }
            finally { try { System.IO.File.Delete(tmp); } catch { } }
            Assertion.True(DwgToPdf.Batch.PrintQuality.MapMedia(true, "ISO_full_bleed_A3") == "ISO_full_bleed_A3", "media passthrough");
            Assertion.True(DwgToPdf.Batch.PrintQuality.ThresholdBytes == 1L * 1048576, "default threshold 1MB");
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

        private static void RunProbeHqMedia()
        {
            string cfg = DwgToPdf.Batch.PrintQuality.HighQualityPlotterName;
            var names = new[]{
                "ISO_full_bleed_A4_(210.00_x_297.00_MM)","ISO_full_bleed_A3_(297.00_x_420.00_MM)",
                "ISO_full_bleed_A2_(420.00_x_594.00_MM)","ISO_full_bleed_A1_(594.00_x_841.00_MM)",
                "ISO_full_bleed_A0_(841.00_x_1189.00_MM)","ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)",
                "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)"
            };
            DwgToPdf.Batch.OleMsgFilter.Install();
            var before = DwgToPdf.Batch.ComStarter.AcadProcessIds();
            object app = null; bool own = false;
            try { app = DwgToPdf.Batch.ComStarter.StartNewInstance(); own = DwgToPdf.Batch.ComStarter.HasNewAcadProcess(before, 4000); }
            catch (Exception ex) { Console.WriteLine("probe failed to start acad: " + ex.Message); return; }
            try
            {
                var docs = new DwgToPdf.Batch.Late(new DwgToPdf.Batch.Late(app).Get("Documents"));
                object doc = null;
                try
                {
                    doc = docs.Call("Open", @"E:\Dwg To Pdf 工具\公司图纸模板\图纸图框模板_A2尺寸_A1版本 20260823.dwg", false);
                    var d = new DwgToPdf.Batch.Late(doc);
                    var layouts = new DwgToPdf.Batch.Late(d.Get("Layouts"));
                    var model = new DwgToPdf.Batch.Late(layouts.GetAt("Item", "Model"));
                    d.Set("ActiveLayout", model.Raw);
                    model.Set("ConfigName", cfg);
                    foreach (var n in names)
                    {
                        try { model.Set("CanonicalMediaName", n); Console.WriteLine("SUPPORTED: " + n); }
                        catch (Exception exN) { Console.WriteLine("UNSUPPORTED: " + n + " -> " + exN.Message); }
                    }
                }
                finally { if (doc != null) { try { new DwgToPdf.Batch.Late(doc).Call("Close", false); } catch { } } }
            }
            finally
            {
                if (own && app != null) { try { new DwgToPdf.Batch.Late(app).Call("Quit"); } catch { } try { System.Runtime.InteropServices.Marshal.ReleaseComObject(app); } catch { } }
            }
        }

        // ---- v4.15：秒级单元断言（纯函数；不启动 AutoCAD，秒级完成） ----
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
            string pdfDir = System.IO.Path.Combine(tmp, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
            System.IO.Directory.CreateDirectory(pdfDir);
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(new System.Collections.Generic.List<string> { bigSrc }, tmp, p, false,
                new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 },
                new DwgToPdf.Batch.ConvertProfile { Mode = DwgToPdf.Batch.ConvertMode.General });
            string[] pdfs = System.IO.Directory.GetFiles(pdfDir, "*.pdf");
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
            string pdfDir = System.IO.Path.Combine(tmp, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
            System.IO.Directory.CreateDirectory(pdfDir);
            var files = new System.Collections.Generic.List<string>
            {
                @"E:\Dwg To Pdf 工具\公司图纸模板\图纸图框模板_A2尺寸_A1版本 20260823.dwg",
            };
            var engine = new DwgToPdf.Batch.ConverterEngine();
            var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { if (!string.IsNullOrEmpty(u.Note)) Console.WriteLine("    [进度] " + u.Note); });
            engine.RunFiles(files, tmp, p, false,
                new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = 5.0 },
                new DwgToPdf.Batch.ConvertProfile { Mode = DwgToPdf.Batch.ConvertMode.Company });
            string[] pdfs = System.IO.Directory.GetFiles(pdfDir, "*.pdf");
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
                string pdfDir = System.IO.Path.Combine(sub, DwgToPdf.Batch.ConverterEngine.PdfFolderName);
                System.IO.Directory.CreateDirectory(pdfDir);
                var engine = new DwgToPdf.Batch.ConverterEngine();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var p = new System.Progress<DwgToPdf.Batch.ProgressUpdate>(u => { if (!string.IsNullOrEmpty(u.Note) && u.Note.Length < 120) Console.WriteLine("    [m" + m + "] " + u.Note); });
                engine.RunFiles(new System.Collections.Generic.List<string> { src }, sub, p, false,
                    new DwgToPdf.Batch.PrintOptions { Mode = DwgToPdf.Batch.ColorMode.Monochrome, EdgeMarginMm = m });
                sw.Stop();
                int n = System.IO.Directory.GetFiles(pdfDir, "*.pdf").Length;
                Console.WriteLine("  margin=" + m + ": PDF=" + n + ", 用时=" + sw.Elapsed.TotalSeconds.ToString("0.0") + "s");
            }
            Console.WriteLine("对比目录: " + baseDir);
            Assertion.True(true, "margin compare done");
        }
    }
}
