// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：ManifestWindow.axaml.cs
// 说明：AndroidManifest.xml 查看窗口：中文注解开关、复制与另存为
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ApkInfos.App;

/// <summary>
/// 显示解码后的 AndroidManifest.xml，可切换中文注解，支持复制与另存为（按当前显示内容）。
/// </summary>
public partial class ManifestWindow : Window
{
    private readonly string _rawXml = "";
    private readonly string _annotatedXml = "";
    private readonly string _suggestedName = "AndroidManifest.xml";

    public ManifestWindow()
    {
        InitializeComponent();
    }

    public ManifestWindow(string sourceName, string rawXml, string annotatedXml, string suggestedName)
        : this()
    {
        _rawXml = rawXml;
        _annotatedXml = string.IsNullOrEmpty(annotatedXml) ? rawXml : annotatedXml;
        _suggestedName = suggestedName;
        Title = $"AndroidManifest.xml - {sourceName}";
        ShowCurrent();
    }

    private string CurrentXml => AnnotateCheck.IsChecked == true ? _annotatedXml : _rawXml;

    private void ShowCurrent()
    {
        string xml = CurrentXml;
        XmlBox.Text = xml;
        InfoText.Text = $"{xml.Split('\n').Length} 行";
    }

    private void OnAnnotateChanged(object? sender, RoutedEventArgs e)
    {
        // 构造前（InitializeComponent 阶段）也会触发，此时内容尚未赋值
        if (_rawXml.Length == 0)
        {
            return;
        }

        ShowCurrent();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        if (clipboard == null)
        {
            return;
        }

        await clipboard.SetTextAsync(CurrentXml);
        InfoText.Text = "已复制到剪贴板";
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 AndroidManifest.xml",
            SuggestedFileName = _suggestedName,
            DefaultExtension = "xml",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("XML 文件")
                {
                    Patterns = new[] { "*.xml" },
                },
            },
        });

        if (file == null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            var bytes = new UTF8Encoding(false).GetBytes(CurrentXml);
            await stream.WriteAsync(bytes);
            InfoText.Text = $"已保存：{file.TryGetLocalPath() ?? file.Name}";
        }
        catch (Exception ex)
        {
            InfoText.Text = $"保存失败：{ex.Message}";
        }
    }
}
