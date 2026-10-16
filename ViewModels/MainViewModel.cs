// MainViewModel.cs —— 主界面视图模型
// 第 1 步：数据改为从 SQLite 读（经 ShipmentService）；空库时自动灌一批演示数据。
namespace ForwarderAssistant.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using ForwarderAssistant.Models;
using ForwarderAssistant.Services;

public partial class MainViewModel : ObservableObject
{
    /// <summary>主列表数据源</summary>
    public ObservableCollection<ShipmentRowViewModel> Shipments { get; } = new();

    /// <summary>状态筛选面板的选项：「全部」+ 五个官方状态</summary>
    public ObservableCollection<StatusFilterOption> StatusFilterOptions { get; } = new();

    /// <summary>当前选中的提单（右侧详情随它变化）</summary>
    [ObservableProperty]
    private ShipmentRowViewModel? _selectedShipment;

    /// <summary>搜索词（提单号 / 客户）。纯内存匹配，边打边过滤。</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>节点搜索词（按节点名过滤右侧详情列表）。纯内存匹配，边打边过滤；换提单时清空。</summary>
    [ObservableProperty]
    private string _milestoneSearchText = "";

    /// <summary>右侧节点列表的过滤视图：跟随当前选中提单的节点集合，随搜索词实时过滤。</summary>
    [ObservableProperty]
    private ICollectionView? _milestoneView;

    /// <summary>数据存放位置说明（便携 / 用户目录），显示在底部状态栏</summary>
    public string DataLocation { get; } =
        (AppPaths.IsPortable ? "便携数据：" : "数据位置：") + AppPaths.DataRoot;

    // 当前勾选的节点状态；勾满五个 = 不过滤。同步「全部」与具体项时用 _syncingFilters 防止递归。
    private readonly HashSet<MilestoneStatus> _checkedStatuses = new();
    private bool _syncingFilters;
    private ICollectionView? _shipmentsView;
    private readonly ShipmentService _service = new();

    // 已弹过提醒的节点键（节点Id + 该次的时间）。改过时间后键会变，于是重新提醒一次。
    private readonly HashSet<string> _remindedKeys = new();

    public MainViewModel()
    {
        var now = DateTime.UtcNow;

        // 手工装配（禁止 DI 容器）：直接从服务层取全部提单，按 ETD「从新往老」排（晚开船在上）
        var rows = _service.GetAll().Select(s => ToRow(s, now)).ToList();
        rows.Sort(CompareRows);
        foreach (var row in rows)
            Shipments.Add(row);

        SelectedShipment = Shipments.FirstOrDefault();

        // 状态筛选：给列表的默认视图挂过滤谓词，勾选项变化时 Refresh 即可
        BuildStatusFilterOptions();
        _shipmentsView = CollectionViewSource.GetDefaultView(Shipments);
        _shipmentsView.Filter = FilterShipment;
    }

    /// <summary>整表重新从库里加载（供「船名 · 航次」窗格改过航次后刷新主列表，如重命名后的船名 / 航次）。</summary>
    public void Reload()
    {
        var now = DateTime.UtcNow;

        var rows = _service.GetAll().Select(s => ToRow(s, now)).ToList();
        rows.Sort(CompareRows);

        Shipments.Clear();
        foreach (var row in rows)
            Shipments.Add(row);

        SelectedShipment = Shipments.FirstOrDefault();
        _shipmentsView?.Refresh();
    }

    /// <summary>主列表排序：按开船日（ETD）从新到老（晚开船在上）；缺 ETD 的沉底，同日再按提单号，保证顺序稳定。</summary>
    private static int CompareRows(ShipmentRowViewModel a, ShipmentRowViewModel b)
    {
        var byEtd = b.EtdSortKey.CompareTo(a.EtdSortKey);
        return byEtd != 0 ? byEtd : string.CompareOrdinal(a.BlNo, b.BlNo);
    }

    // ================= 领域模型 → 界面行 =================
    private static ShipmentRowViewModel ToRow(Shipment s, DateTime now)
    {
        var row = new ShipmentRowViewModel
        {
            Model = s,
            BlNo = s.BlNo,
            Customer = s.Customer,
            VesselName = s.Voyage.VesselName,               // 船名，取自航次身份
            VoyageNo = s.Voyage.VoyageNo,                   // 航次，取自航次身份
            Pol = s.Pol,                                    // 装货港，本票自己的
            Pod = s.Pod,                                    // 卸货港，本票自己的
            FinalDest = string.IsNullOrWhiteSpace(s.FinalDestination) ? "—" : s.FinalDestination, // 最终目的地，每票货各自不同
            EtdText = FormatDate(s.EtdUtc),                // 开船日期，由「装货港」对应挂靠港带出（只要日期）
            EtaText = FormatDate(s.EtaUtc),                // 到港日期，由「卸货港」对应挂靠港带出（只要日期）
            EtdSortKey = s.EtdUtc ?? DateTime.MinValue,    // 默认排序键：开船日，缺则沉底
        };

        foreach (var m in s.Milestones)
            row.Milestones.Add(new MilestoneRowViewModel(m, now));

        row.SortMilestones();
        row.Recompute(now);
        return row;
    }

    // ================= 提单新增 =================

    /// <summary>新增提单：落库 → 建行 → 按 ETD 排序插入左表 → 选中它。</summary>
    public ShipmentRowViewModel AddShipment(Shipment s)
    {
        s.Id = _service.AddShipment(s);

        var row = ToRow(s, DateTime.UtcNow);

        // 左表按 ETD 排序（与默认视图一致），插到正确位置
        var index = 0;
        while (index < Shipments.Count && CompareRows(Shipments[index], row) <= 0)
            index++;
        Shipments.Insert(index, row);

        SelectedShipment = row;
        return row;
    }

    /// <summary>批量删除提单：逐条落库（连同其全部节点）→ 从列表移除 → 选中相邻一行 → 刷新。
    /// 多选时以「最靠上的被删行」的位置为锚，删完选该位置当前这一行（没了就选最后一行）。</summary>
    public void DeleteShipments(IReadOnlyList<ShipmentRowViewModel> rows)
    {
        if (rows.Count == 0) return;

        foreach (var row in rows)
            _service.DeleteShipment(row.Model.Id);

        var anchor = Shipments.Count;
        foreach (var row in rows)
        {
            var index = Shipments.IndexOf(row);
            if (index >= 0 && index < anchor) anchor = index;
        }

        foreach (var row in rows)
            Shipments.Remove(row);

        SelectedShipment = Shipments.Count == 0
            ? null
            : Shipments[Math.Min(anchor, Shipments.Count - 1)];

        _shipmentsView?.Refresh();
    }

    /// <summary>修改提单：落库 → 用编辑后的字段重建该行并重新归位。
    /// 同「船名+航次」的其它提单与本票共用一份挂靠港日期：本票装/卸港对应的日期若有变，
    /// 这些兄弟行的 ETD/ETA 随之刷新（各票的装/卸港本身互不干扰）。</summary>
    public void UpdateShipment(ShipmentRowViewModel row, Shipment edited)
    {
        edited.Milestones = row.Model.Milestones;   // 节点不属于本次编辑，原样保留
        _service.UpdateShipment(edited);

        var now = DateTime.UtcNow;
        ReplaceRow(row, edited, now, select: true);

        // 同航次的其它提单：只把「与本票同港」的那一端日期同步过去，装/卸港各归各票
        foreach (var sibling in Shipments.Where(r =>
                     r.Model.Id != edited.Id &&
                     r.Model.Voyage.VesselName == edited.Voyage.VesselName &&
                     r.Model.Voyage.VoyageNo == edited.Voyage.VoyageNo).ToList())
        {
            if (string.Equals(sibling.Model.Pol, edited.Pol, StringComparison.OrdinalIgnoreCase))
                sibling.Model.EtdUtc = edited.EtdUtc;
            if (string.Equals(sibling.Model.Pod, edited.Pod, StringComparison.OrdinalIgnoreCase))
                sibling.Model.EtaUtc = edited.EtaUtc;
            ReplaceRow(sibling, sibling.Model, now, select: false);
        }

        _shipmentsView?.Refresh();
    }

    /// <summary>用领域对象重建某一行的显示：从列表移除旧行 → 按 ETD 插回正确位置 → 维护选中。</summary>
    private void ReplaceRow(ShipmentRowViewModel row, Shipment model, DateTime nowUtc, bool select)
    {
        var newRow = ToRow(model, nowUtc);

        Shipments.Remove(row);
        var index = 0;
        while (index < Shipments.Count && CompareRows(Shipments[index], newRow) <= 0)
            index++;
        Shipments.Insert(index, newRow);

        if (select || ReferenceEquals(SelectedShipment, row))
            SelectedShipment = newRow;
    }

    // ================= 节点增 / 改 / 删 =================

    /// <summary>新增节点：落库 → 加进详情 → 重算左表概览与状态筛选。</summary>
    public void AddMilestone(ShipmentRowViewModel shipment, Milestone milestone)
    {
        milestone.Id = _service.AddMilestone(shipment.Model.Id, milestone);
        shipment.Model.Milestones.Add(milestone);

        var now = DateTime.UtcNow;
        shipment.Milestones.Add(new MilestoneRowViewModel(milestone, now));
        shipment.SortMilestones();
        shipment.Recompute(now);
        _shipmentsView?.Refresh();
    }

    /// <summary>编辑节点：把对话框产出的数据写回领域对象 → 落库 → 就地刷新显示与左表概览。</summary>
    public void UpdateMilestone(ShipmentRowViewModel shipment, MilestoneRowViewModel row, Milestone edited)
    {
        row.Model.Kind = edited.Kind;
        row.Model.Label = edited.Label;
        row.Model.DueAtUtc = edited.DueAtUtc;
        row.Model.DueDate = edited.DueDate;
        _service.UpdateMilestone(row.Model);

        var now = DateTime.UtcNow;
        row.Refresh(now);
        shipment.SortMilestones();
        shipment.Recompute(now);
        _shipmentsView?.Refresh();
    }

    /// <summary>切换节点的「已处理」标记：没处理过就记为已处理，再点则撤销（可反复点）。
    /// 落库 → 就地刷新该节点状态与左表概览、状态筛选。</summary>
    public void ToggleMilestoneHandled(ShipmentRowViewModel shipment, MilestoneRowViewModel row)
    {
        row.Model.AckAtUtc = row.Model.AckAtUtc is null ? DateTime.UtcNow : null;
        _service.UpdateMilestone(row.Model);

        var now = DateTime.UtcNow;
        row.Refresh(now);
        shipment.Recompute(now);
        _shipmentsView?.Refresh();
    }

    /// <summary>删除节点：落库 → 从详情移除 → 重算左表概览与状态筛选。</summary>
    public void DeleteMilestone(ShipmentRowViewModel shipment, MilestoneRowViewModel row)
    {
        _service.DeleteMilestone(row.Id);
        shipment.Model.Milestones.Remove(row.Model);
        shipment.Milestones.Remove(row);

        var now = DateTime.UtcNow;
        shipment.Recompute(now);
        _shipmentsView?.Refresh();
    }

    /// <summary>UTC → 本地「日期」显示（ETD/ETA 只看哪天，不看几点）；null 用 fallback</summary>
    private static string FormatDate(DateTime? utc, string fallback = "—")
    {
        if (utc is null) return fallback;
        return utc.Value.ToLocalTime().ToString("yyyy-MM-dd");
    }

    // ================= 调度心跳（第 3 步） =================

    /// <summary>每 30 秒重算全部行与节点的状态，并让状态筛选重新过滤（运行中状态不刷新即由此根治）。</summary>
    public void RefreshAllStatuses(DateTime nowUtc)
    {
        foreach (var row in Shipments)
        {
            foreach (var m in row.Milestones)
                m.Refresh(nowUtc);
            row.Recompute(nowUtc);
        }
        _shipmentsView?.Refresh();
    }

    /// <summary>挑出「刚进入需要处理」且还没弹过提醒的节点，返回 (提单号, 节点名)；同一节点每次到点只弹一遍。</summary>
    public List<(string BlNo, string Label)> CollectDueReminders(DateTime nowUtc)
    {
        var due = new List<(string, string)>();
        foreach (var row in Shipments)
            foreach (var m in row.Model.Milestones)
            {
                if (MilestoneStatusCalculator.Calc(m, nowUtc) != MilestoneStatus.NeedsAction) continue;
                if (_remindedKeys.Add(ReminderKey(m)))
                    due.Add((row.BlNo, m.Label));
            }
        return due;
    }

    /// <summary>提醒去重键：同一节点、同一次时间只算一次；时间被改过后视为新的一次。</summary>
    private static string ReminderKey(Milestone m)
        => $"{m.Id}|{MilestoneDisplay.SortKey(m).Ticks}";

    // ================= 状态筛选 =================

    /// <summary>构建筛选面板：第一项「全部」，其后是五个官方状态；默认全选（= 不过滤）。</summary>
    private void BuildStatusFilterOptions()
    {
        AddFilterOption(null, "全部");
        foreach (var status in StatusPresenter.AllStatuses)
            AddFilterOption(status, StatusPresenter.Text(status));

        _syncingFilters = true;
        foreach (var option in StatusFilterOptions)
            option.IsChecked = true;
        _syncingFilters = false;

        RefreshStatusFilter();
    }

    private void AddFilterOption(MilestoneStatus? status, string text)
    {
        var option = new StatusFilterOption { Status = status, Text = text };
        option.PropertyChanged += OnFilterOptionChanged;
        StatusFilterOptions.Add(option);
    }

    /// <summary>勾选变化：维护「全部 ↔ 具体项」联动，然后刷新列表。</summary>
    private void OnFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatusFilterOption.IsChecked) || _syncingFilters) return;
        if (sender is not StatusFilterOption changed) return;

        _syncingFilters = true;
        if (changed.Status is null)
        {
            // 点了「全部」：把五个具体项同步成它的勾选状态
            foreach (var option in StatusFilterOptions.Where(o => o.Status is not null))
                option.IsChecked = changed.IsChecked;
        }
        else
        {
            // 点了某个具体状态：五个具体项都勾上时，反推「全部」也亮
            var allChecked = StatusFilterOptions
                .Where(o => o.Status is not null)
                .All(o => o.IsChecked);
            StatusFilterOptions.First(o => o.Status is null).IsChecked = allChecked;
        }
        _syncingFilters = false;

        RefreshStatusFilter();
    }

    /// <summary>把勾选项收进集合，并让列表重新过滤。</summary>
    private void RefreshStatusFilter()
    {
        _checkedStatuses.Clear();
        foreach (var option in StatusFilterOptions)
            if (option.Status is { } status && option.IsChecked)
                _checkedStatuses.Add(status);

        _shipmentsView?.Refresh();
    }

    /// <summary>过滤谓词：状态筛选 + 搜索词（提单号 / 客户），两者叠加（AND）。
    /// 数据早已全在内存，这里是纯内存字符串匹配，不碰数据库，边打边过滤也不会卡。</summary>
    private bool FilterShipment(object obj)
    {
        if (obj is not ShipmentRowViewModel row) return false;

        // 状态筛选：五个状态全勾 = 不过滤；否则只留状态命中的行（无节点的行仅在不过滤时出现）。
        if (_checkedStatuses.Count != StatusPresenter.AllStatuses.Length)
        {
            if (row.Status is not { } status || !_checkedStatuses.Contains(status)) return false;
        }

        // 搜索：提单号 / 客户，不区分大小写。搜索词为空则不限制。
        var query = SearchText.Trim();
        if (query.Length > 0)
        {
            var hit = row.BlNo.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || row.Customer.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }

        return true;
    }

    /// <summary>搜索词一变就重新过滤（实时搜索）。</summary>
    partial void OnSearchTextChanged(string value) => _shipmentsView?.Refresh();

    // ================= 节点搜索（右侧详情列表） =================

    /// <summary>选中提单变化：把节点列表视图切到该提单的节点集合，并清空节点搜索词
    /// （否则换到另一张提单会看到「莫名被过滤空」的列表）。</summary>
    partial void OnSelectedShipmentChanged(ShipmentRowViewModel? value)
    {
        if (value is null)
        {
            MilestoneView = null;
            return;
        }

        var view = CollectionViewSource.GetDefaultView(value.Milestones);
        view.Filter = FilterMilestone;
        MilestoneView = view;
        MilestoneSearchText = "";
    }

    /// <summary>节点搜索词一变就重新过滤右侧节点列表（实时搜索）。</summary>
    partial void OnMilestoneSearchTextChanged(string value) => MilestoneView?.Refresh();

    /// <summary>节点过滤谓词：按节点名不区分大小写匹配；搜索词为空则不限制。</summary>
    private bool FilterMilestone(object obj)
    {
        if (obj is not MilestoneRowViewModel row) return false;
        var query = MilestoneSearchText.Trim();
        return query.Length == 0 || row.Label.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
