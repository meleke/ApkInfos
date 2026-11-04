using System.Security.Cryptography.X509Certificates;
using ApkInfos.Core.Util;

namespace ApkInfos.Core.Models;

/// <summary>
/// 一张 X.509 证书的摘要信息。
/// </summary>
public sealed class CertInfo
{
    /// <summary>证书来源，例如 "v1"、"v2"、"v3"、"keystore"。</summary>
    public string Source { get; init; } = "";

    public byte[] Der { get; init; } = Array.Empty<byte>();

    public string Md5 { get; init; } = "";

    public string Sha1 { get; init; } = "";

    public string Sha256 { get; init; } = "";

    public string Subject { get; init; } = "";

    public string Issuer { get; init; } = "";

    public string SerialNumber { get; init; } = "";

    public string SignatureAlgorithm { get; init; } = "";

    public DateTime NotBefore { get; init; }

    public DateTime NotAfter { get; init; }

    public string ValidityText => $"{NotBefore:yyyy-MM-dd HH:mm:ss} ~ {NotAfter:yyyy-MM-dd HH:mm:ss}";

    public static CertInfo FromDer(byte[] der, string source)
    {
        string subject = "";
        string issuer = "";
        string serial = "";
        string sigAlg = "";
        DateTime notBefore = default;
        DateTime notAfter = default;

        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(der);
            subject = cert.Subject;
            issuer = cert.Issuer;
            serial = cert.SerialNumber;
            sigAlg = cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "";
            notBefore = cert.NotBefore;
            notAfter = cert.NotAfter;
        }
        catch (Exception)
        {
            // 证书无法解析时仍保留指纹
        }

        return new CertInfo
        {
            Source = source,
            Der = der,
            Md5 = HashUtil.Md5(der),
            Sha1 = HashUtil.Sha1(der),
            Sha256 = HashUtil.Sha256(der),
            Subject = subject,
            Issuer = issuer,
            SerialNumber = serial,
            SignatureAlgorithm = sigAlg,
            NotBefore = notBefore,
            NotAfter = notAfter,
        };
    }

    /// <summary>
    /// 判断两张证书是否为同一张（按 SHA-256 比较）。
    /// </summary>
    public bool SameAs(CertInfo? other)
    {
        return other != null && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
