// ImportPreview.cs —— Excel 导入预览的结果模型（解析后、落库前）
namespace ForwarderAssistant.Models;

/// <summary>一次 Excel 导入的预览结果：逐条草稿 + 是否与库中已有提单号重复，另带被跳过的行与表头提示。</summary>
public sealed class ImportPreview
{
    /// <summary>可导入的提单草稿（逐条标出是否重复）</summary>
    public List<ImportPreviewItem> Items { get; init; } = new();

    /// <summary>被跳过的行说明（给人看的中文原因）</summary>
    public List<string> Skipped { get; init; } = new();

    /// <summary>表头冲突等提示</summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>疑似重复（库中已有同提单号）的条数</summary>
    public int DuplicateCount => Items.Count(i => i.IsDuplicate);
}

/// <summary>预览中的一条提单草稿：领域对象 + 是否与库中已有提单号重复。</summary>
public sealed class ImportPreviewItem
{
    public Shipment Shipment { get; init; } = new();

    /// <summary>库中已存在同提单号（Trim + 忽略大小写后相同）</summary>
    public bool IsDuplicate { get; init; }
}
