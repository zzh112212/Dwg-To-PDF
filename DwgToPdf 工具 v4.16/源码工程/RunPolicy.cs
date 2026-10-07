using System;
using System.Collections.Generic;
using System.Linq;

namespace DwgToPdf.Batch
{
    /// <summary>v4.15：**运行级策略**集中地（纯函数，便于单元测试与后续调参）。
    ///
    /// 目前承载"批次结束后对瞬态类失败自动重试一次"的判定。
    /// 背景（2026-09-16 实测）：accoreconsole 的「未能找到路径中的某个部分」在批量下偶发、
    /// 隔离复跑即消失（v4.12 时代已有记录）；公司版 40 张里 4 张"坐垫按摩气袋"曾因此被判失败
    /// （转 COM 又抛 E_INVALIDARG）。这类失败**重试一次**即可挽回。
    ///
    /// 设计原则：**只重试"瞬态类"原因**——必然复现的问题（图框问题/无图框数据/内容空白/
    /// 页尺寸异常/质量核查不通过）不重试，避免白耗时间。</summary>
    internal static class RunPolicy
    {
        /// <summary>批次结束后自动重试的次数上限（串行执行，1 次）。</summary>
        public const int MaxAutoRetryPasses = 1;

        /// <summary>瞬态类失败原因的关键字（包含匹配，忽略大小写）。</summary>
        private static readonly string[] TransientPatterns =
        {
            "未能找到路径中的某个部分",   // accore 会话临时路径竞态（本轮实测的主要瞬态）
            "出图会话超时",               // 机器负载高时 accore 24x 秒超时；负载下降后可成功
            "E_INVALIDARG",               // COM 参数瞬态（多为上层路径异常连带）
            "参数错误",                   // 上条的中文文案
            "文件被占用",                 // 杀毒/索引占用
            "拒绝访问",                   // 权限瞬时拒绝
            "未检测到 PDF 文件",          // 写盘迟到（已有迟到复核，重试为第二道保险）
        };

        /// <summary>该失败原因是否值得在批次结束后重试一次。</summary>
        public static bool ShouldRetryAfterBatch(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            // 必然复现类：显式排除（即使文案里含其它关键词也不重试）
            if (reason.Contains("图框问题") || reason.Contains("无图框数据")) return false;
            if (reason.Contains("内容为空白")) return false;      // 图纸本身不可按此方式出图
            if (reason.Contains("页尺寸异常") || reason.Contains("页数异常")) return false;
            if (reason.Contains("质量核查") || reason.Contains("内容流过空") || reason.Contains("体积过小")) return false;
            var extra = ExtraPatterns();
            foreach (var p in TransientPatterns) if (reason.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var p in extra) if (!string.IsNullOrEmpty(p) && reason.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>诊断/测试用：`DWGTOPDF_RETRY_EXTRA=关键词1,关键词2` 追加瞬态关键词
        /// （默认空，不影响生产判定；用于自测注入"确定性失败"验证重试链路）。</summary>
        public static string[] ExtraPatterns()
        {
            try
            {
                string s = Environment.GetEnvironmentVariable("DWGTOPDF_RETRY_EXTRA");
                if (string.IsNullOrWhiteSpace(s)) return new string[0];
                return s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            }
            catch { return new string[0]; }
        }

        /// <summary>自动重试总开关（`DWGTOPDF_NO_RETRY=1` 关闭）。</summary>
        public static bool AutoRetryEnabled()
        {
            try { return Environment.GetEnvironmentVariable("DWGTOPDF_NO_RETRY") != "1"; }
            catch { return true; }
        }
    }
}
