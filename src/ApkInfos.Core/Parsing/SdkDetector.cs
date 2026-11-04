// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：SdkDetector.cs
// 说明：识别 Facebook / Adjust SDK 的集成情况与配置
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ApkInfos.Core.Models;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 识别第三方 SDK（Facebook、Adjust）的集成情况与配置。
/// </summary>
public static class SdkDetector
{
    private const string FbPrefix = "com.facebook.sdk.";
    private static readonly byte[] FacebookSdkClass = Encoding.ASCII.GetBytes("Lcom/facebook/FacebookSdk;");
    private static readonly byte[] AdjustPackage = Encoding.ASCII.GetBytes("Lcom/adjust/sdk/");
    private static readonly byte[] AdjustUnityMarker = Encoding.ASCII.GetBytes("AdjustSdk");
    private static readonly Regex DexEntry = new(@"^(?:[^/]+/dex/)?classes\d*\.dex$", RegexOptions.CultureInvariant);
    private static readonly Regex ProviderAppId = new(@"FacebookContentProvider(\d+)$", RegexOptions.CultureInvariant);

    public static void Detect(ZipArchive zip, ManifestElement manifest, ResourceTable? resources, ApkInfo info)
    {
        // dex 只读一遍，供两个 SDK 共用
        bool fbInDex = false;
        bool adjustInDex = false;
        var nativeVersions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (!DexEntry.IsMatch(entry.FullName))
            {
                continue;
            }

            var dex = ReadAll(entry);
            fbInDex |= dex.AsSpan().IndexOf(FacebookSdkClass) >= 0;
            if (dex.AsSpan().IndexOf(AdjustPackage) >= 0)
            {
                adjustInDex = true;
                foreach (var v in FindDexVersionStrings(dex, "android"))
                {
                    nativeVersions.Add(v);
                }
            }
        }

        info.Facebook = DetectFacebook(manifest, resources, fbInDex);
        info.Adjust = DetectAdjust(zip, manifest, adjustInDex, nativeVersions);
    }

    // ---------------- Facebook ----------------

    private static FacebookInfo DetectFacebook(ManifestElement manifest, ResourceTable? resources, bool inDex)
    {
        var fb = new FacebookInfo();
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in Descendants(manifest))
        {
            if (element.Name == "meta-data")
            {
                string name = ApkInfoReader.Text(element.Attr("name"), resources);
                if (name.StartsWith(FbPrefix, StringComparison.Ordinal))
                {
                    meta[name.Substring(FbPrefix.Length)] = ApkInfoReader.Text(element.Attr("value"), resources);
                }
            }
            else if (element.Name == "provider")
            {
                string authorities = ApkInfoReader.Text(element.Attr("authorities"), resources);
                var m = ProviderAppId.Match(authorities);
                if (m.Success)
                {
                    fb.ProviderAppId = m.Groups[1].Value;
                }
            }
        }

        fb.AppId = NormalizeAppId(meta.GetValueOrDefault("ApplicationId") ?? resources?.ResolveStringByName("facebook_app_id"));
        fb.ClientToken = NullIfEmpty(meta.GetValueOrDefault("ClientToken") ?? resources?.ResolveStringByName("facebook_client_token"));
        fb.AutoLogAppEvents = NullIfEmpty(meta.GetValueOrDefault("AutoLogAppEventsEnabled"));
        fb.AdvertiserIdCollection = NullIfEmpty(meta.GetValueOrDefault("AdvertiserIDCollectionEnabled"));
        fb.AutoInit = NullIfEmpty(meta.GetValueOrDefault("AutoInitEnabled"));
        fb.Integrated = inDex || meta.Count > 0 || fb.ProviderAppId != null;

        if (!fb.Integrated)
        {
            return fb;
        }

        // 配置检查
        if (fb.AppId == null)
        {
            fb.Warnings.Add("未找到 App ID（com.facebook.sdk.ApplicationId）");
        }
        else if (fb.AppId.Trim('0').Length == 0)
        {
            fb.Warnings.Add("App ID 为 0，疑似未配置");
        }

        if (fb.ClientToken == null)
        {
            fb.Warnings.Add("未找到 Client Token（SDK v13 起必填）");
        }

        if (fb.ProviderAppId != null && fb.AppId != null && fb.ProviderAppId != fb.AppId)
        {
            fb.Warnings.Add($"FacebookContentProvider 中的 App ID（{fb.ProviderAppId}）与配置不一致");
        }

        return fb;
    }

    /// <summary>
    /// 去掉 "fb" 前缀与空白（Unity Facebook SDK 写入的是 "fb123…"）。
    /// </summary>
    private static string? NormalizeAppId(string? value)
    {
        string? v = NullIfEmpty(value?.Trim().TrimStart('\\').Trim());
        if (v != null && v.StartsWith("fb", StringComparison.OrdinalIgnoreCase) && v.Length > 2 && char.IsDigit(v[2]))
        {
            v = v.Substring(2);
        }

        return v;
    }

    // ---------------- Adjust ----------------

    private static AdjustInfo DetectAdjust(ZipArchive zip, ManifestElement manifest, bool inDex, SortedSet<string> nativeVersions)
    {
        var adjust = new AdjustInfo();
        foreach (var element in Descendants(manifest))
        {
            string name = element.Attr("name")?.StringValue ?? "";
            if (name.StartsWith("com.adjust.sdk.", StringComparison.Ordinal) && element.Name != "meta-data")
            {
                adjust.Evidence.Add($"Manifest：{name}");
            }
        }

        if (inDex)
        {
            adjust.Evidence.Add("代码：com.adjust.sdk.*");
        }

        adjust.NativeVersions.AddRange(nativeVersions);

        // Unity 插件：IL2CPP 元数据（UTF-8）或 Mono 托管 dll（#US 堆为 UTF-16）
        var unityVersions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName;
            if (path.EndsWith("Managed/Metadata/global-metadata.dat", StringComparison.Ordinal))
            {
                var data = ReadAll(entry);
                if (data.AsSpan().IndexOf(AdjustUnityMarker) >= 0)
                {
                    foreach (var v in FindLooseVersionStrings(data, "unity"))
                    {
                        unityVersions.Add(v);
                    }
                }
            }
            else if (path.Contains("/Managed/", StringComparison.Ordinal) && entry.Name.StartsWith("Adjust", StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var text = Encoding.Unicode.GetString(ReadAll(entry));
                foreach (Match m in Regex.Matches(text, @"unity\d{1,2}\.\d{1,3}\.\d{1,3}"))
                {
                    unityVersions.Add(m.Value);
                }
            }
        }

        adjust.UnityVersions.AddRange(unityVersions);
        if (adjust.UnityVersions.Count > 0)
        {
            adjust.Evidence.Add("Unity 插件：AdjustSdk");
        }

        adjust.Integrated = adjust.Evidence.Count > 0;
        if (adjust.Integrated && !inDex)
        {
            adjust.Warnings.Add("声明了 Adjust 组件/插件，但 dex 中未找到 com.adjust.sdk 原生代码，原生 SDK 可能未打包，运行时初始化会失败");
        }

        if (adjust.NativeVersions.Count > 1 || adjust.UnityVersions.Count > 1)
        {
            adjust.Warnings.Add("检测到多个版本号，可能存在重复集成");
        }

        return adjust;
    }

    /// <summary>
    /// 在字节流中查找形如 "unity5.4.2" 的版本串（IL2CPP 元数据中的字符串字面量首尾相连，没有分隔符）。
    /// </summary>
    public static List<string> FindLooseVersionStrings(byte[] data, string prefix)
    {
        var result = new List<string>();
        var needle = Encoding.ASCII.GetBytes(prefix);
        int pos = 0;
        while (true)
        {
            int idx = data.AsSpan(pos).IndexOf(needle);
            if (idx < 0)
            {
                return result;
            }

            int start = pos + idx;
            pos = start + needle.Length;
            int end = pos;
            while (end < data.Length && end - start < 24 && (char.IsAsciiDigit((char)data[end]) || data[end] == '.'))
            {
                end++;
            }

            if (end > pos)
            {
                string candidate = Encoding.ASCII.GetString(data, start, end - start);
                var m = Regex.Match(candidate, $@"^{prefix}\d{{1,2}}\.\d{{1,3}}\.\d{{1,3}}");
                if (m.Success)
                {
                    result.Add(m.Value);
                }
            }
        }
    }

    /// <summary>
    /// 在 dex 的字符串数据区中查找形如 "android5.4.2" 的完整字符串：
    /// dex string_data_item = ULEB128 长度 + MUTF-8 内容 + '\0'，据此精确定界，避免误匹配。
    /// </summary>
    public static List<string> FindDexVersionStrings(byte[] dex, string prefix)
    {
        var result = new List<string>();
        var needle = Encoding.ASCII.GetBytes(prefix);
        int pos = 0;
        while (true)
        {
            int idx = dex.AsSpan(pos).IndexOf(needle);
            if (idx < 0)
            {
                return result;
            }

            int start = pos + idx;
            pos = start + needle.Length;
            int end = pos;
            while (end < dex.Length && (char.IsAsciiDigit((char)dex[end]) || dex[end] == '.'))
            {
                end++;
            }

            int len = end - start;
            bool terminated = end < dex.Length && dex[end] == 0;
            bool lengthPrefixed = start > 0 && dex[start - 1] == len;
            if (terminated && lengthPrefixed && len > needle.Length)
            {
                string candidate = Encoding.ASCII.GetString(dex, start, len);
                if (Regex.IsMatch(candidate, $@"^{prefix}\d{{1,2}}\.\d{{1,3}}\.\d{{1,3}}$"))
                {
                    result.Add(candidate);
                }
            }
        }
    }

    // ---------------- 工具 ----------------

    private static IEnumerable<ManifestElement> Descendants(ManifestElement root)
    {
        var stack = new Stack<ManifestElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            yield return element;
            for (int i = element.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(element.Children[i]);
            }
        }
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
