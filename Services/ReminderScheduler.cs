// ReminderScheduler.cs —— 30 秒扫描心跳（调度扫描间隔见 项目计划 第 3 节）
namespace ForwarderAssistant.Services;

using System.Windows.Threading;

/// <summary>
/// 固定间隔的扫描心跳：只负责「到点了、该扫一遍」，扫什么、怎么提醒由订阅方决定。
/// 用 DispatcherTimer 是因为它随界面线程走，回调里可直接改列表、弹窗口，无需跨线程调度。
/// </summary>
public sealed class ReminderScheduler
{
    private readonly DispatcherTimer _timer;

    /// <summary>每 30 秒触发一次，参数为当前 UTC 时间。</summary>
    public event Action<DateTime>? Elapsed;

    public ReminderScheduler(TimeSpan? interval = null)
    {
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Elapsed?.Invoke(DateTime.UtcNow);
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
