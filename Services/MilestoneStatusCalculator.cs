// MilestoneStatusCalculator.cs —— 五态状态机的唯一计算入口
namespace ForwarderAssistant.Services;

using ForwarderAssistant.Models;

/// <summary>
/// 根据「节点时间 + 是否已处理 + 当前时间」算出节点状态。
/// 这是整个软件的核心规则：界面各处都从这里取状态，避免各写各的、对不上。
/// </summary>
public static class MilestoneStatusCalculator
{
    /// <summary>「需要处理」的时间窗（分钟）：到点前后各 5 分钟。</summary>
    public const int ActionWindowMinutes = 5;

    /// <summary>计算某个节点在 nowUtc 时刻的状态。</summary>
    public static MilestoneStatus Calc(Milestone m, DateTime nowUtc)
    {
        // 1) 已处理优先：只要点过「已处理」，就算时间再走动也永远算处理完
        if (m.AckAtUtc is not null)
            return MilestoneStatus.Handled;

        // 2) 两种时间都没设 = 仅记录
        if (m.DueAtUtc is null && m.DueDate is null)
            return MilestoneStatus.RecordOnly;

        // 3) 仅日期节点（开船 / 付费这类「哪一天」的事）：按本地日历日判断。
        //    规则：到日之前 = 待办；当天整天 = 需要处理；过了当天 = 超时。
        //    （例：12 号截止 → 12 号还有得救，13 号才变红。）
        if (m.DueDate is not null)
        {
            var today = DateOnly.FromDateTime(nowUtc.ToLocalTime());
            if (today < m.DueDate.Value)  return MilestoneStatus.Pending;
            if (today == m.DueDate.Value) return MilestoneStatus.NeedsAction;
            return MilestoneStatus.Overdue;
        }

        // 4) 精确节点：按「到点前后各 ActionWindowMinutes 分钟」的窗口判断
        var due = m.DueAtUtc!.Value;
        var window = TimeSpan.FromMinutes(ActionWindowMinutes);

        if (nowUtc < due - window)  return MilestoneStatus.Pending;      // 还没到点
        if (nowUtc <= due + window) return MilestoneStatus.NeedsAction;  // 到点窗口内
        return MilestoneStatus.Overdue;                                   // 超过窗口仍未处理
    }
}
