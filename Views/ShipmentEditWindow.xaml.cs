// ShipmentEditWindow.xaml.cs —— 新建 / 修改提单对话框
// 用法：new ShipmentEditWindow()（新建）或 new ShipmentEditWindow(existing)（修改）；
//       ShowDialog() == true 时从 Result 取结果。
namespace ForwarderAssistant.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ForwarderAssistant.Models;
using ForwarderAssistant.Services;

public partial class ShipmentEditWindow : Window
{
    private readonly ShipmentService _service = new();

    // 修改模式下待改的提单；新建模式为 null
    private readonly Shipment? _editing;

    // 新建时的初始默认日期：若字段仍等于它，说明用户没动过，可被航次联动软回填覆盖
    private DateTime? _defaultEtd;
    private DateTime? _defaultEta;

    // 防止软回填自己触发的文本变化再次进入联动，形成递归
    private bool _autofilling;

    /// <summary>对话框产出的提单（点「保存」后填充）。</summary>
    public Shipment Result { get; } = new();

    /// <summary>新建提单。</summary>
    public ShipmentEditWindow() : this(null) { }

    /// <summary>新建（existing = null）或修改（existing 为待改提单）一张提单。</summary>
    public ShipmentEditWindow(Shipment? existing)
    {
        InitializeComponent();
        _editing = existing;

        // 船名候选：库里用过的历史船名，边打边筛，也可直接手打新的
        VesselBox.OriginalItemsSource = _service.GetVesselNames();

        if (existing is null)
        {
            // 默认日期：ETD 今天、ETA 今天 + 30 天（记下默认值，供判断用户是否动过）
            _defaultEtd = DateTime.Today;
            _defaultEta = DateTime.Today.AddDays(30);
            EtdPicker.SelectedDate = _defaultEtd;
            EtaPicker.SelectedDate = _defaultEta;
        }
        else
        {
            // 修改模式：标题改名，各字段回填现值
            Title = "修改提单";
            TitleText.Text = "修改提单";
            BlNoBox.Text = existing.BlNo;
            CustomerBox.Text = existing.Customer;
            VesselBox.Text = existing.Voyage.VesselName;
            VoyageBox.Text = existing.Voyage.VoyageNo;
            PolBox.Text = existing.Pol;
            PodBox.Text = existing.Pod;
            DestBox.Text = existing.FinalDestination;
            EtdPicker.SelectedDate = existing.EtdUtc?.ToLocalTime().Date;
            EtaPicker.SelectedDate = existing.EtaUtc?.ToLocalTime().Date;
        }

        // 航次联动：船名 / 航次 / 港口一变，就去库里找同名同航次的历史船期软回填。
        // 放在回填初值之后再挂，避免构造期间被自己触发。
        VesselBox.TextChanged += (_, _) => TryAutofillVoyage();
        VoyageBox.TextChanged += (_, _) => TryAutofillVoyage();
        PolBox.TextChanged += (_, _) => TryAutofillVoyage();
        PodBox.TextChanged += (_, _) => TryAutofillVoyage();

        Loaded += (_, _) => BlNoBox.Focus();
    }

    /// <summary>航次联动（软回填）：船名 + 航次都填好后，找同名同航次的历史船期，
    /// 只把「用户还没填」的字段（起运港 / 卸货港 / ETD / ETA）补上。
    /// 绝不覆盖用户已填内容、也不锁字段——同船名航次若另有转船等情况，用户照样能改。</summary>
    private void TryAutofillVoyage()
    {
        if (_autofilling) return;

        var vessel = VesselBox.Text.Trim();
        var voyageNo = VoyageBox.Text.Trim();
        if (vessel.Length == 0 || voyageNo.Length == 0) return;

        var found = _service.FindVoyage(vessel, voyageNo, PolBox.Text.Trim(), PodBox.Text.Trim());
        if (found is null) return;
        var (foundPol, foundPod, foundEtd, foundEta) = found.Value;

        _autofilling = true;
        try
        {
            if (PolBox.Text.Trim().Length == 0 && foundPol.Length > 0)
                PolBox.Text = foundPol;
            if (PodBox.Text.Trim().Length == 0 && foundPod.Length > 0)
                PodBox.Text = foundPod;

            // 日期：为空，或仍是新建时的初始默认值（= 用户没动过）时才补
            if ((EtdPicker.SelectedDate is null || EtdPicker.SelectedDate == _defaultEtd) && foundEtd is { } etd)
                EtdPicker.SelectedDate = etd.ToLocalTime().Date;
            if ((EtaPicker.SelectedDate is null || EtaPicker.SelectedDate == _defaultEta) && foundEta is { } eta)
                EtaPicker.SelectedDate = eta.ToLocalTime().Date;
        }
        finally { _autofilling = false; }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var blNo = BlNoBox.Text.Trim();
        if (string.IsNullOrEmpty(blNo)) { ShowWarn("请填写提单号。"); return; }

        var customer = CustomerBox.Text.Trim();
        if (string.IsNullOrEmpty(customer)) { ShowWarn("请填写客户名称。"); return; }

        var vessel = VesselBox.Text.Trim();
        if (string.IsNullOrEmpty(vessel)) { ShowWarn("请填写船名。"); return; }

        var voyageNo = VoyageBox.Text.Trim();
        if (string.IsNullOrEmpty(voyageNo)) { ShowWarn("请填写航次。"); return; }

        Result.Id = _editing?.Id ?? 0;
        Result.BlNo = blNo;
        Result.Customer = customer;
        Result.Voyage = new Voyage { VesselName = vessel, VoyageNo = voyageNo };
        Result.Pol = PolBox.Text.Trim();
        Result.Pod = PodBox.Text.Trim();
        // 最终目的地（目的港）没填时，默认用卸货港；手动改过就以手动的为准
        var dest = DestBox.Text.Trim();
        Result.FinalDestination = dest.Length > 0 ? dest : Result.Pod;
        Result.EtdUtc = ToUtc(EtdPicker.SelectedDate);
        Result.EtaUtc = ToUtc(EtaPicker.SelectedDate);

        DialogResult = true;
    }

    // 界面选的日期（本地当天）→ 入库 UTC（本地 00:00 转 UTC）
    private static DateTime? ToUtc(DateTime? picked)
        => picked is null
            ? null
            : DateTime.SpecifyKind(picked.Value.Date, DateTimeKind.Local).ToUniversalTime();

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    // 点日期框任意位置即展开日历；点在右侧日历按钮上时交给控件自己开合，避免切换两次。
    private void OnDatePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithinButton(source)) return;
        if (sender is DatePicker picker) picker.IsDropDownOpen = true;
    }

    private static bool IsWithinButton(DependencyObject node)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ButtonBase) return true;
        return false;
    }

    private void ShowWarn(string message)
        => System.Windows.MessageBox.Show(this, message, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
}
