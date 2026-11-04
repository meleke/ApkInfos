using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;
using ApkInfos.Core.Models;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 读取 APK / AAB 的签名证书：
/// v1（JAR 签名，META-INF/*.RSA|DSA|EC）以及 v2 / v3 / v3.1（APK Signing Block）。
/// </summary>
public static class ApkSignatureReader
{
    private const uint V2BlockId = 0x7109871a;
    private const uint V3BlockId = 0xf05368c0;
    private const uint V31BlockId = 0x1b93ad61;
    private const uint EocdSignature = 0x06054b50;
    private static readonly byte[] SigBlockMagic = Encoding.ASCII.GetBytes("APK Sig Block 42");
    private static readonly Regex V1SignatureFile = new(@"^META-INF/[^/]+\.(RSA|DSA|EC)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed class Result
    {
        public List<string> Schemes { get; } = new();

        /// <summary>按 v3.1 → v3 → v2 → v1 顺序排列。</summary>
        public List<CertInfo> Certificates { get; } = new();

        public List<string> Warnings { get; } = new();
    }

    public static Result Read(string filePath, ZipArchive zip)
    {
        var result = new Result();
        var v1 = ReadV1(zip, result);

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var blocks = ReadSigningBlock(stream);
            if (blocks != null)
            {
                AddBlockCert(blocks, V31BlockId, "v3.1", result);
                AddBlockCert(blocks, V3BlockId, "v3", result);
                AddBlockCert(blocks, V2BlockId, "v2", result);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"读取 APK Signing Block 失败：{ex.Message}");
        }

        if (v1 != null)
        {
            result.Certificates.Add(v1);
        }

        // 方案按 v1, v2, v3, v3.1 顺序展示
        result.Schemes.Sort(CompareScheme);
        return result;
    }

    private static int CompareScheme(string a, string b)
    {
        string[] order = { "v1", "v2", "v3", "v3.1" };
        return Array.IndexOf(order, a).CompareTo(Array.IndexOf(order, b));
    }

    private static CertInfo? ReadV1(ZipArchive zip, Result result)
    {
        foreach (var entry in zip.Entries)
        {
            if (!V1SignatureFile.IsMatch(entry.FullName))
            {
                continue;
            }

            try
            {
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var cms = new SignedCms();
                cms.Decode(ms.ToArray());
                var cert = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
                cert ??= cms.Certificates.Count > 0 ? cms.Certificates[0] : null;
                if (cert != null)
                {
                    result.Schemes.Add("v1");
                    return CertInfo.FromDer(cert.RawData, "v1");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"解析 {entry.FullName} 失败：{ex.Message}");
            }
        }

        return null;
    }

    private static void AddBlockCert(Dictionary<uint, byte[]> blocks, uint id, string scheme, Result result)
    {
        if (!blocks.TryGetValue(id, out var value))
        {
            return;
        }

        result.Schemes.Add(scheme);
        try
        {
            var der = FirstCertificate(value);
            if (der != null)
            {
                result.Certificates.Add(CertInfo.FromDer(der, scheme));
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"解析 {scheme} 签名块失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 定位 ZIP 中央目录之前的 APK Signing Block，返回 ID → 值 的映射；不存在返回 null。
    /// </summary>
    public static Dictionary<uint, byte[]>? ReadSigningBlock(Stream stream)
    {
        long fileSize = stream.Length;
        if (fileSize < 22)
        {
            return null;
        }

        // 1. 从尾部查找 EOCD（注释最长 65535 字节）
        int tailLen = (int)Math.Min(fileSize, 22 + 65535);
        var tail = new byte[tailLen];
        stream.Seek(fileSize - tailLen, SeekOrigin.Begin);
        stream.ReadExactly(tail);

        int eocd = -1;
        for (int i = tailLen - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EocdSignature)
            {
                int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20));
                if (i + 22 + commentLen == tailLen)
                {
                    eocd = i;
                    break;
                }
            }
        }

        if (eocd < 0)
        {
            return null;
        }

        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
        if (cdOffset < 32 || cdOffset > fileSize)
        {
            return null;
        }

        // 2. 中央目录前 24 字节：u64 块大小 + 16 字节魔数
        var footer = new byte[24];
        stream.Seek(cdOffset - 24, SeekOrigin.Begin);
        stream.ReadExactly(footer);
        if (!footer.AsSpan(8, 16).SequenceEqual(SigBlockMagic))
        {
            return null;
        }

        long blockSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(footer);
        long blockStart = cdOffset - blockSize - 8;
        if (blockSize < 24 || blockStart < 0 || blockSize > int.MaxValue)
        {
            return null;
        }

        var block = new byte[blockSize + 8];
        stream.Seek(blockStart, SeekOrigin.Begin);
        stream.ReadExactly(block);

        // 3. 遍历 ID-值 对：u64 长度 + u32 ID + 值
        var map = new Dictionary<uint, byte[]>();
        int pos = 8;
        int pairsEnd = block.Length - 24;
        while (pos + 12 <= pairsEnd)
        {
            long len = (long)BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(pos));
            if (len < 4 || pos + 8 + len > pairsEnd)
            {
                break;
            }

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(pos + 8));
            map[id] = block.AsSpan(pos + 12, (int)len - 4).ToArray();
            pos += 8 + (int)len;
        }

        return map;
    }

    /// <summary>
    /// 从 v2/v3 签名块值中取第一个 signer 的第一张证书。
    /// v2 signer = signed data, signatures, public key；
    /// v3 signer = signed data, minSdk, maxSdk, signatures, public key；
    /// signed data = digests, certificates, ...
    /// </summary>
    private static byte[]? FirstCertificate(byte[] value)
    {
        var signers = LengthPrefixed(value, 0, out _);
        var signer = LengthPrefixed(signers, 0, out _);
        var signedData = LengthPrefixed(signer, 0, out _);
        LengthPrefixed(signedData, 0, out int afterDigests);
        var certificates = LengthPrefixed(signedData, afterDigests, out _);
        if (certificates.Length == 0)
        {
            return null;
        }

        return LengthPrefixed(certificates, 0, out _);
    }

    /// <summary>
    /// 读取 u32 长度前缀的数据段，next 返回其后的位置。
    /// </summary>
    private static byte[] LengthPrefixed(byte[] data, int pos, out int next)
    {
        if (pos + 4 > data.Length)
        {
            throw new InvalidDataException("签名块数据截断");
        }

        int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
        if (len < 0 || pos + 4 + len > data.Length)
        {
            throw new InvalidDataException("签名块长度非法");
        }

        next = pos + 4 + len;
        return data.AsSpan(pos + 4, len).ToArray();
    }
}
