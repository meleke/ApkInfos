// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：FirebaseConfig.cs
// 说明：从字符串资源还原 google-services.json 配置并导出
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using ApkInfos.Core.Parsing;

namespace ApkInfos.Core.Models;

/// <summary>
/// 从包内字符串资源还原的 google-services.json 信息。
/// Google Services Gradle 插件（以及 Unity Firebase SDK）会把 google-services.json 转成这些固定名称的 string 资源。
/// </summary>
public sealed class FirebaseConfig
{
    /// <summary>google_app_id → client_info.mobilesdk_app_id</summary>
    public string? AppId { get; init; }

    /// <summary>gcm_defaultSenderId → project_info.project_number</summary>
    public string? ProjectNumber { get; init; }

    /// <summary>project_id → project_info.project_id</summary>
    public string? ProjectId { get; init; }

    /// <summary>google_storage_bucket → project_info.storage_bucket</summary>
    public string? StorageBucket { get; init; }

    /// <summary>google_api_key → api_key.current_key</summary>
    public string? ApiKey { get; init; }

    /// <summary>google_crash_reporting_api_key</summary>
    public string? CrashReportingApiKey { get; init; }

    /// <summary>firebase_database_url → project_info.firebase_url</summary>
    public string? DatabaseUrl { get; init; }

    /// <summary>default_web_client_id → oauth_client（client_type = 3）</summary>
    public string? DefaultWebClientId { get; init; }

    /// <summary>
    /// 从资源表提取；没有任何相关资源时返回 null。
    /// </summary>
    public static FirebaseConfig? FromResources(ResourceTable? resources)
    {
        if (resources == null)
        {
            return null;
        }

        var config = new FirebaseConfig
        {
            AppId = resources.ResolveStringByName("google_app_id"),
            ProjectNumber = resources.ResolveStringByName("gcm_defaultSenderId"),
            ProjectId = resources.ResolveStringByName("project_id"),
            StorageBucket = resources.ResolveStringByName("google_storage_bucket"),
            ApiKey = resources.ResolveStringByName("google_api_key"),
            CrashReportingApiKey = resources.ResolveStringByName("google_crash_reporting_api_key"),
            DatabaseUrl = resources.ResolveStringByName("firebase_database_url"),
            DefaultWebClientId = resources.ResolveStringByName("default_web_client_id"),
        };

        // google_app_id 是 Firebase 初始化的必需项，没有它视为未接入
        return config.AppId == null ? null : config;
    }

    /// <summary>
    /// 生成最小可用的 google-services.json（无法还原原文件中的 Android OAuth 客户端与其它 App 条目）。
    /// </summary>
    public string ToGoogleServicesJson(string packageName)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();

            w.WriteStartObject("project_info");
            WriteIfPresent(w, "project_number", ProjectNumber);
            WriteIfPresent(w, "firebase_url", DatabaseUrl);
            WriteIfPresent(w, "project_id", ProjectId);
            WriteIfPresent(w, "storage_bucket", StorageBucket);
            w.WriteEndObject();

            w.WriteStartArray("client");
            w.WriteStartObject();

            w.WriteStartObject("client_info");
            w.WriteString("mobilesdk_app_id", AppId ?? "");
            w.WriteStartObject("android_client_info");
            w.WriteString("package_name", packageName);
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartArray("oauth_client");
            WriteWebClient(w);
            w.WriteEndArray();

            w.WriteStartArray("api_key");
            if (ApiKey != null)
            {
                w.WriteStartObject();
                w.WriteString("current_key", ApiKey);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartObject("services");
            w.WriteStartObject("appinvite_service");
            w.WriteStartArray("other_platform_oauth_client");
            WriteWebClient(w);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteString("configuration_version", "1");
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void WriteWebClient(Utf8JsonWriter w)
    {
        if (DefaultWebClientId == null)
        {
            return;
        }

        w.WriteStartObject();
        w.WriteString("client_id", DefaultWebClientId);
        w.WriteNumber("client_type", 3);
        w.WriteEndObject();
    }

    private static void WriteIfPresent(Utf8JsonWriter w, string name, string? value)
    {
        if (value != null)
        {
            w.WriteString(name, value);
        }
    }
}
