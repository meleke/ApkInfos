using ApkInfos.Core.Models;
using ApkInfos.Core.Parsing;
using ApkInfos.Core.Util;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace ApkInfos.App;

public partial class MainWindow : Window
{
    private static readonly string[] PackageExtensions = { ".apk", ".aab" };
    private static readonly string[] KeystoreExtensions = { ".jks", ".keystore", ".p12", ".pfx", ".bks" };

    private static readonly FilePickerFileType PackageFileType = new("Android 包 (*.apk, *.aab)")
    {
        Patterns = new[] { "*.apk", "*.aab" },
    };

    private static readonly FilePickerFileType KeystoreFileType = new("Keystore (*.jks, *.keystore, *.p12, *.pfx)")
    {
        Patterns = new[] { "*.jks", "*.keystore", "*.p12", "*.pfx" },
    };

    private ApkInfo? _info;
    private KeystoreInfo? _keystore;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += OnWindowOpened;
        ClearPackageFields();
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (Program.StartupFile != null)
        {
            await LoadPackageAsync(Program.StartupFile);
        }
    }

    // ---------------- 打开 / 拖拽 ----------------

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 APK / AAB 文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { PackageFileType, FilePickerFileTypes.All },
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path != null)
        {
            await LoadPackageAsync(path);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetDroppedPath(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        string? path = GetDroppedPath(e);
        if (path == null)
        {
            return;
        }

        if (HasExtension(path, KeystoreExtensions))
        {
            KeystorePathBox.Text = path;
            KeystorePasswordBox.Focus();
            StatusText.Text = "已选择 keystore，请输入密码后加载";
            return;
        }

        await LoadPackageAsync(path);
    }

    private static string? GetDroppedPath(DragEventArgs e)
    {
#pragma warning disable CS0618 // 兼容 Avalonia 11 的拖拽数据接口
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files == null)
        {
            return null;
        }

        foreach (var item in files)
        {
            string? path = item.TryGetLocalPath();
            if (path != null && (HasExtension(path, PackageExtensions) || HasExtension(path, KeystoreExtensions)))
            {
                return path;
            }
        }

        return null;
    }

    private static bool HasExtension(string path, string[] extensions)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return extensions.Contains(ext);
    }

    // ---------------- 解析 APK / AAB ----------------

    private async Task LoadPackageAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        StatusText.Text = $"正在解析 {Path.GetFileName(path)} …";
        try
        {
            var info = await Task.Run(() => ApkInfoReader.Read(path));
            _info = info;
            ShowPackage(info);
            StatusText.Text = info.Warnings.Count == 0
                ? $"解析完成：{info.FilePath}"
                : $"解析完成（{info.Warnings.Count} 条提示）：{string.Join("；", info.Warnings)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"解析失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        OpenButton.IsEnabled = !busy;
        ManifestButton.IsEnabled = !busy && _info != null;
    }

    private void ClearPackageFields()
    {
        CompareText.Text = "";
        ModulesBox.Text = "";
    }

    private void ShowPackage(ApkInfo info)
    {
        Title = $"ApkInfos - {info.FileName} [{info.PackageType}]";

        PackageNameBox.Text = info.PackageName;
        LabelBox.Text = info.AppLabel;
        VersionNameBox.Text = info.VersionName;
        VersionCodeBox.Text = info.VersionCode;
        MinSdkBox.Text = info.MinSdk;
        PermissionsBox.Text = info.Permissions.Count == 0 ? "(无)" : string.Join(Environment.NewLine, info.Permissions);

        TargetSdkBox.Text = info.TargetSdk;
        CompileSdkBox.Text = info.CompileSdk;
        DebuggableBox.Text = info.Debuggable;
        AllowBackupBox.Text = info.AllowBackup;
        AbiBox.Text = info.Abis.Count == 0 ? "(无 native 库)" : string.Join(", ", info.Abis);
        ModulesBox.Text = info.PackageType == "AAB" ? string.Join(", ", info.Modules) : "(APK 不适用)";

        var cert = info.PrimaryCertificate;
        if (cert == null)
        {
            CertMd5Box.Text = "未签名";
            SchemesBox.Text = "未签名";
            CertSha1Box.Text = "";
            CertSha256Box.Text = "";
            SubjectBox.Text = "";
            IssuerBox.Text = "";
            ValidityBox.Text = "";
            SerialBox.Text = "";
            SigAlgBox.Text = "";
        }
        else
        {
            CertMd5Box.Text = cert.Md5;
            SchemesBox.Text = FormatSchemes(info);
            CertSha1Box.Text = HashUtil.WithColons(cert.Sha1);
            CertSha256Box.Text = HashUtil.WithColons(cert.Sha256);
            SubjectBox.Text = cert.Subject;
            IssuerBox.Text = cert.Issuer;
            ValidityBox.Text = cert.ValidityText;
            SerialBox.Text = cert.SerialNumber;
            SigAlgBox.Text = cert.SignatureAlgorithm;
        }

        FileNameBox.Text = info.FileName;
        FilePathBox.Text = info.FilePath;
        FileTypeBox.Text = info.PackageType;
        FileMd5Box.Text = info.FileMd5;
        FileSizeBox.Text = ApkInfo.FormatSize(info.FileSize);

        IconImage.Source = LoadBitmap(info.IconBytes);
        if (info.IconBytes != null && IconImage.Source == null)
        {
            info.Warnings.Add($"图标 {info.IconPath} 无法解码");
        }

        UpdateCompare();
    }

    private static string FormatSchemes(ApkInfo info)
    {
        string schemes = string.Join(" + ", info.SignatureSchemes);
        if (info.PackageType == "AAB" && info.SignatureSchemes.Count == 1 && info.SignatureSchemes[0] == "v1")
        {
            return "v1 (jarsigner)";
        }

        // 不同方案使用了不同证书时（如 v3 密钥轮换）提示
        bool rotated = info.Certificates.Any(c => !c.SameAs(info.PrimaryCertificate));
        return rotated ? $"{schemes}（各方案证书不一致，已显示 {info.PrimaryCertificate!.Source} 证书）" : schemes;
    }

    private static Bitmap? LoadBitmap(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------- AndroidManifest.xml ----------------

    private void OnManifestClick(object? sender, RoutedEventArgs e)
    {
        if (_info == null)
        {
            return;
        }

        string fileName = string.IsNullOrEmpty(_info.PackageName) ? "AndroidManifest.xml" : $"{_info.PackageName}_AndroidManifest.xml";
        var window = new ManifestWindow(_info.FileName, _info.ManifestXml, fileName);
        window.Show(this);
    }

    // ---------------- Keystore ----------------

    private async void OnBrowseKeystoreClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Keystore 文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { KeystoreFileType, FilePickerFileTypes.All },
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path != null)
        {
            KeystorePathBox.Text = path;
            KeystorePasswordBox.Focus();
        }
    }

    private async void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await LoadKeystoreAsync();
        }
    }

    private async void OnLoadKeystoreClick(object? sender, RoutedEventArgs e)
    {
        await LoadKeystoreAsync();
    }

    private async Task LoadKeystoreAsync()
    {
        string path = KeystorePathBox.Text?.Trim().Trim('"') ?? "";
        string password = KeystorePasswordBox.Text ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            StatusText.Text = "请先选择存在的 keystore 文件";
            return;
        }

        LoadKeystoreButton.IsEnabled = false;
        try
        {
            var keystore = await Task.Run(() => KeystoreReader.Load(path, password));
            _keystore = keystore;
            AliasCombo.ItemsSource = keystore.Entries;

            // 优先选中与当前 APK 签名一致的 alias
            var match = keystore.Entries.FirstOrDefault(k => k.Certificate != null && k.Certificate.SameAs(_info?.PrimaryCertificate));
            AliasCombo.SelectedItem = match ?? keystore.Entries.FirstOrDefault();

            string note = keystore.IntegrityVerified ? "" : "（未输入密码，未校验完整性）";
            string matchNote = match != null ? $"，与 APK 签名匹配的 alias：{match.Alias}" : "";
            StatusText.Text = $"已加载 {keystore.Format} keystore，共 {keystore.Entries.Count} 个 alias{matchNote}{note}";
        }
        catch (KeystorePasswordException ex)
        {
            ClearKeystore();
            StatusText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            ClearKeystore();
            StatusText.Text = $"加载 keystore 失败：{ex.Message}";
        }
        finally
        {
            LoadKeystoreButton.IsEnabled = true;
        }
    }

    private void ClearKeystore()
    {
        _keystore = null;
        AliasCombo.ItemsSource = null;
        ShowAlias(null);
    }

    private void OnAliasChanged(object? sender, SelectionChangedEventArgs e)
    {
        ShowAlias(AliasCombo.SelectedItem as KeystoreEntry);
    }

    private void ShowAlias(KeystoreEntry? entry)
    {
        var cert = entry?.Certificate;
        if (entry == null)
        {
            AliasInfoBox.Text = "";
        }
        else
        {
            string created = entry.CreationDate.HasValue ? $"，创建于 {entry.CreationDate:yyyy-MM-dd HH:mm:ss}" : "";
            string subject = cert != null ? $"，{cert.Subject}" : "";
            AliasInfoBox.Text = $"{entry.EntryType}，证书链 {entry.ChainLength}{created}{subject}";
        }

        AliasMd5Box.Text = cert?.Md5 ?? "";
        AliasSha1Box.Text = cert != null ? HashUtil.WithColons(cert.Sha1) : "";
        AliasSha256Box.Text = cert != null ? HashUtil.WithColons(cert.Sha256) : "";
        UpdateCompare();
    }

    private void UpdateCompare()
    {
        var entry = AliasCombo.SelectedItem as KeystoreEntry;
        if (_keystore == null || entry?.Certificate == null || _info == null)
        {
            CompareText.Text = _keystore != null && _info == null ? "请先打开 APK / AAB" : "";
            CompareText.Foreground = null;
            return;
        }

        if (_info.PrimaryCertificate == null)
        {
            CompareText.Text = "当前包未签名，无法比对";
            CompareText.Foreground = Brushes.DarkOrange;
            return;
        }

        bool same = entry.Certificate.SameAs(_info.PrimaryCertificate);
        CompareText.Text = same ? "✔ 与当前包签名一致" : "✘ 与当前包签名不一致";
        CompareText.Foreground = same ? Brushes.ForestGreen : Brushes.Crimson;
    }
}
