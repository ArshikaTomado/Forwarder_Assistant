// ImportPreviewWindow.xaml.cs —— Excel 导入预览：逐条勾选后落库（有重复项时才弹）
// 用法：MainWindow 在 PreviewExcel 出重复项后 ShowDialog()；点「导入」由本窗落库，取消则不动库。
namespace ForwarderAssistant.Views;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ForwarderAssistant.Models;
using ForwarderAssistant.Services;

public partial class ImportPreviewWindow : Window
{
    private readonly ShipmentService _service = new();
    private readonly ObservableCollection<ImportPreviewRow> _rows = new();

    /// <summary>实际落库成功的条数（取消 / 未导入则为 0）。</summary>
    public int ImportedCount { get; private set; }

    public ImportPreviewWindow(ImportPreview preview)
    {
        InitializeComponent();

        foreach (var item in preview.Items)
        {
            var row = new ImportPreviewRow(item);
            row.PropertyChanged += OnRowPropertyChanged;
            _rows.Add(row);
        }
        PreviewGrid.ItemsSource = _rows;

        var added = preview.Items.Count - preview.DuplicateCount;
        SummaryText.Text = $"共解析 {preview.Items.Count} 张提单：新增 {added} 张（默认勾选）、"
                           + $"疑似重复 {preview.DuplicateCount} 张（默认不勾）。勾选的会作为全新提单插入。";

        // 表头冲突提示 + 跳过行，有则一并显示在顶部
        var notes = new List<string>(preview.Warnings);
        if (preview.Skipped.Count > 0)
            notes.Add($"另有 {preview.Skipped.Count} 行被跳过（提单号或船名 / 航次为空）。");
        NoteText.Text = string.Join("\n", notes);
        NoteText.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateCount();
    }

    // 勾选状态一变就刷新按钮文案
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportPreviewRow.IsSelected))
            UpdateCount();
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = true;
    }

    private void OnSelectNoneClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
    }

    private void UpdateCount()
    {
        var chosen = _rows.Count(r => r.IsSelected);
        ImportButton.Content = $"导入勾选的 {chosen} 张";
        ImportButton.IsEnabled = chosen > 0;
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var chosen = _rows.Where(r => r.IsSelected).Select(r => r.Shipment).ToList();
        if (chosen.Count == 0) return;

        ImportedCount = _service.ImportShipments(chosen);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>预览里的一行：提单草稿 + 是否勾选（可观察，供全选 / 全不勾即时反映到界面）。</summary>
public partial class ImportPreviewRow : ObservableObject
{
    public Shipment Shipment { get; }
    public bool IsDuplicate { get; }

    public string BlNo => Shipment.BlNo;
    public string Customer => Shipment.Customer;
    public string VesselName => Shipment.Voyage.VesselName;
    public string VoyageNo => Shipment.Voyage.VoyageNo;
    public string Pol => Shipment.Pol;
    public string Pod => Shipment.Pod;
    public string StatusText => IsDuplicate ? "疑似重复" : "新增";

    [ObservableProperty]
    private bool _isSelected;

    public ImportPreviewRow(ImportPreviewItem item)
    {
        Shipment = item.Shipment;
        IsDuplicate = item.IsDuplicate;
        _isSelected = !item.IsDuplicate;   // 默认：新增勾、重复不勾
    }
}
