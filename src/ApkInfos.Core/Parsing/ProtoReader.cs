using System.Buffers.Binary;
using System.Text;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 极简 protobuf wire-format 读取器，用于解析 AAB 中 aapt2 生成的 proto 文件。
/// </summary>
public ref struct ProtoReader
{
    public const int WireVarint = 0;
    public const int WireFixed64 = 1;
    public const int WireLengthDelimited = 2;
    public const int WireStartGroup = 3;
    public const int WireEndGroup = 4;
    public const int WireFixed32 = 5;

    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public ProtoReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public bool IsEnd => _pos >= _data.Length;

    /// <summary>
    /// 读取下一个字段标签；到达末尾返回 false。
    /// </summary>
    public bool TryReadTag(out int field, out int wireType)
    {
        if (IsEnd)
        {
            field = 0;
            wireType = 0;
            return false;
        }

        ulong tag = ReadVarint();
        field = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        return true;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (_pos >= _data.Length)
            {
                throw new InvalidDataException("protobuf varint 越界");
            }

            byte b = _data[_pos];
            _pos++;
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 63)
            {
                throw new InvalidDataException("protobuf varint 过长");
            }
        }
    }

    public uint ReadFixed32()
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_pos, 4));
        _pos += 4;
        return value;
    }

    public ulong ReadFixed64()
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_pos, 8));
        _pos += 8;
        return value;
    }

    /// <summary>
    /// 读取 length-delimited 字段的原始字节（子消息、字符串、bytes）。
    /// </summary>
    public ReadOnlySpan<byte> ReadBytes()
    {
        int len = (int)ReadVarint();
        if (len < 0 || _pos + len > _data.Length)
        {
            throw new InvalidDataException("protobuf 长度越界");
        }

        var slice = _data.Slice(_pos, len);
        _pos += len;
        return slice;
    }

    public string ReadString()
    {
        return Encoding.UTF8.GetString(ReadBytes());
    }

    /// <summary>
    /// 跳过当前字段的值。
    /// </summary>
    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case WireVarint:
            {
                ReadVarint();
                break;
            }
            case WireFixed64:
            {
                _pos += 8;
                break;
            }
            case WireLengthDelimited:
            {
                ReadBytes();
                break;
            }
            case WireFixed32:
            {
                _pos += 4;
                break;
            }
            case WireStartGroup:
            case WireEndGroup:
            default:
            {
                throw new InvalidDataException($"不支持的 protobuf wire type: {wireType}");
            }
        }
    }
}
