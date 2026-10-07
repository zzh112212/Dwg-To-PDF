namespace DwgToPdf.Batch;

/// <summary>转换模式：通用版用旧有逐层图框扫描；公司版用模板标准库快速匹配。</summary>
internal enum ConvertMode
{
    General = 0,
    Company = 1,
}

/// <summary>一次转换批次携带的运行配置（随 RunFiles/RunCore 贯穿各阶段）。</summary>
internal sealed class ConvertProfile
{
    public ConvertMode Mode { get; set; } = ConvertMode.General;

    public bool IsCompany => Mode == ConvertMode.Company;

    public static string Describe(ConvertMode m) => m switch
    {
        ConvertMode.Company => "公司版",
        _ => "通用版",
    };
}