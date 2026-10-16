// MainWindow.xaml.cs —— 主窗口代码后置：装配视图模型 + 状态列头筛选弹层
namespace ForwarderAssistant.Views;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading;
using ForwarderAssistant.Models;
using ForwarderAssistant.Services;
using ForwarderAssistant.ViewModels;
using Wpf.Ui.Controls;

public partial class MainWindow : FluentWindow
{
    // 30 秒心跳：到点重算状态并弹提醒，窗口缩到托盘也照跑。
    private readonly ReminderScheduler _scheduler = new();

    // 「已复制提单号」状态栏提示的自动消退计时器（2 秒）。
    private readonly DispatcherTimer _copyHintTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    // 双击复制的自定义「上次点击」基准（行 + 时刻），见 OnCopyBlNoMouseDown。
    private ShipmentRowViewModel? _lastCopyClickRow;
    private DateTime _lastCopyClickUtc;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Popup 的内容不在主视觉树里，列头里的 DataContext 继承不到它，
        // 会导致筛选面板弹出来是空的；这里显式把它钉到主视图模型。
        StatusFilterPopup.DataContext = DataContext;

        // 托盘「开机自启」勾选状态跟随注册表（默认关）。
        AutoStartMenuItem.IsChecked = StartupRegistration.IsEnabled();

        // 30 秒心跳：重算状态 + 到点弹提醒；窗口最小化到托盘时依然运行。
        _scheduler.Elapsed += OnSchedulerTick;
        _scheduler.Start();

        // 点右上角叉号只隐藏到托盘后台，不退出；真正退出走托盘右键「退出」。
        Closing += OnClosing;

        // 复制提示 2 秒后自动隐去。
        _copyHintTimer.Tick += (_, _) =>
        {
            _copyHintTimer.Stop();
            CopyHintText.Visibility = Visibility.Collapsed;
        };
    }

    // 叉号关闭 = 隐藏到托盘后台（退出只能走托盘右键菜单），后台常驻提醒且不占用任务栏。
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsExiting)
            return;

        e.Cancel = true;
        Hide();     // 隐藏而非最小化：只留托盘图标，任务栏不再占位。
    }

    // 真正退出时收掉托盘图标，避免系统托盘里残留一个点不动的僵尸图标。
    protected override void OnClosed(System.EventArgs e)
    {
        _scheduler.Stop();
        TrayIcon?.Dispose();
        base.OnClosed(e);
    }

    // 托盘右键「显示主窗口」/ 双击托盘图标：把隐藏在后台的主窗口唤回前台。
    private void OnTrayShowClick(object sender, RoutedEventArgs e) => ShowMainWindow();

    // 托盘右键「退出」：放行关闭并退出进程。
    private void OnTrayExitClick(object sender, RoutedEventArgs e)
    {
        App.IsExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowMainWindow()
    {
        Show();     // 若此前被 Hide 到托盘，这里重新显示；已显示时为无操作。
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        // Topmost 抖一下，把被其它窗口盖住的主窗口提到最前。
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // 点「状态 ▾」列头：勾选 → 展开筛选面板
    private void OnStatusHeaderToggleChecked(object sender, RoutedEventArgs e)
        => StatusFilterPopup.IsOpen = true;

    // 取消勾选 → 收起面板
    private void OnStatusHeaderToggleUnchecked(object sender, RoutedEventArgs e)
        => StatusFilterPopup.IsOpen = false;

    // 面板因点别处而关闭时，把列头按钮也复位，否则下次点击要先取消勾选、点了没反应
    private void OnStatusFilterPopupClosed(object sender, System.EventArgs e)
        => StatusHeaderToggle.IsChecked = false;

    // 临时：点「测试提醒」弹一条自绘右下角弹窗，验证提醒效果（第 3 步正式接上调度后删除）。
    // 自绘弹窗不走系统通知，不受「系统通知总开关」影响，作为兜底提醒方案。
    private void OnTestToastClick(object sender, RoutedEventArgs e)
        => ToastWindow.Show("货代提单节点提醒器", "这是一条提醒测试：截关时间快到了，请及时处理。");

    // ================= 调度 / 提醒（第 3 步） =================

    // 超时聚合提醒的节流：同一批超时节点最多每 30 分钟提醒一次（心跳是 30 秒，不加节流会一直弹）。
    private static readonly TimeSpan OverdueToastInterval = TimeSpan.FromMinutes(30);
    private DateTime _nextOverdueToastUtc = DateTime.MinValue;

    // 超时提醒的强调色（与「超时」状态同色）。
    private static readonly Brush OverdueAccent = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    // 每 30 秒：重算列表状态，并对「刚进入需要处理」的节点弹一条自绘提醒；
    // 另把「超时未处理」的节点聚合成一条提醒，最多每 30 分钟弹一次。
    private void OnSchedulerTick(DateTime nowUtc)
    {
        Vm.RefreshAllStatuses(nowUtc);

        foreach (var (blNo, label) in Vm.CollectDueReminders(nowUtc))
            ToastWindow.Show($"节点提醒 · {label}", $"提单 {blNo} 的「{label}」到点了，请及时处理。");

        if (nowUtc >= _nextOverdueToastUtc)
        {
            var overdue = Vm.GetOverdueRows(nowUtc);
            if (overdue.Count > 0)
            {
                ShowOverdueToast(overdue);
                _nextOverdueToastUtc = nowUtc + OverdueToastInterval;
            }
        }
    }

    // 把全部超时节点聚合成一条气泡（点它开详情窗逐条「已处理 / 稍后处理」），避免一堆气泡刷屏。
    private void ShowOverdueToast(List<OverdueRow> rows)
    {
        var preview = string.Join("；", rows.Take(3).Select(r => $"提单 {r.BlNo}「{r.Label}」{r.OverdueText}"));
        var message = rows.Count > 3
            ? $"{preview} 等 {rows.Count} 项超时未处理，点此查看。"
            : $"{preview}，点此查看。";

        ToastWindow.Show($"超时提醒 · {rows.Count} 项", message,
            accent: OverdueAccent, seconds: 12,
            onCardClick: OpenOverdueWindow);
    }

    // 打开超时详情窗；打开时按当前时间重新取一遍（气泡显示到点击之间可能已有变化）。
    private void OpenOverdueWindow()
    {
        var rows = Vm.GetOverdueRows(DateTime.UtcNow);
        if (rows.Count == 0) return;

        var window = new OverdueWindow(Vm, rows);
        window.Show();
        window.Activate();
    }

    // 托盘「开机自启」勾选：勾上写注册表 Run，取消就删掉（默认关）。
    private void OnToggleAutoStartClick(object sender, RoutedEventArgs e)
    {
        if (AutoStartMenuItem.IsChecked)
            StartupRegistration.Enable();
        else
            StartupRegistration.Disable();
    }

    // ================= 提单新增 =================

    // 点「+ 新建」：弹新建提单表单，确认后落库并选中新提单。
    private void OnNewShipmentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ShipmentEditWindow { Owner = this };
        if (dialog.ShowDialog() == true)
            Vm.AddShipment(dialog.Result);
    }

    // 点「船名 · 航次」：弹航次管理窗格（预览 / 重命名 / 删除幽灵航次），关窗后刷新主列表。
    private void OnManageVoyagesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new VoyageManagerWindow { Owner = this };
        dialog.ShowDialog();
        Vm.Reload();
    }

    // 点「导入 Excel」：选客户给的 .xlsx，解析后比对库中已有提单号。
    // 无重复 → 按老流程直接全量导入、只报结果；有重复 → 弹预览窗逐条勾选后再落库。
    private void OnImportExcelClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要导入的 Excel",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog(this) != true) return;

        var service = new ShipmentService();
        try
        {
            var preview = service.PreviewExcel(dialog.FileName);

            // 无重复：直接全量导入，不弹预览框（不给常见情况加摩擦）。
            if (preview.DuplicateCount == 0)
            {
                var imported = service.ImportShipments(preview.Items.Select(i => i.Shipment));
                Vm.Reload();
                ShowImportReport($"导入完成：新增 {imported} 张提单。", preview.Warnings, preview.Skipped);
                return;
            }

            // 有重复：弹预览，勾选后由预览窗落库。
            var window = new ImportPreviewWindow(preview) { Owner = this };
            window.ShowDialog();
            Vm.Reload();
            if (window.ImportedCount > 0)
                ShowImportReport($"导入完成：新增 {window.ImportedCount} 张提单。", null, null);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "导入失败：\n" + ex.Message, "导入 Excel",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    // 导入结果提示：正文 + 可选的表头提示 / 跳过行（有才附）。
    private void ShowImportReport(string headline, List<string>? warnings, List<string>? skipped)
    {
        var message = headline;
        if (warnings is { Count: > 0 })
            message += "\n\n提示：\n" + string.Join("\n", warnings);
        if (skipped is { Count: > 0 })
        {
            message += $"\n\n跳过 {skipped.Count} 行：\n" + string.Join("\n", skipped.Take(15));
            if (skipped.Count > 15) message += $"\n… 其余 {skipped.Count - 15} 行同理。";
        }
        System.Windows.MessageBox.Show(this, message, "导入 Excel",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // 点详情面板的「修改提单」：弹编辑表单（回填现值），确认后落库并刷新。
    private void OnEditShipmentClick(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedShipment is not { } shipment) return;

        var dialog = new ShipmentEditWindow(shipment.Model) { Owner = this };
        if (dialog.ShowDialog() == true)
            Vm.UpdateShipment(shipment, dialog.Result);
    }

    // 点详情面板的「删除提单」：二次确认后把选中的提单（支持多选）连同其全部节点一起删除（不可恢复）。
    private void OnDeleteShipmentClick(object sender, RoutedEventArgs e)
    {
        var selected = ShipmentGrid.SelectedItems.OfType<ShipmentRowViewModel>().ToList();
        if (selected.Count == 0) return;

        var milestones = selected.Sum(r => r.Model.Milestones.Count);
        var message = selected.Count == 1
            ? $"确定删除提单「{selected[0].BlNo}」吗？\n它的 {milestones} 个节点也会一起删除，且不可恢复。"
            : $"确定删除选中的 {selected.Count} 张提单吗？\n它们的 {milestones} 个节点也会一起删除，且不可恢复。";

        var confirm = System.Windows.MessageBox.Show(
            this, message, "删除提单",
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (confirm == System.Windows.MessageBoxResult.OK)
            Vm.DeleteShipments(selected);
    }

    // ================= 复制提单号 =================

    // 专职剪贴板线程：单个、带消息泵的 STA 线程（懒建）。
    // 为什么不能「每次双击就新起一个 STA 线程」：OLE 剪贴板是全局独占资源，狂点复制会起一堆
    // 无消息泵的线程去抢它，谁也拿不到 → 线程越堆越多、整个程序卡死（表现为窗口点不动、
    // 进程关不掉、exe 被占用）。这里只留一个带消息泵（OLE 要求）的线程，请求排上去串行执行。
    private Dispatcher? _clipboardDispatcher;

    // 复制请求「合并到最新」：狂点时只保留最后一次要复制的提单号，串行处理，
    // 既不会堆一堆任务抢剪贴板，也不会把用户的最后一次点击丢掉（旧版是直接丢弃，见下）。
    private readonly object _copyGate = new();
    private string? _pendingCopyText;   // 待复制的最新提单号（null = 无）
    private bool _copyWorkerRunning;    // 处理循环是否已在跑
    private IntPtr _clipboardOwnerHwnd; // 剪贴板「属主窗口」句柄（见 SetClipboard 注释）

    private Dispatcher ClipboardDispatcher
    {
        get
        {
            if (_clipboardDispatcher is { } existing) return existing;

            var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                _clipboardDispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();   // 消息泵，直到进程结束（后台线程，不挡退出）
            })
            { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return _clipboardDispatcher!;
        }
    }

    // 双击提单号（列表列 或 详情抬头）→ 复制到剪贴板，状态栏右侧轻提示 2 秒。
    // 两处的 DataContext 都是该行的 ShipmentRowViewModel，共用一个处理器即可。
    private void OnCopyBlNoMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShipmentRowViewModel row }) return;
        if (row.BlNo.Length == 0) return;

        // 双击判定放宽，两者命中其一即可：
        // ① 系统认出的双击（e.ClickCount == 2）；
        // ② 自己数——同一行、间隔 < 800ms 也算（不要求两次点击落在 4×4 像素小方框里，手抖 / 稍慢也认）。
        var now = DateTime.UtcNow;
        var isDoubleClick = e.ClickCount == 2
                            || (ReferenceEquals(_lastCopyClickRow, row)
                                && now - _lastCopyClickUtc < TimeSpan.FromMilliseconds(800));

        _lastCopyClickRow = row;
        _lastCopyClickUtc = now;

        if (!isDoubleClick) return;

        // 命中后清掉基准，避免继续连点被反复当成新的双击。
        _lastCopyClickRow = null;
        _lastCopyClickUtc = default;

        QueueCopy(row.BlNo);
    }

    // 把「复制某提单号」登记进队列（只在 UI 线程调用）。
    // 合并到最新：狂点时永远只留最后一次请求——既不会堆一堆任务抢剪贴板，
    // 也不会像旧版那样把用户的最后一次点击直接丢掉（丢请求 = 「有时不灵」的来源之一）。
    private void QueueCopy(string blNo)
    {
        // 剪贴板「属主窗口」必须在 UI 线程上取（窗口句柄归 UI 线程）。
        // 绝不能拿 IntPtr.Zero 当属主：那样 EmptyClipboard 会把属主置空，SetClipboardData 直接失败。
        if (_clipboardOwnerHwnd == IntPtr.Zero)
            _clipboardOwnerHwnd = new WindowInteropHelper(this).EnsureHandle();

        lock (_copyGate)
        {
            _pendingCopyText = blNo;
            if (_copyWorkerRunning) return;   // 已有循环在跑，它稍后会取走这个最新值
            _copyWorkerRunning = true;
        }
        ClipboardDispatcher.BeginInvoke(ProcessCopyQueue);   // 在专职 STA 线程上跑
    }

    // 专职 STA 线程上的串行处理循环：取走「最新待复制」→ 写剪贴板 → 回 UI 提示。
    private void ProcessCopyQueue()
    {
        while (true)
        {
            string? text;
            lock (_copyGate)
            {
                text = _pendingCopyText;
                _pendingCopyText = null;
                if (text is null) { _copyWorkerRunning = false; return; }
            }

            var ok = SetClipboard(text, _clipboardOwnerHwnd);
            // 只回 UI 改提示——绝不在 UI 线程上 sleep（那是上一个版本把界面冻死的元凶）。
            Dispatcher.BeginInvoke(() => ShowCopyHint(ok, text));
        }
    }

    // 状态栏轻提示：成功 / 失败都显示，失败能一眼看出。（只在 UI 线程调用）
    private void ShowCopyHint(bool ok, string blNo)
    {
        CopyHintText.Text = ok ? $"已复制提单号：{blNo}" : "复制失败（剪贴板被占用），请再试一次";
        CopyHintText.Foreground = ok
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x6C, 0xBD))
            : System.Windows.Media.Brushes.IndianRed;
        CopyHintText.Visibility = Visibility.Visible;
        _copyHintTimer.Stop();
        _copyHintTimer.Start();
    }

    // 写剪贴板。剪贴板是 Windows 的「全局独占」资源：任何时刻只允许一个程序打开，
    // 输入法（本机是搜狗）、剪贴板工具、ERP 等都会来抢，抢不到 OpenClipboard 就返回失败。
    // 这是系统本质，不是实现缺陷——所以这里「重试」而不是「一次失败就放弃」。
    // 关键：走 Win32 原生 API 直接写，而不是 WPF 的 Clipboard.SetText。
    //   ① SetText 走 OLE「延迟渲染」：数据先不落地，等别人粘贴时系统再回头找设置方线程要；
    //      那个回调没接住，对方就粘出「空 / 上一次的旧值」（用户实测现象）。
    //   ② 原生 OpenClipboard → EmptyClipboard → SetClipboardData(CF_UNICODETEXT) 是
    //      「立即落地」的，SetClipboardData 返回非空就代表真写进去了，无需回读、无延迟回调。
    // 注意：OpenClipboard 的属主必须传一个「真实的窗口句柄」（ownerHwnd），不能传 IntPtr.Zero——
    //   否则 EmptyClipboard 会把剪贴板属主置为 NULL，SetClipboardData 必定失败（微软文档明确写了）。
    // 只在一个专职 STA 线程上调用（串行），不与其他复制请求打架。
    private static bool SetClipboard(string text, IntPtr ownerHwnd)
    {
        // 重试窗口约 1.2 秒（20 × 60ms）：足够挺过输入法 / 剪贴板工具的短暂独占。
        const int maxAttempts = 20;
        const int delayMs = 60;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (TryWriteClipboard(text, ownerHwnd)) return true;
            Thread.Sleep(delayMs);
        }
        return false;
    }

    // 单次尝试：失败返回 false（此时剪贴板正被别的程序占用）。
    private static bool TryWriteClipboard(string text, IntPtr ownerHwnd)
    {
        if (!OpenClipboard(ownerHwnd)) return false;   // 被占用，交给上层重试

        IntPtr hMem = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard()) return false;

            var bytes = (text.Length + 1) * sizeof(char);        // +1：结尾 NUL
            hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hMem == IntPtr.Zero) return false;

            var target = GlobalLock(hMem);
            if (target == IntPtr.Zero) return false;
            try
            {
                // GlobalAlloc 出来的内存已清零，结尾 NUL 自动为 0。
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            }
            finally { GlobalUnlock(hMem); }

            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero) return false;

            hMem = IntPtr.Zero;   // 成功：内存所有权已交给系统，不能再 free
            return true;
        }
        finally
        {
            if (hMem != IntPtr.Zero) GlobalFree(hMem);   // 失败路径：回收自己申请的内存
            CloseClipboard();
        }
    }

    // —— Win32 剪贴板 API ——
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    // ================= 节点增 / 改 / 删 =================

    private MainViewModel Vm => (MainViewModel)DataContext;

    // 点「＋ 加节点」：弹编辑框，确认后落库并刷新。
    private void OnAddMilestoneClick(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedShipment is not { } shipment) return;

        var dialog = new MilestoneEditWindow(shipment.BlNo, null) { Owner = this };
        if (dialog.ShowDialog() == true)
            Vm.AddMilestone(shipment, dialog.Result);
    }

    // 点节点行里的圆圈对勾：切换「已处理」（可反复点，不是一次性的）。
    private void OnToggleMilestoneHandledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MilestoneRowViewModel row }) return;
        if (Vm.SelectedShipment is not { } shipment) return;

        Vm.ToggleMilestoneHandled(shipment, row);
    }

    // 点节点行里的「🗑」：二次确认后删除。
    private void OnDeleteMilestoneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MilestoneRowViewModel row }) return;
        if (Vm.SelectedShipment is not { } shipment) return;

        var confirm = System.Windows.MessageBox.Show(
            this, $"确定删除节点「{row.Label}」吗？", "删除节点",
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (confirm == System.Windows.MessageBoxResult.OK)
            Vm.DeleteMilestone(shipment, row);
    }

    // 双击节点行：弹编辑框改节点（点在删除按钮上时不触发）。
    private void OnMilestoneMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (e.OriginalSource is DependencyObject origin && IsWithinButton(origin)) return;
        if (sender is not FrameworkElement { DataContext: MilestoneRowViewModel row }) return;
        if (Vm.SelectedShipment is not { } shipment) return;

        var dialog = new MilestoneEditWindow(shipment.BlNo, row.Model) { Owner = this };
        if (dialog.ShowDialog() == true)
            Vm.UpdateMilestone(shipment, row, dialog.Result);
    }

    // 判断某个可视元素是否位于按钮内部（用于避免双击删除按钮时又打开编辑框）。
    private static bool IsWithinButton(DependencyObject element)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Button) return true;
        return false;
    }
}
