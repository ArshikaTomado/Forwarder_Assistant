// Milestone.cs —— 节点实体（第 0 步仅用于展示假数据）
namespace ForwarderAssistant.Models;

/// <summary>提单上的一个时间节点，如 截关 / 开船 / 到港 / 免箱期。</summary>
public class Milestone
{
    /// <summary>数据库主键；0 = 尚未落库（手工新建、还没写进 milestones 表）</summary>
    public long Id { get; set; }

    /// <summary>节点代码，对应 config.toml 里的 code（如 cutoff）；手工节点用 custom</summary>
    public string Kind { get; set; } = "";

    /// <summary>显示名，如「截关」</summary>
    public string Label { get; set; } = "";

    /// <summary>精确节点时间（UTC，精确到分）；与 <see cref="DueDate"/> 二选一</summary>
    public DateTime? DueAtUtc { get; set; }

    /// <summary>仅日期节点（本地日历日，不含时分）；与 <see cref="DueAtUtc"/> 二选一。
    /// 为什么按本地日存、不转 UTC：像「开船 / 付费」这种是"某一天"，强行存 UTC 的零点会跨时区漂成别的日子。</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>用户点「已处理」的时刻（UTC）；null = 未处理</summary>
    public DateTime? AckAtUtc { get; set; }
}
