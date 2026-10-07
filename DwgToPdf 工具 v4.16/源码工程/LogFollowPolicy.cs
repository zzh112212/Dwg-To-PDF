using System;

namespace DwgToPdf.Batch
{
    /// <summary>v4.15：日志面板"跟随滚动"策略（纯函数，便于单测）。
    ///
    /// 背景（用户反馈）：批量转换时日志刷新很快，原实现**每次追加后无条件**
    /// `SelectionStart=文末; ScrollToCaret()` → 用户向上翻阅历史日志会被不断拽回底部
    /// （表现为"日志不能上拉"）。
    ///
    /// 现规则：**勾选"自动滚动"且当前已停在末行**时跟随；一旦上翻（视口离开底部）即自动暂停跟随，
    /// 滚回底部或重新勾选后自动恢复 —— 与主流 IDE 的输出面板行为一致。</summary>
    internal static class LogFollowPolicy
    {
        /// <summary>是否应把日志面板跟随滚动到底部。
        /// autoScrollOn=界面"自动滚动"勾选状态；lastVisibleLine=当前可见的最后一行；
        /// lastLine=文末所在行（GetLineFromCharIndex(TextLength)）。保留 1 行宽容，
        /// 避免滚动条像素级误差导致"明明在底部却不跟随"。</summary>
        public static bool ShouldAutoScroll(bool autoScrollOn, int lastVisibleLine, int lastLine)
            => autoScrollOn && lastVisibleLine >= lastLine - 1;
    }
}
