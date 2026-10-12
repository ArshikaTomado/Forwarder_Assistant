// StartupRegistration.cs —— 开机自启登记（只动 HKCU，不需要管理员权限）
namespace ForwarderAssistant.Services;

using Microsoft.Win32;

/// <summary>
/// 把本程序登记到当前用户的 Run 项，实现开机自启。
/// 绿色便携程序「解压到哪就从哪启动」，所以每次开启都用当前 exe 路径覆写。
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ForwarderAssistant";

    /// <summary>当前 exe 完整路径（自启项指向它）。</summary>
    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>是否已登记自启（且指向当前这个 exe）。</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return string.Equals(value, Quoted(ExePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>登记自启（写当前 exe 路径）。</summary>
    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key?.SetValue(ValueName, Quoted(ExePath));
    }

    /// <summary>取消自启（删掉该项）。</summary>
    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // 路径带空格时必须用引号包住，否则 Windows 会把路径拆错。
    private static string Quoted(string path) => $"\"{path}\"";
}
