// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：ResourceTable.cs
// 说明：统一的资源表模型（字符串解析、图标按密度选取）
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 资源值类型。
/// </summary>
public enum ResValueKind
{
    String,
    Reference,
    File,
    Other,
}

/// <summary>
/// 某资源在某个配置（语言/密度）下的值。
/// </summary>
public sealed class ResValue
{
    /// <summary>语言区域，如 ""、"zh"、"zh-CN"。</summary>
    public string Locale { get; set; } = "";

    /// <summary>屏幕密度，0 = 默认，0xFFFE = anydpi，0xFFFF = nodpi。</summary>
    public int Density { get; set; }

    public ResValueKind Kind { get; set; }

    /// <summary>引用 ID 或原始数据。</summary>
    public uint Data { get; set; }

    /// <summary>字符串值或文件路径。</summary>
    public string? Text { get; set; }
}

/// <summary>
/// 一个资源条目（如 string/app_name）。
/// </summary>
public sealed class ResEntry
{
    public uint Id { get; set; }

    public string Type { get; set; } = "";

    public string Name { get; set; } = "";

    public List<ResValue> Values { get; } = new();
}

/// <summary>
/// 资源表：APK 的 resources.arsc 与 AAB 的 resources.pb 统一转换为此模型。
/// </summary>
public sealed class ResourceTable
{
    public const int DensityAny = 0xFFFE;
    public const int DensityNone = 0xFFFF;

    private readonly Dictionary<uint, ResEntry> _entries = new();

    public int Count => _entries.Count;

    public ResEntry GetOrAdd(uint id, string type, string name)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            entry = new ResEntry
            {
                Id = id,
                Type = type,
                Name = name,
            };
            _entries[id] = entry;
        }

        return entry;
    }

    public ResEntry? Get(uint id)
    {
        return _entries.TryGetValue(id, out var entry) ? entry : null;
    }

    private Dictionary<string, ResEntry>? _nameIndex;

    /// <summary>
    /// 按 "type/name" 查找资源（如 string/google_app_id），多个包同名时取第一个。
    /// </summary>
    public ResEntry? FindByName(string type, string name)
    {
        if (_nameIndex == null)
        {
            _nameIndex = new Dictionary<string, ResEntry>(StringComparer.Ordinal);
            foreach (var entry in _entries.Values.OrderBy(e => e.Id))
            {
                _nameIndex.TryAdd($"{entry.Type}/{entry.Name}", entry);
            }
        }

        return _nameIndex.TryGetValue($"{type}/{name}", out var found) ? found : null;
    }

    /// <summary>
    /// 按名称解析字符串资源，不存在或为空返回 null。
    /// </summary>
    public string? ResolveStringByName(string name)
    {
        var entry = FindByName("string", name);
        if (entry == null)
        {
            return null;
        }

        string? value = ResolveString(entry.Id);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// 返回 "type/name"，未知 ID 返回 null。
    /// </summary>
    public string? GetName(uint id)
    {
        var entry = Get(id);
        return entry == null ? null : $"{entry.Type}/{entry.Name}";
    }

    /// <summary>
    /// 解析字符串资源，按语言优先级选取：zh-CN → zh → 默认 → en → 第一个。
    /// </summary>
    public string? ResolveString(uint id, int depth = 0)
    {
        if (depth > 8)
        {
            return null;
        }

        var entry = Get(id);
        if (entry == null || entry.Values.Count == 0)
        {
            return null;
        }

        var best = PickByLocale(entry.Values);
        if (best == null)
        {
            return null;
        }

        if (best.Kind == ResValueKind.Reference)
        {
            return ResolveString(best.Data, depth + 1);
        }

        return best.Text;
    }

    /// <summary>
    /// 列出资源在各配置下对应的文件（跟随引用），用于查找图标。
    /// </summary>
    public List<ResValue> ResolveFiles(uint id, int depth = 0)
    {
        var result = new List<ResValue>();
        if (depth > 8)
        {
            return result;
        }

        var entry = Get(id);
        if (entry == null)
        {
            return result;
        }

        foreach (var value in entry.Values)
        {
            if (value.Kind == ResValueKind.Reference)
            {
                foreach (var sub in ResolveFiles(value.Data, depth + 1))
                {
                    // 被引用的值若无密度信息，继承引用方的密度
                    if (sub.Density == 0 && value.Density != 0)
                    {
                        sub.Density = value.Density;
                    }

                    result.Add(sub);
                }
            }
            else if (!string.IsNullOrEmpty(value.Text) && (value.Kind == ResValueKind.File || LooksLikeFile(value.Text)))
            {
                result.Add(new ResValue
                {
                    Locale = value.Locale,
                    Density = value.Density != 0 ? value.Density : DensityFromPath(value.Text),
                    Kind = ResValueKind.File,
                    Text = value.Text,
                });
            }
        }

        return result;
    }

    private static ResValue? PickByLocale(List<ResValue> values)
    {
        string[] preferred = { "zh-cn", "zh-hans-cn", "zh-hans", "zh", "", "en-us", "en" };
        foreach (var locale in preferred)
        {
            foreach (var value in values)
            {
                if (NormalizeLocale(value.Locale) == locale)
                {
                    return value;
                }
            }
        }

        return values.Count > 0 ? values[0] : null;
    }

    /// <summary>
    /// 统一语言区域写法：zh-rCN / zh_CN / b+zh+Hans → zh-cn / zh-hans。
    /// </summary>
    public static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrEmpty(locale))
        {
            return "";
        }

        string s = locale.Trim();
        if (s.StartsWith("b+", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(2).Replace('+', '-');
        }

        s = s.Replace('_', '-').Replace("-r", "-");
        return s.ToLowerInvariant();
    }

    public static bool LooksLikeFile(string text)
    {
        if (!text.Contains('/'))
        {
            return false;
        }

        string ext = Path.GetExtension(text).ToLowerInvariant();
        return ext is ".png" or ".webp" or ".jpg" or ".jpeg" or ".xml" or ".gif";
    }

    /// <summary>
    /// 从资源路径的目录限定符推断密度，如 res/mipmap-xxhdpi-v4/ic.png → 480。
    /// </summary>
    public static int DensityFromPath(string path)
    {
        string dir = Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        string folder = dir.Contains('/') ? dir.Substring(dir.LastIndexOf('/') + 1) : dir;
        foreach (var part in folder.Split('-'))
        {
            switch (part)
            {
                case "ldpi":
                {
                    return 120;
                }
                case "mdpi":
                {
                    return 160;
                }
                case "tvdpi":
                {
                    return 213;
                }
                case "hdpi":
                {
                    return 240;
                }
                case "xhdpi":
                {
                    return 320;
                }
                case "xxhdpi":
                {
                    return 480;
                }
                case "xxxhdpi":
                {
                    return 640;
                }
                case "anydpi":
                {
                    return DensityAny;
                }
                case "nodpi":
                {
                    return DensityNone;
                }
            }

            if (part.EndsWith("dpi") && int.TryParse(part.AsSpan(0, part.Length - 3), out int dpi))
            {
                return dpi;
            }
        }

        return 0;
    }
}
