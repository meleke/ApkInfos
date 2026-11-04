using System.Buffers.Binary;
using System.Text;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// Android 二进制资源格式中的 ResStringPool（AXML 与 resources.arsc 共用）。
/// </summary>
public sealed class StringPool
{
    private const uint Utf8Flag = 1 << 8;

    private readonly byte[] _data;
    private readonly int _stringsStart;
    private readonly int _chunkEnd;
    private readonly uint[] _offsets;
    private readonly bool _utf8;
    private readonly string?[] _cache;

    public int Count => _offsets.Length;

    /// <summary>
    /// 从 chunk 起始位置解析字符串池（不立即解码，按需解码并缓存）。
    /// </summary>
    public StringPool(byte[] data, int chunkStart)
    {
        _data = data;
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(chunkStart + 2));
        uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(chunkStart + 4));
        uint stringCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(chunkStart + 8));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(chunkStart + 16));
        uint stringsStart = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(chunkStart + 20));

        _chunkEnd = (int)Math.Min((long)chunkStart + chunkSize, data.Length);
        _utf8 = (flags & Utf8Flag) != 0;
        _stringsStart = chunkStart + (int)stringsStart;

        int offsetsStart = chunkStart + headerSize;
        long maxCount = Math.Max(0, (_chunkEnd - offsetsStart) / 4);
        int count = (int)Math.Min(stringCount, maxCount);
        _offsets = new uint[count];
        for (int i = 0; i < count; i++)
        {
            _offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetsStart + i * 4));
        }

        _cache = new string?[count];
    }

    /// <summary>
    /// 取第 index 个字符串；越界或损坏返回 null。
    /// </summary>
    public string? Get(uint index)
    {
        if (index >= _offsets.Length)
        {
            return null;
        }

        var cached = _cache[index];
        if (cached != null)
        {
            return cached;
        }

        try
        {
            int pos = _stringsStart + (int)_offsets[index];
            string value = _utf8 ? DecodeUtf8(pos) : DecodeUtf16(pos);
            _cache[index] = value;
            return value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string DecodeUtf8(int pos)
    {
        // 先是 UTF-16 字符数，再是 UTF-8 字节数，均为 1~2 字节变长
        ReadLength8(ref pos);
        int byteLen = ReadLength8(ref pos);
        int len = Math.Min(byteLen, _chunkEnd - pos);
        return Encoding.UTF8.GetString(_data, pos, Math.Max(0, len));
    }

    private string DecodeUtf16(int pos)
    {
        int charLen = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(pos));
        pos += 2;
        if ((charLen & 0x8000) != 0)
        {
            int low = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(pos));
            pos += 2;
            charLen = ((charLen & 0x7FFF) << 16) | low;
        }

        int byteLen = Math.Min(charLen * 2, _chunkEnd - pos);
        return Encoding.Unicode.GetString(_data, pos, Math.Max(0, byteLen));
    }

    private int ReadLength8(ref int pos)
    {
        int len = _data[pos];
        pos++;
        if ((len & 0x80) != 0)
        {
            len = ((len & 0x7F) << 8) | _data[pos];
            pos++;
        }

        return len;
    }
}
