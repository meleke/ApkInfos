using Avalonia;

namespace ApkInfos.App;

internal static class Program
{
    /// <summary>
    /// 通过命令行（或把文件拖到 exe 上）传入的 APK/AAB 路径。
    /// </summary>
    public static string? StartupFile { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartupFile = args.FirstOrDefault(File.Exists);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
