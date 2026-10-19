using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DwgToPdf.Batch;

internal sealed partial class ConverterEngine
{
	// v3.0 优化：PDF 统一输出到输出文件夹下的子文件夹，与 frame_cache.json / logs 分离
	// v4.17 及以前：固定名 "PDF文件"。
	// v4.17 修订三：改为按模板（AppSettings.PdfFolderTemplate，默认 "PDF-HH:mm:ss"）每轮展开一次，
	// 使每批 PDF 归入独立文件夹；此处仅保留常量作为"缺省/兜底"名与历史引用基准。
	internal const string PdfFolderName = "PDF文件";

	/// <summary>v4.17 修订三：本轮转换的 PDF 子文件夹名（RunCore 开始时按模板 + 本轮开始时刻解析一次）。
	/// 多个 plot worker 线程启动前完成赋值、之后只读，无需加锁。</summary>
	private string _pdfSubFolder = PdfFolderName;

	private volatile bool _cancelRequested;
	/// <summary>v4.3：阶段1 图框校验不通过的文件路径集合。阶段2/3 跳过这些 file，避免硬出坏 PDF。
	/// 校验在阶段1 末尾填入；阶段2/3 启动后只读，无并发修改。</summary>
	private HashSet<string> _frameRejectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	// v4.5：转换结果记录（多 worker 并发写入 → lock 保护），RunCore 结束输出日志失败分类摘要。
	// v4.4 的 CSV 转换报告文件已按用户反馈移除（日志文本报告已够用）。
	private readonly object _recordLock = new object();
	private readonly List<ConvertRecord> _records = new List<ConvertRecord>();

	/// <summary>v4.5：跨子目录同名源图的"工作名"映射（dwgPath → 含子目录标识的基名）。
	/// 同名文件的预处理副本与扁平输出 PDF 都用工作名，消除并发下同名文件相互覆盖的竞态。
	/// RunCore 开始时填充，之后只读（多 worker 共享）。</summary>
	private Dictionary<string, string> _workNameMap;

	/// <summary>出图取件顺序 —— **固定为"先小文件、后大文件"**（按 dwg 大小升序）。
	/// 用户明确要求该顺序不得更改：小图先出，批量结果尽快可见、也便于中途停止时已完成的
	/// 都是可直接使用的图；大 dwg（走高质量 pc3、单张最慢）排在最后，不阻塞前面绝大多数文件。
	/// 取大小失败（文件被占用/权限）时按 0 处理，保持稳定排序。</summary>
	private static List<string> PlotOrderBySizeAsc(List<string> files)
	{
		try
		{
			return files.OrderBy(delegate (string f)
			{
				try { return new FileInfo(f).Length; } catch { return 0L; }
			}).ToList();
		}
		catch { return files; }
	}

	/// <summary>v4.14：出图池启动（原阶段3 的"建线程 + 错峰启动 + 等待"抽出，串行/流水线共用）。
	/// plotFiles 的实际顺序即取件顺序（调用方保证"先小后大"）。</summary>
	private void LaunchPlotWorkers(List<string> plotFiles, List<string> files, int total, string outputFolder,
		IProgress<ProgressUpdate> progress, int[] booted, int[] stat, LogWriter log, bool tight, PrintOptions print,
		string accore, string tmpDir, double marginMm, Dictionary<string, string> copies, string commonRoot,
		Dictionary<string, FrameCache.Entry> frameSnapshot, bool accoreSetup, int plotWorkers)
	{
		if (plotFiles == null || plotFiles.Count == 0) return;
		int workers = Math.Max(1, Math.Min(plotWorkers, plotFiles.Count));
		int[] waitNext = new int[1]; // 共享原子计数器：出图动态任务池
		List<Thread> list = new List<Thread>(workers);
		for (int i = 0; i < workers; i++)
		{
			int wi = i;
			Thread thread = new Thread((ThreadStart)delegate
			{
				WorkerLoopV3(wi, waitNext, plotFiles, total, outputFolder, progress, booted, stat, log, tight, print, accore, tmpDir, marginMm, copies, commonRoot, frameSnapshot, accoreSetup);
			});
			thread.SetApartmentState(ApartmentState.STA); // 兜底 COM 需要 STA
			thread.IsBackground = true;
			list.Add(thread);
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0)
			{
				Thread.Sleep(800); // 错开冷启动，避免多个 accoreconsole 同时争抢
			}
			list[i].Start();
		}
		foreach (Thread item in list)
		{
			item.Join();
		}
	}

	/// <summary>v4.14：有界流水线（分块交错）——把"识别"与"出图"重叠：
	///   识别块0 → [后台识别块1] ∥ 出图块0 → [后台识别块2] ∥ 出图块1 → …
	/// 块按"先小文件后大文件"切分（不改变用户要求的出图顺序）；识别块 6 路 ∥ 出图 plotWorkers 路，
	/// 总会话数受控（手册第4章：并发过高反而更慢、甚至卡死）。
	/// 判定规则完全复用：识别链（公司版→通用版补识别→重试）、FrameValidator、失败记录、
	/// 缓存写入、质量核查与 COM 兜底都在既有代码里，本方法只安排先后与并行。
	/// 返回 true 表示已接管识别与出图；串行路径在 usePipeline=false 时照旧执行。</summary>
	private bool RunPipelineBatched(List<string> need, List<string> files, int total, string outputFolder,
		IProgress<ProgressUpdate> progress, int[] booted, int[] stat, LogWriter log, PrintOptions print, string accore,
		string tmpDir, double marginMm, Dictionary<string, string> copies, string commonRoot,
		int plotWorkers, bool tight, ConvertProfile profile, FrameExtractor extractor, out string timingNote)
	{
		timingNote = null;
		var swAll = System.Diagnostics.Stopwatch.StartNew();
		List<string> order = PlotOrderBySizeAsc(need);
		int recogWorkers = Math.Max(2, Math.Min(6, SmartConcurrency.RecognitionWorkers(need.Count) / 2));
		int chunkCount = Math.Max(2, Math.Min(8, (order.Count + 29) / 30));   // 每块约 30 张 → 2~8 块
		int chunkSize = (order.Count + chunkCount - 1) / chunkCount;
		chunkCount = (order.Count + chunkSize - 1) / chunkSize;
		log.WriteLine("阶段1/3 流水线: " + order.Count + " 张分 " + chunkCount + " 块交错执行（识别 "
			+ recogWorkers + " 路 ∥ 出图 " + plotWorkers + " 路），块内保持\"先小后大\"");
		var recogDone = new int[1];
		Task<Dictionary<string, FrameBox>> recogTask = null;
		for (int c = 0; c < chunkCount; c++)
		{
			if (_cancelRequested) break;
			int from = c * chunkSize;
			int len = Math.Min(chunkSize, order.Count - from);
			if (len <= 0) break;
			var chunk = order.GetRange(from, len);
			string recogErrorNote = null;   // CQ-01：本块后台识别的真实异常原因（取消/正常=null）
			Dictionary<string, FrameBox> boxes;
			if (c == 0)
			{
				boxes = RecognizeChunkPipeline(chunk, extractor, profile, tmpDir, recogWorkers, log, progress, total, need.Count, recogDone);
			}
			else
			{
				// CQ-01：取消与真实异常分开——取消保持原语义；真实异常记全貌并透传原因（chunk 失败范围不变）
				try { boxes = recogTask.Result; }
				catch (OperationCanceledException)
				{
					// 取消语义保持：识别被用户停止，置空字典让本 chunk 走取消路径
					log.WriteLine("[信息] 后台识别已取消（用户停止）。");
					boxes = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
				}
				catch (Exception ex)
				{
					// CQ-01：识别失败用户必须知道——记全异常（类型/消息/堆栈）并透传真实原因
					log.WriteLine("[失败] 后台识别异常：" + ex.GetType().Name + ": " + ex.Message
						+ "\n" + ex.StackTrace);
					boxes = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
					recogErrorNote = RecognitionFailureNote(ex);
				}
			}
			// 启动"下一块"的后台识别 —— 与本块出图并行（这是流水线的收益来源）
			int nfrom = (c + 1) * chunkSize;
			if (nfrom < order.Count)
			{
				var nextChunk = order.GetRange(nfrom, Math.Min(chunkSize, order.Count - nfrom));
				recogTask = Task.Factory.StartNew(delegate
				{
					return RecognizeChunkPipeline(nextChunk, extractor, profile, tmpDir, recogWorkers, log, progress, total, need.Count, recogDone);
				});
			}
			// 校验 + 写缓存 + 刷新快照（与串行路径同口径）
			Dictionary<string, FrameBox> passed = ValidateChunkPipeline(chunk, boxes, files, total, outputFolder, progress, log, recogErrorNote);
			Dictionary<string, FrameCache.Entry> snapshot = FrameCache.Snapshot(outputFolder);
			// 出图本块（顺序 = chunk 顺序 = 先小后大）
			var plotFiles = new List<string>(chunk.Count);
			foreach (var f in chunk) if (passed.ContainsKey(f)) plotFiles.Add(f);
			var swChunk = System.Diagnostics.Stopwatch.StartNew();
			LaunchPlotWorkers(plotFiles, files, total, outputFolder, progress, booted, stat, log, tight, print,
				accore, tmpDir, marginMm, copies, commonRoot, snapshot, true, plotWorkers);
			log.WriteLine("[耗时] 流水线第 " + (c + 1) + "/" + chunkCount + " 块: 出图 " + plotFiles.Count + " 张，"
				+ swChunk.Elapsed.TotalSeconds.ToString("0.0") + " 秒（含并行识别）");
			extractor.MaxWorkers = 0;   // 复位并发上限，避免影响后续补识别/重试路径
		}
		if (recogTask != null)
		{
			try { recogTask.Wait(2000); } catch { }   // 取消等提前退出时，等后台识别收尾
			recogTask = null;
		}
		swAll.Stop();
		timingNote = "[耗时] 流水线（识别与出图并行）: " + swAll.Elapsed.TotalSeconds.ToString("0.0") + " 秒";
		return true;
	}

	/// <summary>v4.14：识别"一块"文件 —— 复用串行路径的三段链（公司版→通用版补识别→通用版重试），
	/// 仅把识别并发压到 recogWorkers（与出图池并行时控制总会话数）。</summary>
	private Dictionary<string, FrameBox> RecognizeChunkPipeline(List<string> chunk, FrameExtractor extractor,
		ConvertProfile profile, string tmpDir, int recogWorkers, LogWriter log, IProgress<ProgressUpdate> progress,
		int total, int needTotal, int[] recogDone)
	{
		extractor.MaxWorkers = recogWorkers;
		Action<string, bool> onDone = delegate (string f, bool ok)
		{
			int n = Interlocked.Increment(ref recogDone[0]);
			progress.Report(new ProgressUpdate
			{
				Index = 0,
				Total = total,
				FileName = "",
				Status = ItemStatus.Running,
				Booting = true,
				Phase = RunPhase.FrameExtract,
				PhaseIndex = n,
				PhaseTotal = needTotal,
				Note = "并行识别 " + n + "/" + needTotal + "（出图同时进行）"
			});
		};
		Dictionary<string, FrameBox> boxes = profile.IsCompany
			? extractor.RunExtractCompany(chunk, tmpDir, onDone, () => _cancelRequested)
			: extractor.RunExtract(chunk, tmpDir, onDone, () => _cancelRequested);
		if (profile.IsCompany)
		{
			var leftover = new List<string>();
			foreach (var f in chunk) if (!boxes.ContainsKey(f)) leftover.Add(f);
			if (leftover.Count > 0)
			{
				log.WriteLine("[信息] 公司版未识别 " + leftover.Count + " 张，改用通用版识别: "
					+ string.Join("; ", leftover.ConvertAll(Path.GetFileName)));
				var more = extractor.RunExtract(leftover, tmpDir, null, () => _cancelRequested);
				foreach (var kv in more) if (!boxes.ContainsKey(kv.Key)) boxes[kv.Key] = kv.Value;
				log.WriteLine("[信息] 通用版补识别: 补识别 " + more.Count + "/" + leftover.Count);
			}
		}
		var retry = new List<string>();
		foreach (var f in chunk) if (!boxes.ContainsKey(f)) retry.Add(f);
		if (retry.Count > 0)
		{
			foreach (var f in retry) log.WriteLine("[信息] 阶段1 图框识别失败，重试: " + Path.GetFileName(f));
			var more = extractor.RunExtract(retry, tmpDir, null, () => _cancelRequested);
			foreach (var kv in more) if (!boxes.ContainsKey(kv.Key)) boxes[kv.Key] = kv.Value;
			log.WriteLine("[信息] 阶段1 重试: 补识别 " + more.Count + "/" + retry.Count);
		}
		return boxes;
	}

	/// <summary>v4.14：校验"一块"识别结果并写缓存 —— 与串行路径同口径：FrameValidator 判定、
	/// 不通过即报 UI 失败并登记记录（进失败栏、不进阶段3），通过者写 frame_cache.json。
	/// 返回通过校验的图框表。
	/// recogErrorNote：v4.17 修订十（CQ-01）本块识别异常真实原因；非空时附到块失败清单行，
	/// 替换误导性的裸失败列表（chunk 失败文件范围不变，仍以 passed 为准）。</summary>
	private Dictionary<string, FrameBox> ValidateChunkPipeline(List<string> chunk, Dictionary<string, FrameBox> boxes,
		List<string> files, int total, string outputFolder, IProgress<ProgressUpdate> progress, LogWriter log,
		string recogErrorNote = null)
	{
		var passedBoxes = new Dictionary<string, FrameBox>(StringComparer.OrdinalIgnoreCase);
		int rejectedCnt = 0;
		foreach (var kv in boxes)
		{
			string hint = string.IsNullOrEmpty(kv.Value.BlockHint) ? null
				: "；图框疑似为块（" + kv.Value.BlockHint + "），建议在设置中开启\"识别块图框\"后重转";
			if (FrameValidator.IsAcceptable(kv.Value, out string why))
			{
				passedBoxes[kv.Key] = kv.Value;
				if (string.Equals(kv.Value.Source, "EXTENT", StringComparison.OrdinalIgnoreCase))
				{
					log.WriteLine("[信息] " + Path.GetFileName(kv.Key)
						+ " —— 识别框未覆盖整幅图纸，已按全图范围出图（"
						+ kv.Value.Width.ToString("0") + "×" + kv.Value.Height.ToString("0") + "mm），避免内容被裁");
				}
				else if (kv.Value.ExtentOut && kv.Value.Extent != null)
				{
					log.WriteLine("[提示] " + Path.GetFileName(kv.Key)
						+ " —— 图框外检测到其他内容（识别框 "
						+ kv.Value.Width.ToString("0") + "×" + kv.Value.Height.ToString("0") + "mm，全图 "
						+ (kv.Value.Extent[2] - kv.Value.Extent[0]).ToString("0") + "×"
						+ (kv.Value.Extent[3] - kv.Value.Extent[1]).ToString("0")
						+ "mm），出图可能缺内容，请人工确认");
				}
				if (hint != null)
					log.WriteLine("[提示] " + Path.GetFileName(kv.Key) + " —— 检测到大块疑似图框块（"
						+ kv.Value.BlockHint + "），若出图内容缺失可开启\"识别块图框\"后重转");
			}
			else
			{
				rejectedCnt++;
				_frameRejectedPaths.Add(kv.Key);
				log.WriteLine("[失败] " + Path.GetFileName(kv.Key) + " —— 图框问题：未识别到正规图框。" + why + hint);
				int ridx = files.IndexOf(kv.Key);
				if (ridx >= 0)
				{
					progress.Report(new ProgressUpdate
					{
						Index = ridx,
						Total = total,
						FileName = kv.Key,
						Status = ItemStatus.Failed,
						// 不带 Booting=true：让 UI 把该行从待处理栏移到失败栏（同串行路径注释）
						Phase = RunPhase.FrameExtract,
						Note = "图框问题：" + ShortFrameReason(why)
					});
				}
				AddRecord(new ConvertRecord
				{
					Index = ridx,
					DwgPath = kv.Key,
					Status = ItemStatus.Failed,
					Reason = "图框问题：未识别到正规图框（" + why + "）" + hint,
				});
			}
		}
		if (passedBoxes.Count > 0)
		{
			var entries = FrameCache.LoadEntries(outputFolder);
			foreach (var kv in passedBoxes)
			{
				entries.RemoveAll(e => string.Equals(e.Path, kv.Key, StringComparison.OrdinalIgnoreCase));
				var fi = new FileInfo(kv.Key);
				entries.Add(new FrameCache.Entry
				{
					Path = kv.Key,
					Length = fi.Length,
					MtimeUtc = fi.LastWriteTimeUtc.Ticks,
					MinX = kv.Value.MinX,
					MinY = kv.Value.MinY,
					MaxX = kv.Value.MaxX,
					MaxY = kv.Value.MaxY,
					Source = kv.Value.Source,
					ImageCount = kv.Value.ImageCount,   // v4.15：图片/嵌入对象/参考底图总数
					RasterCount = kv.Value.RasterCount,             // v4.15：真光栅数
					RasterMissingCount = kv.Value.RasterMissingCount, // v4.15：外部图缺失数
					G76 = kv.Value.G76,               // v4.16：Model 布局着色打印组码 76
				});
			}
			FrameCache.Save(outputFolder, entries);
		}
		var missing = new List<string>();
		foreach (var f in chunk) if (!passedBoxes.ContainsKey(f) && !_frameRejectedPaths.Contains(f)) missing.Add(f);
		if (missing.Count > 0)
			log.WriteLine("阶段1 完成(本块): 识别 " + passedBoxes.Count + " 份有效图框，失败 " + missing.Count
				+ "（" + string.Join("; ", missing.ConvertAll(Path.GetFileName).ToArray()) + "）（失败文件走 COM 兜底）"
				+ (string.IsNullOrEmpty(recogErrorNote) ? "" : "；" + recogErrorNote));
		if (rejectedCnt > 0)
			log.WriteLine("[信息] 阶段1 图框校验：拒绝 " + rejectedCnt + " 份（图框问题，详见上方 [失败] 行）");
		return passedBoxes;
	}

	/// <summary>v4.15：列出含光栅图片（IMAGE 实体）的图纸，并说明其清晰度决定因素。
	/// 背景（用户反馈"含图片图纸需要调光栅 DPI 1200"）：实测 AutoCAD PDF 导出**把已附加的
	/// 光栅图像按原始像素嵌入**，出图侧参数（pc3 的 Raster_Limit / 会话内 RASTERDPI）均不改变
	/// 其分辨率（三组对照 PDF 逐字节相同，详见手册第26章）。故这里只做"告知 + 建议"，
	/// 不提供无效的开关。</summary>
	private static void WriteImageDrawingsNote(LogWriter log, Dictionary<string, FrameBox> boxes, PrintOptions print)
	{
		try
		{
			if (log == null || boxes == null || boxes.Count == 0) return;
			int dpi = print != null ? print.RasterImageDpi : 0;
			var names = new List<string>();
			foreach (var kv in boxes)
			{
				// v4.15：只列"真光栅图片"图纸（OLE 嵌入图片/参考底图不算，它们仍走常规通道）
				if (kv.Value != null && PrintQuality.UseRasterBoost(kv.Value.RasterCount, kv.Value.RasterMissingCount, dpi))
					names.Add(Path.GetFileName(kv.Key));
			}
			if (names.Count == 0) return;
			names.Sort(StringComparer.OrdinalIgnoreCase);
			string shown = "";
			for (int i = 0; i < names.Count && i < 6; i++)
			{
				if (i > 0) shown += "、";
				shown += names[i];
			}
			if (names.Count > 6) shown += " 等";
			if (dpi > 0)
			{
				log.WriteLine("[信息] 含光栅图片(IMAGE)图纸 " + names.Count + " 张（" + shown + "）："
					+ "将改用 COM + 高清打印配置（光栅图像质量 " + dpi + "dpi）出图，单张更慢但图片清晰"
					+ "（accoreconsole 的 -EXPORT 不认打印质量，会把图片压到 ~26-44dpi）。");
			}
			else
			{
				log.WriteLine("[提示] 含光栅图片图纸 " + names.Count + " 张（" + shown + "）："
					+ "当前未开启高清出图；若打印时图片发虚，可在「设置」中把"
					+ "\"含图片图纸的高清出图\"设为 4800dpi 重转（该类图纸会改走 COM，单张更慢）。");
			}
			// v4.15：光栅图片**外部文件缺失**的图纸单独告知（用户可感知，避免"为什么这张没变清晰"）
			// 依据（2026-09-16 实测）：这类图纸走 COM 打印会输出空白页，故自动改回常规通道；
			// 同时图片本身在 PDF 里也不会出现（源图片文件找不到），需要图档方补图。
			var missing = new List<string>();
			foreach (var kv in boxes)
			{
				if (kv.Value != null && kv.Value.RasterMissingCount > 0) missing.Add(Path.GetFileName(kv.Key));
			}
			if (missing.Count > 0)
			{
				missing.Sort(StringComparer.OrdinalIgnoreCase);
				string ms = "";
				for (int i = 0; i < missing.Count && i < 6; i++)
				{
					if (i > 0) ms += "、";
					ms += missing[i];
				}
				if (missing.Count > 6) ms += " 等";
				log.WriteLine("[提示] 有 " + missing.Count + " 张图纸的光栅图片**引用了找不到的外部文件**"
					+ "（已自动按常规通道出图，避免空白页）：" + ms);
				log.WriteLine("       这类图纸的图片不会出现在 PDF 中，需要图档提供方补回图片文件后重转。");
			}
		}
		// best-effort：本段提示写失败不影响转换主流程
		catch { }
	}

	/// <summary>v4.12：图框失败原因简称（供界面列表显示）——取括号前的定性短语，最长 18 字。
	/// 完整说明（含处理建议）仍写在日志行内，避免界面提示过长遮挡内容。</summary>
	private static string ShortFrameReason(string why)
	{
		if (string.IsNullOrEmpty(why)) return "未识别到正规图框";
		int i = why.IndexOf('（');
		if (i <= 0) i = why.IndexOf('(');
		string s = i > 0 ? why.Substring(0, i) : why;
		if (s.Length > 18) s = s.Substring(0, 18) + "…";
		return s;
	}

	/// <summary>v4.15：收集"瞬态类失败"文件（供批次结束后自动重试一次）。
	/// 排除图框类失败（必然复现）与策略判定为确定性的原因，见 RunPolicy.ShouldRetryAfterBatch。</summary>
	private List<string> CollectTransientFailures()
	{
		var list = new List<string>();
		try
		{
			List<ConvertRecord> snapshot;
			lock (_recordLock) { snapshot = new List<ConvertRecord>(_records); }
			foreach (var r in snapshot)
			{
				if (r == null || r.Status != ItemStatus.Failed || string.IsNullOrEmpty(r.DwgPath)) continue;
				if (_frameRejectedPaths.Contains(r.DwgPath)) continue;      // 图框问题：重试必然复现
				if (!RunPolicy.ShouldRetryAfterBatch(r.Reason)) continue;
				if (!list.Contains(r.DwgPath)) list.Add(r.DwgPath);
			}
		}
		// best-effort：重试清单收集失败按无重试处理
		catch { }
		return list;
	}

	/// <summary>v4.5：登记一条转换结果（线程安全；重复登记同一文件时以后者覆盖——重试/兜底轮取最终态）。</summary>
	private void AddRecord(ConvertRecord r)
	{
		if (r == null || string.IsNullOrEmpty(r.DwgPath)) return;
		lock (_recordLock)
		{
			_records.RemoveAll(x => string.Equals(x.DwgPath, r.DwgPath, StringComparison.OrdinalIgnoreCase));
			_records.Add(r);
		}
	}

	/// <summary>v4.5：日志输出失败原因分类摘要（如"失败原因分类: 图框问题 2、质量核查不通过 1"）。
	/// v4.7：通用版模式下出现图框类失败时，追加模式防呆提示（公司版模板库对标准图框识别率更高）。</summary>
	private void LogFailBreakdown(LogWriter log, ConvertProfile profile)
	{
		try
		{
			List<ConvertRecord> snapshot;
			lock (_recordLock) { snapshot = new List<ConvertRecord>(_records); }
			string s = ConvertReport.SummarizeFailures(snapshot);
			if (!string.IsNullOrEmpty(s))
			{
				log.WriteLine("失败原因分类: " + s);
				// v4.7 防呆：通用版逐层识别易把公司标准图框图纸判为"图框问题"，提示切模式
				if (profile != null && !profile.IsCompany
					&& (s.Contains("图框问题") || s.Contains("无图框数据")))
					log.WriteLine("提示: 本次为通用版模式（逐层识别）。若含公司标准图框图纸，建议切换\"公司版\"模式后重试，图框识别率更高。");
			}
		}
		// best-effort：失败分类摘要失败不影响主流程
		catch { }
	}

	// v3.4：COM 兜底并发信号量（上限 FallbackMax=4）。完整 AutoCAD 实例内存占用高，
	// 无限制时 8 个出图线程可能同时拉起 8 个 AutoCAD 导致资源耗尽、进度卡死。
	private readonly SemaphoreSlim _fallbackGate = new SemaphoreSlim(SmartConcurrency.FallbackMax, SmartConcurrency.FallbackMax);

	// v4.15：含图片图纸的高清出图提示只写一次（避免含图片的每张都刷日志）
	private volatile bool _rasterBoostLogged;
	private volatile bool _rasterBoostWarned;
	// v4.16：含图片/嵌入对象(OLE2FRAME)/参考底图图纸改走 COM 的提示只写一次
	private volatile bool _imageComLogged;

	// v3.6：转换开始前 Activity Insights 伴随进程快照（AcEventSync/AcQMod 等）。
	// 转换结束/停止时只清理快照之后新出现的进程，不碰用户已有的。
	private volatile HashSet<int> _insightsBefore;

	/// <summary>本轮转换是否已被请求停止（只读）。由收尾逻辑据此区分「已完成 / 已停止」并决定是否落定进度条；由 Cancel() 置位、下一轮入口 ResetCancel() 复位。</summary>
	public bool IsCancelled => _cancelRequested;

	/// <summary>请求停止本轮转换：置取消标志，并强制结束本工具自启的 AutoCAD 进程——让阻塞中的 COM 调用因 RPC 断开快速失败返回，界面立即恢复响应（此前只能等调用自然结束，可能卡死）；同时清理本轮新起的 Activity Insights 伴随进程。绝不触碰用户未登记的实例。由界面「停止」按钮 / Esc 触发。</summary>
	public void Cancel()
	{
		_cancelRequested = true;
		// v3.4 整改③：停止时强制结束工具自启的 AutoCAD 进程，让阻塞中的 COM 调用
		// 因 RPC 断开快速失败返回，UI 立即恢复响应（此前只能等 COM 调用自然结束，可能卡死）。
		// 绝不触碰未登记（用户）实例。
		try { ComStarter.KillAllOwned(); } catch { }
		// v3.6：Activity Insights 伴随进程不会随 acad 退出，停止时一并清理本次新起的
		try { ComStarter.KillActivityInsightsSince(_insightsBefore); } catch { }
	}

	/// <summary>清除取消标志，供新一轮转换开始前复位（两个 RunFiles 重载入口都会先调用），避免上一轮的停止请求影响本轮。</summary>
	public void ResetCancel()
	{
		_cancelRequested = false;
	}

	/// <summary>v3.2：自测用。为 true 时阶段3 出图失败后不再回退 COM（用于假 dwg 冒烟，
	/// 避免 24 次真实 AutoCAD 实例启停拖慢/挂死自测），文件直接判失败。GUI 恒为 false。</summary>
	internal bool NoComFallback { get; set; }

	// v4.6：删除未使用的 Run(inputFolder)×2 与 CollectDwgFiles()——GUI/自测试统一走
	// RunFiles（文件清单由 MainForm 收集，支持拖拽/粘贴/失败续转），死代码。

	/// <summary>转换入口（文件清单版，不指定转换模式）：先复位取消标志，再转交 RunCore 执行，转换模式取默认。供不关心「公司 / 通用」模式的调用方使用（自测试多处走此重载）。</summary>
	public void RunFiles(List<string> files, string outputFolder, IProgress<ProgressUpdate> progress, bool tight = false, PrintOptions print = null)
	{
		_cancelRequested = false;
		RunCore(files, outputFolder, progress, tight, "文件清单: " + files.Count + " 个 dwg 文件", print);
	}
	/// <summary>转换入口（指定转换模式版）：与清单版相同，但显式传入 ConvertProfile（公司 / 通用模式）。界面「开始转换」走此重载。</summary>
	public void RunFiles(List<string> files, string outputFolder, IProgress<ProgressUpdate> progress, bool tight, PrintOptions print, ConvertProfile profile)
	{
		_cancelRequested = false;
		RunCore(files, outputFolder, progress, tight, "文件清单: " + files.Count + " 个 dwg 文件", print, profile);
	}

	/// <summary>转换执行核心（全流程唯一实现，两个 RunFiles 重载均转交至此）：排序（小文件优先）→ 清空图框校验集合与结果记录 →准备公共目录 / 工作名 / 图框缓存快照 / 日志（默认不落盘，仅 DWGTOPDF_LOG_DIR 设置时写文件）→ 启动阶段看门狗（15 分钟无日志进展即结束自启 AutoCAD 解除 COM 阻塞）→逐文件三阶段转换（识别 / 质量审查 / 出图）→ finally 收尾（停看门狗、删临时副本、落盘运行历史）。取消请求在各文件间隙生效。</summary>
	private void RunCore(List<string> files, string outputFolder, IProgress<ProgressUpdate> progress, bool tight, string sourceDesc, PrintOptions print = null, ConvertProfile profile = null)
	{
		profile ??= new ConvertProfile();
		// v4.8 需求①：按文件大小升序排序（小文件优先转换，大文件集中在最后）。
		// 小文件先出结果，用户尽早看到产出；读不到大小的文件排最后。UI 按 path 归栏，不受清单顺序影响。
		files = SortBySizeAsc(files);
		int total = files.Count;
		// v4.3：清空上一轮"图框校验失败"集合
		_frameRejectedPaths.Clear();
		// v4.5：PDF 输出结构（flatOutput：true=全部放 PDF文件\ 根下；false=按原文件夹结构）
		bool flatOutput = print != null && print.FlatOutput;
		lock (_recordLock) { _records.Clear(); }
		// v4.6：log 声明提前到 try 外（finally 已判 log != null）；准备段（公共目录/
		// 建目录/工作名/缓存快照/日志创建/进程快照）全部纳入 try——此前准备段任一失败
		// 会把异常直接抛向 UI 线程的 Task（无 catch），按钮/列表永不恢复。
		LogWriter log = null;
		string tmpDir = null;
		Dictionary<string, string> copies = null;
		// v4.11：阶段看门狗控制量（在 finally 中停表；v4.17 修订十一改事件等待）
		ManualResetEventSlim stopEvent = null;
		Thread watchdogThread = null;
		// v3.6：记录转换开始前已存在的 Activity Insights 进程，收尾只清理本次新起的。
		// v4.6：先置 null（准备段失败时 KillActivityInsightsSince(null) 不清理任何进程）
		_insightsBefore = null;
		// v4.15：运行历史（语料指纹 + 上次结果对比）——变量声明在 try 外，finally 里落盘
		var runWatch = System.Diagnostics.Stopwatch.StartNew();
		int[] runStatRef = null;
		string corpusFp = null;
		int corpusCount = 0;
		try
		{
			// v3.9.1：计算所有输入文件的公共祖先目录，用于保留输入文件夹结构输出 PDF
			string commonRoot = GetCommonRoot(files);
			Directory.CreateDirectory(outputFolder);
			// v4.17 修订三：PDF 子文件夹名按模板「每轮展开一次」——以**本轮开始时刻**（RunFiles 传入的
			// print.RunStartedAt，取不到则用当前时刻）展开 AppSettings.PdfFolderTemplate
		// （默认 "PDF-HH:mm:ss" → PDF-10：30：15，全角冒号；Windows 文件名不允许半角冒号），
			// 使每批 PDF 各自归入独立文件夹、互不覆盖。
			// 同一秒内再次转换时自动追加 -2、-3（用户确认的语义：绝不与已有批合并）。
			_pdfSubFolder = ResolvePdfSubFolder(outputFolder,
				print != null ? print.RunStartedAt : (DateTime?)null,
				print != null ? print.PdfFolderTemplate : null);
			Directory.CreateDirectory(Path.Combine(outputFolder, _pdfSubFolder));
			// v4.5：检测跨子目录同名源图 → 生成"工作名"（子目录标识_基名）。
			// 预处理副本恒用工作名（消除并发副本覆盖竞态）；扁平输出 PDF 用工作名（同名 PDF 相互覆盖）。
			BuildWorkNameMap(files, commonRoot);
			// v4.4：一次性加载图框缓存到内存快照（阶段1 统计 + 阶段3 出图共用，
			// 免去每文件重读磁盘解析 JSON；阶段1 识别落盘后刷新一次）
			var frameSnapshot = FrameCache.Snapshot(outputFolder);
			// v4.17：**默认不写日志文件** —— 输出文件夹不再生成 logs\ 目录与 *.log 文件（用户要求：
			// 转换完成后输出文件夹只留 PDF）。日志行经 LogWriter 回调实时镜像到界面日志面板。
			// 仅当显式设置 DWGTOPDF_LOG_DIR（自测/排查用）时才把日志写到该目录。
			string logDir = null;
			// best-effort 探测：读取失败按未设置走默认
			try { logDir = Environment.GetEnvironmentVariable("DWGTOPDF_LOG_DIR"); } catch { }
			string text = string.IsNullOrEmpty(logDir)
				? null
				: Path.Combine(logDir, "DwgToPdf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
			// v3.9：日志行实时镜像到界面（LogWriter 回调 → progress.Report(LogLine)）。
			// Progress<T> 线程安全：多 worker 并发写日志时 Post 到 UI 线程无冲突。
			log = new LogWriter(text, line => progress.Report(new ProgressUpdate { LogLine = line }));
			_insightsBefore = ComStarter.ActivityInsightsSnapshot();
			// v4.11：阶段看门狗 —— COM 收尾偶发 RPC 挂起会让引擎永久等待（实测曾挂 7 小时）。
			// 以"日志连续 15 分钟无写入"为停滞信号：告警并结束本工具自启的 AutoCAD 实例，
			// 使被阻塞的 COM 调用因 RPC 断开而快速失败（绝不触碰用户实例）。阈值 15 分钟 >
			// 单图最大出图超时 600s，避免误判正常的长图出图。
			// v4.17：停滞信号改读 LogWriter.LastWriteUtc（内存），不再依赖磁盘日志文件。
			stopEvent = new ManualResetEventSlim(false);   // false = 未置位（运行中）
			watchdogThread = new Thread(() =>
			{
				const long stallMs = 900000L;      // 15 分钟无日志写入
				const long killCooldownMs = 180000L;
				long lastKillAt = -killCooldownMs;
				var sw = System.Diagnostics.Stopwatch.StartNew();
				while (!stopEvent.IsSet)
				{
					// v4.17 修订十一：由 Thread.Sleep 轮询改为事件等待——停止时立即唤醒，
					// 退出耗时从「最坏 15s」降到毫秒级；超时仍为 15s，判定语义不变。
					if (stopEvent.Wait(15000)) break;   // true = 已收到停止信号，立即退出
					try
					{
						// v4.17：改读内存时间戳（日志不再落盘）
						var last = log.LastWriteUtc;
						long idle = (long)(DateTime.UtcNow - last).TotalMilliseconds;
						if (idle >= stallMs && (sw.ElapsedMilliseconds - lastKillAt) >= killCooldownMs)
						{
							lastKillAt = sw.ElapsedMilliseconds;
							// best-effort：日志写出失败不影响转换主流程
							try { log.WriteLine("[警告] 转换已 " + (idle / 60000) + " 分钟无进展，疑似 COM 调用被阻塞；尝试结束本工具自启的 AutoCAD 实例以解除阻塞…"); } catch { }
							int k = ComStarter.KillAllOwned();
							// best-effort：日志写出失败不影响转换主流程
							try { log.WriteLine("[警告] 已结束自启 AutoCAD 实例 " + k + " 个（不影响用户自己的 CAD），继续转换。"); } catch { }
						}
					}
					// best-effort：看门狗异常退出不影响转换主流程
					catch { }
				}
			});
			watchdogThread.IsBackground = true;
			watchdogThread.Start();
			log.WriteLine("==== 转换开始 ====");
			var totalSw = System.Diagnostics.Stopwatch.StartNew();
			log.WriteLine(sourceDesc);
			log.WriteLine("输出文件夹: " + outputFolder);
			log.WriteLine("待转换 dwg 文件: " + total);
			// v4.15：语料指纹 + 与上次运行对比（语料被增删时自动明示，避免误判回归）
			corpusCount = total;
			corpusFp = RunHistory.Fingerprint(files);
			RunHistory.LogComparison(log, corpusFp, corpusCount);
			log.WriteLine("已按文件大小升序排序（小文件优先，大文件最后）");
			log.WriteLine("已存在的 Activity Insights 伴随进程: " + (_insightsBefore != null ? _insightsBefore.Count : 0));
			if (total == 0)
			{
				log.WriteLine("未找到任何可转换的 dwg 文件");
				progress.Report(new ProgressUpdate
				{
					Index = 0,
					Total = 0,
					FileName = "",
					Status = ItemStatus.Failed,
					Note = "没有找到可转换的 dwg 文件"
				});
				return;
			}
			// v4.8 需求② / v4.12 变更：大文件阈值——"文件超过设定值将退回待转换"（0=不限制）。
			// 超阈值文件本轮不识别/不出图，但**不判失败**：保留在 UI"待处理"栏，
			// 用户可在其他图纸转换完成后继续对它们转换（可用"合并待转换"把这一批复制出来单独跑）。
			// v4.12：非首次运行（用户再次点击"开始转换"）时忽略该限制，便于直接补转退回的文件。
			bool ignoreLimits = print != null && print.IgnoreSizeTimeLimits;
			long maxDwgBytes = (print != null && !ignoreLimits) ? print.MaxDwgSizeBytes : 0;
			if (maxDwgBytes > 0)
			{
				var oversized = new List<KeyValuePair<string, long>>();
				foreach (var f in files)
				{
					long len = -1;
					// best-effort 探测：取不到大小按 0 处理
					try { var fi = new FileInfo(f); if (fi.Exists) len = fi.Length; } catch { }
					if (len > maxDwgBytes) oversized.Add(new KeyValuePair<string, long>(f, len));
				}
				if (oversized.Count > 0)
				{
					log.WriteLine("大文件阈值: " + AppSettings.FormatBytes(maxDwgBytes)
						+ "，超限 " + oversized.Count + " 个（本轮跳过，退回待处理）");
					foreach (var kv in oversized)
					{
						string f = kv.Key;
						string name = Path.GetFileNameWithoutExtension(f);
						string why = "文件超过设定值（" + AppSettings.FormatBytes(kv.Value) + "），已退回待处理";
						log.WriteLine("[待转换] " + name + " —— " + why);
						int ridx = files.IndexOf(f);
						progress.Report(new ProgressUpdate
						{
							Index = ridx,
							Total = total,
							FileName = f,
							Status = ItemStatus.Pending,
							Note = why
						});
					}
					var skipSet = new HashSet<string>(oversized.ConvertAll(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
					files = files.Where(f => !skipSet.Contains(f)).ToList();
					total = files.Count;
					if (total == 0)
					{
						log.WriteLine("所有文件均超过大文件阈值，本轮无需转换（已退回待处理）");
						log.WriteLine("==== 转换结束 ==== 成功 0 张，失败 0 张");
						if (text != null) log.WriteLine("日志文件: " + text);
						return; // finally 统一清理（此时 tmpDir/copies 均为 null，安全）
					}
				}
			}
			else if (ignoreLimits)
			{
				log.WriteLine("本轮忽略文件大小与转换时间限制（用户再次发起转换）");
			}
			// v3.9：阶段条第 1 段"准备"（定位 Core Console、建目录、统计缓存等）
			ReportPhase(progress, RunPhase.Prepare, total, "正在准备：定位 AutoCAD Core Console、创建输出目录…");
			int plotWorkers = SmartConcurrency.PlotWorkers(total);
			int[] booted = new int[1];
			int[] stat = new int[2];
			runStatRef = stat;   // v4.15：供 finally 落盘运行历史
			log.WriteLine("出图并行会话数(CPU自适应,上限8): " + plotWorkers);
			log.WriteLine("打印方式: " + (tight ? "贴边(不留白边)" : "常规(居中)") + "；配色: " + (print != null ? PrintOptions.Describe(print.Mode) : "黑白"));
			log.WriteLine("运行模式: " + ConvertProfile.Describe(profile.Mode) + (profile.IsCompany ? "（公司版模板库识别）" : ""));
			// v4.5：记录 PDF 输出结构模式（用户在「设置」中选择）
			log.WriteLine("PDF 输出结构: " + (flatOutput ? "全部放在同一文件夹" : "按原文件夹结构"));

			string ctb = print != null ? print.StyleSheetName : "monochrome.ctb";
			double marginMm = tight ? 0.0 : (print != null && print.EdgeMarginMm.HasValue ? print.EdgeMarginMm.Value : 0.0);

			// 阶段 0：定位 accoreconsole；找不到 → 整批回退 COM（v2.3.1 流程）
			string accore = CoreConsoleLocator.FindAccoreConsole();
			if (accore == null)
			{
				log.WriteLine("未找到 AutoCAD Core Console(accoreconsole.exe)，整批回退 COM 通道");
				// v3.9：阶段条跳过图框识别/页面设置（整批回退 COM 直接出图）
				progress.Report(new ProgressUpdate
				{
					Index = 0, Total = total, FileName = "",
					Status = ItemStatus.Running, Booting = true,
					Phase = RunPhase.FrameExtract, PhaseSkipped = true,
					Note = "未找到 Core Console，跳过图框识别（COM 直出）"
				});
				progress.Report(new ProgressUpdate
				{
					Index = 0, Total = total, FileName = "",
					Status = ItemStatus.Running, Booting = true,
					Phase = RunPhase.PageSetup, PhaseSkipped = true,
					Note = "整批回退 COM，跳过页面设置"
				});
				RunComOnly(files, total, outputFolder, plotWorkers, progress, booted, stat, log, tight, print, commonRoot);
				log.WriteLine("==== 转换结束 ==== 成功 " + stat[0] + " 张，失败 " + stat[1] + " 张");
				LogFailBreakdown(log, profile);
				if (text != null) log.WriteLine("日志文件: " + text);
				ReportPhase(progress, RunPhase.Finalize, total, "收尾：清理残留进程与临时文件…");
				return;
			}
			log.WriteLine("AutoCAD Core Console: " + accore);

			// v4.11：临时目录根由 %TEMP% 改为 %LOCALAPPDATA%\DwgToPdf。
			// 原因：%TEMP% 会被系统清理/杀毒在转换中途清除（手册 8.7），plot_*.scr 消失后
			// accoreconsole 出图整批失败并回退 COM，产出错误纸型的 PDF（实测 H47 一轮 12/19 失败）。
			// 约束：accoreconsole /s 不支持中文路径（实测 ErrorStatus=76），故根路径必须 ASCII；
			// 用户名含中文（%LOCALAPPDATA% 非 ASCII）时回退系统 Temp（保持旧行为，ASCII 可用）。
			string tmpRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DwgToPdf");
			bool asciiRoot = true;
			foreach (char c in tmpRoot) { if (c > 127) { asciiRoot = false; break; } }
			if (!asciiRoot) tmpRoot = Path.GetTempPath();
				tmpDir = Path.Combine(tmpRoot, "DwgToPdf_v3tmp_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
			// 幂等：先清旧产物，删除失败不影响后续写出
			try { Directory.CreateDirectory(tmpDir); } catch { }

			var phase1Sw = System.Diagnostics.Stopwatch.StartNew();
				// ---------- 阶段 1：图框识别（缓存命中即跳过） ----------
			var need = new List<string>();
			int cacheHits = 0;
			foreach (var f in files)
			{
				if (FrameCache.TryGetFrom(frameSnapshot, f, out _)) cacheHits++;
				else need.Add(f);
			}
			log.WriteLine("阶段1 图框识别: 缓存命中 " + cacheHits + "/" + total + "，需识别 " + need.Count);
			// v4.14：有界流水线（分块交错）——识别块 i+1 与出图块 i 并行，使总耗时接近
			// max(识别, 出图) 而不是两者之和。只改编排，不碰判定规则（FrameValidator /
			// 质量核查 / 结果记录 / 进度口径全部复用）；串行路径完整保留作回退：
			// 置 DWGTOPDF_SERIAL=1 或 print.PipelineMode=false 即走 v4.12 两阶段流程。
			bool pipelineHandled = false;
			string pipelineTiming = null;
			// v4.14：**默认关闭流水线** —— 实测（183 张全量，16 核）流水线 951.1s vs 串行 661.4s
			// 反而慢 44%：本机"识别 12 路 + 出图 8 路"已把 CPU 吃满，两块并行只是把同样的 CPU
			// 工作挤在一起（后段大文件块出图从 68s 恶化到 240s）。结论：本硬件上"重叠"没有收益，
			// 瓶颈是总 CPU 工作量而非阶段串行。保留代码仅作后续大核数机器的可选项：
			//   DWGTOPDF_PIPELINE=1 强制开启；DWGTOPDF_SERIAL=1 强制关闭。
			bool usePipeline = need.Count > 0 && print != null && print.PipelineMode
				&& print.AccorePageSetup && !tight && marginMm > 0.0
				&& string.Equals(Environment.GetEnvironmentVariable("DWGTOPDF_PIPELINE"), "1", StringComparison.Ordinal);
			if (usePipeline)
			{
				// 与串行路径同参的识别器（块识别开关 + 自定义块名）
				var pipelineExtractor = new FrameExtractor(accore)
				{
					RecognizeBlockFrame = print.RecognizeBlockFrame,
					ExtraBlockNames = print.ExtraBlockNames
				};
				pipelineHandled = RunPipelineBatched(need, files, total, outputFolder, progress, booted, stat, log,
					print, accore, tmpDir, marginMm, copies, commonRoot, plotWorkers, tight, profile, pipelineExtractor, out pipelineTiming);
			}
			if (need.Count > 0 && !pipelineHandled)
			{
				int[] extractDone = new int[1];
				try
				{
					progress.Report(new ProgressUpdate
					{
						Index = 0,
						Total = total,
						FileName = "",
						Status = ItemStatus.Running,
						Booting = true,
						Phase = RunPhase.FrameExtract,
						Note = "识别图框 0/" + need.Count + "（缓存命中可跳过）"
					});
					// v4.3：把设置中的块名列表传给 FrameExtractor（额外补充模式）
					var extractor = new FrameExtractor(accore)
					{
						RecognizeBlockFrame = print.RecognizeBlockFrame,
						ExtraBlockNames = print.ExtraBlockNames
					};
					Action<string, bool> onDone = delegate (string f, bool ok)
					{
						int n = Interlocked.Increment(ref extractDone[0]);
						// v4.12：识别阶段逐文件写入日志（LogWriter → 界面日志栏实时镜像），
						// 便于用户在识别阶段就能看到进度与失败文件（原先只有阶段开始/结束两行）。
						log.WriteLine("[识别] " + n + "/" + need.Count + " " + Path.GetFileNameWithoutExtension(f)
							+ (ok ? "" : " —— 未识别到图框"));
						progress.Report(new ProgressUpdate
						{
							Index = 0,
							Total = total,
							FileName = "",
							Status = ItemStatus.Running,
							Booting = true,
							Phase = RunPhase.FrameExtract,
							PhaseIndex = n,
							PhaseTotal = need.Count,
							Note = "识别图框 " + n + "/" + need.Count + "（缓存命中可跳过）"
						});
					};
					Dictionary<string, FrameBox> boxes = profile.IsCompany
						? extractor.RunExtractCompany(need, tmpDir, onDone, () => _cancelRequested)
						: extractor.RunExtract(need, tmpDir, onDone, () => _cancelRequested);
					// v3.5 整改：公司版识别失败 → 先用通用版 lsp 补识别（全模型逐层求图框），
					// 再失败才走 COM 兜底。此前公司版识别不到的文件（如 PISCES/泵阀总成图纸）
					// 直接涌入 COM 兜底，35 张要 35 次 AutoCAD 冷启动。
					if (profile.IsCompany)
					{
						var leftover = new List<string>();
						foreach (var f in need)
							if (!boxes.ContainsKey(f)) leftover.Add(f);
						if (leftover.Count > 0)
						{
							log.WriteLine("[信息] 公司版未识别 " + leftover.Count + " 张，改用通用版识别: "
								+ string.Join("; ", leftover.ConvertAll(f => Path.GetFileName(f))));
							int[] genDone = new int[1];
							Action<string, bool> onGen = delegate (string f, bool ok)
							{
								int n = Interlocked.Increment(ref genDone[0]);
								progress.Report(new ProgressUpdate
								{
									Index = 0,
									Total = total,
									FileName = "",
									Status = ItemStatus.Running,
									Booting = true,
									Phase = RunPhase.FrameExtract,
									PhaseIndex = n,
									PhaseTotal = leftover.Count,
									Note = "通用版补识别 " + n + "/" + leftover.Count
								});
							};
							Dictionary<string, FrameBox> boxesGen = extractor.RunExtract(leftover, tmpDir, onGen, () => _cancelRequested);
							foreach (var kv in boxesGen)
								if (!boxes.ContainsKey(kv.Key)) boxes[kv.Key] = kv.Value;
							log.WriteLine("[信息] 通用版补识别: 补识别 " + boxesGen.Count + "/" + leftover.Count);
						}
					}
					// v3.3 整改①：阶段1 失败文件逐个记录名称；并发下 accoreconsole 偶发崩溃/超时属于瞬时故障，自动重试一次
					// v3.5：重试统一用通用版 lsp（覆盖更广；公司版失败文件多已在此前补识别）
					var retryList = new List<string>();
					foreach (var f in need)
						if (!boxes.ContainsKey(f)) retryList.Add(f);
					if (retryList.Count > 0)
					{
						foreach (var f in retryList)
							log.WriteLine("[信息] 阶段1 图框识别失败，重试: " + Path.GetFileName(f));
						int[] retryDone = new int[1];
						Action<string, bool> onRetry = delegate (string f, bool ok)
						{
							int n = Interlocked.Increment(ref retryDone[0]);
							progress.Report(new ProgressUpdate
							{
								Index = 0,
								Total = total,
								FileName = "",
								Status = ItemStatus.Running,
								Booting = true,
								Phase = RunPhase.FrameExtract,
								PhaseIndex = n,
								PhaseTotal = retryList.Count,
								Note = "重试识别 " + n + "/" + retryList.Count
							});
						};
						Dictionary<string, FrameBox> boxes2 = extractor.RunExtract(retryList, tmpDir, onRetry, () => _cancelRequested);
						foreach (var kv in boxes2)
							boxes[kv.Key] = kv.Value;
						int retryOk = boxes2.Count;
						log.WriteLine("[信息] 阶段1 重试: 补识别 " + retryOk + "/" + retryList.Count);
					}
					// v4.3：图框合理性校验 —— 散 LINE 图框（非 polyline/非块）按 layer 合并 bbox 会被
					// 同层散线撑大到异常范围（如 H47 风扇 5534×841 比例 6.6，参考 PDF 仅 297×420），
					// 出图后图框/内容被裁。校验不通过的文件判失败（原走 COM 兜底会硬出坏 PDF），
					// 原因标注"图框问题"，由用户人工在 CAD 中处理。
					_frameRejectedPaths.Clear();
					var rejected = new List<KeyValuePair<string, string>>();
					var passedBoxes = new Dictionary<string, FrameBox>();
					foreach (var kv in boxes)
					{
						// v4.10：图框疑似为块 —— lsp 检测到短边≥500mm 且不小于识别图框的大块时，
						// 在日志/失败原因中提示用户开启"识别块图框"后重转（仅提示，不自动重转）。
						string hint = string.IsNullOrEmpty(kv.Value.BlockHint) ? null
							: "；图框疑似为块（" + kv.Value.BlockHint + "），建议在设置中开启\"识别块图框\"后重转";
						if (FrameValidator.IsAcceptable(kv.Value, out string why))
						{
							passedBoxes[kv.Key] = kv.Value;
							// v4.12 修订：识别框完整性 ——
							// ① EXTENT：识别框没覆盖整幅图纸，lsp 已改用"全图范围"（实测修复 H47 总成图被裁 240×137mm）
							// ② ExtentOut：图框外仍有内容，但全图范围不可信（散线撑大等）→ 保持原框出图并提示人工确认
							if (string.Equals(kv.Value.Source, "EXTENT", StringComparison.OrdinalIgnoreCase))
							{
								log.WriteLine("[信息] " + Path.GetFileName(kv.Key)
									+ " —— 识别框未覆盖整幅图纸，已按全图范围出图（"
									+ kv.Value.Width.ToString("0") + "×" + kv.Value.Height.ToString("0") + "mm），避免内容被裁");
							}
							else if (kv.Value.ExtentOut && kv.Value.Extent != null)
							{
								log.WriteLine("[提示] " + Path.GetFileName(kv.Key)
									+ " —— 图框外检测到其他内容（识别框 "
									+ kv.Value.Width.ToString("0") + "×" + kv.Value.Height.ToString("0") + "mm，全图 "
									+ (kv.Value.Extent[2] - kv.Value.Extent[0]).ToString("0") + "×"
									+ (kv.Value.Extent[3] - kv.Value.Extent[1]).ToString("0")
									+ "mm），出图可能缺内容，请人工确认");
							}
							if (hint != null)
								log.WriteLine("[提示] " + Path.GetFileName(kv.Key) + " —— 检测到大块疑似图框块（"
									+ kv.Value.BlockHint + "），若出图内容缺失可开启\"识别块图框\"后重转");
						}
						else
						{
							rejected.Add(new KeyValuePair<string, string>(kv.Key, why));
							_frameRejectedPaths.Add(kv.Key);
							log.WriteLine("[失败] " + Path.GetFileName(kv.Key) + " —— 图框问题：未识别到正规图框。" + why + hint);
							// 立即 progress 报失败，UI 立即看到
							int ridx = files.IndexOf(kv.Key);
							if (ridx >= 0)
							{
								progress.Report(new ProgressUpdate
								{
									Index = ridx,
									Total = total,
									FileName = kv.Key,
									Status = ItemStatus.Failed,
									// 不带 Booting=true：Booting 是阶段内进度消息标志，
									// 设 true 会让 MainForm.ApplyUpdate 早 return 跳过 UpdateRow，
									// 失败行就无法从待处理栏移到失败栏。
									Phase = RunPhase.FrameExtract,
									// v4.12：列表只显示简短原因（完整说明在日志行内），避免提示过长遮挡内容
									Note = "图框问题：" + ShortFrameReason(why)
								});
							}
							// v4.5：登记转换结果（日志失败分类数据源）（图框问题判失败，不进阶段2/3）
							AddRecord(new ConvertRecord
							{
								Index = ridx,
								DwgPath = kv.Key,
								Status = ItemStatus.Failed,
								Reason = "图框问题：未识别到正规图框（" + why + "）" + hint,
							});
						}
					}
					boxes = passedBoxes;
					// 合并写入输出文件夹缓存（临时文件+原子替换由 FrameCache 负责）
					var entries = FrameCache.LoadEntries(outputFolder);
					foreach (var kv in boxes)
					{
						entries.RemoveAll(e => string.Equals(e.Path, kv.Key, StringComparison.OrdinalIgnoreCase));
						var fi = new FileInfo(kv.Key);
						entries.Add(new FrameCache.Entry
						{
							Path = kv.Key,
							Length = fi.Length,
							MtimeUtc = fi.LastWriteTimeUtc.Ticks,
							MinX = kv.Value.MinX,
							MinY = kv.Value.MinY,
							MaxX = kv.Value.MaxX,
							MaxY = kv.Value.MaxY,
							Source = kv.Value.Source,
							ImageCount = kv.Value.ImageCount,   // v4.15：图片/嵌入对象/参考底图总数
							RasterCount = kv.Value.RasterCount,             // v4.15：真光栅数
							RasterMissingCount = kv.Value.RasterMissingCount, // v4.15：外部图缺失数
							G76 = kv.Value.G76,               // v4.16：Model 布局着色打印组码 76
						});
					}
					FrameCache.Save(outputFolder, entries);
					// v4.4：识别结果已落盘，刷新内存快照供阶段3 出图查询（含本轮新识别的图框）
					frameSnapshot = FrameCache.Snapshot(outputFolder);
					// v3.3 整改①：仍失败的文件在日志中点名，便于排查（此前只报数量）
					int failedCnt = need.Count - boxes.Count - rejected.Count;
					if (rejected.Count > 0)
					{
						log.WriteLine("[信息] 阶段1 图框校验：拒绝 " + rejected.Count + " 份（图框问题，详见上方 [失败] 行）");
					}
					if (failedCnt > 0)
					{
						var still = new List<string>();
						foreach (var f in need)
							if (!boxes.ContainsKey(f)) still.Add(Path.GetFileName(f));
						log.WriteLine("阶段1 完成: 识别 " + boxes.Count + " 份有效图框，失败 " + failedCnt
							+ "（" + string.Join("; ", still) + "）（失败文件走 COM 兜底）");
					}
					else
					{
						log.WriteLine("阶段1 完成: 识别 " + boxes.Count + " 份有效图框，失败 0");
						log.WriteLine("[耗时] 阶段1 图框识别: " + phase1Sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
			// v4.15：列出含光栅图片的图纸并说明处理方式（每轮只写一次）
			WriteImageDrawingsNote(log, boxes, print);
					}
				}
				catch (Exception ex)
				{
					log.WriteLine("[失败] 阶段1 图框识别异常: " + ex.Message + "（相关文件走 COM 兜底）");
				}
			}
			else if (!pipelineHandled)
			{
				log.WriteLine("阶段1 完成: 全部命中缓存，跳过识别");
				progress.Report(new ProgressUpdate
				{
					Index = 0, Total = total, FileName = "",
					Status = ItemStatus.Running, Booting = true,
					Phase = RunPhase.FrameExtract, PhaseSkipped = true,
					Note = "图框缓存全部命中，跳过识别"
				});
			}

			if (_cancelRequested)
			{
				log.WriteLine("用户停止，结束转换");
				MarkAllCancelled(files, total, progress, log);
				return;
			}

			var phase2Sw = System.Diagnostics.Stopwatch.StartNew();
			// ---------- 阶段 2：预处理（COM 批量设置图纸副本的 Model 页面设置） ----------
			ReportPhase(progress, RunPhase.PageSetup, total, "设置页面设置 0/" + total);
			// v4.3：过滤掉阶段1 已判失败的文件（图框问题），避免预处理浪费
			var prepFiles = new List<string>();
			foreach (var f in files)
				if (!_frameRejectedPaths.Contains(f)) prepFiles.Add(f);
			// v4.12：页面设置内联（有边距/居中 + 设置项开启）→ 跳过整个阶段2：
			// 不生成副本、不启动 AUTOCAD，页面设置由阶段3 出图会话内 entmod 完成（等效且更快）。
			// 贴边(M=0)或用户关闭设置项时保留原 COM 预处理路径（含非全出血/居中以外的兼容行为）。
			bool accorePageSetup = print != null && print.AccorePageSetup && !tight && marginMm > 0.0;
			if (accorePageSetup)
			{
				copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				log.WriteLine("阶段2 页面设置: 已并入出图会话，无需预处理");
				log.WriteLine("[耗时] 阶段2 页面设置: " + phase2Sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
			}
			else
			{
				var prep = new PageSetupPreprocessor();
				// v4.5：跨子目录同名源图 → 副本名用工作名（子目录标识_基名），消除并发副本覆盖竞态
				// v4.17：副本目录改到临时目录（不再占用输出文件夹 logs\_prep，收尾随临时目录一并删除）
				copies = prep.Preprocess(prepFiles, outputFolder, Path.Combine(tmpDir, "_prep"), ctb, marginMm, tight, progress, log, out string prepErr, () => _cancelRequested, profile, showLineWeight: print.ShowLineWeight, nameOverride: _workNameMap);
				if (!string.IsNullOrEmpty(prepErr)) log.WriteLine("[信息] " + prepErr);
				log.WriteLine("阶段2 预处理: 生成副本 " + (copies != null ? copies.Count : 0) + "/" + total + "（未生成副本的文件走 COM 兜底）");
				log.WriteLine("[耗时] 阶段2 预处理: " + phase2Sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒");
			}

			if (_cancelRequested)
			{
				log.WriteLine("用户停止，结束转换");
				MarkAllCancelled(files, total, progress, log);
				return;
			}

			var phase3Sw = System.Diagnostics.Stopwatch.StartNew();
			// ---------- 阶段 3：批量出图（accoreconsole -EXPORT 副本，失败→COM 兜底） ----------
			ReportPhase(progress, RunPhase.Plot, total, "出图 0/" + total);
			if (!pipelineHandled)
			{
				// v4.12 修订：出图取件顺序 —— 固定"先小文件、后大文件"（按 dwg 大小升序，用户要求不得更改）。
				// 小图先出：批量结果尽快可见，中途停止时已完成的多为可直接使用的图；
				// 大 dwg（高质量 pc3）排在最后，不阻塞前面绝大多数文件。
				// 安全性：WorkerLoopV3 的进度与结果记录都以 FileName 为准（MainForm.UpdateRow
				// 也按 FileName 定位行），这里只改"取件顺序"，不影响行归属与统计口径。
				List<string> plotOrder = PlotOrderBySizeAsc(files);
				LaunchPlotWorkers(plotOrder, files, total, outputFolder, progress, booted, stat, log, tight, print,
					accore, tmpDir, marginMm, copies, commonRoot, frameSnapshot, accorePageSetup, plotWorkers);
			}
			// v4.15：失败项自动重试 —— 仅"瞬态类"原因（路径竞态/会话超时/参数瞬态/占用/写盘迟到），
			// 批次结束后**串行重试一次**（1 路，避免与已完成的负载叠加）。DWGTOPDF_NO_RETRY=1 关闭。
			// 计数口径：用独立 retryStat，重试后合并回 stat（先撤销原失败计数，再计入新结果），
			// 保证"成功 + 失败 = 总张数"不被破坏。
			int retriedCount = 0, retryGained = 0;
			try
			{
				if (RunPolicy.AutoRetryEnabled())
				{
					List<string> retryFiles = CollectTransientFailures();
					retriedCount = retryFiles.Count;
					if (retriedCount > 0)
					{
						var retryStat = new int[2];
						string preview = "";
						for (int i = 0; i < retryFiles.Count && i < 6; i++)
						{
							if (i > 0) preview += "、";
							preview += Path.GetFileNameWithoutExtension(retryFiles[i]);
						}
						log.WriteLine("[重试] 检测到 " + retriedCount + " 张瞬态类失败，串行重试一次：" + preview
							+ (retryFiles.Count > 6 ? " 等" : ""));
						LaunchPlotWorkers(retryFiles, files, total, outputFolder, progress, booted, retryStat, log, tight, print,
							accore, tmpDir, marginMm, copies, commonRoot, frameSnapshot, accorePageSetup, 1);
						retryGained = retryStat[0];
						stat[0] += retryStat[0];
						stat[1] -= retriedCount;
						stat[1] += retryStat[1];
						log.WriteLine("[重试] 结果：挽回 " + retryGained + " 张，仍失败 " + retryStat[1] + " 张");
					}
				}
			}
			catch (Exception exRetry)
			{
				// best-effort：日志写出失败不影响转换主流程
				try { log.WriteLine("[重试] 重试阶段异常（已忽略，不影响已完成产出）：" + exRetry.Message); } catch { }
			}
			ReportPhase(progress, RunPhase.Finalize, total, "收尾：清理残留进程与临时文件…");

				// v4.14：流水线模式打印流水线自身耗时（阶段3 计时器在流水线下仅为空转）
				log.WriteLine(pipelineHandled && pipelineTiming != null
					? pipelineTiming
					: ("[耗时] 阶段3 出图: " + phase3Sw.Elapsed.TotalSeconds.ToString("0.0") + " 秒"));
				log.WriteLine("[耗时] 总耗时: " + totalSw.Elapsed.TotalSeconds.ToString("0.0") + " 秒（识别+预处理+出图）");
			// v4.11：迟到 PDF 复核 —— 后台打印/写盘延迟可能让"未检测到 PDF"成为假失败。
			// 出图结束后统一复核一次：失败记录的期望 PDF 若已生成且非空白，改判成功。
			int lateOk = 0;
			lock (_recordLock)
			{
				foreach (var r in _records)
				{
					if (r.Status != ItemStatus.Failed || string.IsNullOrEmpty(r.PdfPath)) continue;
					bool exists = false; long len = 0;
					// best-effort 探测：取不到大小按 0 处理
					try { var fi = new FileInfo(r.PdfPath); exists = fi.Exists; len = fi.Length; } catch { }
					if (exists && len > 5000) { r.Status = ItemStatus.Success; r.Reason = ""; lateOk++; }
				}
			}
			if (lateOk > 0)
			{
				stat[0] += lateOk;
				stat[1] = Math.Max(0, stat[1] - lateOk);
				log.WriteLine("[信息] 复核：发现 " + lateOk + " 张此前判失败的 PDF 实际已生成（后台/写盘延迟），已改判成功");
			}
			// v4.11：观测性——出图耗时分布 + 最慢 10 张（便于定位需人工出图的图，少跑全量）
			try
			{
				var secs = new List<double>();
				var slowRecs = new List<ConvertRecord>();
				lock (_recordLock)
				{
					foreach (var r in _records)
					{
						if (r.Seconds <= 0) continue;
						secs.Add(r.Seconds);
						slowRecs.Add(r);
					}
				}
				if (secs.Count > 0)
				{
					secs.Sort();
					double sum = 0; foreach (var s in secs) sum += s;
					double p50 = secs[Math.Min(secs.Count - 1, (int)Math.Round(secs.Count * 0.5))];
					double p95 = secs[Math.Min(secs.Count - 1, (int)Math.Round(secs.Count * 0.95))];
					log.WriteLine("[观测] 出图耗时 n=" + secs.Count + " 平均=" + (sum / secs.Count).ToString("0.0")
						+ "s P50=" + p50.ToString("0.0") + "s P95=" + p95.ToString("0.0") + "s");
					slowRecs.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
					int n = Math.Min(10, slowRecs.Count);
					if (n > 0)
					{
						log.WriteLine("[观测] 单张出图最慢 Top" + n + "：");
						for (int i = 0; i < n; i++)
						{
							var r = slowRecs[i];
							log.WriteLine("    " + (i + 1) + ". " + r.Seconds.ToString("0.0") + "s  "
								+ Path.GetFileNameWithoutExtension(r.DwgPath) + (r.Status == ItemStatus.Failed ? "（失败）" : ""));
						}
					}
				}
			}
			// best-effort：统计块失败不影响收尾
			catch { }
			log.WriteLine("==== 转换结束 ==== 成功 " + stat[0] + " 张，失败 " + stat[1] + " 张");
			// v4.17 修订十（CQ-02）：写盘失败在此收口——该行走 onLine 镜像，界面日志面板可见
			if (log.WriteFailed) log.WriteLine("[警告] 日志写入失败：" + log.LastWriteError + "；后续排查请以后台日志为准");
			LogFailBreakdown(log, profile);
			if (text != null) log.WriteLine("日志文件: " + text);
		}
		finally
		{
			// v4.5：CSV 转换报告已移除（用户反馈日志文本报告已够用），失败分类摘要已在上方写入日志
			// v3.6：收尾清理本次转换新起的 Activity Insights 伴随进程（正常结束/异常/整批回退都走这里）。
			// acad.exe 退出后它们不会跟随退出（官方确认 AcEventSync/AcQMod 残留是已知行为），
			// 不清理则每次批量转换都会堆积进程。仅清理快照之后新出现的，不碰用户 CAD 伴随进程。
			try
			{
				int cleaned = ComStarter.KillActivityInsightsSince(_insightsBefore);
				if (cleaned > 0 && log != null)
					log.WriteLine("[信息] 清理 Activity Insights 残留进程 " + cleaned + " 个（本次转换期间启动）");
			}
			// best-effort：本段（统计/收尾/镜像）失败不影响已完成产出
			catch { }
			// v4.11：收尾兜底——收割登记表中仍存活的 accoreconsole 孤儿 + 未正常退出的自启 acad
			try { int ka = ComStarter.KillLeftoverAccore(); if (ka > 0 && log != null) log.WriteLine("[警告] 收尾收割残留 accoreconsole 进程 " + ka + " 个"); } catch { }
			// best-effort：日志写出失败不影响转换主流程
			try { int kb = ComStarter.KillAllOwned(); if (kb > 0 && log != null) log.WriteLine("[警告] 收尾结束未正常退出的自启 AutoCAD 实例 " + kb + " 个"); } catch { }
			// 幂等：先清旧产物，删除失败不影响后续写出
			try { CleanupTemp(outputFolder, tmpDir, copies); } catch { }
			// v4.17：清理输出文件夹内的转换产物（logs\ 目录 + frame_cache.json）——用户要求
			// 转换完成后输出文件夹只保留 PDF，不留日志文件与 JSON 缓存。
			try { CleanupArtifacts(outputFolder); } catch { }
			// v4.15：落盘本次运行记录（语料指纹 + 结果），供下次运行自动对比
			try
			{
				if (runStatRef != null)
					RunHistory.Save(corpusFp, corpusCount, runStatRef[0], runStatRef[1], runWatch.Elapsed.TotalSeconds);
			}
			catch { }
			// v4.11：停止阶段看门狗（须在 log.Dispose 之前，避免看门狗写已释放的日志）
			// v4.17 修订十一：Set 唤醒看门狗（毫秒级退出）。
			if (stopEvent != null) stopEvent.Set();
			// best-effort：看门狗线程可能阻塞，等待失败直接继续收尾
			bool wdJoined = false;
			if (watchdogThread != null) { try { wdJoined = watchdogThread.Join(3000); } catch { } }
			// v4.17 修订十一：仅在看门狗线程确已退出时才释放句柄——若 Join 超时（线程仍存活），
			// 它可能仍在循环中（尚未进入 Wait 时被抢占的极窄窗口），此时释放会让其随后调用
			// Wait 抛 ObjectDisposedException（后台线程无 catch 会终止进程）；故宁可交给 GC 回收
			// （ManualResetEventSlim 内部句柄由 SafeHandle 终结器兜底）。IsSet 为纯字段读、不受影响。
			try { if (wdJoined && stopEvent != null) stopEvent.Dispose(); } catch { }
			if (log != null)
			{
				((IDisposable)log).Dispose();
			}
		}
	}

	/// <summary>v4.8 需求①：文件清单按大小升序排序（小文件优先，大文件最后）。
	/// 读不到大小的文件（不存在/无权限）排最后；同大小时按路径稳定排序。失败回退原顺序。</summary>
	private static List<string> SortBySizeAsc(List<string> files)
	{
		try
		{
			if (files == null || files.Count < 2) return files;
			var sized = new List<KeyValuePair<long, string>>(files.Count);
			foreach (var f in files)
			{
				long len = long.MaxValue; // 读不到大小的排最后
				// best-effort 探测：取不到大小按 0 处理
				try { var fi = new FileInfo(f); if (fi.Exists) len = fi.Length; } catch { }
				sized.Add(new KeyValuePair<long, string>(len, f));
			}
			sized.Sort((a, b) =>
			{
				int c = a.Key.CompareTo(b.Key);
				return c != 0 ? c : string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
			});
			return sized.ConvertAll(kv => kv.Value);
		}
		catch { return files; }
	}

	/// <summary>v3.9：报告阶段切换（Booting=true 携带当前阶段，UI 据此推进阶段条并刷新阶段详情）。</summary>
	private static void ReportPhase(IProgress<ProgressUpdate> progress, RunPhase phase, int total, string note)
	{
		progress.Report(new ProgressUpdate
		{
			Index = 0,
			Total = total,
			FileName = "",
			Status = ItemStatus.Running,
			Booting = true,
			Phase = phase,
			Note = note
		});
	}

	/// <summary>v4.5：检测输入清单中跨子目录的同名 dwg，生成"工作名"映射（dwgPath → 子目录标识_基名）。
	/// 基名冲突必然来自不同子目录（同目录不可能有两个同名文件），子目录标识互异 → 工作名全局唯一。
	/// 无同名冲突时不生成映射（null），全部走原文件名，行为与旧版一致。</summary>
	private void BuildWorkNameMap(List<string> files, string commonRoot)
	{
		_workNameMap = null;
		try
		{
			if (files == null || files.Count < 2) return;
			var byBase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var f in files)
			{
				string b = Path.GetFileNameWithoutExtension(f);
				if (string.IsNullOrEmpty(b)) continue;
				if (!byBase.TryGetValue(b, out var list)) byBase[b] = list = new List<string>();
				list.Add(f);
			}
			foreach (var kv in byBase)
			{
				if (kv.Value.Count < 2) continue;
				_workNameMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var f in kv.Value)
				{
					string sub = GetRelativeSubDir(f, commonRoot) ?? "";
					string tag = sub.Replace('\\', '_').Replace('/', '_').Trim();
					_workNameMap[f] = string.IsNullOrEmpty(tag) ? kv.Key + "_同目录1" : tag + "_" + kv.Key;
				}
			}
		}
		catch { _workNameMap = null; }
	}

	/// <summary>v4.5：取文件的工作基名（同名冲突文件返回含子目录标识的名字，否则返回原名）。</summary>
	private string WorkBaseName(string dwgPath, string baseName)
	{
		if (_workNameMap != null && dwgPath != null && _workNameMap.TryGetValue(dwgPath, out string wn))
			return wn;
		return baseName;
	}


	/// <summary>取消收尾：把全部未达终态的文件登记为「已停止」——日志写取消行、UI 行报 Cancelled；阶段1 已判「图框问题」的文件已按 Failed 登记故跳过（防失败+取消双计数）；已有终态记录的不覆盖。</summary>
	private void MarkAllCancelled(List<string> files, int total, IProgress<ProgressUpdate> progress, LogWriter log)
	{
		for (int i = 0; i < files.Count; i++)
		{
			// v4.6：阶段1 已判"图框问题"的文件在阶段1 末尾已报 Failed 并登记——
			// 此处再报 Cancelled 会双计数（失败+取消各 +1）且 UI 行从失败栏移回待处理栏
			if (_frameRejectedPaths.Contains(files[i])) continue;
			log.WriteLine("[取消] " + Path.GetFileNameWithoutExtension(files[i]) + " —— 用户停止");
			progress.Report(new ProgressUpdate
			{
				Index = i,
				Total = total,
				FileName = files[i],
				Status = ItemStatus.Cancelled,
				Note = "已停止"
			});
			// v4.4：尚未登记过结果的文件补一条"已停止"（已有终态记录的不覆盖，日志摘要保留真实结果）
			bool recorded;
			lock (_recordLock)
			{
				recorded = _records.Exists(x => string.Equals(x.DwgPath, files[i], StringComparison.OrdinalIgnoreCase));
			}
			if (!recorded)
			{
				AddRecord(new ConvertRecord { Index = i, DwgPath = files[i], Status = ItemStatus.Cancelled, Reason = "已停止" });
			}
		}
	}

	/// <summary>删除预处理副本、_prep 目录与临时目录（出图完成后/取消时调用，幂等）。</summary>
	private static void CleanupTemp(string outputFolder, string tmpDir, Dictionary<string, string> copies)
	{
		if (copies != null)
		{
			foreach (var kv in copies)
			{
				// 幂等：先清旧产物，删除失败不影响后续写出
				try { if (File.Exists(kv.Value)) File.Delete(kv.Value); } catch { }
			}
			copies.Clear();
		}
		try
		{
			string prepDir = Path.Combine(outputFolder, "logs", "_prep");
			if (Directory.Exists(prepDir)) Directory.Delete(prepDir, true);
		}
		// 幂等：先清旧产物，删除失败不影响后续写出
		catch { }
		// v4.12 修订：诊断钩子 —— 置 DWGTOPDF_KEEP_TEMP=1 时保留临时目录
		//（内含本轮的出图/识别脚本与会话日志 session.log），用于排查会话级问题
		//（例如"某个提示的默认值到底是是还是否"）。正常使用无需设置。
		bool keepTemp = false;
		// best-effort 探测：读取失败按未设置走默认
		try { keepTemp = Environment.GetEnvironmentVariable("DWGTOPDF_KEEP_TEMP") == "1"; } catch { }
		if (!keepTemp)
		{
			// 幂等：先清旧产物，删除失败不影响后续写出
			try { if (!string.IsNullOrEmpty(tmpDir) && Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
		}
	}

	/// <summary>v4.17：转换结束后清理输出文件夹内的临时产物 —— logs\ 目录与 frame_cache.json。
	/// 用户要求：转换完成后输出文件夹只保留 PDF（不留日志文件与 JSON 缓存）。幂等。
	/// 置 DWGTOPDF_KEEP_CACHE=1 可保留 frame_cache.json（供 --qa 等事后审查用）。</summary>
	private static void CleanupArtifacts(string outputFolder)
	{
		if (string.IsNullOrEmpty(outputFolder)) return;
		// logs\：v4.17 起不再创建（日志不落盘、_prep 副本已移到临时目录）；
		// 此处兜底删除旧版本残留的 logs\ 目录。
		try
		{
			string logsDir = Path.Combine(outputFolder, "logs");
			if (Directory.Exists(logsDir)) Directory.Delete(logsDir, true);
		}
		// 幂等：先清旧产物，删除失败不影响后续写出
		catch { }
		bool keepCache = false;
		// best-effort 探测：读取失败按未设置走默认
		try { keepCache = Environment.GetEnvironmentVariable("DWGTOPDF_KEEP_CACHE") == "1"; } catch { }
		if (keepCache) return;
		try
		{
			string cache = Path.Combine(outputFolder, FrameCache.FileName);
			if (File.Exists(cache)) File.Delete(cache);
		}
		// 幂等：先清旧产物，删除失败不影响后续写出
		catch { }
	}

	/// <summary>v3.9.1：计算所有文件的公共祖先目录（用于保留输入文件夹结构输出 PDF）。
	/// 文件来自不同驱动器时返回空字符串（此时扁平化输出）。</summary>
	private static string GetCommonRoot(List<string> files)
	{
		if (files == null || files.Count == 0) return "";
		string[] dirs = new string[files.Count];
		for (int i = 0; i < files.Count; i++) dirs[i] = Path.GetDirectoryName(files[i]);
		if (dirs.Length == 1) return dirs[0];
		string prefix = dirs[0] ?? "";
		foreach (string d in dirs.Skip(1))
		{
			if (string.IsNullOrEmpty(d)) continue;
			int minLen = Math.Min(prefix.Length, d.Length);
			int i = 0;
			while (i < minLen && char.ToUpperInvariant(prefix[i]) == char.ToUpperInvariant(d[i])) i++;
			// 仅当匹配在中间断开时才回退到路径分隔符；若 prefix 完全是 d 的前缀（i==prefix.Length），prefix 本身就是合法目录，无需回退
			if (i < prefix.Length)
			{
				while (i > 0 && prefix[i - 1] != Path.DirectorySeparatorChar && prefix[i - 1] != Path.AltDirectorySeparatorChar) i--;
			}
			prefix = prefix.Substring(0, i);
		}
		return prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	/// <summary>v3.9.1：计算文件相对于公共根目录的子目录路径（不含文件名）。</summary>
	private static string GetRelativeSubDir(string filePath, string commonRoot)
	{
		if (string.IsNullOrEmpty(commonRoot)) return "";
		string dir = Path.GetDirectoryName(filePath);
		if (string.IsNullOrEmpty(dir)) return "";
		if (dir.StartsWith(commonRoot, StringComparison.OrdinalIgnoreCase))
		{
			return dir.Substring(commonRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		return "";
	}

	/// <summary>为输出 PDF 取一个不冲突的完整路径。先确保 outputFolder 存在（v3.9.1 输出可能带子目录）；baseName 不含扩展名，返回路径已含 .pdf；同名时依次尝试 baseName(2).pdf、baseName(3).pdf …（半角括号、从 2 起）。注意与「PDF 子文件夹」的同名规则不同——后者用「-N」（见 ResolvePdfSubFolder），两者勿混淆。</summary>
	internal static string UniquePdfPath(string outputFolder, string baseName)
	{
		// v3.9.1：输出可能带子目录（保留输入文件夹结构），需先确保目录存在
		Directory.CreateDirectory(outputFolder);
		string text = Path.Combine(outputFolder, baseName + ".pdf");
		if (!File.Exists(text))
		{
			return text;
		}
		int num = 2;
		while (File.Exists(Path.Combine(outputFolder, baseName + "(" + num + ").pdf")))
		{
			num++;
		}
		return Path.Combine(outputFolder, baseName + "(" + num + ").pdf");
	}

	/// <summary>v4.17 修订三：解析本轮 PDF 子文件夹名（模板展开 + 同名追加 -2/-3）。
	/// startedAt 缺省时用当前时刻；模板为空/非法时回落 AppSettings 默认值/PDF文件。
	/// 去重规则：目标名已存在（含空文件夹）就追加 "-N"，最多试到 999；
	/// 999 仍占用则退回带纳秒的唯一名（避免覆盖任何已有批次）。</summary>
	internal static string ResolvePdfSubFolder(string outputFolder, DateTime? startedAt, string template)
	{
		DateTime when = startedAt ?? DateTime.Now;
		string name = AppSettings.ExpandPdfFolderTemplate(template, when);
		string full = Path.Combine(outputFolder, name);
		if (!Directory.Exists(full) && !File.Exists(full)) return name;
		for (int i = 2; i < 1000; i++)
		{
			string cand = name + "-" + i;
			if (!Directory.Exists(Path.Combine(outputFolder, cand))
			    && !File.Exists(Path.Combine(outputFolder, cand)))
			{
				return cand;
			}
		}
		return name + "-" + when.ToString("HHmmssffff");
	}
	/// <summary>v4.17 修订十（CQ-01）：纯助手——把后台识别任务异常翻译成用户可见原因。
	/// 取消（OperationCanceledException）或 null 入参 → 返回 null（调用方保持取消语义/无需透传）；
	/// 其余 → "后台识别异常：{异常类型}: {消息}"。可空安全、无副作用、不触网。</summary>
	internal static string RecognitionFailureNote(Exception ex)
	{
		if (ex == null || ex is OperationCanceledException) return null;
		return "后台识别异常：" + ex.GetType().Name + ": " + ex.Message;
	}
}
