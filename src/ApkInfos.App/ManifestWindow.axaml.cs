using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ApkInfos.App;

/// <summary>
/// 显示解码后的 AndroidManifest.xml，支持复制与另存为。
/// </summary>
public partial class ManifestWindow : Window
{
    private readonly string _xml = "";
    private readonly string _suggestedName = "AndroidManifest.xml";

    public ManifestWindow()
    {
        InitializeComponent();
    }

    public ManifestWindow(string sourceName, string xml, string suggestedName)
        : this()
    {
        _xml = xml;
        _suggestedName = suggestedName;
        Title = $"AndroidManifest.xml - {sourceName}";
        XmlBox.Text = xml;
        InfoText.Text = $"{xml.Split('\n').Length} 行";
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        if (clipboard == null)
        {
            return;
        }

        await clipboard.SetTextAsync(_xml);
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
            var bytes = new UTF8Encoding(false).GetBytes(_xml);
            await stream.WriteAsync(bytes);
            InfoText.Text = $"已保存：{file.TryGetLocalPath() ?? file.Name}";
        }
        catch (Exception ex)
        {
            InfoText.Text = $"保存失败：{ex.Message}";
        }
    }
}
