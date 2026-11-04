// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：KeystoreReader.cs
// 说明：读取 JKS / JCEKS / PKCS12 Keystore 的 alias 与证书
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using ApkInfos.Core.Models;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// Keystore 密码错误。
/// </summary>
public sealed class KeystorePasswordException : Exception
{
    public KeystorePasswordException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 读取 JKS / JCEKS / PKCS12 格式的 keystore，列出 alias 及证书。
/// </summary>
public static class KeystoreReader
{
    private const uint JksMagic = 0xFEEDFEED;
    private const uint JceksMagic = 0xCECECECE;
    private const string FriendlyNameOid = "1.2.840.113549.1.9.20";
    private const string LocalKeyIdOid = "1.2.840.113549.1.9.21";

    /// <summary>
    /// 加载 keystore。password 为空时 JKS 跳过完整性校验（与 keytool 行为一致）。
    /// </summary>
    public static KeystoreInfo Load(string path, string? password)
    {
        var data = File.ReadAllBytes(path);
        return Load(data, password);
    }

    public static KeystoreInfo Load(byte[] data, string? password)
    {
        if (data.Length >= 4)
        {
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(data);
            if (magic == JksMagic)
            {
                return LoadJks(data, password, "JKS");
            }

            if (magic == JceksMagic)
            {
                return LoadJks(data, password, "JCEKS");
            }
        }

        return LoadPkcs12(data, password ?? "");
    }

    // ---------------- JKS / JCEKS ----------------

    private static KeystoreInfo LoadJks(byte[] data, string? password, string format)
    {
        int pos = 4;
        uint version = ReadU32(data, ref pos);
        if (version != 1 && version != 2)
        {
            throw new InvalidDataException($"不支持的 {format} 版本：{version}");
        }

        int count = (int)ReadU32(data, ref pos);
        var entries = new List<KeystoreEntry>();
        for (int i = 0; i < count; i++)
        {
            uint tag = ReadU32(data, ref pos);
            string alias = ReadUtf(data, ref pos);
            long millis = (long)ReadU64(data, ref pos);
            DateTime created = DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime;

            if (tag == 1)
            {
                // 私钥条目：加密私钥 + 证书链
                int keyLen = (int)ReadU32(data, ref pos);
                pos += keyLen;
                int chainLen = (int)ReadU32(data, ref pos);
                byte[]? first = null;
                for (int c = 0; c < chainLen; c++)
                {
                    var cert = ReadCert(data, ref pos, version);
                    first ??= cert;
                }

                entries.Add(new KeystoreEntry
                {
                    Alias = alias,
                    EntryType = "PrivateKeyEntry",
                    CreationDate = created,
                    ChainLength = chainLen,
                    Certificate = first != null ? CertInfo.FromDer(first, "keystore") : null,
                });
            }
            else if (tag == 2)
            {
                // 受信任证书条目
                var cert = ReadCert(data, ref pos, version);
                entries.Add(new KeystoreEntry
                {
                    Alias = alias,
                    EntryType = "trustedCertEntry",
                    CreationDate = created,
                    ChainLength = 1,
                    Certificate = CertInfo.FromDer(cert, "keystore"),
                });
            }
            else
            {
                // JCEKS 的密钥条目（tag 3）是 Java 序列化对象，无法安全跳过，停止解析
                throw new InvalidDataException($"{format} 中包含不支持的条目类型（{tag}），alias：{alias}");
            }
        }

        bool verified = false;
        if (!string.IsNullOrEmpty(password))
        {
            // 完整性校验：SHA1(密码 UTF-16BE + "Mighty Aphrodite" + 之前全部内容)
            if (pos + 20 > data.Length)
            {
                throw new InvalidDataException("keystore 数据截断");
            }

            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            sha1.AppendData(Encoding.BigEndianUnicode.GetBytes(password));
            sha1.AppendData(Encoding.UTF8.GetBytes("Mighty Aphrodite"));
            sha1.AppendData(data, 0, pos);
            var digest = sha1.GetHashAndReset();
            if (!digest.AsSpan().SequenceEqual(data.AsSpan(pos, 20)))
            {
                throw new KeystorePasswordException("Keystore 密码错误");
            }

            verified = true;
        }

        return new KeystoreInfo
        {
            Format = format,
            Entries = entries,
            IntegrityVerified = verified,
        };
    }

    private static byte[] ReadCert(byte[] data, ref int pos, uint version)
    {
        if (version == 2)
        {
            // 证书类型字符串，如 "X.509"
            ReadUtf(data, ref pos);
        }

        int len = (int)ReadU32(data, ref pos);
        Ensure(data, pos, len);
        var cert = data.AsSpan(pos, len).ToArray();
        pos += len;
        return cert;
    }

    private static uint ReadU32(byte[] data, ref int pos)
    {
        Ensure(data, pos, 4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
        pos += 4;
        return value;
    }

    private static ulong ReadU64(byte[] data, ref int pos)
    {
        Ensure(data, pos, 8);
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(pos));
        pos += 8;
        return value;
    }

    /// <summary>
    /// Java DataOutput.writeUTF：u16 长度 + Modified UTF-8（常规别名与标准 UTF-8 一致）。
    /// </summary>
    private static string ReadUtf(byte[] data, ref int pos)
    {
        Ensure(data, pos, 2);
        int len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
        pos += 2;
        Ensure(data, pos, len);
        string s = Encoding.UTF8.GetString(data, pos, len);
        pos += len;
        return s;
    }

    private static void Ensure(byte[] data, int pos, int len)
    {
        if (len < 0 || pos < 0 || pos + len > data.Length)
        {
            throw new InvalidDataException("keystore 数据损坏或格式不支持");
        }
    }

    // ---------------- PKCS12 ----------------

    private static KeystoreInfo LoadPkcs12(byte[] data, string password)
    {
        Pkcs12Info info;
        try
        {
            info = Pkcs12Info.Decode(data, out _, skipCopy: true);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("无法识别的 keystore 格式（支持 JKS / JCEKS / PKCS12）", ex);
        }

        if (info.IntegrityMode == Pkcs12IntegrityMode.Password && !info.VerifyMac(password))
        {
            throw new KeystorePasswordException("Keystore 密码错误");
        }

        // 证书包：localKeyId → (friendlyName, 证书)
        var certs = new List<(string? Name, byte[]? KeyId, byte[] Der)>();
        var keys = new List<(string? Name, byte[]? KeyId)>();

        foreach (var safe in info.AuthenticatedSafe)
        {
            if (safe.ConfidentialityMode == Pkcs12ConfidentialityMode.Password)
            {
                try
                {
                    safe.Decrypt(password);
                }
                catch (CryptographicException)
                {
                    throw new KeystorePasswordException("Keystore 密码错误");
                }
            }
            else if (safe.ConfidentialityMode != Pkcs12ConfidentialityMode.None)
            {
                continue;
            }

            foreach (var bag in safe.GetBags())
            {
                string? name = GetFriendlyName(bag);
                byte[]? keyId = GetLocalKeyId(bag);
                if (bag is Pkcs12CertBag certBag && certBag.IsX509Certificate)
                {
                    certs.Add((name, keyId, UnwrapCertificate(certBag.EncodedCertificate)));
                }
                else if (bag is Pkcs12ShroudedKeyBag || bag is Pkcs12KeyBag)
                {
                    keys.Add((name, keyId));
                }
            }
        }

        var entries = new List<KeystoreEntry>();
        var usedCerts = new HashSet<int>();
        foreach (var key in keys)
        {
            int idx = certs.FindIndex(c => key.KeyId != null && c.KeyId != null && c.KeyId.AsSpan().SequenceEqual(key.KeyId));
            if (idx < 0)
            {
                idx = certs.FindIndex(c => key.Name != null && c.Name == key.Name);
            }

            if (idx >= 0)
            {
                usedCerts.Add(idx);
            }

            entries.Add(new KeystoreEntry
            {
                Alias = key.Name ?? certs.ElementAtOrDefault(idx).Name ?? $"key{entries.Count + 1}",
                EntryType = "PrivateKeyEntry",
                ChainLength = idx >= 0 ? 1 : 0,
                Certificate = idx >= 0 ? CertInfo.FromDer(certs[idx].Der, "keystore") : null,
            });
        }

        // 没有对应私钥、但带别名的证书视为受信任证书条目
        for (int i = 0; i < certs.Count; i++)
        {
            if (usedCerts.Contains(i) || certs[i].Name == null)
            {
                continue;
            }

            entries.Add(new KeystoreEntry
            {
                Alias = certs[i].Name!,
                EntryType = "trustedCertEntry",
                ChainLength = 1,
                Certificate = CertInfo.FromDer(certs[i].Der, "keystore"),
            });
        }

        return new KeystoreInfo
        {
            Format = "PKCS12",
            Entries = entries,
            IntegrityVerified = info.IntegrityMode == Pkcs12IntegrityMode.Password,
        };
    }

    /// <summary>
    /// CertBag 的 certValue 是包裹证书 DER 的 OCTET STRING，需解包。
    /// </summary>
    private static byte[] UnwrapCertificate(ReadOnlyMemory<byte> encoded)
    {
        if (encoded.Length > 0 && encoded.Span[0] == 0x04)
        {
            var reader = new AsnReader(encoded, AsnEncodingRules.BER);
            return reader.ReadOctetString();
        }

        return encoded.ToArray();
    }

    private static string? GetFriendlyName(Pkcs12SafeBag bag)
    {
        foreach (var attr in bag.Attributes)
        {
            if (attr.Oid?.Value != FriendlyNameOid || attr.Values.Count == 0)
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(attr.Values[0].RawData, AsnEncodingRules.BER);
                return reader.ReadCharacterString(UniversalTagNumber.BMPString);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    private static byte[]? GetLocalKeyId(Pkcs12SafeBag bag)
    {
        foreach (var attr in bag.Attributes)
        {
            if (attr.Oid?.Value != LocalKeyIdOid || attr.Values.Count == 0)
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(attr.Values[0].RawData, AsnEncodingRules.BER);
                return reader.ReadOctetString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}
