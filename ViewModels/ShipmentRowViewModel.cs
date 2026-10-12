// ShipmentRowViewModel.cs —— 列表行 / 详情行的展示模型，以及「状态 → 文字/颜色」的统一映射
namespace ForwarderAssistant.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ForwarderAssistant.Models;
using ForwarderAssistant.Services;

/// <summary>主列表中的一行：一张提单的概览。最近节点与状态可随节点增/改/删实时重算。</summary>
public partial class ShipmentRowViewModel : ObservableObject
{
    /// <summary>对应的领域对象（节点增/改/删都改它，并据此重算概览）</summary>
    public Shipment Model { get; init; } = new();

    public string BlNo { get; init; } = "";
    public string Customer { get; init; } = "";
    public string VesselName { get; init; } = "";     // 船名（取自船期）
    public string VoyageNo { get; init; } = "";       // 航次（取自船期）
    public string Pol { get; init; } = "";            // 起运港（UN/LOCODE，取自船期）
    public string Pod { get; init; } = "";            // 卸货港（UN/LOCODE，取自船期）
    public string FinalDest { get; init; } = "—";     // 最终目的地（内陆点，每票货各自不同）
    public string EtdText { get; init; } = "—";       // 开船时间（取自船期）
    public string EtaText { get; init; } = "—";       // 到港时间（取自船期）

    /// <summary>开船日排序键：主列表默认按它「从新往老」排（晚开船在上）；缺 ETD 的给 MinValue，沉底。</summary>
    public DateTime EtdSortKey { get; init; } = DateTime.MinValue;

    /// <summary>该提单下的所有节点（详情区数据源，可增删）</summary>
    public ObservableCollection<MilestoneRowViewModel> Milestones { get; } = new();

    // 最近节点与概览状态：节点增/改/删后会变，所以做成可观察属性，让左表就地刷新。
    [ObservableProperty] private string _nearestLabel = "";
    [ObservableProperty] private string _nearestDueText = "";
    [ObservableProperty] private DateTime _nearestDueSortKey = DateTime.MaxValue;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private Brush _statusBrush = Brushes.Gray;
    [ObservableProperty] private MilestoneStatus? _status;

    /// <summary>按当前时间重算「最近节点」与概览状态。节点增/改/删后调用。</summary>
    public void Recompute(DateTime nowUtc)
    {
        var nearest = Model.Milestones
            .Select(m => new { Milestone = m, Status = MilestoneStatusCalculator.Calc(m, nowUtc) })
            .OrderBy(x => StatusPresenter.Rank(x.Status))
            .ThenBy(x => MilestoneDisplay.SortKey(x.Milestone))
            .FirstOrDefault();

        NearestLabel = nearest?.Milestone.Label ?? "—";
        NearestDueText = nearest is null ? "—" : MilestoneDisplay.Due(nearest.Milestone);
        NearestDueSortKey = nearest is null ? DateTime.MaxValue : MilestoneDisplay.SortKey(nearest.Milestone);
        StatusText = nearest is null ? "—" : StatusPresenter.Text(nearest.Status);
        StatusBrush = nearest is null ? Brushes.Gray : StatusPresenter.Brush(nearest.Status);
        Status = nearest?.Status;
    }

    /// <summary>把详情列表按节点时间升序就地重排（早的在上；未设时间的排最后）。增/改节点后调用。</summary>
    public void SortMilestones()
    {
        var ordered = Milestones.OrderBy(r => MilestoneDisplay.SortKey(r.Model)).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var from = Milestones.IndexOf(ordered[i]);
            if (from != i) Milestones.Move(from, i);
        }
    }
}

/// <summary>详情区中的一行：提单下的一个节点。可随编辑/时间变化刷新显示。</summary>
public partial class MilestoneRowViewModel : ObservableObject
{
    /// <summary>对应的领域对象（编辑时改它并落库）</summary>
    public Milestone Model { get; }

    /// <summary>节点主键（删除时用）</summary>
    public long Id => Model.Id;

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _dueText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private Brush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _ackGlyph = "";
    [ObservableProperty] private Brush _ackBrush = Brushes.Gray;
    [ObservableProperty] private string _ackTooltip = "";

    // 「已处理」切换按钮的字形配色：未处理=实心圆(中灰)，已处理=对勾(强调蓝)
    private static readonly Brush AckOn  = new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD));
    private static readonly Brush AckOff = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    public MilestoneRowViewModel(Milestone model, DateTime nowUtc)
    {
        Model = model;
        Refresh(nowUtc);
    }

    /// <summary>按当前时间重算时间显示与状态（编辑后、时间流逝后、切换已处理后调用）。</summary>
    public void Refresh(DateTime nowUtc)
    {
        var status = MilestoneStatusCalculator.Calc(Model, nowUtc);
        Label = Model.Label;
        DueText = MilestoneDisplay.Due(Model);
        StatusText = StatusPresenter.Text(status);
        StatusBrush = StatusPresenter.Brush(status);

        var handled = Model.AckAtUtc is not null;
        AckGlyph = handled ? "✓" : "●";
        AckBrush = handled ? AckOn : AckOff;
        AckTooltip = handled ? "撤销「已处理」" : "标记为已处理";
    }
}

/// <summary>节点时间 → 显示文本 / 排序键 的统一换算（唯一出处）。</summary>
public static class MilestoneDisplay
{
    /// <summary>仅日期节点只显示日期，精确节点显示到分，都没设则「未设置时间」。</summary>
    public static string Due(Milestone m)
        => m.DueDate is not null
            ? m.DueDate.Value.ToString("yyyy-MM-dd")
            : m.DueAtUtc is null ? "未设置时间" : m.DueAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>排序键：两种粒度归一到可比较的 UTC 时刻（仅日期取其当天本地 00:00 再转 UTC），都没设排最后。</summary>
    public static DateTime SortKey(Milestone m)
        => m.DueDate is not null
            ? DateTime.SpecifyKind(m.DueDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime()
            : m.DueAtUtc ?? DateTime.MaxValue;
}

/// <summary>状态筛选面板里的一项：要么是「全部」，要么是某个具体状态。</summary>
public partial class StatusFilterOption : ObservableObject
{
    /// <summary>对应的状态；null 表示「全部」这一项</summary>
    public MilestoneStatus? Status { get; init; }

    public string Text { get; init; } = "";

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>状态 → 中文名 / 颜色 / 排序优先级 的统一映射（唯一出处）。</summary>
public static class StatusPresenter
{
    /// <summary>全部节点状态（用于遍历与「是否全选」判断，顺序即筛选面板里的显示顺序）。</summary>
    public static readonly MilestoneStatus[] AllStatuses =
    {
        MilestoneStatus.RecordOnly,
        MilestoneStatus.Pending,
        MilestoneStatus.NeedsAction,
        MilestoneStatus.Overdue,
        MilestoneStatus.Handled,
    };

    public static string Text(MilestoneStatus s) => s switch
    {
        MilestoneStatus.RecordOnly  => "仅记录",
        MilestoneStatus.Pending     => "待办",
        MilestoneStatus.NeedsAction => "需要处理",
        MilestoneStatus.Overdue     => "超时",
        MilestoneStatus.Handled     => "已处理",
        _                           => "未知"
    };

    public static Brush Brush(MilestoneStatus s) => s switch
    {
        MilestoneStatus.RecordOnly  => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), // 灰
        MilestoneStatus.Pending     => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), // 绿
        MilestoneStatus.NeedsAction => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), // 橙
        MilestoneStatus.Overdue     => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), // 红
        MilestoneStatus.Handled     => new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD)), // 强调蓝
        _                           => Brushes.Gray
    };

    /// <summary>越小越该被「提到最上面」显示：超时最急，已处理最不需要关注。</summary>
    public static int Rank(MilestoneStatus s) => s switch
    {
        MilestoneStatus.Overdue     => 0,
        MilestoneStatus.NeedsAction => 1,
        MilestoneStatus.Pending     => 2,
        MilestoneStatus.RecordOnly  => 3,
        MilestoneStatus.Handled     => 4,
        _                           => 5
    };
}
