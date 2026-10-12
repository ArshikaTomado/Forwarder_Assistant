// ToastWindow.xaml.cs —— 自绘右下角提醒弹窗（不依赖系统通知设置）。
// 用法：ToastWindow.Show("标题", "正文") —— 同屏可堆叠多条，从下往上排。
namespace ForwarderAssistant.Views;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

public partial class ToastWindow : Window
{
    // 同屏可能有多条提醒，按加入顺序从下往上堆叠；关闭任意一条后重排其余。
    private static readonly List<ToastWindow> OpenToasts = new();

    // 应用主蓝色（品牌色），作为默认强调色。
    private static readonly Brush DefaultAccent = Frozen(Color.FromRgb(0x0F, 0x6C, 0xBD));

    private readonly DispatcherTimer _lifeTimer;
    private bool _closing;

    private ToastWindow(string title, string message, Brush accent, int seconds)
    {
        InitializeComponent();

        TitleText.Text = title;
        BodyText.Text = message;
        AccentBar.Background = accent;

        // 先透明，等布局完成再淡入，避免出现「先闪一下再归位」。
        Opacity = 0;
        Loaded += (_, _) => FadeIn();

        _lifeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _lifeTimer.Tick += (_, _) => CloseWithFade();

        Closed += OnClosed;
    }

    /// <summary>
    /// 弹出右下角提醒。accent 为空时用应用主蓝色；seconds 为自动关闭秒数（鼠标悬停会暂停计时）。
    /// </summary>
    public static void Show(string title, string message, Brush? accent = null, int seconds = 6)
    {
        var toast = new ToastWindow(title, message, accent ?? DefaultAccent, seconds);

        OpenToasts.Add(toast);

        // Show 之前先给个大致位置，防止系统把它摆到左上角再跳过来；
        // 真正的堆叠位置在布局完成后由 RepositionAll 精确计算。
        var wa = SystemParameters.WorkArea;
        toast.Left = wa.Right - toast.Width;
        toast.Top = wa.Bottom - 120;

        toast.Show();
        RepositionAll();
        toast._lifeTimer.Start();
    }

    // 把所有弹窗贴着屏幕右下角、从下往上依次堆叠（最新的一条在最下面）。
    private static void RepositionAll()
    {
        var wa = SystemParameters.WorkArea;
        double bottom = wa.Bottom;

        foreach (var toast in OpenToasts)
        {
            toast.UpdateLayout();
            bottom -= toast.ActualHeight;
            toast.Top = bottom;
            toast.Left = wa.Right - toast.Width;
        }
    }

    private void FadeIn()
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void CloseWithFade()
    {
        if (_closing)
            return;
        _closing = true;
        _lifeTimer.Stop();

        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150));
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        _lifeTimer.Stop();
        OpenToasts.Remove(this);
        RepositionAll();
    }

    // 鼠标悬停暂停自动关闭，移开继续计时（读不完的提醒不会被秒关）。
    private void OnCardMouseEnter(object sender, MouseEventArgs e) => _lifeTimer.Stop();

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_closing)
            _lifeTimer.Start();
    }

    // 点击弹窗 → 唤起主窗口，并收起本条提醒。
    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        ActivateMainWindow();
        CloseWithFade();
    }

    private static void ActivateMainWindow()
    {
        if (Application.Current?.MainWindow is not Window main)
            return;

        // 主窗口可能被叉号 Hide 到托盘后台，先重新显示再唤回前台。
        main.Show();
        if (main.WindowState == WindowState.Minimized)
            main.WindowState = WindowState.Normal;

        main.Activate();
        // Topmost 抖一下，把已被其它窗口盖住的主窗口提到最前（比直接置顶更不打扰）。
        main.Topmost = true;
        main.Topmost = false;
        main.Focus();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
