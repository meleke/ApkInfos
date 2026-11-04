using System.IO.Compression;
using ApkInfos.Core.Models;
using ApkInfos.Core.Util;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// APK / AAB 解析总入口。
/// </summary>
public static class ApkInfoReader
{
    private const string AabManifestPath = "base/manifest/AndroidManifest.xml";

    public static ApkInfo Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("文件不存在", path);
        }

        var info = new ApkInfo
        {
            FilePath = file.FullName,
            FileName = file.Name,
            FileSize = file.Length,
            FileMd5 = HashUtil.FileMd5(file.FullName),
        };

        using var zip = ZipFile.OpenRead(file.FullName);
        bool isAab = zip.GetEntry(AabManifestPath) != null;
        info.PackageType = isAab ? "AAB" : "APK";

        // 1. Manifest 与资源表
        ManifestElement manifest;
        ResourceTable? resources = null;
        string resPrefix;
        if (isAab)
        {
            manifest = ProtoXmlParser.Parse(ReadEntry(zip, AabManifestPath)!);
            resources = TryParse(info, "base/resources.pb", () => ProtoResourceParser.Parse(ReadEntry(zip, "base/resources.pb")!), zip);
            resPrefix = "base/";
            CollectModules(zip, info);
        }
        else
        {
            var manifestBytes = ReadEntry(zip, "AndroidManifest.xml") ?? throw new InvalidDataException("APK 中缺少 AndroidManifest.xml");
            manifest = AxmlParser.Parse(manifestBytes);
            resources = TryParse(info, "resources.arsc", () => ArscParser.Parse(ReadEntry(zip, "resources.arsc")!), zip);
            resPrefix = "";
        }

        // 2. 基本字段
        FillManifestFields(manifest, resources, info);

        // 3. ABI
        CollectAbis(zip, info, isAab);

        // 4. 图标
        try
        {
            LoadIcon(zip, manifest, resources, resPrefix, isAab, info);
        }
        catch (Exception ex)
        {
            info.Warnings.Add($"读取图标失败：{ex.Message}");
        }

        // 5. 签名
        var sig = ApkSignatureReader.Read(file.FullName, zip);
        info.SignatureSchemes.AddRange(sig.Schemes);
        info.Certificates.AddRange(sig.Certificates);
        info.Warnings.AddRange(sig.Warnings);

        // 6. 可读 Manifest
        info.ManifestXml = ManifestXmlWriter.Write(manifest, resources);
        return info;
    }

    private static ResourceTable? TryParse(ApkInfo info, string entryName, Func<ResourceTable> parse, ZipArchive zip)
    {
        if (zip.GetEntry(entryName) == null)
        {
            info.Warnings.Add($"未找到 {entryName}");
            return null;
        }

        try
        {
            return parse();
        }
        catch (Exception ex)
        {
            info.Warnings.Add($"解析 {entryName} 失败：{ex.Message}");
            return null;
        }
    }

    private static void FillManifestFields(ManifestElement manifest, ResourceTable? resources, ApkInfo info)
    {
        info.PackageName = manifest.Attr("package")?.StringValue ?? manifest.Attr("package")?.DisplayValue ?? "";
        info.VersionCode = Text(manifest.Attr("versionCode"), resources);
        info.VersionName = Text(manifest.Attr("versionName"), resources);

        string compileSdk = Text(manifest.Attr("compileSdkVersion"), resources);
        if (string.IsNullOrEmpty(compileSdk))
        {
            compileSdk = Text(manifest.Attr("platformBuildVersionCode"), resources);
        }

        info.CompileSdk = compileSdk;

        var usesSdk = manifest.Element("uses-sdk");
        info.MinSdk = Text(usesSdk?.Attr("minSdkVersion"), resources);
        info.TargetSdk = Text(usesSdk?.Attr("targetSdkVersion"), resources);
        if (string.IsNullOrEmpty(info.MinSdk))
        {
            info.MinSdk = "1 (未声明)";
        }

        if (string.IsNullOrEmpty(info.TargetSdk))
        {
            info.TargetSdk = "(未声明，同 minSdk)";
        }

        foreach (var child in manifest.Children)
        {
            if (child.Name is "uses-permission" or "uses-permission-sdk-23" or "uses-permission-sdk-m")
            {
                string name = Text(child.Attr("name"), resources);
                if (!string.IsNullOrEmpty(name) && !info.Permissions.Contains(name))
                {
                    info.Permissions.Add(name);
                }
            }
        }

        var app = manifest.Element("application");
        var label = app?.Attr("label");
        info.AppLabel = ResolveLabel(label, resources);

        var debuggable = app?.Attr("debuggable")?.AsBool();
        info.Debuggable = debuggable.HasValue ? (debuggable.Value ? "true" : "false") : "false (默认)";
        var allowBackup = app?.Attr("allowBackup")?.AsBool();
        info.AllowBackup = allowBackup.HasValue ? (allowBackup.Value ? "true" : "false") : "true (默认)";
    }

    private static string ResolveLabel(ManifestAttribute? label, ResourceTable? resources)
    {
        if (label == null)
        {
            return "";
        }

        if (label.Kind == AttrKind.Reference)
        {
            string? resolved = resources?.ResolveString(label.Data);
            if (!string.IsNullOrEmpty(resolved))
            {
                return resolved;
            }
        }

        return ManifestXmlWriter.FormatValue(label, resources);
    }

    /// <summary>
    /// 属性的展示文本；引用到字符串资源时解析出实际值。
    /// </summary>
    private static string Text(ManifestAttribute? attr, ResourceTable? resources)
    {
        if (attr == null)
        {
            return "";
        }

        if (attr.Kind == AttrKind.Reference && resources != null)
        {
            string? resolved = resources.ResolveString(attr.Data);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return ManifestXmlWriter.FormatValue(attr, resources);
    }

    private static void CollectModules(ZipArchive zip, ApkInfo info)
    {
        foreach (var entry in zip.Entries)
        {
            var parts = entry.FullName.Split('/');
            if (parts.Length == 3 && parts[1] == "manifest" && parts[2] == "AndroidManifest.xml" && !info.Modules.Contains(parts[0]))
            {
                info.Modules.Add(parts[0]);
            }
        }

        info.Modules.Sort((a, b) => a == "base" ? -1 : b == "base" ? 1 : string.CompareOrdinal(a, b));
    }

    private static void CollectAbis(ZipArchive zip, ApkInfo info, bool isAab)
    {
        foreach (var entry in zip.Entries)
        {
            var parts = entry.FullName.Split('/');
            string? abi = null;
            if (!isAab && parts.Length >= 3 && parts[0] == "lib")
            {
                abi = parts[1];
            }
            else if (isAab && parts.Length >= 4 && parts[1] == "lib")
            {
                abi = parts[2];
            }

            if (!string.IsNullOrEmpty(abi) && !info.Abis.Contains(abi))
            {
                info.Abis.Add(abi);
            }
        }

        info.Abis.Sort(StringComparer.Ordinal);
    }

    // ---------------- 图标 ----------------

    private static void LoadIcon(ZipArchive zip, ManifestElement manifest, ResourceTable? resources, string resPrefix, bool isAab, ApkInfo info)
    {
        var app = manifest.Element("application");
        var candidates = new List<uint>();
        foreach (var name in new[] { "icon", "roundIcon" })
        {
            var attr = app?.Attr(name);
            if (attr != null && attr.Kind == AttrKind.Reference && attr.Data != 0)
            {
                candidates.Add(attr.Data);
            }
        }

        if (resources != null)
        {
            foreach (var id in candidates)
            {
                var path = FindBitmap(zip, resources, id, resPrefix, isAab, 0);
                if (path != null)
                {
                    info.IconPath = path;
                    info.IconBytes = ReadEntry(zip, path);
                    return;
                }
            }
        }

        // 回退：按文件名在资源目录中查找最大的启动图标
        var fallback = zip.Entries
            .Where(e => IsBitmap(e.FullName) && e.FullName.StartsWith(resPrefix + "res/", StringComparison.Ordinal))
            .Where(e =>
            {
                string n = Path.GetFileNameWithoutExtension(e.Name).ToLowerInvariant();
                return n is "ic_launcher" or "app_icon" or "icon" or "ic_launcher_round" or "app_icon_round";
            })
            .OrderByDescending(e => e.Length)
            .FirstOrDefault();
        if (fallback != null)
        {
            info.IconPath = fallback.FullName;
            info.IconBytes = ReadEntry(zip, fallback.FullName);
        }
    }

    /// <summary>
    /// 在资源 ID 的各配置中选择最高密度的位图；若为 adaptive-icon XML，则解析其 foreground / background。
    /// </summary>
    private static string? FindBitmap(ZipArchive zip, ResourceTable resources, uint id, string resPrefix, bool isAab, int depth)
    {
        if (depth > 3)
        {
            return null;
        }

        var files = resources.ResolveFiles(id);
        var bitmaps = files
            .Where(f => f.Text != null && IsBitmap(f.Text) && zip.GetEntry(resPrefix + f.Text) != null)
            .OrderByDescending(f => DensityRank(f.Density))
            .ToList();
        if (bitmaps.Count > 0)
        {
            return resPrefix + bitmaps[0].Text;
        }

        foreach (var xml in files.Where(f => f.Text != null && f.Text.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var data = ReadEntry(zip, resPrefix + xml.Text);
            if (data == null)
            {
                continue;
            }

            ManifestElement root;
            try
            {
                root = isAab ? ProtoXmlParser.Parse(data) : AxmlParser.Parse(data);
            }
            catch (Exception)
            {
                continue;
            }

            // adaptive-icon：优先 foreground，其次 background；其它 drawable XML（如 bitmap/inset）取 drawable/src 属性
            var layers = new[] { root.Element("foreground"), root.Element("background"), root };
            foreach (var layer in layers)
            {
                if (layer == null)
                {
                    continue;
                }

                foreach (var attrName in new[] { "drawable", "src" })
                {
                    var attr = layer.Attr(attrName);
                    if (attr != null && attr.Kind == AttrKind.Reference && attr.Data != 0)
                    {
                        var found = FindBitmap(zip, resources, attr.Data, resPrefix, isAab, depth + 1);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }

                // 子元素（如 <foreground><inset android:drawable=.../></foreground>）
                foreach (var child in layer.Children)
                {
                    var attr = child.Attr("drawable") ?? child.Attr("src");
                    if (attr != null && attr.Kind == AttrKind.Reference && attr.Data != 0)
                    {
                        var found = FindBitmap(zip, resources, attr.Data, resPrefix, isAab, depth + 1);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static int DensityRank(int density)
    {
        // anydpi / nodpi 的位图排在具体密度之后
        if (density == ResourceTable.DensityAny || density == ResourceTable.DensityNone)
        {
            return 1;
        }

        return density == 0 ? 2 : density;
    }

    private static bool IsBitmap(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".webp" or ".jpg" or ".jpeg";
    }

    private static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry == null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
