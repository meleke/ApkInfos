// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：ProtoResourceParser.cs
// 说明：解析 AAB 的 resources.pb 资源表
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 解析 AAB 中的 base/resources.pb（aapt2 ResourceTable 的 protobuf 格式）。
/// </summary>
public static class ProtoResourceParser
{
    // message ResourceTable { StringPool source_pool = 1; repeated Package package = 2; ... }
    public static ResourceTable Parse(byte[] data)
    {
        var table = new ResourceTable();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 2 && wire == ProtoReader.WireLengthDelimited)
            {
                ParsePackage(reader.ReadBytes(), table);
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return table;
    }

    // message Package { PackageId package_id = 1; string package_name = 2; repeated Type type = 3; }
    private static void ParsePackage(ReadOnlySpan<byte> data, ResourceTable table)
    {
        uint packageId = 0x7f;
        var types = new List<byte[]>();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                packageId = ReadIdMessage(reader.ReadBytes());
            }
            else if (field == 3 && wire == ProtoReader.WireLengthDelimited)
            {
                types.Add(reader.ReadBytes().ToArray());
            }
            else
            {
                reader.Skip(wire);
            }
        }

        foreach (var type in types)
        {
            ParseType(type, packageId, table);
        }
    }

    // message Type { TypeId type_id = 1; string name = 2; repeated Entry entry = 3; }
    private static void ParseType(ReadOnlySpan<byte> data, uint packageId, ResourceTable table)
    {
        uint typeId = 0;
        string name = "";
        var entries = new List<byte[]>();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                typeId = ReadIdMessage(reader.ReadBytes());
            }
            else if (field == 2 && wire == ProtoReader.WireLengthDelimited)
            {
                name = reader.ReadString();
            }
            else if (field == 3 && wire == ProtoReader.WireLengthDelimited)
            {
                entries.Add(reader.ReadBytes().ToArray());
            }
            else
            {
                reader.Skip(wire);
            }
        }

        foreach (var entry in entries)
        {
            ParseEntry(entry, packageId, typeId, name, table);
        }
    }

    // message Entry { EntryId entry_id = 1; string name = 2; ... repeated ConfigValue config_value = 6; }
    private static void ParseEntry(ReadOnlySpan<byte> data, uint packageId, uint typeId, string typeName, ResourceTable table)
    {
        uint entryId = 0;
        string name = "";
        var configValues = new List<byte[]>();
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                entryId = ReadIdMessage(reader.ReadBytes());
            }
            else if (field == 2 && wire == ProtoReader.WireLengthDelimited)
            {
                name = reader.ReadString();
            }
            else if (field == 6 && wire == ProtoReader.WireLengthDelimited)
            {
                configValues.Add(reader.ReadBytes().ToArray());
            }
            else
            {
                reader.Skip(wire);
            }
        }

        uint resId = (packageId << 24) | (typeId << 16) | entryId;
        var resEntry = table.GetOrAdd(resId, typeName, name);
        foreach (var cv in configValues)
        {
            var value = ParseConfigValue(cv);
            if (value != null)
            {
                resEntry.Values.Add(value);
            }
        }
    }

    // message ConfigValue { Configuration config = 1; Value value = 2; }
    private static ResValue? ParseConfigValue(ReadOnlySpan<byte> data)
    {
        string locale = "";
        ResValue? value = null;
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireLengthDelimited)
            {
                locale = ReadLocale(reader.ReadBytes());
            }
            else if (field == 2 && wire == ProtoReader.WireLengthDelimited)
            {
                value = ParseValue(reader.ReadBytes());
            }
            else
            {
                reader.Skip(wire);
            }
        }

        if (value != null)
        {
            value.Locale = locale;
            if (value.Kind == ResValueKind.File && value.Text != null)
            {
                value.Density = ResourceTable.DensityFromPath(value.Text);
            }
        }

        return value;
    }

    // message Value { Source source = 1; string comment = 2; bool weak = 3; oneof { Item item = 4; CompoundValue compound_value = 5; } }
    private static ResValue? ParseValue(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 4 && wire == ProtoReader.WireLengthDelimited)
            {
                var item = ProtoItem.Parse(reader.ReadBytes());
                return item.Kind switch
                {
                    ProtoItem.ItemKind.String => new ResValue
                    {
                        Kind = ResValueKind.String,
                        Text = item.Text,
                    },
                    ProtoItem.ItemKind.File => new ResValue
                    {
                        Kind = ResValueKind.File,
                        Text = item.Text,
                    },
                    ProtoItem.ItemKind.Reference => new ResValue
                    {
                        Kind = ResValueKind.Reference,
                        Data = item.Data,
                    },
                    _ => new ResValue
                    {
                        Kind = ResValueKind.Other,
                        Data = item.Data,
                    },
                };
            }

            reader.Skip(wire);
        }

        return null;
    }

    // message Configuration { uint32 mcc = 1; uint32 mnc = 2; string locale = 3; ... }
    private static string ReadLocale(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 3 && wire == ProtoReader.WireLengthDelimited)
            {
                return reader.ReadString();
            }

            reader.Skip(wire);
        }

        return "";
    }

    // PackageId / TypeId / EntryId 都是 { uint32 id = 1; }
    private static uint ReadIdMessage(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        while (reader.TryReadTag(out int field, out int wire))
        {
            if (field == 1 && wire == ProtoReader.WireVarint)
            {
                return (uint)reader.ReadVarint();
            }

            reader.Skip(wire);
        }

        return 0;
    }
}
