// AppPaths.cs —— 应用数据根目录的唯一决定处
namespace ForwarderAssistant;

/// <summary>
/// 数据库、config.toml、日志等所有运行期数据都放在这里。
/// 策略：优先跟着 exe 走（放 exe 同级的 data\ 目录）—— 绿色便携，拷到 U 盘 / 换台电脑，
/// 数据一起带走；若 exe 所在目录不可写（例如装在 Program Files），自动退回用户目录。
/// </summary>
public static class AppPaths
{
    /// <summary>数据根目录（构造时已确保存在）。</summary>
    public static string DataRoot { get; }

    /// <summary>是否处于便携模式：true = 数据在 exe 同级 data\；false = 退回了用户目录。</summary>
    public static bool IsPortable { get; }

    static AppPaths()
    {
        // 1) 优先：exe 同级的 data\ 目录
        var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var portableDir = System.IO.Path.Combine(exeDir, "data");
        if (TryPrepareWritable(portableDir))
        {
            DataRoot = portableDir;
            IsPortable = true;
            return;
        }

        // 2) 退回：用户目录 %LOCALAPPDATA%\ForwarderAssistant
        var userDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForwarderAssistant");
        System.IO.Directory.CreateDirectory(userDir);
        DataRoot = userDir;
        IsPortable = false;
    }

    /// <summary>数据库文件完整路径。</summary>
    public static string DbFile => System.IO.Path.Combine(DataRoot, "forwarder.db");

    /// <summary>建目录并真正验证可写（写一个探针文件再删掉），失败返回 false。</summary>
    private static bool TryPrepareWritable(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var probe = System.IO.Path.Combine(dir, ".write_test");
            System.IO.File.WriteAllText(probe, "");
            System.IO.File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
