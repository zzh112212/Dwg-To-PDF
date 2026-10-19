using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DwgToPdf.Batch
{
    /// <summary>图框坐标缓存：存于输出文件夹 frame_cache.json。键=路径+长度+修改时间+版本。</summary>
    internal static class FrameCache
    {
        // v4.6.1：1→2。v4.6.0 及更早版本写入的缓存中，legacy（"图层图框"层）结果未经尺寸校验，
        // 可能是 2.5×2.5 / 277×131 这类误识框；若沿用会让修复后的校验要么误拒（极小框）、
        // 要么放过中尺寸坏框（277×131 能通过 100mm 下限）再次产出错误 PDF。
        // 升版本号使全部旧缓存一次性失效，重新识别一次即可。
        // v4.9：2→3。图框识别引入 RECT4（真实矩形图框）与 RECT4_OUT（疑似离群实体）判定，
        // 旧缓存无该结果，沿用会导致新逻辑完全不生效（重测全部缓存命中），故整体失效重建。
        // v4.10：3→4。RECT4 算法升级为多段边线聚类联合覆盖（双线图框外框可识别），
        // 旧缓存的内框结果沿用会继续裁掉外框，故整体失效重建。
        // v4.15：版本 4 → 5（Entry 新增 img=光栅图片实体数；旧缓存缺该字段会被
        // 误判为"无图片"→ 图片图纸拿不到光栅增强。版本不一致时缓存自然失效，
        // 触发一次全量重识别（约 3 分钟），属预期代价）
        // v4.15：版本 5 → 6（Entry 新增 imgr/imgb=真光栅 IMAGE 数 / 外部图缺失数；
        // 旧缓存缺这两个字段会被误判为"无真光栅图"→ 光栅增强完全不生效，故失效重建）
        // v4.16：版本 6 → 7（Entry 新增 g76=Model 布局"着色打印"组码值；旧缓存缺该字段
        // 会默认为 0 → 8 张 g76=4(渲染) 的"打印发浅"图纸拿不到 COM 出图修复，故失效重建）
        public const int Version = 7;
        public const string FileName = "frame_cache.json";

        [DataContract]
        public sealed class Entry
        {
#pragma warning disable CS0649 // 字段由 DataContractJsonSerializer 反射赋值，编译器静态分析无法识别
            [DataMember(Name = "path")] public string Path;
            [DataMember(Name = "len")] public long Length;
            [DataMember(Name = "mt")] public long MtimeUtc;
            [DataMember(Name = "minx")] public double MinX;
            [DataMember(Name = "miny")] public double MinY;
            [DataMember(Name = "maxx")] public double MaxX;
            [DataMember(Name = "maxy")] public double MaxY;
            [DataMember(Name = "src")] public string Source;
        /// <summary>v4.15：图片/嵌入对象/参考底图实体总数（0=无）。</summary>
        [DataMember(Name = "img")] public int ImageCount;
        /// <summary>v4.15：真光栅 IMAGE 实体数（0=无真光栅图）。</summary>
        [DataMember(Name = "imgr")] public int RasterCount;
        /// <summary>v4.15：真光栅 IMAGE 中"外部图文件缺失"的个数（&gt;0 → 不启用光栅增强）。</summary>
        [DataMember(Name = "imgb")] public int RasterMissingCount;
        /// <summary>v4.16：Model 布局"着色打印"组码 76 值（lsp G76|n；0/1=按显示，4=渲染…）。
        /// ≠0 时出图改用 COM 通道（accore 的 -EXPORT 会把整页按渲染光栅化 → 线条发浅）。</summary>
        [DataMember(Name = "g76")] public int G76;
#pragma warning restore CS0649
        }

        [DataContract]
        private sealed class CacheFile
        {
            [DataMember(Name = "version")] public int Version = FrameCache.Version;
            [DataMember(Name = "entries")] public List<Entry> Entries = new List<Entry>();
        }

        public static string CachePath(string outputFolder) => Path.Combine(outputFolder, FileName);

        /// <summary>命中返回 true 且 out entry 有效；文件失配/缓存缺失/损坏返回 false（不抛异常）。
        /// 缓存文件定位在 dwg 文件所在目录（保持 v2.3 语义，供既有调用/自测使用）。</summary>
        public static bool TryGet(string dwgPath, out Entry entry)
        {
            entry = null;
            try
            {
                var fi = new FileInfo(dwgPath);
                if (!fi.Exists) return false;
                return TryGet(dwgPath, fi.DirectoryName, out entry);
            }
            catch { return false; }
        }

        /// <summary>按指定的缓存目录读取（v3.0 流程：缓存文件存于输出文件夹 frame_cache.json）。</summary>
        public static bool TryGet(string dwgPath, string cacheFolder, out Entry entry)
        {
            entry = null;
            try
            {
                var fi = new FileInfo(dwgPath);
                if (!fi.Exists) return false;
                Load(cacheFolder, out CacheFile cache);
                if (cache == null) return false;
                foreach (var e in cache.Entries)
                {
                    if (string.Equals(e.Path, dwgPath, System.StringComparison.OrdinalIgnoreCase)
                        && e.Length == fi.Length
                        && e.MtimeUtc == fi.LastWriteTimeUtc.Ticks)
                    {
                        entry = e;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>整体落盘（临时文件+原子替换）；输出目录不存在则创建。失败静默。</summary>
        public static void Save(string outputFolder, List<Entry> entries)
        {
            try
            {
                Directory.CreateDirectory(outputFolder);
                var file = new CacheFile { Entries = entries ?? new List<Entry>() };
                var ser = new DataContractJsonSerializer(typeof(CacheFile));
                string tmp = CachePath(outputFolder) + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    ser.WriteObject(fs, file);
                try { System.IO.File.Copy(tmp, CachePath(outputFolder), true); }
                finally { try { System.IO.File.Delete(tmp); } catch { } }
            }
            catch { }
        }

        /// <summary>读取缓存文件内容（损坏/缺失→空列表）。</summary>
        public static List<Entry> LoadEntries(string outputFolder)
        {
            Load(outputFolder, out CacheFile cache);
            return cache?.Entries ?? new List<Entry>();
        }

        /// <summary>v4.4：一次性把缓存加载进内存（键=路径，忽略大小写）。
        /// 单轮转换内对每文件查询 2 次（阶段1 统计 + 阶段3 出图），
        /// 原实现每次 TryGet 都重读磁盘并解析 JSON（40 张 ≈ 80 次读盘），
        /// 快照后整轮只需 1~2 次读盘（v4.0 优化清单第 1 条落地）。</summary>
        public static Dictionary<string, Entry> Snapshot(string outputFolder)
        {
            var dict = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in LoadEntries(outputFolder))
                {
                    if (!string.IsNullOrEmpty(e.Path) && !dict.ContainsKey(e.Path))
                        dict[e.Path] = e;
                }
            }
            catch { }
            return dict;
        }

        /// <summary>v4.4：从内存快照查询，命中语义与 TryGet(dwgPath, cacheFolder) 完全一致
        /// （文件存在 + 长度一致 + 修改时间一致）。快照缺失/失配返回 false，不抛异常。</summary>
        public static bool TryGetFrom(Dictionary<string, Entry> snapshot, string dwgPath, out Entry entry)
        {
            entry = null;
            try
            {
                if (snapshot == null || snapshot.Count == 0) return false;
                var fi = new FileInfo(dwgPath);
                if (!fi.Exists) return false;
                if (snapshot.TryGetValue(dwgPath, out var e)
                    && e != null
                    && e.Length == fi.Length
                    && e.MtimeUtc == fi.LastWriteTimeUtc.Ticks)
                {
                    entry = e;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void Load(string outputFolder, out CacheFile cache)
        {
            try
            {
                if (!System.IO.File.Exists(CachePath(outputFolder))) { cache = null; return; }
                var ser = new DataContractJsonSerializer(typeof(CacheFile));
                using (var fs = new FileStream(CachePath(outputFolder), FileMode.Open, FileAccess.Read))
                {
                    var f = (CacheFile)ser.ReadObject(fs);
                    cache = (f != null && f.Version == Version) ? f : null;
                }
            }
            catch { cache = null; }
        }
    }
}
