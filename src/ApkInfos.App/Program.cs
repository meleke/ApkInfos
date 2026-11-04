// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：Program.cs
// 说明：程序入口：解析命令行参数并启动 Avalonia 应用
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

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
