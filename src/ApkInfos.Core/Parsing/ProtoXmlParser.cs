using System.Globalization;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 解析 AAB 中 proto 格式的 XML（aapt2 Resources.proto 中的 XmlNode），
/// 如 base/manifest/AndroidManifest.xml、base/res/**/*.xml。
/// </summary>
public static class ProtoXmlParser
{
    public static ManifestElement Parse(byte[] data)
    {
        var root = ParseNode(data);
        return root ?? throw new InvalidDataException("proto XML 中未找到根元素");
    }

    // message XmlNode { oneof node { XmlElement element = 1; string text = 2; } SourcePosition source = 3; }
    private static ManifestElement? ParseNode(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        ManifestElement? element = null;
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                element = ParseElement(reader.ReadBytes());
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return element;
    }

    // message XmlElement { repeated XmlNamespace namespace_declaration = 1; string namespace_uri = 2;
    //   string name = 3; repeated XmlAttribute attribute = 4; repeated XmlNode child = 5; }
    private static ManifestElement ParseElement(ReadOnlySpan<byte> data)
    {
        var element = new ManifestElement();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (wire != ProtoReader.WireLengthDelimited)
            {
                reader.Skip(wire);
                continue;
            }

            switch (field)
            {
                case 1:
                {
                    element.Namespaces.Add(ParseNamespace(reader.ReadBytes()));
                    break;
                }
                case 2:
                {
                    element.NamespaceUri = reader.ReadString();
                    break;
                }
                case 3:
                {
                    element.Name = reader.ReadString();
                    break;
                }
                case 4:
                {
                    element.Attributes.Add(ParseAttribute(reader.ReadBytes()));
                    break;
                }
                case 5:
                {
                    var child = ParseNode(reader.ReadBytes());
                    if (child != null)
                    {
                        element.Children.Add(child);
                    }

                    break;
                }
                default:
                {
                    reader.Skip(wire);
                    break;
                }
            }
        }

        return element;
    }

    // message XmlNamespace { string prefix = 1; string uri = 2; SourcePosition source = 3; }
    private static KeyValuePair<string, string> ParseNamespace(ReadOnlySpan<byte> data)
    {
        string prefix = "";
        string uri = "";
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                prefix = reader.ReadString();
            }
            else if (field == 2 && wire == ProtoReader.WireLengthDelimited)
            {
                uri = reader.ReadString();
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return new KeyValuePair<string, string>(prefix, uri);
    }

    // message XmlAttribute { string namespace_uri = 1; string name = 2; string value = 3;
    //   SourcePosition source = 4; uint32 resource_id = 5; Item compiled_item = 6; }
    private static ManifestAttribute ParseAttribute(ReadOnlySpan<byte> data)
    {
        var attr = new ManifestAttribute();
        string? value = null;
        bool hasItem = false;
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoReader.WireLengthDelimited:
                {
                    attr.NamespaceUri = reader.ReadString();
                    break;
                }
                case 2 when wire == ProtoReader.WireLengthDelimited:
                {
                    attr.Name = reader.ReadString();
                    break;
                }
                case 3 when wire == ProtoReader.WireLengthDelimited:
                {
                    value = reader.ReadString();
                    break;
                }
                case 5 when wire == ProtoReader.WireVarint:
                {
                    attr.ResourceId = (uint)reader.ReadVarint();
                    break;
                }
                case 6 when wire == ProtoReader.WireLengthDelimited:
                {
                    hasItem = ProtoItem.Fill(reader.ReadBytes(), attr);
                    break;
                }
                default:
                {
                    reader.Skip(wire);
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(attr.Name))
        {
            attr.Name = AndroidAttrs.NameOf(attr.ResourceId) ?? $"attr_0x{attr.ResourceId:x8}";
        }

        attr.DisplayValue = value;
        if (!hasItem)
        {
            attr.Kind = AttrKind.String;
            attr.StringValue = value ?? "";
        }

        return attr;
    }
}

/// <summary>
/// 解析 aapt2 的 Item 消息（属性值与资源值共用）。
/// </summary>
internal static class ProtoItem
{
    /// <summary>
    /// message Item { oneof value { Reference ref = 1; String str = 2; RawString raw_str = 3;
    ///   StyledString styled_str = 4; FileReference file = 5; Id id = 6; Primitive prim = 7; } }
    /// 返回 true 表示识别到了值。
    /// </summary>
    public static bool Fill(ReadOnlySpan<byte> data, ManifestAttribute attr)
    {
        var item = Parse(data);
        if (item.Kind == ItemKind.None)
        {
            return false;
        }

        switch (item.Kind)
        {
            case ItemKind.Reference:
            {
                attr.Kind = item.IsAttribute ? AttrKind.Attribute : AttrKind.Reference;
                attr.Data = item.Data;
                break;
            }
            case ItemKind.String:
            case ItemKind.File:
            {
                attr.Kind = AttrKind.String;
                attr.StringValue = item.Text ?? "";
                break;
            }
            case ItemKind.Primitive:
            {
                attr.Kind = item.PrimKind;
                attr.Data = item.Data;
                break;
            }
            default:
            {
                return false;
            }
        }

        return true;
    }

    public enum ItemKind
    {
        None,
        Reference,
        String,
        File,
        Primitive,
    }

    public struct Item
    {
        public ItemKind Kind;
        public bool IsAttribute;
        public uint Data;
        public string? Text;
        public AttrKind PrimKind;
    }

    public static Item Parse(ReadOnlySpan<byte> data)
    {
        var item = new Item();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (wire != ProtoReader.WireLengthDelimited)
            {
                reader.Skip(wire);
                continue;
            }

            var sub = reader.ReadBytes();
            switch (field)
            {
                case 1:
                {
                    ParseReference(sub, ref item);
                    break;
                }
                case 2:
                case 3:
                case 4:
                {
                    // String / RawString / StyledString 的 value 都是字段 1
                    item.Kind = ItemKind.String;
                    item.Text = ReadStringField1(sub);
                    break;
                }
                case 5:
                {
                    // FileReference { string path = 1; Type type = 2; }
                    item.Kind = ItemKind.File;
                    item.Text = ReadStringField1(sub);
                    break;
                }
                case 7:
                {
                    ParsePrimitive(sub, ref item);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        return item;
    }

    // message Reference { Type type = 1; uint32 id = 2; string name = 3; ... }
    private static void ParseReference(ReadOnlySpan<byte> data, ref Item item)
    {
        item.Kind = ItemKind.Reference;
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireVarint)
            {
                item.IsAttribute = reader.ReadVarint() == 1;
            }
            else if (field == 2 && wire == ProtoReader.WireVarint)
            {
                item.Data = (uint)reader.ReadVarint();
            }
            else if (field == 3 && wire == ProtoReader.WireLengthDelimited)
            {
                item.Text = reader.ReadString();
            }
            else
            {
                reader.Skip(wire);
            }
        }
    }

    // message Primitive { oneof { NullType null_value = 1; EmptyType empty_value = 2; float float_value = 3;
    //   uint32 dimension_value = 13; uint32 fraction_value = 14; int32 int_decimal_value = 6;
    //   uint32 int_hexadecimal_value = 7; bool boolean_value = 8; uint32 color_argb8_value = 9;
    //   uint32 color_rgb8_value = 10; uint32 color_argb4_value = 11; uint32 color_rgb4_value = 12; } }
    private static void ParsePrimitive(ReadOnlySpan<byte> data, ref Item item)
    {
        item.Kind = ItemKind.Primitive;
        item.PrimKind = AttrKind.Null;
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            uint raw;
            if (wire == ProtoReader.WireVarint)
            {
                raw = (uint)reader.ReadVarint();
            }
            else if (wire == ProtoReader.WireFixed32)
            {
                raw = reader.ReadFixed32();
            }
            else
            {
                reader.Skip(wire);
                continue;
            }

            item.Data = raw;
            item.PrimKind = field switch
            {
                3 => AttrKind.Float,
                6 => AttrKind.IntDec,
                7 => AttrKind.IntHex,
                8 => AttrKind.Bool,
                9 or 10 or 11 or 12 => AttrKind.Color,
                13 => AttrKind.Dimension,
                14 => AttrKind.Fraction,
                _ => AttrKind.Other,
            };
        }
    }

    private static string? ReadStringField1(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                return reader.ReadString();
            }

            reader.Skip(wire);
        }

        return null;
    }

    public static string FormatFloat(uint raw)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)raw)).ToString(CultureInfo.InvariantCulture);
    }
}
