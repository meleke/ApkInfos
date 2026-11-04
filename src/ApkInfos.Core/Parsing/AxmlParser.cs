using System.Buffers.Binary;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 解析 APK 中的二进制 XML（AXML），如 AndroidManifest.xml、res/**/*.xml。
/// </summary>
public static class AxmlParser
{
    private const ushort ResXmlType = 0x0003;
    private const ushort ResStringPoolType = 0x0001;
    private const ushort ResXmlResourceMapType = 0x0180;
    private const ushort ResXmlStartNamespaceType = 0x0100;
    private const ushort ResXmlEndNamespaceType = 0x0101;
    private const ushort ResXmlStartElementType = 0x0102;
    private const ushort ResXmlEndElementType = 0x0103;

    private const uint NoIndex = 0xFFFFFFFF;

    /// <summary>
    /// 判断字节流是否为二进制 XML。
    /// </summary>
    public static bool IsAxml(byte[] data)
    {
        return data.Length >= 8 && BinaryPrimitives.ReadUInt16LittleEndian(data) == ResXmlType;
    }

    /// <summary>
    /// 解析为统一的 ManifestElement 树，返回根元素。
    /// </summary>
    public static ManifestElement Parse(byte[] data)
    {
        if (data.Length < 8)
        {
            throw new InvalidDataException("AXML 数据过短");
        }

        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        int pos = headerSize >= 8 && headerSize < data.Length ? headerSize : 8;
        int end = data.Length;

        StringPool? pool = null;
        uint[] resourceIds = Array.Empty<uint>();
        var pendingNs = new List<KeyValuePair<string, string>>();
        var stack = new Stack<ManifestElement>();
        ManifestElement? root = null;

        while (pos + 8 <= end)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
            int chunkHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2));
            int chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            if (chunkSize < 8 || pos + chunkSize > end)
            {
                // 部分加固包会篡改 chunk 大小，此处尽量容错
                if (chunkSize < 8)
                {
                    break;
                }

                chunkSize = end - pos;
            }

            switch (type)
            {
                case ResStringPoolType:
                {
                    pool = new StringPool(data, pos);
                    break;
                }
                case ResXmlResourceMapType:
                {
                    int count = (chunkSize - chunkHeaderSize) / 4;
                    resourceIds = new uint[Math.Max(0, count)];
                    for (int i = 0; i < resourceIds.Length; i++)
                    {
                        resourceIds[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + chunkHeaderSize + i * 4));
                    }

                    break;
                }
                case ResXmlStartNamespaceType:
                {
                    // ResXMLTree_node 头固定 16 字节，扩展部分从 pos+16 开始
                    int ext = pos + 16;
                    string prefix = Str(pool, ReadU32(data, ext)) ?? "";
                    string uri = Str(pool, ReadU32(data, ext + 4)) ?? "";
                    pendingNs.Add(new KeyValuePair<string, string>(prefix, uri));
                    break;
                }
                case ResXmlStartElementType:
                {
                    var element = ParseStartElement(data, pos, pool, resourceIds);
                    element.Namespaces.AddRange(pendingNs);
                    pendingNs.Clear();
                    if (stack.Count > 0)
                    {
                        stack.Peek().Children.Add(element);
                    }
                    else if (root == null)
                    {
                        root = element;
                    }

                    stack.Push(element);
                    break;
                }
                case ResXmlEndElementType:
                {
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    break;
                }
                case ResXmlEndNamespaceType:
                default:
                {
                    break;
                }
            }

            pos += chunkSize;
        }

        return root ?? throw new InvalidDataException("AXML 中未找到根元素");
    }

    private static ManifestElement ParseStartElement(byte[] data, int pos, StringPool? pool, uint[] resourceIds)
    {
        int ext = pos + 16;
        uint nsIndex = ReadU32(data, ext);
        uint nameIndex = ReadU32(data, ext + 4);
        int attributeStart = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ext + 8));
        int attributeSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ext + 10));
        int attributeCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(ext + 12));
        if (attributeSize < 20)
        {
            attributeSize = 20;
        }

        var element = new ManifestElement
        {
            Name = Str(pool, nameIndex) ?? "",
            NamespaceUri = Str(pool, nsIndex) ?? "",
        };

        int attrPos = ext + attributeStart;
        for (int i = 0; i < attributeCount; i++)
        {
            int a = attrPos + i * attributeSize;
            if (a + 20 > data.Length)
            {
                break;
            }

            uint attrNs = ReadU32(data, a);
            uint attrName = ReadU32(data, a + 4);
            uint rawValue = ReadU32(data, a + 8);
            byte dataType = data[a + 15];
            uint valueData = ReadU32(data, a + 16);

            uint resId = attrName < resourceIds.Length ? resourceIds[attrName] : 0;
            string? name = Str(pool, attrName);
            if (string.IsNullOrWhiteSpace(name) || !IsValidName(name))
            {
                name = AndroidAttrs.NameOf(resId) ?? (resId != 0 ? $"attr_0x{resId:x8}" : $"attr_{i}");
            }

            var attr = new ManifestAttribute
            {
                NamespaceUri = Str(pool, attrNs) ?? "",
                Name = name,
                ResourceId = resId,
                Data = valueData,
                Kind = MapType(dataType),
            };

            if (attr.Kind == AttrKind.String)
            {
                attr.StringValue = Str(pool, valueData) ?? Str(pool, rawValue) ?? "";
            }
            else if (rawValue != NoIndex)
            {
                attr.StringValue = Str(pool, rawValue);
            }

            element.Attributes.Add(attr);
        }

        return element;
    }

    /// <summary>
    /// Res_value.dataType → AttrKind。
    /// </summary>
    public static AttrKind MapType(byte dataType)
    {
        return dataType switch
        {
            0x00 => AttrKind.Null,
            0x01 => AttrKind.Reference,
            0x02 => AttrKind.Attribute,
            0x03 => AttrKind.String,
            0x04 => AttrKind.Float,
            0x05 => AttrKind.Dimension,
            0x06 => AttrKind.Fraction,
            0x07 => AttrKind.Reference,
            0x08 => AttrKind.Attribute,
            0x10 => AttrKind.IntDec,
            0x11 => AttrKind.IntHex,
            0x12 => AttrKind.Bool,
            >= 0x1c and <= 0x1f => AttrKind.Color,
            _ => AttrKind.Other,
        };
    }

    private static bool IsValidName(string name)
    {
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':'))
            {
                return false;
            }
        }

        return !char.IsDigit(name[0]);
    }

    private static uint ReadU32(byte[] data, int pos)
    {
        if (pos < 0 || pos + 4 > data.Length)
        {
            return NoIndex;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
    }

    private static string? Str(StringPool? pool, uint index)
    {
        if (pool == null || index == NoIndex)
        {
            return null;
        }

        return pool.Get(index);
    }
}
