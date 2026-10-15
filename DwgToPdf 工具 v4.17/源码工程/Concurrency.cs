using System;
namespace DwgToPdf.Batch
{
    /// <summary>CPU 自适应并发。
    /// v4.1：并发上限恢复为硬编码默认值（v4.0 实测最优），移除 settings.json 可配置。
    /// v4.0 性能验证：20核/32GB 机器上调高均导致资源竞争反而变慢。
    /// v4.12 修订：并发实测最优值是在"阶段2 走 COM 预处理"的旧架构下测得的；v4.12 起
    /// 出图链路已无 COM，本版新增环境变量钩子便于重新标定（仅测量用，GUI 不受影响）：
    ///   DWGTOPDF_RECOG_WORKERS / DWGTOPDF_PLOT_WORKERS / DWGTOPDF_PREP_WORKERS</summary>
    internal static class SmartConcurrency
    {
        public const int RecognitionMax = 12;
        public const int PrepMax = 4;
        public const int PlotMax = 8;
        public const int FallbackMax = 4;

        public static int RecognitionWorkers(int fileCount) => Clamp(fileCount, EnvMax("DWGTOPDF_RECOG_WORKERS", RecognitionMax));
        public static int PrepWorkers(int fileCount) => Clamp(fileCount, EnvMax("DWGTOPDF_PREP_WORKERS", PrepMax));
        public static int PlotWorkers(int fileCount) => Clamp(fileCount, EnvMax("DWGTOPDF_PLOT_WORKERS", PlotMax));

        /// <summary>v4.12 修订：并发上限可用环境变量覆盖（用于实测标定；未设置时返回默认值）。</summary>
        private static int EnvMax(string name, int dflt)
        {
            string s = null;
            try { s = Environment.GetEnvironmentVariable(name); } catch { }
            int v;
            if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s.Trim(), out v) && v >= 1 && v <= 64)
                return v;
            return dflt;
        }

        private static int Clamp(int fileCount, int max)
        {
            if (fileCount <= 0) return 1;
            int w = Math.Min(Environment.ProcessorCount, max);
            if (w > fileCount) w = fileCount;
            return Math.Max(w, 1);
        }
    }
}
