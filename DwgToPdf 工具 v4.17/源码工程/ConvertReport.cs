using System;
using System.Collections.Generic;
using System.Linq;

namespace DwgToPdf.Batch;

/// <summary>v4.5：单个 dwg 的转换结果记录（内存中的数据单元）。
/// 由 ConverterEngine 在各终态（成功/失败/已停止）埋点收集，
/// 仅用于转换结束在日志文本中输出"失败原因分类摘要"，不再生成任何报告文件（v4.4 的 CSV 报告已按用户反馈移除）。</summary>
internal sealed class ConvertRecord
{
	/// <summary>文件在输入清单中的序号。</summary>
	public int Index;

	public string DwgPath;

	/// <summary>终态：Success / Failed / Cancelled。</summary>
	public ItemStatus Status;

	/// <summary>失败/停止原因（成功为空）。</summary>
	public string Reason;

	/// <summary>图框尺寸 mm（无图框数据时为 0）。</summary>
	public double FrameW;
	public double FrameH;

	/// <summary>图框来源（图层名 / company / legacy / BLOCK:块名）。</summary>
	public string FrameSource;

	/// <summary>纸型长/短边 mm（未能解析页面时为 0）。</summary>
	public double PaperLong;
	public double PaperShort;

	/// <summary>单文件出图用时（秒，取消/未出图为 0）。</summary>
	public double Seconds;

	/// <summary>生成的 PDF 路径（成功时）。</summary>
	public string PdfPath;
}

/// <summary>v4.5：失败原因归类与摘要（仅写日志，不生成文件）。</summary>
internal static class ConvertReport
{
	/// <summary>失败原因归类（日志摘要 + UI 完成摘要共用）。
	/// 关键字与各失败埋点的 reason 文案保持一致，新增失败原因时应同步此处。</summary>
	public static string ClassifyReason(string reason)
	{
		if (string.IsNullOrEmpty(reason)) return "";
		if (reason.Contains("已停止")) return "已停止";
		// v4.8：大文件跳过（需求②，"文件过大已跳过: xx > 阈值 yy"）
		if (reason.Contains("文件过大")) return "文件过大跳过";
		if (reason.Contains("图框问题")) return "图框问题";
		if (reason.Contains("体积过小") || reason.Contains("页尺寸异常")
			|| reason.Contains("内容流过空") || reason.Contains("页数异常"))
			return "质量核查不通过";
		if (reason.Contains("无图框数据")) return "无图框数据";
		if (reason.Contains("无预处理副本")) return "无预处理副本";
		return "出图失败";
	}

	/// <summary>失败原因分类摘要："图框问题 2、质量核查不通过 1"。无失败返回空串。</summary>
	public static string SummarizeFailures(List<ConvertRecord> records)
	{
		if (records == null) return "";
		var counts = new Dictionary<string, int>();
		foreach (var r in records)
		{
			if (r.Status != ItemStatus.Failed) continue;
			string cat = ClassifyReason(r.Reason);
			if (string.IsNullOrEmpty(cat)) cat = "出图失败";
			counts.TryGetValue(cat, out int c);
			counts[cat] = c + 1;
		}
		if (counts.Count == 0) return "";
		return string.Join("、", counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value));
	}
}
