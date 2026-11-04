// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：ManifestXmlWriter.cs
// 说明：把 Manifest 模型输出为可读 XML（可带中文注解）
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using System.Xml;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 把 ManifestElement 树输出为可读的 XML 文本。
/// </summary>
public static class ManifestXmlWriter
{
    /// <param name="annotate">为 true 时在元素前插入中文注解（XML 注释）。</param>
    public static string Write(ManifestElement root, ResourceTable? resources, bool annotate = false)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            NewLineOnAttributes = true,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(false),
            CheckCharacters = false,
        };

        using (var sw = new Utf8StringWriter(sb))
        using (var writer = XmlWriter.Create(sw, settings))
        {
            writer.WriteStartDocument();
            var prefixes = new Dictionary<string, string>();
            var annotator = annotate ? new ManifestAnnotator(root, resources) : null;
            if (annotator != null)
            {
                writer.WriteComment(SafeComment("由 ApkInfos 解码生成，注释为工具自动添加的中文注解，并非原始内容"));
            }

            WriteElement(writer, root, null, resources, prefixes, annotator);
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    private static void WriteElement(XmlWriter writer, ManifestElement element, ManifestElement? parent, ResourceTable? resources, Dictionary<string, string> prefixes, ManifestAnnotator? annotator)
    {
        string? note = annotator?.Annotate(element, parent);
        if (!string.IsNullOrWhiteSpace(note))
        {
            writer.WriteComment(SafeComment(note));
        }

        // 记录本层声明的命名空间前缀，子元素继承
        var scope = new Dictionary<string, string>(prefixes);
        foreach (var ns in element.Namespaces)
        {
            scope[ns.Value] = ns.Key;
        }

        // 若属性用到 android 命名空间但没有声明（极少数加固包），补上声明
        if (!scope.ContainsKey(AndroidAttrs.AndroidNs) && element.Attributes.Any(a => a.NamespaceUri == AndroidAttrs.AndroidNs))
        {
            scope[AndroidAttrs.AndroidNs] = "android";
            element.Namespaces.Add(new KeyValuePair<string, string>("android", AndroidAttrs.AndroidNs));
        }

        writer.WriteStartElement(SafeName(element.Name));
        foreach (var ns in element.Namespaces)
        {
            if (string.IsNullOrEmpty(ns.Key))
            {
                continue;
            }

            writer.WriteAttributeString("xmlns", SafeName(ns.Key), null, ns.Value);
        }

        foreach (var attr in element.Attributes)
        {
            string value = FormatValue(attr, resources);
            string name = SafeName(attr.Name);
            if (!string.IsNullOrEmpty(attr.NamespaceUri) && scope.TryGetValue(attr.NamespaceUri, out var prefix) && !string.IsNullOrEmpty(prefix))
            {
                writer.WriteAttributeString(SafeName(prefix), name, attr.NamespaceUri, value);
            }
            else
            {
                writer.WriteAttributeString(name, value);
            }
        }

        foreach (var child in element.Children)
        {
            WriteElement(writer, child, element, resources, scope, annotator);
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// 把属性值格式化为 aapt 风格的文本。
    /// </summary>
    public static string FormatValue(ManifestAttribute attr, ResourceTable? resources)
    {
        switch (attr.Kind)
        {
            case AttrKind.String:
            {
                return attr.StringValue ?? attr.DisplayValue ?? "";
            }
            case AttrKind.Reference:
            {
                return FormatReference("@", attr.Data, resources, attr.DisplayValue);
            }
            case AttrKind.Attribute:
            {
                return FormatReference("?", attr.Data, resources, attr.DisplayValue);
            }
        }

        if (!string.IsNullOrEmpty(attr.DisplayValue))
        {
            return attr.DisplayValue;
        }

        uint data = attr.Data;
        return attr.Kind switch
        {
            AttrKind.Null => "",
            AttrKind.IntDec => unchecked((int)data).ToString(CultureInfo.InvariantCulture),
            AttrKind.IntHex => $"0x{data:x8}",
            AttrKind.Bool => data != 0 ? "true" : "false",
            AttrKind.Float => BitConverter.Int32BitsToSingle(unchecked((int)data)).ToString(CultureInfo.InvariantCulture),
            AttrKind.Dimension => ComplexToFloat(data).ToString(CultureInfo.InvariantCulture) + DimensionUnit(data),
            AttrKind.Fraction => (ComplexToFloat(data) * 100).ToString(CultureInfo.InvariantCulture) + ((data & 0xF) == 0 ? "%" : "%p"),
            AttrKind.Color => $"#{data:x8}",
            _ => $"0x{data:x8}",
        };
    }

    private static string FormatReference(string prefix, uint id, ResourceTable? resources, string? display)
    {
        if (!string.IsNullOrEmpty(display) && (display.StartsWith('@') || display.StartsWith('?')))
        {
            return display;
        }

        if (id == 0)
        {
            return prefix + "null";
        }

        string? name = resources?.GetName(id);
        if (name != null)
        {
            return prefix + name;
        }

        if ((id >> 24) == 0x01)
        {
            return $"{prefix}android:0x{id:x8}";
        }

        return $"{prefix}0x{id:x8}";
    }

    private static float ComplexToFloat(uint data)
    {
        float[] radixMults =
        {
            1.0f / (1 << 8),
            1.0f / (1 << 15),
            1.0f / (1 << 23),
            1.0f / (1L << 31),
        };
        int mantissa = unchecked((int)(data & 0xFFFFFF00));
        return mantissa * radixMults[(data >> 4) & 0x3];
    }

    private static string DimensionUnit(uint data)
    {
        return (data & 0xF) switch
        {
            0 => "px",
            1 => "dp",
            2 => "sp",
            3 => "pt",
            4 => "in",
            5 => "mm",
            _ => "",
        };
    }

    /// <summary>
    /// XML 注释中不允许出现 "--"，也不能以 "-" 结尾。
    /// </summary>
    private static string SafeComment(string text)
    {
        string s = text.Replace("--", "- -");
        return " " + s + " ";
    }

    private static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        try
        {
            XmlConvert.VerifyName(name);
            return name;
        }
        catch (XmlException)
        {
            return XmlConvert.EncodeLocalName(name) ?? "_";
        }
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder sb)
            : base(sb, CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
