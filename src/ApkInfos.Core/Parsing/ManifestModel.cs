// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：ManifestModel.cs
// 说明：统一的 Manifest 元素/属性模型与 android 系统属性 ID 表
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 属性值类型（对应 Android Res_value 的主要类型）。
/// </summary>
public enum AttrKind
{
    Null,
    String,
    IntDec,
    IntHex,
    Bool,
    Reference,
    Attribute,
    Float,
    Dimension,
    Fraction,
    Color,
    Other,
}

/// <summary>
/// Manifest 元素属性。APK（二进制 AXML）与 AAB（protobuf XML）统一转换为此模型。
/// </summary>
public sealed class ManifestAttribute
{
    public string NamespaceUri { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>系统属性资源 ID（如 android:versionCode = 0x0101021b），没有为 0。</summary>
    public uint ResourceId { get; set; }

    public AttrKind Kind { get; set; } = AttrKind.String;

    /// <summary>原始 32 位数据（整数、布尔、引用 ID 等）。</summary>
    public uint Data { get; set; }

    /// <summary>字符串值（Kind 为 String 时有效）或原始字符串。</summary>
    public string? StringValue { get; set; }

    /// <summary>已格式化的展示文本（AAB 中直接来自 aapt2 的 value 字段）。</summary>
    public string? DisplayValue { get; set; }

    public bool IsAndroid => NamespaceUri == AndroidAttrs.AndroidNs;

    /// <summary>
    /// 取整数值；字符串形式的数字也会被解析。
    /// </summary>
    public long? AsInt()
    {
        switch (Kind)
        {
            case AttrKind.IntDec:
            {
                return unchecked((int)Data);
            }
            case AttrKind.IntHex:
            case AttrKind.Bool:
            {
                return Data;
            }
        }

        string? text = StringValue ?? DisplayValue;
        if (text != null && long.TryParse(text.Trim(), out long value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// 取布尔值；非布尔类型返回 null。
    /// </summary>
    public bool? AsBool()
    {
        if (Kind == AttrKind.Bool)
        {
            return Data != 0;
        }

        string? text = StringValue ?? DisplayValue;
        if (text != null && bool.TryParse(text.Trim(), out bool value))
        {
            return value;
        }

        return null;
    }
}

/// <summary>
/// Manifest 元素节点。
/// </summary>
public sealed class ManifestElement
{
    public string Name { get; set; } = "";

    public string NamespaceUri { get; set; } = "";

    /// <summary>在本元素上声明的命名空间（前缀, URI）。</summary>
    public List<KeyValuePair<string, string>> Namespaces { get; } = new();

    public List<ManifestAttribute> Attributes { get; } = new();

    public List<ManifestElement> Children { get; } = new();

    /// <summary>
    /// 查找 android 命名空间（或无命名空间）下的属性，名称被混淆时按资源 ID 匹配。
    /// </summary>
    public ManifestAttribute? Attr(string name)
    {
        uint id = AndroidAttrs.IdOf(name);
        ManifestAttribute? plain = null;
        foreach (var attr in Attributes)
        {
            if (id != 0 && attr.ResourceId == id)
            {
                return attr;
            }

            if (attr.Name == name)
            {
                if (attr.IsAndroid)
                {
                    return attr;
                }

                plain ??= attr;
            }
        }

        return plain;
    }

    public IEnumerable<ManifestElement> Elements(string name)
    {
        return Children.Where(c => c.Name == name);
    }

    public ManifestElement? Element(string name)
    {
        return Children.FirstOrDefault(c => c.Name == name);
    }
}

/// <summary>
/// 常用 android 系统属性名与资源 ID 映射，用于属性名被加固/混淆时回退。
/// </summary>
public static class AndroidAttrs
{
    public const string AndroidNs = "http://schemas.android.com/apk/res/android";

    private static readonly Dictionary<uint, string> IdToName = new()
    {
        [0x01010000] = "theme",
        [0x01010001] = "label",
        [0x01010002] = "icon",
        [0x01010003] = "name",
        [0x01010006] = "permission",
        [0x0101000e] = "enabled",
        [0x0101000f] = "debuggable",
        [0x01010010] = "exported",
        [0x01010011] = "process",
        [0x01010018] = "authorities",
        [0x0101001d] = "launchMode",
        [0x0101001e] = "screenOrientation",
        [0x0101001f] = "configChanges",
        [0x01010024] = "value",
        [0x01010025] = "resource",
        [0x01010027] = "scheme",
        [0x01010028] = "host",
        [0x01010199] = "drawable",
        [0x0101020c] = "minSdkVersion",
        [0x0101021b] = "versionCode",
        [0x0101021c] = "versionName",
        [0x01010270] = "targetSdkVersion",
        [0x01010271] = "maxSdkVersion",
        [0x01010280] = "allowBackup",
        [0x01010281] = "glEsVersion",
        [0x0101028e] = "required",
        [0x010102b7] = "installLocation",
        [0x010102d3] = "hardwareAccelerated",
        [0x0101035a] = "largeHeap",
        [0x010104ea] = "extractNativeLibs",
        [0x010104ec] = "usesCleartextTraffic",
        [0x01010527] = "networkSecurityConfig",
        [0x0101052c] = "roundIcon",
        [0x01010572] = "compileSdkVersion",
        [0x01010573] = "compileSdkVersionCodename",
        [0x0101057a] = "appComponentFactory",
        [0x01010603] = "requestLegacyExternalStorage",
    };

    private static readonly Dictionary<string, uint> NameToId = IdToName.ToDictionary(p => p.Value, p => p.Key);

    public static string? NameOf(uint id)
    {
        return IdToName.TryGetValue(id, out var name) ? name : null;
    }

    public static uint IdOf(string name)
    {
        return NameToId.TryGetValue(name, out var id) ? id : 0;
    }
}
