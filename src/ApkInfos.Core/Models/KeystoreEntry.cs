// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：KeystoreEntry.cs
// 说明：Keystore 条目（alias）与加载结果模型
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

namespace ApkInfos.Core.Models;

/// <summary>
/// Keystore 中的一个条目（alias）。
/// </summary>
public sealed class KeystoreEntry
{
    public string Alias { get; init; } = "";

    /// <summary>条目类型：PrivateKeyEntry / trustedCertEntry。</summary>
    public string EntryType { get; init; } = "";

    public DateTime? CreationDate { get; init; }

    /// <summary>证书链长度。</summary>
    public int ChainLength { get; init; }

    /// <summary>链上第一张证书（即签名证书）。</summary>
    public CertInfo? Certificate { get; init; }

    public override string ToString() => Alias;
}

/// <summary>
/// Keystore 加载结果。
/// </summary>
public sealed class KeystoreInfo
{
    /// <summary>格式：JKS / JCEKS / PKCS12。</summary>
    public string Format { get; init; } = "";

    public List<KeystoreEntry> Entries { get; init; } = new();

    /// <summary>未输入密码时为 false，表示未校验完整性。</summary>
    public bool IntegrityVerified { get; init; }
}
