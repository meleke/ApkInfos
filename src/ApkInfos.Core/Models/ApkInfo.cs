namespace ApkInfos.Core.Models;

/// <summary>
/// 一个 APK / AAB 的解析结果。
/// </summary>
public sealed class ApkInfo
{
    // ---------- 文件信息 ----------
    public string FilePath { get; set; } = "";

    public string FileName { get; set; } = "";

    public long FileSize { get; set; }

    public string FileMd5 { get; set; } = "";

    /// <summary>"APK" 或 "AAB"。</summary>
    public string PackageType { get; set; } = "APK";

    // ---------- 基本信息 ----------
    public string PackageName { get; set; } = "";

    public string AppLabel { get; set; } = "";

    public string VersionName { get; set; } = "";

    public string VersionCode { get; set; } = "";

    public string MinSdk { get; set; } = "";

    public List<string> Permissions { get; } = new();

    // ---------- 构建信息 ----------
    public string TargetSdk { get; set; } = "";

    public string CompileSdk { get; set; } = "";

    public string Debuggable { get; set; } = "";

    public string AllowBackup { get; set; } = "";

    public List<string> Abis { get; } = new();

    /// <summary>AAB 的模块列表（base + 动态功能模块）。</summary>
    public List<string> Modules { get; } = new();

    // ---------- 图标 ----------
    public byte[]? IconBytes { get; set; }

    public string IconPath { get; set; } = "";

    // ---------- 签名 ----------
    /// <summary>检测到的签名方案，如 v1、v2、v3。</summary>
    public List<string> SignatureSchemes { get; } = new();

    /// <summary>各方案取到的证书（v3 → v3.1 → v2 → v1 顺序）。</summary>
    public List<CertInfo> Certificates { get; } = new();

    /// <summary>主证书：用于显示指纹与 keystore 比对。</summary>
    public CertInfo? PrimaryCertificate => Certificates.Count > 0 ? Certificates[0] : null;

    // ---------- Manifest ----------
    public string ManifestXml { get; set; } = "";

    /// <summary>解析过程中的非致命问题。</summary>
    public List<string> Warnings { get; } = new();

    public static string FormatSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return $"{bytes:N0} 字节 ({mb:F2} MB)";
    }
}
