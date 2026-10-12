// MilestoneEditWindow.xaml.cs —— 节点新增 / 编辑对话框
// 用法：new MilestoneEditWindow(blNo, existing)；ShowDialog() == true 时从 Result 取结果。
namespace ForwarderAssistant.Views;

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ForwarderAssistant.Models;

public partial class MilestoneEditWindow : Window
{
    // 编辑对象（改模式时保留 Kind / Id / AckAtUtc，只让用户改名与时间）
    private readonly Milestone _editing;

    /// <summary>对话框产出的节点（点「保存」后填充）。</summary>
    public Milestone Result { get; } = new();

    public MilestoneEditWindow(string blNo, Milestone? existing)
    {
        InitializeComponent();

        TitleText.Text = existing is null ? "加节点" : "编辑节点";
        BlNoText.Text = blNo;

        _editing = existing ?? new Milestone { Kind = "custom" };

        // 时间默认当前本地时间（切到「精确时刻」时直接可用）
        var now = DateTime.Now;
        HourBox.Value = now.Hour;
        MinuteBox.Value = now.Minute;

        // 回填：按已有时间的种类选中对应模式，避免精确/仅日期互相覆盖
        LabelBox.Text = _editing.Label;
        if (existing is null)
        {
            // 新建：默认「仅日期」，日期默认今天
            ModeBox.SelectedIndex = 1;
            DateInput.SelectedDate = DateTime.Today;
        }
        else if (_editing.DueAtUtc is not null)
        {
            ModeBox.SelectedIndex = 0;
            var local = _editing.DueAtUtc.Value.ToLocalTime();
            DateInput.SelectedDate = local.Date;
            HourBox.Value = local.Hour;
            MinuteBox.Value = local.Minute;
        }
        else if (_editing.DueDate is not null)
        {
            ModeBox.SelectedIndex = 1;
            DateInput.SelectedDate = _editing.DueDate.Value.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            ModeBox.SelectedIndex = 2;
        }

        UpdateInputsEnabled();
        ModeBox.SelectionChanged += (_, _) => UpdateInputsEnabled();
        Loaded += (_, _) => LabelBox.Focus();
    }

    // 时间输入随模式启用/禁用
    private void UpdateInputsEnabled()
    {
        var mode = ModeBox.SelectedIndex;
        DateInput.IsEnabled = mode != 2;
        HourBox.IsEnabled = mode == 0;
        MinuteBox.IsEnabled = mode == 0;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text.Trim();
        if (string.IsNullOrEmpty(label))
        {
            ShowWarn("请填写节点名。");
            return;
        }

        var mode = ModeBox.SelectedIndex;   // 0=精确 1=仅日期 2=不设时间
        DateTime? dueAtUtc = null;
        DateOnly? dueDate = null;

        if (mode != 2)
        {
            if (DateInput.SelectedDate is not { } picked)
            {
                ShowWarn("请选择日期。");
                return;
            }

            if (mode == 0)
            {
                if (HourBox.Value is not { } hour || MinuteBox.Value is not { } minute
                    || hour < 0 || hour > 23 || minute < 0 || minute > 59)
                {
                    ShowWarn("请设置时间（时 0–23、分 0–59）。");
                    return;
                }
                // 界面选的是本地时间，入库一律转 UTC
                var local = picked.Date.AddHours(hour).AddMinutes(minute);
                dueAtUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            }
            else
            {
                dueDate = DateOnly.FromDateTime(picked);
            }
        }

        Result.Id = _editing.Id;
        Result.Kind = _editing.Kind;
        Result.Label = label;
        Result.DueAtUtc = dueAtUtc;
        Result.DueDate = dueDate;
        Result.AckAtUtc = _editing.AckAtUtc;

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    // 点日期框任意位置就展开日历；点在右侧日历按钮上时交给控件自己开合，避免切换两次。
    private void OnDatePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithinButton(source))
            return;
        DateInput.IsDropDownOpen = true;
    }

    private static bool IsWithinButton(DependencyObject node)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ButtonBase)
                return true;
        return false;
    }

    private void ShowWarn(string message)
        => System.Windows.MessageBox.Show(this, message, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
}
