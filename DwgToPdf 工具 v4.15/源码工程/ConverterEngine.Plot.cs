using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DwgToPdf.Batch;

// v4.6：从 ConverterEngine.cs 拆分出阶段3 出图相关方法（纯移动，不改逻辑）：
// WorkerLoopV3（Core Console 出图轮询）/ FallbackComPersistent（COM 兜底限流）
// / RunComOnly（整批回退 COM 编排）/ WorkerLoop（v2.3.1 COM 出图轮询）
internal sealed partial class ConverterEngine
{
	// v4.12：删除"长时转换提醒"（原 v4.8 需求③ StartSlowRemind + SlowRemindSeconds 设置项）——
	// 用户反馈该提醒无实际价值，改为不提醒（超时/慢文件仍可在日志头部看到耗时统计）。

	/// <summary>阶段 3 出图 worker：accoreconsole -EXPORT 副本（继承预处理页面设置），失败回退 COM 源图。
	/// v4.4：新增 frameSnapshot（图框缓存内存快照）与逐文件结果记录（日志失败分类）。</summary>
	private void WorkerLoopV3(int wi, int[] next, List<string> files, int total, string outputFolder,
		IProgress<ProgressUpdate> progress, int[] booted, int[] stat, LogWriter log,
		bool tight, PrintOptions print, string accore, string tmpDir, double marginMm, Dictionary<string, string> copies, int[] hqStat, string commonRoot,
		Dictionary<string, FrameCache.Entry> frameSnapshot, bool accoreSetup)
	{
		OleMsgFilter.Install();
		var plotter = new PlotSession(accore);
		string ctb = print != null ? print.StyleSheetName : "monochrome.ctb";
		// v4.5：PDF 输出结构（true=全部放 PDF文件\ 根下；false=按原文件夹结构）
		bool flatOutput = print != null && print.FlatOutput;
		// v3.4：本线程兜底复用的 COM 实例（懒加载，跨文件复用，收尾统一 Shutdown）
		CadConverter fallbackConv = null;
		while (true)
		{
			if (_cancelRequested) break;
			int idx = Interlocked.Increment(ref next[0]) - 1;
			// v4.14：取件边界必须用"本次出图列表的长度"而不是 total ——
			// 流水线模式下每次只传一个块（files.Count < total），用 total 会数组越界。
			// （串行模式下 files.Count == total，行为与 v4.12 完全一致）
			if (idx >= files.Count) break;
			// v4.3：阶段1 已判失败（图框问题）的文件跳过（progress 已在阶段1 报 Failed）
			if (_frameRejectedPaths.Contains(files[idx])) continue;
		if (_cancelRequested)
		{
			log.WriteLine("[取消] " + Path.GetFileNameWithoutExtension(files[idx]) + " —— 用户停止");
			progress.Report(new ProgressUpdate
			{
				Index = idx,
				Total = total,
				FileName = files[idx],
				Status = ItemStatus.Cancelled,
				Phase = RunPhase.Plot,
				Note = "已停止"
			});
			// v4.5：登记转换结果（日志失败分类数据源）
			AddRecord(new ConvertRecord { Index = idx, DwgPath = files[idx], Status = ItemStatus.Cancelled, Reason = "已停止" });
			continue;
		}
			string src = null;
			string name = null;
			var fileSw = System.Diagnostics.Stopwatch.StartNew(); // v4.4：单文件出图用时（日志统计）
			// v4.12：单张转换超时（0=不限制）—— 超过则中止该张会话并退回「待处理」；
			// 用户再次发起转换（IgnoreSizeTimeLimits=true）时忽略该限制。
			int convTimeoutSec = (print != null && !print.IgnoreSizeTimeLimits) ? print.ConvertTimeoutSeconds : 0;
			bool timedOut = false;
			try
			{
				src = files[idx];
				name = Path.GetFileNameWithoutExtension(src);
				// v3.9.1/v4.5：PDF 输出目录——按原文件夹结构，或全部放 PDF文件\ 根下（用户可选）
				string subDir = flatOutput ? "" : GetRelativeSubDir(src, commonRoot);
				string pdfDir = string.IsNullOrEmpty(subDir) ? Path.Combine(outputFolder, PdfFolderName) : Path.Combine(outputFolder, PdfFolderName, subDir);
				// v4.5：扁平模式下同名文件用工作名（子目录标识_基名）避免并发出图相互覆盖；
				// 默认结构模式保持纯基名（各子目录物理隔离无冲突，与旧版输出名一致）
				string pdfPath = UniquePdfPath(pdfDir, flatOutput ? WorkBaseName(src, name) : name);
				progress.Report(new ProgressUpdate
				{
					Index = idx,
					Total = total,
					FileName = src,
					Status = ItemStatus.Running,
					Phase = RunPhase.Plot,
					Note = ""
				});

					bool ok = false;
					string reason = "";
					bool hasBox = FrameCache.TryGetFrom(frameSnapshot, src, out FrameCache.Entry entry) && entry != null;
					string copyPath = null;
					if (copies != null) copies.TryGetValue(src, out copyPath);
					bool hasCopy = !string.IsNullOrEmpty(copyPath) && File.Exists(copyPath);
					// v4.12：页面设置内联——"有边距(居中)"模式下无副本时直接用源图出图，
					// 页面设置在出图会话内 entmod 完成（等效 COM 预处理，见附录 24 章）
					bool useAccoreSetup = accoreSetup && !hasCopy;
					bool canPlot = hasBox && (hasCopy || useAccoreSetup);

						PageSpec.Resolved ps = null; // v4.1：移到外部供质量核查使用
					string qualityPc3 = null;    // v4.15：含图片图纸的高清 pc3（COM 出图用；非空=跳过 accore）
					// v4.15：以下提到外层作用域——"COM 高清失败 → 回退常规出图"重试时要用同一套
					// 参数（设备/纸型/旋转/取消判定），否则重试会走错设置。
					FrameBox box = null;
					string pc3Name = null;
					string mediaName = null;
					bool landscape = false;
					// v4.12：取消判定同时包含"用户停止"与"单张转换超时"（超时=中止该张会话）
					Func<bool> plotCancel = delegate
					{
						if (_cancelRequested) return true;
						return convTimeoutSec > 0 && fileSw.Elapsed.TotalSeconds > convTimeoutSec;
					};
					if (canPlot)
					{
						box = new FrameBox
						{
							MinX = entry.MinX,
							MinY = entry.MinY,
							MaxX = entry.MaxX,
							MaxY = entry.MaxY,
							Layer = entry.Source ?? "",
							Source = "cache",
							ImageCount = entry.ImageCount,   // v4.15：图片/嵌入对象/参考底图总数（日志用）
							// v4.15：真光栅 IMAGE 数 / 其中外部图缺失数——增强出图只对真光栅且无缺失图启用
							RasterCount = entry.RasterCount,
							RasterMissingCount = entry.RasterMissingCount,
						};
						ps = PageSpec.Resolve(box.Width, box.Height, marginMm);
						landscape = box.Width >= box.Height;
						pc3Name = PrintQuality.PlotterFor(src, print.HighQualityThresholdBytes);
						bool isHighQ = PrintQuality.IsHighQuality(src, print.HighQualityThresholdBytes);
						mediaName = PrintQuality.MapMedia(isHighQ, ps.Media);
						try
						{
							// v4.15：含光栅图片图纸 → 改用 COM（打印）路径 + 高清 pc3。
							// 实测依据（手册第26章）：accoreconsole 的 -EXPORT **不认设备/质量**
							// （改设备名、改 pc3、改 DefaultConfig 均无效，图片被压到 ~26-44dpi）；
							// 而 COM 路径认 pc3 的矢量/光栅质量（同一张图 2400/600 → 图片 ≈40dpi，
							// 4800/4800 → ≈161dpi，PDF 1877KB → 2949KB）。
							// v4.15 修订收紧（2026-09-16 全量 183 张实测教训，见手册第26章 十一）：
							//   ① 只认**真光栅 IMAGE 实体**——OLE 嵌入图片/参考底图不启用（全量里 35 张
							//      被判"含图片"，真含 IMAGE 的只有 8 张；其余白走 COM，单张最慢 932s）；
							//   ② 有"外部图缺失"的图纸**不启用**——实测这类图纸走 COM 打印会输出
							//      **空白页**（accoreconsole 正常），6 张 H77 双面胶即此类。
							if (PrintQuality.UseRasterBoost(box.RasterCount, box.RasterMissingCount, print.RasterImageDpi) && !NoComFallback)
							{
								string qWhy;
								qualityPc3 = RasterPlotterFactory.Ensure(print.RasterImageDpi, out qWhy);
								if (qualityPc3 == null && !_rasterBoostWarned)
								{
									_rasterBoostWarned = true;
									log.WriteLine("[提示] 高清打印配置生成失败（" + qWhy + "），含图片图纸按普通方式出图");
								}
							}
							if (qualityPc3 != null)
							{
								ok = false;                     // 不跑 accore，直接走下方 COM 分支（带高清 pc3）
								reason = "含光栅图片：改用 COM 高清出图";
							}
							else
							{
								// v4.16（2026-10-07，安全修复，依据 TRAE 实测 R7）：**始终把副本交给 accoreconsole**。
// 原代码是 `useAccoreSetup ? src : copyPath` —— accore 模式下把**源图原件**直接交给
// accoreconsole，而出图脚本会对 Model 布局做 entmod（页面设置 / 组码 76 等）。
// AutoCAD 的自动保存与崩溃恢复会把这种就地打开的图**写回源文件**。
// 实测后果：`打印问题文件` 里 8 张源图的 mtime 全部集中在同一分钟，其中 3 张
// （7633639/ 7633640 / 7643423）**内容 MD5 被改写** —— 违反"不得改变图纸内容"的硬约束。
// 现在两条通道统一用 copyPath，源图只被读取（图框识别仍读源，见FrameExtractor），
// 任何写回都只会落在临时目录的副本上。
// 代价：副本在临时目录，图纸里的 Xref 相对路径可能解析不到；出图只用 extents/窗口，
// 实测 40 张公司版 + 8 张 PISCES 均不受影响（若将来有 Xref 依赖图纸需另做相对路径重定向）。
							ok = plotter.Plot(copyPath, pdfPath, box, pc3Name, mediaName, landscape, ctb, marginMm, tmpDir, out reason, plotCancel, useAccoreSetup ? ps : null, print.ShowLineWeight);
							}
							if (ok && isHighQ) hqStat[0]++;
						}
						catch (OperationCanceledException)
						{
							ok = false;
							timedOut = !_cancelRequested && convTimeoutSec > 0 && fileSw.Elapsed.TotalSeconds >= convTimeoutSec;
							reason = timedOut ? ("转换超过 " + convTimeoutSec + " 秒") : "已停止";
						}
					}
					else
					{
						reason = hasBox ? "无预处理副本" : "无图框数据";
					}
						// v4.3：质量审查多元化 —— 不仅文件大小，PDF 页尺寸/MediaBox/内容流字节数综合判定
						// 防止 acoreconsole 静默产出"大小达标但内容全空"的 PDF
						// settings 的 QualityMinFileSizeBytes 替代之前的纸型估算硬编码
						if (ok && print.QualityCheck && File.Exists(pdfPath))
						{
							if (!PdfContentCheck.Check(pdfPath, ps.PaperLong, ps.PaperShort, out string qcReason, print.QualityMinFileSizeBytes))
							{
								log.WriteLine("[信息] " + name + " —— 质量核查: " + qcReason + "，回退 COM");
								try { File.Delete(pdfPath); } catch { }
								ok = false;
								reason = qcReason;
							}
						}

					// v4.12：单张转换超时 → 中止该张并退回「待处理」（不判失败、不回退 COM），
					// 用户可在其他图纸转换完成后再单独处理该批（配合"合并待转换"）。
					if (timedOut)
					{
						log.WriteLine("[待转换] " + name + " —— 转换超时（>" + convTimeoutSec + " 秒），已退回待处理");
						try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { }
						progress.Report(new ProgressUpdate
						{
							Index = idx,
							Total = total,
							FileName = src,
							Status = ItemStatus.Pending,
							Phase = RunPhase.Plot,
							Note = "转换超时（>" + convTimeoutSec + " 秒），已退回待处理"
						});
						continue;
					}
					if (!ok)
					{
						if (_cancelRequested)   // 用户停止：按取消处理，不再回退 COM
						{
							log.WriteLine("[取消] " + name + " —— 用户停止");
							progress.Report(new ProgressUpdate { Index = idx, Total = total, FileName = src, Status = ItemStatus.Cancelled, Phase = RunPhase.Plot, Note = "已停止" });
							// v4.5：登记转换结果（日志失败分类数据源）
							AddRecord(new ConvertRecord { Index = idx, DwgPath = src, Status = ItemStatus.Cancelled, Reason = "已停止", Seconds = fileSw.Elapsed.TotalSeconds });
							continue;
						}
						if (NoComFallback)      // v3.2 自测：跳过 COM 兜底，直接判失败（假 dwg 冒烟专用）
						{
							log.WriteLine("[信息] " + name + " —— Core Console 出图失败(" + reason + ")，自测模式跳过 COM 兜底");
						}
						else
						{
							if (qualityPc3 != null)
							{
								if (!_rasterBoostLogged)
								{
									_rasterBoostLogged = true;
									log.WriteLine("[信息] 含光栅图片(IMAGE)图纸改用 COM 高清打印配置（光栅图像质量 "
										+ print.RasterImageDpi + "dpi）出图；accoreconsole 的 -EXPORT 不认打印质量，故走 COM（更慢但清晰）");
								}
								log.WriteLine("[信息] " + name + " —— 含光栅图片，改用 COM 高清出图");
							}
							else
							{
								log.WriteLine("[信息] " + name + " —— Core Console 出图失败(" + reason + ")，回退 COM");
							}
							// v4.15：把识别缓存的图框范围传给 COM，跳过"逐实体遍历求范围"（10 万实体约 390 秒）
							double[] comLl = (entry != null) ? new[] { entry.MinX, entry.MinY } : null;
							double[] comUr = (entry != null) ? new[] { entry.MaxX, entry.MaxY } : null;
							// 诊断开关（默认关闭，不影响生产）：DWGTOPDF_COM_NOEXT=1 → 不传识别缓存范围，
							// 让 COM 自行遍历求范围（用于判断"缓存范围"是否为空白 PDF 的根因）
							if (Environment.GetEnvironmentVariable("DWGTOPDF_COM_NOEXT") == "1") { comLl = null; comUr = null; }
							// 诊断开关：DWGTOPDF_COM_PLOTTER=<设备名> → 覆盖 COM 出图设备（用于判断高清 pc3 是否为根因）
							string comPlotter = qualityPc3;
							string plotterOvr = Environment.GetEnvironmentVariable("DWGTOPDF_COM_PLOTTER");
							if (!string.IsNullOrWhiteSpace(plotterOvr)) comPlotter = plotterOvr;
							ok = FallbackComPersistent(ref fallbackConv, src, pdfPath, log, tight, print, comPlotter, comLl, comUr, out reason);
							// v4.15 安全网：COM 高清出图未成功（含"产出空白页"）→ 回退常规出图通道重试，
							// 保证"高清功能"绝不导致漏图。实测触发场景：图纸的光栅图片引用了**缺失的外部文件**
							// 时 COM 打印输出空页（accoreconsole 的 -EXPORT 正常，见手册第26章 十一）。
							if (!ok && qualityPc3 != null && !_cancelRequested)
							{
								log.WriteLine("[信息] " + name + " —— COM 高清出图未成功（" + reason + "），回退常规出图重试");
								try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { }
								// v4.16（2026-10-07，安全修复，依据 TRAE 实测 R7）：**始终把副本交给 accoreconsole**。
// 原代码是 `useAccoreSetup ? src : copyPath` —— accore 模式下把**源图原件**直接交给
// accoreconsole，而出图脚本会对 Model 布局做 entmod（页面设置 / 组码 76 等）。
// AutoCAD 的自动保存与崩溃恢复会把这种就地打开的图**写回源文件**。
// 实测后果：`打印问题文件` 里 8 张源图的 mtime 全部集中在同一分钟，其中 3 张
// （7633639/ 7633640 / 7643423）**内容 MD5 被改写** —— 违反"不得改变图纸内容"的硬约束。
// 现在两条通道统一用 copyPath，源图只被读取（图框识别仍读源，见FrameExtractor），
// 任何写回都只会落在临时目录的副本上。
// 代价：副本在临时目录，图纸里的 Xref 相对路径可能解析不到；出图只用 extents/窗口，
// 实测 40 张公司版 + 8 张 PISCES 均不受影响（若将来有 Xref 依赖图纸需另做相对路径重定向）。
							ok = plotter.Plot(copyPath, pdfPath, box, pc3Name, mediaName, landscape, ctb, marginMm, tmpDir, out reason, plotCancel, useAccoreSetup ? ps : null, print.ShowLineWeight);
							}
							// v4.15 修订：COM 兜底也失败时，再给常规通道一次机会——accore 失败常为**瞬态**
							// （"未能找到路径中的某个部分"：全量 183 张/公司版 40 张批量下偶发，隔离复跑即消失，
							//   v4.12 时代亦有记录）。重试一次可挽回这类漏图（实测公司版 4 张按摩气袋）。
							if (!ok && qualityPc3 == null && box != null && !_cancelRequested)
							{
								log.WriteLine("[信息] " + name + " —— COM 兜底亦失败（" + reason + "），重试常规通道一次");
								try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { }
								// v4.16（2026-10-07，安全修复，依据 TRAE 实测 R7）：**始终把副本交给 accoreconsole**。
// 原代码是 `useAccoreSetup ? src : copyPath` —— accore 模式下把**源图原件**直接交给
// accoreconsole，而出图脚本会对 Model 布局做 entmod（页面设置 / 组码 76 等）。
// AutoCAD 的自动保存与崩溃恢复会把这种就地打开的图**写回源文件**。
// 实测后果：`打印问题文件` 里 8 张源图的 mtime 全部集中在同一分钟，其中 3 张
// （7633639/ 7633640 / 7643423）**内容 MD5 被改写** —— 违反"不得改变图纸内容"的硬约束。
// 现在两条通道统一用 copyPath，源图只被读取（图框识别仍读源，见FrameExtractor），
// 任何写回都只会落在临时目录的副本上。
// 代价：副本在临时目录，图纸里的 Xref 相对路径可能解析不到；出图只用 extents/窗口，
// 实测 40 张公司版 + 8 张 PISCES 均不受影响（若将来有 Xref 依赖图纸需另做相对路径重定向）。
							ok = plotter.Plot(copyPath, pdfPath, box, pc3Name, mediaName, landscape, ctb, marginMm, tmpDir, out reason, plotCancel, useAccoreSetup ? ps : null, print.ShowLineWeight);
							}
							// v4.3：COM 兜底后也用多元化质量审查（文件大小 + MediaBox + 内容流）
							// v4.6：ps 可能为 null（无图框/无副本的文件走 COM 兜底成功时）——
							// 此时无纸型可比对，跳过质量核查直接采用 COM 结果（此前此处 NRE 被
							// 外层 catch 误判为转换失败）
							if (ok && ps != null && print.QualityCheck && File.Exists(pdfPath))
							{
								if (!PdfContentCheck.Check(pdfPath, ps.PaperLong, ps.PaperShort, out string qcReason2, print.QualityMinFileSizeBytes))
								{
									log.WriteLine("[失败] " + name + " —— 质量核查(COM兜底后): " + qcReason2);
									try { File.Delete(pdfPath); } catch { }
									ok = false;
									reason = qcReason2;
								}
							}
						}
					}

					if (ok)
					{
						Interlocked.Increment(ref stat[0]);
						log.WriteLine("[成功] " + name);
					}
					else
					{
						Interlocked.Increment(ref stat[1]);
						log.WriteLine("[失败] " + name + " —— " + reason);
					}
					int dn = Interlocked.Increment(ref booted[0]);
					progress.Report(new ProgressUpdate
					{
						Index = idx,
						Total = total,
						FileName = src,
						Status = ok ? ItemStatus.Success : ItemStatus.Failed,
						Phase = RunPhase.Plot,
						Note = ok ? ("出图 " + dn + "/" + total) : ("出图 " + dn + "/" + total + "（失败：" + reason + "）")
					});
					// v4.5：登记转换结果（日志失败分类数据源）
					AddRecord(new ConvertRecord
					{
						Index = idx,
						DwgPath = src,
						Status = ok ? ItemStatus.Success : ItemStatus.Failed,
						Reason = ok ? "" : reason,
						FrameW = (hasBox && entry != null) ? (entry.MaxX - entry.MinX) : 0,
						FrameH = (hasBox && entry != null) ? (entry.MaxY - entry.MinY) : 0,
						FrameSource = (hasBox && entry != null) ? (entry.Source ?? "") : "",
						PaperLong = ps != null ? ps.PaperLong : 0,
						PaperShort = ps != null ? ps.PaperShort : 0,
						Seconds = fileSw.Elapsed.TotalSeconds,
						// v4.11：失败也记录期望路径——收尾"迟到 PDF 复核"需要它改判假失败
						PdfPath = pdfPath,
					});
				}
				catch (Exception ex)
				{
					if (_cancelRequested)
					{
						log.WriteLine("[取消] " + name + " —— 用户停止");
						progress.Report(new ProgressUpdate { Index = idx, Total = total, FileName = src, Status = ItemStatus.Cancelled, Phase = RunPhase.Plot, Note = "已停止" });
						AddRecord(new ConvertRecord { Index = idx, DwgPath = src ?? files[idx], Status = ItemStatus.Cancelled, Reason = "已停止", Seconds = fileSw.Elapsed.TotalSeconds });
					}
					else
					{
						log.WriteLine("[失败] " + name + " —— " + ex.Message);
						Interlocked.Increment(ref stat[1]);
						progress.Report(new ProgressUpdate { Index = idx, Total = total, FileName = src, Status = ItemStatus.Failed, Phase = RunPhase.Plot, Note = ex.Message });
						AddRecord(new ConvertRecord { Index = idx, DwgPath = src ?? files[idx], Status = ItemStatus.Failed, Reason = ex.Message, Seconds = fileSw.Elapsed.TotalSeconds });
					}
				}
				finally
				{
					// v4.12：长时转换提醒已删除（原 v4.8 需求③ 的 slowTimer 释放逻辑一并移除）
				}
		}
		// v3.4：worker 收尾统一关闭兜底复用实例（避免每文件冷启动；仅关工具自启实例）
		if (fallbackConv != null && fallbackConv.OwnsInstance)
		{
			try { fallbackConv.Shutdown(); } catch { }
		}
	}

	/// <summary>v3.4：COM 出图兜底（实例跨文件复用 + 并发限流）。
	/// 此前每文件新建→转换→关闭，35 张兜底 = 35 次 AutoCAD 冷启动（每次 15~40s）；
	/// 且无并发限制，8 个出图线程可能同时拉起 8 个完整 AutoCAD 导致资源耗尽、进度卡死。
	/// 现在：同一 worker 线程内的兜底复用同一实例（懒加载），线程收尾统一 Shutdown；
	/// 全工具同时最多 FallbackMax=4 个 COM 转换（含实例创建）。停止时实例被强杀，
	/// COM 调用快速抛 RPC 错误失败返回。</summary>
	private bool FallbackComPersistent(ref CadConverter conv, string dwg, string pdf, LogWriter log, bool tight, PrintOptions print, string plotterOverride,
		double[] knownLl, double[] knownUr, out string reason)
	{
		reason = "";
		bool gateEntered = false;
		try
		{
			// 限流等待（可中断）：用户停止时不再等待槽位
			while (!_fallbackGate.Wait(200))
			{
				if (_cancelRequested) { reason = "已停止"; return false; }
			}
			gateEntered = true;
			if (_cancelRequested) { reason = "已停止"; return false; }
			OleMsgFilter.Install();
			if (conv == null) conv = new CadConverter();
			CadResult r = conv.Convert(dwg, pdf,
				// v4.15：把 COM 分段计时写进日志（含图片图纸走 COM 高清出图，需要能看出慢在哪）
				msg => { try { log?.WriteLine("[COM] " + System.IO.Path.GetFileName(dwg) + " " + msg); } catch { } },
				tight, print,
				string.IsNullOrEmpty(plotterOverride) ? PrintQuality.PlotterFor(dwg, print.HighQualityThresholdBytes) : plotterOverride,
				knownLl, knownUr);
			if (r.Ok) { reason = ""; return true; }
			reason = r.Message ?? "COM 转换失败";
			return false;
		}
		catch (Exception ex)
		{
			// 停止时实例被强杀 → COM 调用抛 RPC/对象断开错误，按失败返回即可
			reason = ex.Message;
			return false;
		}
		finally
		{
			if (gateEntered) try { _fallbackGate.Release(); } catch { }
		}
	}

	/// <summary>整批回退 COM（accoreconsole 不可用）。批次/线程编排等同 v2.3.1，WorkerLoop 为 v2.3.1 原逻辑。</summary>
	private void RunComOnly(List<string> files, int total, string outputFolder, int workerCount,
		IProgress<ProgressUpdate> progress, int[] booted, int[] stat, LogWriter log, bool tight, PrintOptions print, string commonRoot)
	{
		int[] next = new int[1];
		List<Thread> list = new List<Thread>(workerCount);
		for (int i = 0; i < workerCount; i++)
		{
			int wi = i;
			Thread thread = new Thread((ThreadStart)delegate
			{
				WorkerLoop(wi, next, files, total, outputFolder, progress, booted, stat, log, tight, print, commonRoot);
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.IsBackground = true;
			list.Add(thread);
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0)
			{
				Thread.Sleep(3500); // COM 实例冷启动慢，沿用 v2.3.1 错开
			}
			list[i].Start();
		}
		foreach (Thread item in list)
		{
			item.Join();
		}
	}

	private void WorkerLoop(int wi, int[] next, List<string> files, int total, string outputFolder, IProgress<ProgressUpdate> progress, int[] booted, int[] stat, LogWriter log, bool tight, PrintOptions print, string commonRoot)
	{
		OleMsgFilter.Install();
		CadConverter cadConverter = null;
		// v4.5：PDF 输出结构（true=全部放 PDF文件\ 根下；false=按原文件夹结构）
		bool flatOutput = print != null && print.FlatOutput;
		try
		{
			// v2.2 优化：检测到已运行的 AutoCAD 时，仅第一个 worker(wi==0) 优先附加，
			// 其余 worker 仍启动独立的 AutoCAD 实例，避免多个 worker 抢用同一实例。
			// 附加可绕过 COM 类工厂，规避 80040154 启动失败（详见《AutoCAD连接失败问题报告》）。
			if (wi == 0 && ComStarter.TryGetRunningInstance(out object runningApp))
			{
				cadConverter = CadConverter.Attach(runningApp);
				log.WriteLine("[信息] 检测到已运行的 AutoCAD，worker 0 优先附加该实例。");
			}
			else
			{
				cadConverter = new CadConverter();
			}
			int num = Interlocked.Increment(ref booted[0]);
			progress.Report(new ProgressUpdate
			{
				Index = 0,
				Total = 0,
				FileName = "",
				Status = ItemStatus.Running,
				Booting = true,
				Phase = RunPhase.Plot,
				Note = "正在启动 AutoCAD 实例 " + num + " …"
			});
		}
		catch (Exception ex)
		{
			log.WriteLine("[失败] worker" + wi + " —— AutoCAD 启动失败：" + ex.Message);
			return;
		}
		try
		{
			while (true)
			{
				if (_cancelRequested) break;
				int item2 = Interlocked.Increment(ref next[0]) - 1;
				if (item2 >= total) break;
				if (_cancelRequested)
				{
					string fileNameWithoutExtension2 = Path.GetFileNameWithoutExtension(files[item2]);
					log.WriteLine("[取消] " + fileNameWithoutExtension2 + " —— 用户停止");
					progress.Report(new ProgressUpdate
					{
						Index = item2,
						Total = total,
						FileName = files[item2],
						Status = ItemStatus.Cancelled,
						Phase = RunPhase.Plot,
						Note = "已停止"
					});
					// v4.5：登记转换结果（日志失败分类数据源）
					AddRecord(new ConvertRecord { Index = item2, DwgPath = files[item2], Status = ItemStatus.Cancelled, Reason = "已停止" });
					continue;
				}
				string text = null;
				string fileNameWithoutExtension3 = null;
				try
				{
					text = files[item2];
					fileNameWithoutExtension3 = Path.GetFileNameWithoutExtension(text);
					// v3.9.1/v4.5：PDF 输出目录——按原文件夹结构，或全部放 PDF文件\ 根下（用户可选）
					string subDir2 = flatOutput ? "" : GetRelativeSubDir(text, commonRoot);
					string pdfDir2 = string.IsNullOrEmpty(subDir2) ? Path.Combine(outputFolder, PdfFolderName) : Path.Combine(outputFolder, PdfFolderName, subDir2);
					// v4.5：扁平模式下同名文件用工作名（子目录标识_基名）避免并发出图相互覆盖；
					// 默认结构模式保持纯基名（各子目录物理隔离无冲突，与旧版输出名一致）
					string pdfPath = UniquePdfPath(pdfDir2, flatOutput ? WorkBaseName(text, fileNameWithoutExtension3) : fileNameWithoutExtension3);
					progress.Report(new ProgressUpdate
					{
						Index = item2,
						Total = total,
						FileName = text,
						Status = ItemStatus.Running,
						Phase = RunPhase.Plot,
						Note = ""
					});
					CadResult cadResult = cadConverter.Convert(text, pdfPath, delegate
					{
					}, tight, print, PrintQuality.PlotterFor(text, print.HighQualityThresholdBytes));
					if (cadResult.Ok)
					{
						Interlocked.Increment(ref stat[0]);
						log.WriteLine("[成功] " + fileNameWithoutExtension3);
					}
					else
					{
						Interlocked.Increment(ref stat[1]);
						log.WriteLine("[失败] " + fileNameWithoutExtension3 + " —— " + cadResult.Message);
					}
					// v4.5：登记转换结果（COM 直出路径，无图框/纸型数据）
					AddRecord(new ConvertRecord
					{
						Index = item2,
						DwgPath = text,
						Status = cadResult.Ok ? ItemStatus.Success : ItemStatus.Failed,
						Reason = cadResult.Ok ? "" : (cadResult.Message ?? "COM 转换失败"),
						// v4.11：失败也记录期望路径——收尾"迟到 PDF 复核"需要它改判假失败
						PdfPath = pdfPath,
					});
					progress.Report(new ProgressUpdate
					{
						Index = item2,
						Total = total,
						FileName = text,
						Status = (cadResult.Ok ? ItemStatus.Success : ItemStatus.Failed),
						Phase = RunPhase.Plot,
						Note = cadResult.Message
					});
				}
				catch (Exception ex)
				{
					log.WriteLine("[失败] " + fileNameWithoutExtension3 + " —— " + ex.Message);
					Interlocked.Increment(ref stat[1]);
					progress.Report(new ProgressUpdate
					{
						Index = item2,
						Total = total,
						FileName = text,
						Status = ItemStatus.Failed,
						Phase = RunPhase.Plot,
						Note = ex.Message
					});
					AddRecord(new ConvertRecord { Index = item2, DwgPath = text ?? files[item2], Status = ItemStatus.Failed, Reason = ex.Message });
				}
				finally
				{
					// v4.12：长时转换提醒已删除（原 v4.8 需求③ 的 slowTimer2 释放逻辑一并移除）
				}
			}
		}
		finally
		{
			// v2.3：仅关闭本工具启动的 AutoCAD 实例；附加的用户已开实例不关。
			if (cadConverter != null && cadConverter.OwnsInstance)
			{
				try { cadConverter.Shutdown(); } catch { }
			}
		}
	}
}
