using System;

namespace DwgToPdf.Batch
{
    /// <summary>纸张/旋转/比例/边距换算。COM 兜底与 Core Console 通道共用，保证输出一致。</summary>
    internal static class PageSpec
    {
        public const int AcDegrees0 = 0;
        public const int AcDegrees90 = 1;

        public static readonly double[] PaperLongMm = { 297.0, 420.0, 594.0, 841.0, 1189.0, 1682.0, 2378.0 };

        // v4.6：删除未引用的 MediaNames（非全出血纸名数组，v2.3.1 诊断遗留；两通道均走 FullBleedMediaNames）
        public static readonly string[] FullBleedMediaNames =
        {
            "ISO_full_bleed_A4_(210.00_x_297.00_MM)", "ISO_full_bleed_A3_(297.00_x_420.00_MM)",
            "ISO_full_bleed_A2_(420.00_x_594.00_MM)", "ISO_full_bleed_A1_(594.00_x_841.00_MM)",
            "ISO_full_bleed_A0_(841.00_x_1189.00_MM)", "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)",
            "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)"
        };

        /// <summary>
        /// v4.16（2026-10-07，用户反馈 PISCES 2A0 图纸"打印发浅"）：选纸容差。
        /// 原逻辑按"图框 + 2*边距"与纸型**严格**比较，于是像 1692x1199mm 这种
        /// "比2A0(1682x1189)只大 10mm" 的图框会被顶到 **4A0**，页面上同样的内容
        /// 缩到约 1/1.41 ——按页缩放查看/打印时，线宽相对纸面宽度减半，**看起来就"发浅"**
        /// （文字仍可读，因为字高远大于线宽）。用户自己的手动打印（plot.log）正是
        /// "ISO full bleed 2A0 (1189x1682) 1:1"，即参照就是 2A0。
        /// 现在允许纸型比"图框+2*边距"最多小 PaperFitTolerance（3%），由下面的
        /// s=min((纸长-2M)/窗口长, (纸短-2M)/窗口短) 自然把内容缩一点点放下去：
        /// 1692x1199 → 2A0，s=0.959（内容 1642x1169，四周 ≥10mm 余量）。
        /// 3% 的来历（实测 7633640/7643423：图框 1692x1199，边距 10mm，短边需求 1219mm
        /// 对 2A0 短边 1189mm 超出 2.52% ⇒ 2% 不够，3% 刚好）。
        /// 该容差只在"图框略微超纸"时生效（A 系列正好卡边界的图纸），
        /// 其它图纸选纸结果与 v4.15 完全一致 ⇒ 无回归面。
        /// 对比：命中容差时内容最多缩 3%（不可察觉），而不命中就会跳到下一张纸、
        /// 内容缩到 1/1.41（相对纸面宽度细一半 = 用户看到的"发浅"）。
        /// </summary>
        public const double PaperFitTolerance = 0.03;

        public sealed class Resolved
        {
            public string Media;
            public int Rotation;      // PageSpec.AcDegrees0 / AcDegrees90
            public double PaperLong;  // mm
            public double PaperShort; // mm
            public double Scale;      // mm per drawing unit（精确模式）
        }

        /// <summary>按"图框+2*边距"选择全出血纸张；返回纸张长/短边(mm)、旋转、精确缩放比 s。
        /// 算法逐行等价于原 CadConverter.ResolvePageEx(frameW,frameH,margin,out media,out rotation,out paperLong,out paperShort)。</summary>
        public static Resolved Resolve(double frameW, double frameH, double margin)
        {
            bool landscape = frameW >= frameH;
            double longNeed = (landscape ? frameW : frameH) + 2.0 * margin;
            double shortNeed = (landscape ? frameH : frameW) + 2.0 * margin;
            var shorts = ShortSides();
            int idx = 0;
            for (int i = 0; i < PaperLongMm.Length; i++)
            {
                // v4.16：带 PaperFitTolerance 的比较（见常量注释）。只在图框略微超纸时
                // 才会选到"小一点的纸"，其余情况与 v4.15 逐位等价。
                double tolLong = PaperLongMm[i] * (1.0 + PaperFitTolerance);
                double tolShort = shorts[i] * (1.0 + PaperFitTolerance);
                if (tolLong >= longNeed && tolShort >= shortNeed) { idx = i; break; }
            }
            if (idx >= PaperLongMm.Length) idx = PaperLongMm.Length - 1; // 超大图框兜底到 4A0
            double paperLong = PaperLongMm[idx];
            double paperShort = shorts[idx];
            double frameAlongLong = landscape ? frameW : frameH;
            double frameAlongShort = landscape ? frameH : frameW;
            // v3.8 方案 G（2026-08-31）：出图窗口=图框+2M（识别 frame 常小于真实图框外框，
            // 窗口=frame 会裁掉超出部分，见 11 张 H56E 图框显示不完全 bug），
            // 故 s 以"窗口尺寸 = frame + 2M"为分母，保证 (frame+2M)×s ≤ 纸-2M 两个方向
            // 都成立——短边恰好留 M 边距（精确 10mm）、长边边距更大但对称居中，
            // 且真实图框（含识别偏差）完整落在窗口内。
            // 数学验证（LB200：frame 1189×841、M=10、2A0 纸 1682×1189）：
            //   s = min(1662/1209, 1169/861) = 1.3577
            //   打印 = 1209×1.3577=1641.5、861×1.3577=1169.0
            //   边距 L=R=(1682-1641.5)/2=20.2、T=B=(1189-1169)/2=10 ✓ 真实图框完整 ✓
            double winLong = frameAlongLong + 2.0 * margin;
            double winShort = frameAlongShort + 2.0 * margin;
            double s = Math.Min((paperLong - 2.0 * margin) / winLong,
                                (paperShort - 2.0 * margin) / winShort);
            if (s <= 0.0) s = Math.Min(paperLong / frameAlongLong, paperShort / frameAlongShort); // 兜底保证 s 恒正（相对 v2.3.1 的健壮性增强）

            return new Resolved
            {
                Media = FullBleedMediaNames[idx],
                Rotation = landscape ? AcDegrees90 : AcDegrees0,
                PaperLong = paperLong,
                PaperShort = paperShort,
                Scale = s,
            };
        }

        /// <summary>v4.12：从全出血纸型名解析"实际纸宽/高"（组码 44/45 用真实纸宽高）。
        /// 例："ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)" → 1682 / 2378。
        /// 注意与 PaperShort 的区别：PaperShort=round(长边/√2) 是近似值（4A0 → 1681），
        /// 而 COM 预处理实测写入布局的是真实纸宽 1682（居中原点按 1682 计算）——故取纸型名尺寸为准。</summary>
        public static bool TryMediaDims(string media, out double w, out double h)
        {
            w = h = 0.0;
            if (string.IsNullOrEmpty(media)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(
                media, @"_\((\d+(?:\.\d+)?)_x_(\d+(?:\.\d+)?)_MM\)");
            if (!m.Success) return false;
            double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out w);
            double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out h);
            return w > 0.0 && h > 0.0;
        }

        public static double[] ShortSides()
        {
            var shorts = new double[PaperLongMm.Length];
            for (int i = 0; i < PaperLongMm.Length; i++)
                shorts[i] = Math.Round(PaperLongMm[i] / Math.Sqrt(2.0));
            return shorts;
        }
    }
}
