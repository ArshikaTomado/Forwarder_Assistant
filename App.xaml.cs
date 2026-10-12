// App.xaml.cs —— 应用程序入口
using System.Threading;
using System.Windows;

namespace ForwarderAssistant;

public partial class App : Application
{
    // 单实例锁：进程存活期间一直持有；进程退出后系统自动释放。
    private Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"), e.ExceptionObject.ToString());
    }

    // 单实例：已有实例在跑（多半缩在托盘里），第二个直接退出——
    // 否则会出现两个托盘图标、同一条提醒弹两遍、两个进程抢同一个数据库文件。
    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "ForwarderAssistant.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "程序已在运行（见系统托盘）。", "货代提单节点提醒器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;   // 不调 base，StartupUri 的主窗口就不创建
        }

        base.OnStartup(e);

        // 主窗口在这里显式创建（App.xaml 已去掉 StartupUri）：
        // 这样第二个实例在拦下并 Shutdown 后绝不会再冒出窗口。
        new Views.MainWindow().Show();
    }

    /// <summary>
    /// true = 真正退出（由托盘右键「退出」或系统关机/注销触发）。
    /// false = 点窗口叉号，主窗口只最小化到任务栏，不退出进程。
    /// </summary>
    public static bool IsExiting { get; set; }

    // 系统关机 / 注销时放行关闭，避免被「关闭即最小化」拦下导致退不了。
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
        base.OnSessionEnding(e);
    }
}
