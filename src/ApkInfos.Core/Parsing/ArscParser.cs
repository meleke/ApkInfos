using System.Buffers.Binary;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 解析 APK 中的 resources.arsc，生成 ResourceTable。
/// 仅提取 label / icon 所需的简单值（字符串、引用、文件路径），复杂值（style 等）只记录名称。
/// </summary>
public static class ArscParser
{
    private const ushort ResStringPoolType = 0x0001;
    private const ushort ResTableType = 0x0002;
    private const ushort ResTablePackageType = 0x0200;
    private const ushort ResTableTypeType = 0x0201;

    private const ushort FlagComplex = 0x0001;
    private const ushort FlagCompact = 0x0008;
    private const byte TypeFlagSparse = 0x01;
    private const byte TypeFlagOffset16 = 0x02;
    private const uint NoEntry = 0xFFFFFFFF;

    public static ResourceTable Parse(byte[] data)
    {
        var table = new ResourceTable();
        if (data.Length < 12 || BinaryPrimitives.ReadUInt16LittleEndian(data) != ResTableType)
        {
            throw new InvalidDataException("不是有效的 resources.arsc");
        }

        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        int pos = headerSize;
        StringPool? globalPool = null;

        while (pos + 8 <= data.Length)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            if (size < 8 || pos + size > data.Length)
            {
                break;
            }

            if (type == ResStringPoolType && globalPool == null)
            {
                globalPool = new StringPool(data, pos);
            }
            else if (type == ResTablePackageType)
            {
                ParsePackage(data, pos, size, globalPool, table);
            }

            pos += size;
        }

        return table;
    }

    private static void ParsePackage(byte[] data, int start, int size, StringPool? globalPool, ResourceTable table)
    {
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + 2));
        uint packageId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 8));
        // id(4) + name(256) 之后依次为 typeStrings、lastPublicType、keyStrings、lastPublicKey
        uint typeStringsOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 12 + 256));
        uint keyStringsOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 12 + 256 + 8));

        int end = start + size;
        StringPool? typePool = null;
        StringPool? keyPool = null;
        if (typeStringsOffset > 0 && typeStringsOffset < size)
        {
            typePool = new StringPool(data, start + (int)typeStringsOffset);
        }

        if (keyStringsOffset > 0 && keyStringsOffset < size)
        {
            keyPool = new StringPool(data, start + (int)keyStringsOffset);
        }

        int pos = start + headerSize;
        while (pos + 8 <= end)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
            int chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            if (chunkSize < 8 || pos + chunkSize > end)
            {
                break;
            }

            if (type == ResStringPoolType)
            {
                // 偏移字段缺失时按出现顺序：先类型名池，后键名池
                if (typePool == null)
                {
                    typePool = new StringPool(data, pos);
                }
                else if (keyPool == null && typeStringsOffset == 0)
                {
                    keyPool = new StringPool(data, pos);
                }
            }
            else if (type == ResTableTypeType)
            {
                try
                {
                    ParseType(data, pos, chunkSize, packageId, globalPool, typePool, keyPool, table);
                }
                catch (Exception)
                {
                    // 单个类型块损坏不影响整体
                }
            }

            pos += chunkSize;
        }
    }

    private static void ParseType(
        byte[] data,
        int start,
        int size,
        uint packageId,
        StringPool? globalPool,
        StringPool? typePool,
        StringPool? keyPool,
        ResourceTable table)
    {
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + 2));
        byte typeId = data[start + 8];
        byte flags = data[start + 9];
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 12));
        uint entriesStart = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 16));

        // ResTable_config 从 start+20 开始
        int config = start + 20;
        string locale = ReadLocale(data, config);
        int density = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(config + 14));

        string typeName = typePool?.Get((uint)(typeId - 1)) ?? $"type{typeId}";
        int offsetsPos = start + headerSize;
        int end = start + size;
        bool sparse = (flags & TypeFlagSparse) != 0;
        bool offset16 = (flags & TypeFlagOffset16) != 0;

        for (int i = 0; i < entryCount; i++)
        {
            int entryIndex;
            uint offset;
            if (sparse)
            {
                int p = offsetsPos + i * 4;
                entryIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
                offset = (uint)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 2)) * 4;
            }
            else if (offset16)
            {
                entryIndex = i;
                ushort o = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offsetsPos + i * 2));
                offset = o == 0xFFFF ? NoEntry : (uint)o * 4;
            }
            else
            {
                entryIndex = i;
                offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetsPos + i * 4));
            }

            if (offset == NoEntry)
            {
                continue;
            }

            int e = start + (int)entriesStart + (int)offset;
            if (e + 8 > end)
            {
                continue;
            }

            uint resId = (packageId << 24) | ((uint)typeId << 16) | (uint)entryIndex;
            ushort entryFlags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(e + 2));

            uint keyIndex;
            byte valueType;
            uint valueData;
            bool hasValue;

            if ((entryFlags & FlagCompact) != 0)
            {
                // 紧凑条目：key(u16) flags(u16，高 8 位为 dataType) data(u32)
                keyIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(e));
                valueType = (byte)(entryFlags >> 8);
                valueData = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e + 4));
                hasValue = true;
            }
            else
            {
                int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(e));
                keyIndex = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e + 4));
                hasValue = (entryFlags & FlagComplex) == 0 && e + entrySize + 8 <= end;
                valueType = hasValue ? data[e + entrySize + 3] : (byte)0;
                valueData = hasValue ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(e + entrySize + 4)) : 0;
            }

            string keyName = keyPool?.Get(keyIndex) ?? $"0x{resId:x8}";
            var entry = table.GetOrAdd(resId, typeName, keyName);
            if (!hasValue)
            {
                continue;
            }

            var value = new ResValue
            {
                Locale = locale,
                Density = density,
                Data = valueData,
            };

            switch (valueType)
            {
                case 0x03:
                {
                    value.Kind = ResValueKind.String;
                    value.Text = globalPool?.Get(valueData);
                    break;
                }
                case 0x01:
                case 0x07:
                {
                    value.Kind = ResValueKind.Reference;
                    break;
                }
                default:
                {
                    value.Kind = ResValueKind.Other;
                    break;
                }
            }

            entry.Values.Add(value);
        }
    }

    /// <summary>
    /// 读取 ResTable_config 中的 language / country（仅处理 2 字母编码）。
    /// </summary>
    private static string ReadLocale(byte[] data, int config)
    {
        byte l0 = data[config + 8];
        byte l1 = data[config + 9];
        byte c0 = data[config + 10];
        byte c1 = data[config + 11];
        if (l0 == 0 || (l0 & 0x80) != 0)
        {
            return "";
        }

        string lang = $"{(char)l0}{(char)l1}";
        if (c0 == 0 || (c0 & 0x80) != 0)
        {
            return lang;
        }

        return $"{lang}-{(char)c0}{(char)c1}";
    }
}
