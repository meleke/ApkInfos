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
