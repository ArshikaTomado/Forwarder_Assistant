// VoyageManagerWindow.xaml.cs —— 船名 · 航次管理：预览 / 重命名 / 删除幽灵航次
// 用法：ShowDialog() 关闭后，由调用方刷新主列表（重命名会改动提单显示的船名 / 航次）。
namespace ForwarderAssistant.Views;

using System.Windows;
using System.Windows.Controls;
using ForwarderAssistant.Models;
using ForwarderAssistant.Services;

public partial class VoyageManagerWindow : Window
{
    private readonly ShipmentService _service = new();

    public VoyageManagerWindow()
    {
        InitializeComponent();
        Reload(selectId: null);
    }

    private VoyageSummary? Selected => VoyageGrid.SelectedItem as VoyageSummary;

    /// <summary>重新拉取全部航次总览；selectId 指定要复原选中的航次，null 则选第一行。</summary>
    private void Reload(long? selectId)
    {
        var summaries = _service.GetVoyageSummaries();
        VoyageGrid.ItemsSource = summaries;

        var target = selectId is { } id
            ? summaries.FirstOrDefault(s => s.Id == id)
            : summaries.FirstOrDefault();
        VoyageGrid.SelectedItem = target;
        UpdateDetail(target);
    }

    private void OnVoyageSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDetail(Selected);

    /// <summary>把选中航次的明细（挂靠港 / 在用提单）和可编辑字段填到右侧与底栏。</summary>
    private void UpdateDetail(VoyageSummary? s)
    {
        if (s is null)
        {
            DetailHeader.Text = "（未选中）";
            PortsList.ItemsSource = null;
            ShipmentsList.ItemsSource = null;
            VesselBox.Text = "";
            VoyageBox.Text = "";
            SetEditEnabled(false);
            return;
        }

        DetailHeader.Text = $"{s.VesselName} / {s.VoyageNo}";
        PortsList.ItemsSource = s.Ports.Count > 0
            ? s.Ports.Select(FormatPort).ToList()
            : new List<string> { "（无）" };
        ShipmentsList.ItemsSource = s.ShipmentBlNos.Count > 0
            ? s.ShipmentBlNos
            : new List<string> { "（无）" };
        VesselBox.Text = s.VesselName;
        VoyageBox.Text = s.VoyageNo;
        SetEditEnabled(true);
    }

    private void SetEditEnabled(bool on)
    {
        VesselBox.IsEnabled = on;
        VoyageBox.IsEnabled = on;
        SaveButton.IsEnabled = on;
        DeleteButton.IsEnabled = on;
    }

    /// <summary>挂靠港一行文本：「港口码  港口名  开 yyyy-MM-dd  到 yyyy-MM-dd」（本地日期）。</summary>
    private static string FormatPort(VoyagePort p)
    {
        var parts = new List<string> { p.PortCode };
        if (p.PortName.Length > 0) parts.Add(p.PortName);
        if (p.DepartureUtc is { } dep) parts.Add("开 " + dep.ToLocalTime().ToString("yyyy-MM-dd"));
        if (p.ArrivalUtc is { } arr) parts.Add("到 " + arr.ToLocalTime().ToString("yyyy-MM-dd"));
        return string.Join("  ", parts);
    }

    // 点「保存修改」：把船名 / 航次改名；重名则拦下。
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;

        var vessel = VesselBox.Text.Trim();
        var voyageNo = VoyageBox.Text.Trim();
        if (vessel.Length == 0) { ShowWarn("请填写船名。"); return; }
        if (voyageNo.Length == 0) { ShowWarn("请填写航次。"); return; }
        if (vessel == s.VesselName && voyageNo == s.VoyageNo) return;   // 没改

        if (!_service.RenameVoyage(s.Id, vessel, voyageNo))
        {
            ShowWarn($"已存在「{vessel} / {voyageNo}」这个航次，不能重名。");
            return;
        }

        Reload(s.Id);
    }

    // 点「删除航次」：二次确认后批量删除（支持多选）；还有提单在用的会被跳过并报告。
    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = VoyageGrid.SelectedItems.Cast<VoyageSummary>().ToList();
        if (selected.Count == 0) return;

        var inUse = selected.Count(s => s.ShipmentCount > 0);
        string message;
        if (selected.Count == 1)
        {
            var s = selected[0];
            var note = s.ShipmentCount > 0
                ? $"\n\n注意：该航次还有 {s.ShipmentCount} 张在用提单，删除会被拦下。"
                : "";
            message = $"确定删除航次「{s.VesselName} / {s.VoyageNo}」吗？\n它的 {s.PortCount} 条挂靠港记录也会一起删除，且不可恢复。{note}";
        }
        else
        {
            var note = inUse > 0 ? $"\n\n其中 {inUse} 个还有提单在用，会被自动跳过。" : "";
            message = $"确定删除选中的 {selected.Count} 个航次吗？\n它们的挂靠港记录也会一起删除，且不可恢复。{note}";
        }

        var confirm = System.Windows.MessageBox.Show(
            this, message, "删除航次",
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var deleted = 0;
        var blocked = new List<string>();
        foreach (var s in selected)
        {
            if (_service.DeleteVoyage(s.Id) == 0) deleted++;
            else blocked.Add($"{s.VesselName} / {s.VoyageNo}");
        }

        if (blocked.Count > 0)
            ShowWarn($"已删除 {deleted} 个航次。\n以下 {blocked.Count} 个还有提单在用，已跳过：\n"
                     + string.Join("\n", blocked));

        Reload(selectId: null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowWarn(string message)
        => System.Windows.MessageBox.Show(this, message, "无法操作", MessageBoxButton.OK, MessageBoxImage.Warning);
}
