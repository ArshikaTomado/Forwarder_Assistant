// OverdueWindow.xaml.cs —— 超时提醒详情窗：逐行「已处理 / 稍后处理」，全部处理完自动关窗。
namespace ForwarderAssistant.Views;

using System.Collections.ObjectModel;
using System.Windows;
using ForwarderAssistant.ViewModels;

public partial class OverdueWindow : Window
{
    // 「稍后处理」再次提醒的间隔：1 小时。
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(1);

    private readonly MainViewModel _vm;
    private readonly ObservableCollection<OverdueRow> _rows;

    public OverdueWindow(MainViewModel vm, IEnumerable<OverdueRow> rows)
    {
        InitializeComponent();
        _vm = vm;
        _rows = new ObservableCollection<OverdueRow>(rows);
        OverdueList.ItemsSource = _rows;
        UpdateSummary();
    }

    // 点「已处理」：落库标记为已处理 → 从本次列表移除。
    private void OnHandledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverdueRow row }) return;
        _vm.MarkMilestoneHandled(row.Shipment, row.Milestone);
        Remove(row);
    }

    // 点「稍后处理」：该节点 1 小时内不再进超时提醒 → 从本次列表移除。
    private void OnSnoozeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverdueRow row }) return;
        _vm.SnoozeOverdue(row.Milestone.Id, DateTime.UtcNow + SnoozeDuration);
        Remove(row);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // 移除一行；全处理完就自动关窗（没什么可看的了）。
    private void Remove(OverdueRow row)
    {
        _rows.Remove(row);
        UpdateSummary();
        if (_rows.Count == 0) Close();
    }

    private void UpdateSummary()
        => SummaryText.Text = $"共 {_rows.Count} 项超时未处理。「稍后处理」会在 1 小时后再次提醒。";
}
