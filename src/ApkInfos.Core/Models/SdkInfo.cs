// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：SdkInfo.cs
// 说明：第三方 SDK（Facebook / Adjust）信息模型
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

namespace ApkInfos.Core.Models;

/// <summary>
/// Facebook SDK 配置（来自 AndroidManifest meta-data 与字符串资源）。
/// </summary>
public sealed class FacebookInfo
{
    public bool Integrated { get; set; }

    /// <summary>App ID（已去掉 "fb" 前缀）。</summary>
    public string? AppId { get; set; }

    public string? ClientToken { get; set; }

    /// <summary>com.facebook.sdk.AutoLogAppEventsEnabled，null 表示未设置。</summary>
    public string? AutoLogAppEvents { get; set; }

    /// <summary>com.facebook.sdk.AdvertiserIDCollectionEnabled，null 表示未设置。</summary>
    public string? AdvertiserIdCollection { get; set; }

    /// <summary>com.facebook.sdk.AutoInitEnabled，null 表示未设置。</summary>
    public string? AutoInit { get; set; }

    /// <summary>FacebookContentProvider 的 authorities 中携带的 App ID。</summary>
    public string? ProviderAppId { get; set; }

    /// <summary>配置检查提示，如 App ID 为 0、与 ContentProvider 不一致等。</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Adjust SDK 集成信息。App Token 在运行时由代码传入，无法从包中可靠获取。
/// </summary>
public sealed class AdjustInfo
{
    public bool Integrated { get; set; }

    /// <summary>判定依据，如 Manifest 组件、dex 中的类。</summary>
    public List<string> Evidence { get; } = new();

    /// <summary>原生 SDK 版本（dex 中的 "androidX.Y.Z"）。</summary>
    public List<string> NativeVersions { get; } = new();

    /// <summary>Unity 插件版本（IL2CPP 元数据或托管 dll 中的 "unityX.Y.Z"）。</summary>
    public List<string> UnityVersions { get; } = new();

    /// <summary>集成检查提示。</summary>
    public List<string> Warnings { get; } = new();
}
