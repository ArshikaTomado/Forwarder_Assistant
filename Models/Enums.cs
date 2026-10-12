// Enums.cs —— 节点状态枚举：整个软件的核心状态机定义
namespace ForwarderAssistant.Models;

/// <summary>
/// 节点状态。任一时刻，一个节点只会落在其中一种状态里。
/// 只有「已处理」是用户手动确认的；其余四种都由当前时间自动算出。
/// </summary>
public enum MilestoneStatus
{
    /// <summary>仅记录：没设置时间，只当个记录（灰）</summary>
    RecordOnly,

    /// <summary>待办：有时间，还没到点（绿）</summary>
    Pending,

    /// <summary>需要处理：到点前后 5 分钟内（橙）</summary>
    NeedsAction,

    /// <summary>超时：超过 5 分钟还没处理（红）</summary>
    Overdue,

    /// <summary>已处理：用户手动确认处理完（蓝）</summary>
    Handled
}
