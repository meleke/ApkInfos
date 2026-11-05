// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：TextViewWindow.axaml.cs
// 说明：文本查看窗口：先预览内容，由用户决定复制或另存为（可选中文注解开关）
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
/// 查看窗口中显示的一份文本及其保存方式。
/// </summary>
public sealed class TextViewContent
{
    /// <summary>窗口标题中的文件名，如 "AndroidManifest.xml"。</summary>
    public required string DisplayName { get; init; }

    /// <summary>原始文本。</summary>
    public required string Text { get; init; }

    /// <summary>带中文注解的文本；为 null 时不显示「中文注解」开关。</summary>
    public string? AnnotatedText { get; init; }

    /// <summary>另存为时的默认文件名。</summary>
    public required string SuggestedFileName { get; init; }

    /// <summary>文件类型名称，如 "XML 文件"。</summary>
    public required string FileTypeName { get; init; }

    /// <summary>扩展名（不含点），如 "xml"。</summary>
    public required string Extension { get; init; }

    /// <summary>显示在工具栏下方的说明，可为空。</summary>
    public string? Hint { get; init; }
}

/// <summary>
/// 显示一份文本（AndroidManifest.xml、google-services.json 等），支持复制与另存为（按当前显示内容）。
/// </summary>
public partial class TextViewWindow : Window
{
    private readonly TextViewContent? _content;

    public TextViewWindow()
    {
        InitializeComponent();
    }

    public TextViewWindow(string sourceName, TextViewContent content)
        : this()
    {
        _content = content;
        Title = $"{content.DisplayName} - {sourceName}";
        AnnotateCheck.IsVisible = !string.IsNullOrEmpty(content.AnnotatedText);
        HintText.Text = content.Hint;
        HintText.IsVisible = !string.IsNullOrEmpty(content.Hint);
        ShowCurrent();
    }

    private string CurrentText
    {
        get
        {
            if (_content == null)
            {
                return "";
            }

            if (AnnotateCheck.IsVisible && AnnotateCheck.IsChecked == true && !string.IsNullOrEmpty(_content.AnnotatedText))
            {
                return _content.AnnotatedText;
            }

            return _content.Text;
        }
    }

    private void ShowCurrent()
    {
        string text = CurrentText;
        ContentBox.Text = text;
        InfoText.Text = $"{text.Split('\n').Length} 行";
    }

    private void OnAnnotateChanged(object? sender, RoutedEventArgs e)
    {
        // 构造前（InitializeComponent 阶段）也会触发，此时内容尚未赋值
        if (_content == null)
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

        await clipboard.SetTextAsync(CurrentText);
        InfoText.Text = "已复制到剪贴板";
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_content == null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"保存 {_content.DisplayName}",
            SuggestedFileName = _content.SuggestedFileName,
            DefaultExtension = _content.Extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(_content.FileTypeName)
                {
                    Patterns = new[] { $"*.{_content.Extension}" },
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
            var bytes = new UTF8Encoding(false).GetBytes(CurrentText);
            await stream.WriteAsync(bytes);
            InfoText.Text = $"已保存：{file.TryGetLocalPath() ?? file.Name}";
        }
        catch (Exception ex)
        {
            InfoText.Text = $"保存失败：{ex.Message}";
        }
    }
}
