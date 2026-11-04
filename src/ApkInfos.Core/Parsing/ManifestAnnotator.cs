// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：ManifestAnnotator.cs
// 说明：为 AndroidManifest 元素生成中文注解
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.Globalization;

namespace ApkInfos.Core.Parsing;

/// <summary>
/// 为 AndroidManifest 元素生成中文注解（输出为元素前的 XML 注释）。
/// </summary>
public sealed class ManifestAnnotator
{
    private const string Sep = " · ";

    private readonly ResourceTable? _resources;
    private readonly string _package;

    public ManifestAnnotator(ManifestElement root, ResourceTable? resources)
    {
        _resources = resources;
        _package = root.Attr("package")?.StringValue ?? root.Attr("package")?.DisplayValue ?? "";
    }

    /// <summary>
    /// 返回元素的注解；无需注解时返回 null。
    /// </summary>
    public string? Annotate(ManifestElement e, ManifestElement? parent = null)
    {
        if (parent?.Name == "queries")
        {
            return QueryItem(e);
        }

        return e.Name switch
        {
            "manifest" => Manifest(e),
            "uses-sdk" => UsesSdk(e),
            "uses-permission" or "uses-permission-sdk-23" or "uses-permission-sdk-m" => UsesPermission(e),
            "permission" => Permission(e),
            "uses-feature" => UsesFeature(e),
            "uses-library" or "uses-native-library" => UsesLibrary(e),
            "queries" => "包可见性声明（Android 11+）：列出本应用需要查询或交互的其它应用 / Intent",
            "supports-screens" => "支持的屏幕尺寸",
            "application" => Application(e),
            "activity" => Component(e, "Activity（界面）"),
            "activity-alias" => Component(e, "Activity 别名"),
            "service" => Component(e, "Service（后台服务）"),
            "receiver" => Component(e, "BroadcastReceiver（广播接收器）"),
            "provider" => Component(e, "ContentProvider（内容提供者）"),
            "intent-filter" => IntentFilter(e),
            "meta-data" => MetaData(e),
            "profileable" => "允许性能分析工具（Profiler）附加" + (Bool(e, "shell") == true ? "，shell 可用" : ""),
            _ => null,
        };
    }

    // ---------------- 各元素 ----------------

    private string Manifest(ManifestElement e)
    {
        var parts = new List<string> { $"应用包名 {_package}" };
        AddIf(parts, "版本号(versionName)", Text(e, "versionName"));
        AddIf(parts, "内部版本号(versionCode)", Text(e, "versionCode"));

        string compile = Text(e, "compileSdkVersion");
        if (compile.Length == 0)
        {
            compile = Text(e, "platformBuildVersionCode");
        }

        if (int.TryParse(compile, out int api))
        {
            parts.Add($"编译 SDK {ApiText(api)}");
        }

        string install = EnumText(e.Attr("installLocation"), InstallLocations);
        if (install.Length > 0)
        {
            parts.Add($"安装位置 {install}");
        }

        string shared = Text(e, "sharedUserId");
        if (shared.Length > 0)
        {
            parts.Add($"共享用户 ID {shared}（已废弃）");
        }

        return string.Join(Sep, parts);
    }

    private string UsesSdk(ManifestElement e)
    {
        var parts = new List<string>();
        if (int.TryParse(Text(e, "minSdkVersion"), out int min))
        {
            parts.Add($"最低支持 {ApiText(min)}");
        }

        if (int.TryParse(Text(e, "targetSdkVersion"), out int target))
        {
            parts.Add($"目标 {ApiText(target)}");
        }

        if (int.TryParse(Text(e, "maxSdkVersion"), out int max))
        {
            parts.Add($"最高 {ApiText(max)}");
        }

        return parts.Count == 0 ? "SDK 版本声明" : string.Join(Sep, parts);
    }

    private string UsesPermission(ManifestElement e)
    {
        string name = Text(e, "name");
        string text = DescribePermission(name);
        if (e.Name != "uses-permission")
        {
            text += "（仅 Android 6.0+ 申请）";
        }

        if (int.TryParse(Text(e, "maxSdkVersion"), out int max))
        {
            text += $"（仅 API ≤ {max} 时申请）";
        }

        return text;
    }

    private string Permission(ManifestElement e)
    {
        string name = Text(e, "name");
        var level = e.Attr("protectionLevel");
        string levelText = "";
        if (level != null)
        {
            if (level.AsInt() is long v)
            {
                string baseName = (v & 0xF) switch
                {
                    0 => "normal（普通）",
                    1 => "dangerous（危险）",
                    2 => "signature（仅同签名应用）",
                    3 => "signatureOrSystem（同签名或系统应用）",
                    _ => $"0x{v & 0xF:x}",
                };
                levelText = (v & ~0xFL) != 0 ? $"{baseName} + 附加标志 0x{v & ~0xFL:x}" : baseName;
            }
            else
            {
                levelText = ManifestXmlWriter.FormatValue(level, _resources);
            }
        }

        return $"声明自定义权限 {name}" + (levelText.Length > 0 ? $"{Sep}保护级别 {levelText}" : "");
    }

    private string UsesFeature(ManifestElement e)
    {
        bool required = Bool(e, "required") ?? true;
        string need = required ? "必需（不支持的设备在商店中不可见）" : "可选";
        var gl = e.Attr("glEsVersion");
        if (gl != null && gl.AsInt() is long v)
        {
            return $"需要 OpenGL ES {v >> 16}.{v & 0xFFFF}";
        }

        string name = Text(e, "name");
        string desc = Features.TryGetValue(name, out var d) ? d : name;
        return $"设备特性：{desc}{Sep}{need}";
    }

    private string UsesLibrary(ManifestElement e)
    {
        string name = Text(e, "name");
        bool required = Bool(e, "required") ?? true;
        string desc = name switch
        {
            "org.apache.http.legacy" => "Apache HTTP 旧版客户端库",
            "androidx.window.extensions" => "Jetpack WindowManager 扩展（折叠屏）",
            "androidx.window.sidecar" => "Jetpack WindowManager Sidecar（折叠屏）",
            _ => name,
        };
        return $"使用共享库：{desc}{Sep}{(required ? "必需" : "可选")}";
    }

    private string Application(ManifestElement e)
    {
        var parts = new List<string>();
        var label = e.Attr("label");
        if (label != null)
        {
            string resolved = label.Kind == AttrKind.Reference ? _resources?.ResolveString(label.Data) ?? "" : Text(e, "label");
            if (resolved.Length > 0)
            {
                parts.Add($"应用名称「{resolved}」");
            }
        }

        string appClass = Text(e, "name");
        if (appClass.Length > 0)
        {
            parts.Add($"自定义 Application 类 {FullName(appClass)}{Owner(FullName(appClass))}");
        }

        if (Bool(e, "debuggable") == true)
        {
            parts.Add("⚠ 可调试（debuggable=true，正式包不应开启）");
        }

        bool? backup = Bool(e, "allowBackup");
        parts.Add(backup == false ? "禁止系统备份" : "允许系统备份" + (backup == null ? "（默认）" : ""));

        if (Bool(e, "usesCleartextTraffic") == true)
        {
            parts.Add("⚠ 允许明文 HTTP 流量");
        }

        if (e.Attr("networkSecurityConfig") != null)
        {
            parts.Add("使用自定义网络安全配置");
        }

        if (Bool(e, "largeHeap") == true)
        {
            parts.Add("申请大内存堆(largeHeap)");
        }

        if (Bool(e, "hardwareAccelerated") == false)
        {
            parts.Add("关闭硬件加速");
        }

        bool? extract = Bool(e, "extractNativeLibs");
        if (extract != null)
        {
            parts.Add(extract.Value ? "安装时解压 so 库" : "so 库不解压（需 4K 对齐、未压缩存储）");
        }

        if (Bool(e, "requestLegacyExternalStorage") == true)
        {
            parts.Add("请求旧版外部存储模式（Android 10 兼容）");
        }

        return string.Join(Sep, parts);
    }

    private string Component(ManifestElement e, string kind)
    {
        string name = FullName(Text(e, "name"));
        var parts = new List<string>();

        bool launcher = e.Elements("intent-filter").Any(IsLauncher);
        parts.Add(launcher ? $"【启动入口】{kind}" : kind);
        parts.Add(ShortName(name) + Owner(name));

        bool? exported = Bool(e, "exported");
        bool hasFilter = e.Elements("intent-filter").Any();
        if (exported == true)
        {
            parts.Add("对外导出（其它应用可调用）");
        }
        else if (exported == null && hasFilter)
        {
            parts.Add("未声明 exported（含 intent-filter，Android 12+ 必须显式声明）");
        }

        string permission = Text(e, "permission");
        if (permission.Length > 0)
        {
            parts.Add($"调用方需持有权限 {permission}");
        }

        string launchMode = EnumText(e.Attr("launchMode"), LaunchModes);
        if (launchMode.Length > 0)
        {
            parts.Add($"启动模式 {launchMode}");
        }

        string orientation = EnumText(e.Attr("screenOrientation"), Orientations);
        if (orientation.Length > 0)
        {
            parts.Add($"屏幕方向 {orientation}");
        }

        var config = e.Attr("configChanges");
        if (config != null)
        {
            parts.Add($"自行处理配置变更：{FlagsText(config, ConfigChanges)}");
        }

        string process = Text(e, "process");
        if (process.Length > 0)
        {
            parts.Add($"运行在进程 {process}");
        }

        string authorities = Text(e, "authorities");
        if (authorities.Length > 0)
        {
            parts.Add($"authorities={authorities}");
        }

        if (Bool(e, "enabled") == false)
        {
            parts.Add("默认禁用");
        }

        return string.Join(Sep, parts);
    }

    private string? IntentFilter(ManifestElement e)
    {
        var actions = e.Elements("action").Select(a => Text(a, "name")).Where(s => s.Length > 0).ToList();
        var categories = e.Elements("category").Select(c => Text(c, "name")).ToList();

        if (IsLauncher(e))
        {
            return "桌面启动入口（MAIN + LAUNCHER）";
        }

        if (actions.Contains("android.intent.action.VIEW") && categories.Contains("android.intent.category.BROWSABLE"))
        {
            var links = new List<string>();
            foreach (var data in e.Elements("data"))
            {
                string scheme = Text(data, "scheme");
                string host = Text(data, "host");
                string path = Text(data, "path") + Text(data, "pathPrefix") + Text(data, "pathPattern");
                if (scheme.Length > 0 || host.Length > 0)
                {
                    links.Add($"{(scheme.Length > 0 ? scheme : "*")}://{host}{path}");
                }
            }

            string verify = Bool(e, "autoVerify") == true ? "（App Links 自动验证）" : "";
            return links.Count > 0 ? $"深度链接{verify}：{string.Join("、", links)}" : $"可被浏览器打开的链接{verify}";
        }

        var known = actions.Select(a => Actions.TryGetValue(a, out var d) ? d : null).Where(d => d != null).ToList();
        return known.Count > 0 ? "响应：" + string.Join("、", known) : null;
    }

    private string? QueryItem(ManifestElement e)
    {
        switch (e.Name)
        {
            case "package":
            {
                string name = Text(e, "name");
                return KnownApps.TryGetValue(name, out var app) ? $"可查询应用：{app}（{name}）" : $"可查询应用：{name}";
            }
            case "intent":
            {
                var actions = e.Elements("action").Select(a => Text(a, "name")).Where(a => a.Length > 0).ToList();
                var schemes = e.Elements("data").Select(d => Text(d, "scheme")).Where(x => x.Length > 0).ToList();
                var mimes = e.Elements("data").Select(d => Text(d, "mimeType")).Where(x => x.Length > 0).ToList();
                var parts = new List<string> { "可查询能处理此 Intent 的应用" };
                if (actions.Count > 0)
                {
                    parts.Add("action=" + string.Join(",", actions.Select(ShortName)));
                }

                if (schemes.Count > 0)
                {
                    parts.Add("scheme=" + string.Join(",", schemes));
                }

                if (mimes.Count > 0)
                {
                    parts.Add("mimeType=" + string.Join(",", mimes));
                }

                return string.Join(Sep, parts);
            }
            case "provider":
            {
                return $"可查询内容提供者：{Text(e, "authorities")}";
            }
            default:
            {
                return null;
            }
        }
    }

    private string? MetaData(ManifestElement e)
    {
        string name = Text(e, "name");
        if (MetaKeys.TryGetValue(name, out var desc))
        {
            return desc;
        }

        foreach (var (prefix, owner) in MetaPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return $"{owner} 配置";
            }
        }

        return null;
    }

    // ---------------- 工具 ----------------

    private static bool IsLauncher(ManifestElement filter)
    {
        bool main = filter.Elements("action").Any(a => a.Attr("name")?.StringValue == "android.intent.action.MAIN");
        bool launcher = filter.Elements("category").Any(c => c.Attr("name")?.StringValue is "android.intent.category.LAUNCHER" or "android.intent.category.LEANBACK_LAUNCHER");
        return main && launcher;
    }

    private string Text(ManifestElement e, string attr)
    {
        return ApkInfoReader.Text(e.Attr(attr), _resources);
    }

    private static bool? Bool(ManifestElement e, string attr)
    {
        return e.Attr(attr)?.AsBool();
    }

    private string FullName(string name)
    {
        if (name.StartsWith('.'))
        {
            return _package + name;
        }

        return !name.Contains('.') && name.Length > 0 && _package.Length > 0 ? $"{_package}.{name}" : name;
    }

    private static string ShortName(string name)
    {
        int idx = name.LastIndexOf('.');
        return idx >= 0 ? name.Substring(idx + 1) : name;
    }

    private string Owner(string className)
    {
        if (_package.Length > 0 && className.StartsWith(_package + ".", StringComparison.Ordinal))
        {
            return "";
        }

        foreach (var (prefix, owner) in Owners)
        {
            if (className.StartsWith(prefix, StringComparison.Ordinal))
            {
                return $"（{owner}）";
            }
        }

        return "";
    }

    /// <summary>
    /// 枚举属性：AAB 中 value 已是名称（如 "singleTop"），APK 中为整数。
    /// </summary>
    private static string EnumText(ManifestAttribute? attr, Dictionary<long, string> map)
    {
        if (attr == null)
        {
            return "";
        }

        string? display = attr.DisplayValue;
        if (!string.IsNullOrEmpty(display) && !long.TryParse(display, out _) && !display.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var match = map.Values.FirstOrDefault(v => v.StartsWith(display + "(", StringComparison.Ordinal) || v == display);
            return match ?? display;
        }

        if (attr.AsInt() is long v)
        {
            return map.TryGetValue(v, out var name) ? name : v.ToString(CultureInfo.InvariantCulture);
        }

        return attr.StringValue ?? "";
    }

    private static string FlagsText(ManifestAttribute attr, (long Flag, string Name)[] flags)
    {
        string? display = attr.DisplayValue;
        if (!string.IsNullOrEmpty(display) && !display.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && !long.TryParse(display, out _))
        {
            return display;
        }

        if (attr.AsInt() is not long v)
        {
            return attr.StringValue ?? "";
        }

        var names = flags.Where(f => (v & f.Flag) == f.Flag).Select(f => f.Name).ToList();
        return names.Count > 0 ? string.Join("|", names) : $"0x{v:x}";
    }

    private static void AddIf(List<string> parts, string label, string value)
    {
        if (value.Length > 0)
        {
            parts.Add($"{label} {value}");
        }
    }

    /// <summary>
    /// API 级别 → "API 34（Android 14）"。
    /// </summary>
    public static string ApiText(int api)
    {
        return AndroidVersions.TryGetValue(api, out var v) ? $"API {api}（Android {v}）" : $"API {api}";
    }

    /// <summary>
    /// 权限中文说明，危险权限 / 特殊权限加标注。
    /// </summary>
    public static string DescribePermission(string name)
    {
        if (Permissions.TryGetValue(name, out var p))
        {
            string tag = p.Level switch
            {
                'D' => "【危险权限，需运行时授权】",
                'S' => "【特殊权限，需用户在设置中授予】",
                'P' => "【系统/签名级权限，普通应用无法获得】",
                _ => "",
            };
            return $"权限：{p.Desc}{tag}";
        }

        if (name.StartsWith("android.permission.FOREGROUND_SERVICE_", StringComparison.Ordinal))
        {
            return $"权限：前台服务类型 {name.Substring("android.permission.FOREGROUND_SERVICE_".Length)}（Android 14+）";
        }

        if (name.EndsWith(".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION", StringComparison.Ordinal))
        {
            return "权限：AndroidX 自动生成，用于保护未导出的动态广播接收器";
        }

        if (name.EndsWith(".permission.C2D_MESSAGE", StringComparison.Ordinal))
        {
            return "权限：旧版 GCM 推送消息权限";
        }

        return $"权限：{name}";
    }

    // ---------------- 词典 ----------------

    private static readonly Dictionary<int, string> AndroidVersions = new()
    {
        [1] = "1.0", [2] = "1.1", [3] = "1.5", [4] = "1.6", [5] = "2.0", [6] = "2.0.1", [7] = "2.1", [8] = "2.2",
        [9] = "2.3", [10] = "2.3.3", [11] = "3.0", [12] = "3.1", [13] = "3.2", [14] = "4.0", [15] = "4.0.3",
        [16] = "4.1", [17] = "4.2", [18] = "4.3", [19] = "4.4", [20] = "4.4W", [21] = "5.0", [22] = "5.1",
        [23] = "6.0", [24] = "7.0", [25] = "7.1", [26] = "8.0", [27] = "8.1", [28] = "9", [29] = "10",
        [30] = "11", [31] = "12", [32] = "12L", [33] = "13", [34] = "14", [35] = "15", [36] = "16", [37] = "17",
    };

    private static readonly Dictionary<long, string> InstallLocations = new()
    {
        [0] = "auto(自动)",
        [1] = "internalOnly(仅内部存储)",
        [2] = "preferExternal(优先外部存储)",
    };

    private static readonly Dictionary<long, string> LaunchModes = new()
    {
        [0] = "standard(标准)",
        [1] = "singleTop(栈顶复用)",
        [2] = "singleTask(栈内单例)",
        [3] = "singleInstance(全局单例)",
        [4] = "singleInstancePerTask(每任务单例)",
    };

    private static readonly Dictionary<long, string> Orientations = new()
    {
        [-1] = "unspecified(未指定)",
        [0] = "landscape(横屏)",
        [1] = "portrait(竖屏)",
        [2] = "user(跟随用户设置)",
        [3] = "behind(同前一个 Activity)",
        [4] = "sensor(随重力)",
        [5] = "nosensor(忽略重力)",
        [6] = "sensorLandscape(横屏，可左右翻转)",
        [7] = "sensorPortrait(竖屏，可上下翻转)",
        [8] = "reverseLandscape(反向横屏)",
        [9] = "reversePortrait(反向竖屏)",
        [10] = "fullSensor(四向随重力)",
        [11] = "userLandscape(用户横屏)",
        [12] = "userPortrait(用户竖屏)",
        [13] = "fullUser(四向，遵循用户锁定)",
        [14] = "locked(锁定当前方向)",
    };

    private static readonly (long Flag, string Name)[] ConfigChanges =
    {
        (0x1, "mcc"), (0x2, "mnc"), (0x4, "locale"), (0x8, "touchscreen"), (0x10, "keyboard"),
        (0x20, "keyboardHidden"), (0x40, "navigation"), (0x80, "orientation"), (0x100, "screenLayout"),
        (0x200, "uiMode"), (0x400, "screenSize"), (0x800, "smallestScreenSize"), (0x1000, "density"),
        (0x2000, "layoutDirection"), (0x4000, "colorMode"), (0x8000, "grammaticalGender"),
        (0x10000000, "fontWeightAdjustment"), (0x40000000, "fontScale"),
    };

    /// <summary>权限说明：Level N=普通 D=危险 S=特殊 P=系统/签名级。</summary>
    private static readonly Dictionary<string, (string Desc, char Level)> Permissions = new()
    {
        ["android.permission.INTERNET"] = ("访问网络", 'N'),
        ["android.permission.ACCESS_NETWORK_STATE"] = ("查看网络连接状态", 'N'),
        ["android.permission.ACCESS_WIFI_STATE"] = ("查看 WLAN 状态", 'N'),
        ["android.permission.CHANGE_WIFI_STATE"] = ("修改 WLAN 状态", 'N'),
        ["android.permission.CHANGE_NETWORK_STATE"] = ("修改网络连接状态", 'N'),
        ["android.permission.WAKE_LOCK"] = ("防止设备休眠", 'N'),
        ["android.permission.VIBRATE"] = ("控制振动", 'N'),
        ["android.permission.RECEIVE_BOOT_COMPLETED"] = ("开机后自动启动", 'N'),
        ["android.permission.FOREGROUND_SERVICE"] = ("运行前台服务", 'N'),
        ["android.permission.POST_NOTIFICATIONS"] = ("发送通知（Android 13+）", 'D'),
        ["android.permission.CAMERA"] = ("使用相机", 'D'),
        ["android.permission.RECORD_AUDIO"] = ("录音", 'D'),
        ["android.permission.READ_EXTERNAL_STORAGE"] = ("读取外部存储", 'D'),
        ["android.permission.WRITE_EXTERNAL_STORAGE"] = ("写入外部存储", 'D'),
        ["android.permission.MANAGE_EXTERNAL_STORAGE"] = ("管理所有文件", 'S'),
        ["android.permission.READ_MEDIA_IMAGES"] = ("读取图片（Android 13+）", 'D'),
        ["android.permission.READ_MEDIA_VIDEO"] = ("读取视频（Android 13+）", 'D'),
        ["android.permission.READ_MEDIA_AUDIO"] = ("读取音频（Android 13+）", 'D'),
        ["android.permission.READ_MEDIA_VISUAL_USER_SELECTED"] = ("读取用户选择的照片/视频（Android 14+）", 'D'),
        ["android.permission.ACCESS_MEDIA_LOCATION"] = ("读取照片中的位置信息", 'D'),
        ["android.permission.ACCESS_FINE_LOCATION"] = ("精确位置", 'D'),
        ["android.permission.ACCESS_COARSE_LOCATION"] = ("大致位置", 'D'),
        ["android.permission.ACCESS_BACKGROUND_LOCATION"] = ("后台访问位置", 'D'),
        ["android.permission.READ_PHONE_STATE"] = ("读取手机状态和身份", 'D'),
        ["android.permission.READ_PHONE_NUMBERS"] = ("读取本机号码", 'D'),
        ["android.permission.CALL_PHONE"] = ("直接拨打电话", 'D'),
        ["android.permission.READ_CONTACTS"] = ("读取联系人", 'D'),
        ["android.permission.WRITE_CONTACTS"] = ("修改联系人", 'D'),
        ["android.permission.GET_ACCOUNTS"] = ("读取设备账户列表", 'D'),
        ["android.permission.READ_CALENDAR"] = ("读取日历", 'D'),
        ["android.permission.WRITE_CALENDAR"] = ("修改日历", 'D'),
        ["android.permission.SEND_SMS"] = ("发送短信", 'D'),
        ["android.permission.RECEIVE_SMS"] = ("接收短信", 'D'),
        ["android.permission.READ_SMS"] = ("读取短信", 'D'),
        ["android.permission.BODY_SENSORS"] = ("身体传感器", 'D'),
        ["android.permission.ACTIVITY_RECOGNITION"] = ("识别身体活动", 'D'),
        ["android.permission.BLUETOOTH"] = ("使用蓝牙（Android 11 及以下）", 'N'),
        ["android.permission.BLUETOOTH_ADMIN"] = ("管理蓝牙（Android 11 及以下）", 'N'),
        ["android.permission.BLUETOOTH_CONNECT"] = ("连接蓝牙设备（Android 12+）", 'D'),
        ["android.permission.BLUETOOTH_SCAN"] = ("扫描蓝牙设备（Android 12+）", 'D'),
        ["android.permission.BLUETOOTH_ADVERTISE"] = ("蓝牙广播（Android 12+）", 'D'),
        ["android.permission.NEARBY_WIFI_DEVICES"] = ("附近的 WLAN 设备（Android 13+）", 'D'),
        ["android.permission.NFC"] = ("使用 NFC", 'N'),
        ["android.permission.USE_BIOMETRIC"] = ("使用生物识别", 'N'),
        ["android.permission.USE_FINGERPRINT"] = ("使用指纹（已废弃）", 'N'),
        ["android.permission.MODIFY_AUDIO_SETTINGS"] = ("修改音频设置", 'N'),
        ["android.permission.SYSTEM_ALERT_WINDOW"] = ("显示悬浮窗", 'S'),
        ["android.permission.REQUEST_INSTALL_PACKAGES"] = ("请求安装应用", 'S'),
        ["android.permission.QUERY_ALL_PACKAGES"] = ("查询设备上所有应用（Google Play 有使用限制）", 'N'),
        ["android.permission.SCHEDULE_EXACT_ALARM"] = ("设置精确闹钟", 'S'),
        ["android.permission.USE_EXACT_ALARM"] = ("使用精确闹钟（闹钟/日历类应用）", 'N'),
        ["android.permission.USE_FULL_SCREEN_INTENT"] = ("全屏通知", 'N'),
        ["android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS"] = ("请求忽略电池优化", 'N'),
        ["android.permission.WRITE_SETTINGS"] = ("修改系统设置", 'S'),
        ["android.permission.ACCESS_NOTIFICATION_POLICY"] = ("访问勿扰模式", 'N'),
        ["android.permission.HIGH_SAMPLING_RATE_SENSORS"] = ("高采样率传感器", 'N'),
        ["android.permission.INSTALL_PACKAGES"] = ("安装应用", 'P'),
        ["android.permission.READ_LOGS"] = ("读取系统日志", 'P'),
        ["android.permission.READ_PRIVILEGED_PHONE_STATE"] = ("读取特权手机状态", 'P'),
        ["android.permission.WRITE_MEDIA_STORAGE"] = ("写入媒体存储", 'P'),
        ["android.permission.ACCESS_ADSERVICES_AD_ID"] = ("隐私沙盒：广告 ID", 'N'),
        ["android.permission.ACCESS_ADSERVICES_ATTRIBUTION"] = ("隐私沙盒：归因报告", 'N'),
        ["android.permission.ACCESS_ADSERVICES_TOPICS"] = ("隐私沙盒：Topics API", 'N'),
        ["com.google.android.gms.permission.AD_ID"] = ("读取广告 ID（GAID，Android 13+ 需声明）", 'N'),
        ["com.google.android.finsky.permission.BIND_GET_INSTALL_REFERRER_SERVICE"] = ("读取 Google Play 安装来源（Install Referrer）", 'N'),
        ["com.android.vending.BILLING"] = ("Google Play 应用内购买", 'N'),
        ["com.android.vending.CHECK_LICENSE"] = ("Google Play 许可验证", 'N'),
        ["com.google.android.c2dm.permission.RECEIVE"] = ("接收 Firebase 云消息（FCM）推送", 'N'),
    };

    private static readonly Dictionary<string, string> Features = new()
    {
        ["android.hardware.camera"] = "后置摄像头",
        ["android.hardware.camera.any"] = "任意摄像头",
        ["android.hardware.camera.autofocus"] = "摄像头自动对焦",
        ["android.hardware.camera.front"] = "前置摄像头",
        ["android.hardware.touchscreen"] = "触摸屏",
        ["android.hardware.touchscreen.multitouch"] = "多点触控",
        ["android.hardware.touchscreen.multitouch.distinct"] = "独立多点触控",
        ["android.hardware.location"] = "定位",
        ["android.hardware.location.gps"] = "GPS",
        ["android.hardware.location.network"] = "网络定位",
        ["android.hardware.microphone"] = "麦克风",
        ["android.hardware.bluetooth"] = "蓝牙",
        ["android.hardware.bluetooth_le"] = "低功耗蓝牙",
        ["android.hardware.wifi"] = "WLAN",
        ["android.hardware.telephony"] = "电话功能",
        ["android.hardware.nfc"] = "NFC",
        ["android.hardware.sensor.accelerometer"] = "加速度传感器",
        ["android.hardware.sensor.gyroscope"] = "陀螺仪",
        ["android.hardware.vulkan.version"] = "Vulkan",
        ["android.hardware.vulkan.level"] = "Vulkan 硬件级别",
        ["android.hardware.gamepad"] = "游戏手柄",
        ["android.hardware.faketouch"] = "模拟触控（鼠标等）",
        ["android.software.leanback"] = "Android TV 界面",
        ["android.software.webview"] = "WebView",
        ["android.hardware.type.watch"] = "手表设备",
        ["android.hardware.type.pc"] = "PC 设备（ChromeOS 等）",
    };

    private static readonly Dictionary<string, string> Actions = new()
    {
        ["android.intent.action.BOOT_COMPLETED"] = "开机完成",
        ["android.intent.action.LOCKED_BOOT_COMPLETED"] = "直接启动模式开机完成",
        ["android.intent.action.QUICKBOOT_POWERON"] = "快速开机",
        ["android.intent.action.MY_PACKAGE_REPLACED"] = "本应用被更新",
        ["android.intent.action.PACKAGE_ADDED"] = "有应用安装",
        ["android.intent.action.PACKAGE_REMOVED"] = "有应用卸载",
        ["android.intent.action.TIMEZONE_CHANGED"] = "时区变化",
        ["android.intent.action.TIME_SET"] = "时间被修改",
        ["android.intent.action.SEND"] = "接收分享内容",
        ["android.intent.action.SEND_MULTIPLE"] = "接收多项分享内容",
        ["android.net.conn.CONNECTIVITY_CHANGE"] = "网络连接变化",
        ["com.android.vending.INSTALL_REFERRER"] = "Google Play 安装来源广播（旧版）",
        ["com.google.firebase.MESSAGING_EVENT"] = "Firebase 云消息（FCM）",
        ["com.google.firebase.INSTANCE_ID_EVENT"] = "Firebase 实例 ID（旧版）",
        ["com.google.android.c2dm.intent.RECEIVE"] = "推送消息（GCM/FCM）",
        ["android.service.quicksettings.action.QS_TILE"] = "快捷设置磁贴",
        ["android.appwidget.action.APPWIDGET_UPDATE"] = "桌面小部件更新",
        ["android.intent.action.MAIN"] = "主入口",
        ["androidx.work.diagnostics.REQUEST_DIAGNOSTICS"] = "WorkManager 诊断",
        ["android.intent.action.ACTION_POWER_CONNECTED"] = "接通电源",
        ["android.intent.action.ACTION_POWER_DISCONNECTED"] = "断开电源",
        ["android.intent.action.BATTERY_OKAY"] = "电量恢复",
        ["android.intent.action.BATTERY_LOW"] = "电量低",
        ["android.intent.action.DEVICE_STORAGE_LOW"] = "存储空间不足",
        ["android.intent.action.DEVICE_STORAGE_OK"] = "存储空间恢复",
    };

    private static readonly Dictionary<string, string> MetaKeys = new()
    {
        ["com.google.android.gms.ads.APPLICATION_ID"] = "Google AdMob 应用 ID",
        ["com.google.android.gms.version"] = "Google Play 服务版本",
        ["com.google.android.geo.API_KEY"] = "Google Maps API Key",
        ["com.google.android.play.billingclient.version"] = "Google Play 结算库版本",
        ["com.google.firebase.messaging.default_notification_icon"] = "FCM 默认通知图标",
        ["com.google.firebase.messaging.default_notification_color"] = "FCM 默认通知颜色",
        ["com.google.firebase.messaging.default_notification_channel_id"] = "FCM 默认通知渠道",
        ["firebase_analytics_collection_enabled"] = "Firebase Analytics 是否默认采集",
        ["firebase_analytics_collection_deactivated"] = "永久停用 Firebase Analytics",
        ["firebase_crashlytics_collection_enabled"] = "Crashlytics 是否默认采集",
        ["firebase_performance_collection_enabled"] = "Firebase Performance 是否默认采集",
        ["google_analytics_adid_collection_enabled"] = "Analytics 是否采集广告 ID",
        ["google_analytics_default_allow_ad_storage"] = "同意模式：ad_storage 默认值",
        ["google_analytics_default_allow_analytics_storage"] = "同意模式：analytics_storage 默认值",
        ["google_analytics_default_allow_ad_user_data"] = "同意模式：ad_user_data 默认值",
        ["google_analytics_default_allow_ad_personalization_signals"] = "同意模式：ad_personalization 默认值",
        ["com.facebook.sdk.ApplicationId"] = "Facebook App ID",
        ["com.facebook.sdk.ClientToken"] = "Facebook Client Token",
        ["com.facebook.sdk.AutoLogAppEventsEnabled"] = "Facebook 自动记录应用事件",
        ["com.facebook.sdk.AdvertiserIDCollectionEnabled"] = "Facebook 收集广告 ID",
        ["com.facebook.sdk.AutoInitEnabled"] = "Facebook SDK 自动初始化",
        ["applovin.sdk.key"] = "AppLovin SDK Key",
        ["android.max_aspect"] = "最大屏幕宽高比",
        ["android.notch_support"] = "华为刘海屏适配",
        ["notch.config"] = "小米刘海屏适配",
        ["android.allow_multiple_resumed_activities"] = "允许多个 Activity 同时处于 resumed 状态",
        ["unity.splash-mode"] = "Unity 启动画面模式",
        ["unity.splash-enable"] = "Unity 是否显示启动画面",
        ["unity.launch-fullscreen"] = "Unity 全屏启动",
        ["unity.render-outside-safearea"] = "Unity 是否渲染到安全区之外",
        ["unity.allow-resizable-window"] = "Unity 允许调整窗口大小",
        ["unity.auto-report-fully-drawn"] = "Unity 自动上报完全绘制",
        ["unity.strip-engine-code"] = "Unity 引擎代码裁剪",
        ["unity.build-id"] = "Unity 构建 ID",
        ["unityplayer.UnityActivity"] = "Unity 主 Activity 标记",
        ["unityplayer.SkipPermissionsDialog"] = "Unity 跳过启动时的权限弹窗",
        ["android.app.lib_name"] = "原生 Activity 加载的 so 库名",
        ["com.android.vending.derived.apk.id"] = "Google Play 生成的 APK 标识（从 AAB 派生）",
        ["com.android.dynamic.apk.fused.modules"] = "已合并的动态功能模块",
        ["com.android.stamp.source"] = "Google Play 来源戳（Source Stamp）",
        ["com.android.stamp.type"] = "Google Play 来源戳类型",
        ["com.android.vending.splits.required"] = "需要拆分 APK（缺失时无法运行）",
        ["com.android.vending.splits"] = "拆分 APK 列表",
    };

    private static readonly Dictionary<string, string> KnownApps = new()
    {
        ["com.facebook.katana"] = "Facebook",
        ["com.facebook.orca"] = "Messenger",
        ["com.instagram.android"] = "Instagram",
        ["com.whatsapp"] = "WhatsApp",
        ["com.twitter.android"] = "X (Twitter)",
        ["jp.naver.line.android"] = "LINE",
        ["com.vkontakte.android"] = "VK",
        ["org.telegram.messenger"] = "Telegram",
        ["com.zhiliaoapp.musically"] = "TikTok",
        ["com.ss.android.ugc.aweme"] = "抖音",
        ["com.tencent.mm"] = "微信",
        ["com.tencent.mobileqq"] = "QQ",
        ["com.sina.weibo"] = "微博",
        ["com.eg.android.AlipayGphone"] = "支付宝",
        ["com.android.vending"] = "Google Play 商店",
        ["com.google.android.gms"] = "Google Play 服务",
        ["com.google.android.youtube"] = "YouTube",
        ["com.discord"] = "Discord",
        ["com.kakao.talk"] = "KakaoTalk",
    };

    private static readonly (string Prefix, string Owner)[] MetaPrefixes =
    {
        ("com.google.firebase.", "Firebase"),
        ("com.google.android.gms.", "Google Play 服务"),
        ("com.facebook.", "Facebook"),
        ("com.adjust.", "Adjust"),
        ("unity.", "Unity"),
        ("com.applovin.", "AppLovin"),
        ("com.huawei.", "华为 HMS"),
    };

    private static readonly (string Prefix, string Owner)[] Owners =
    {
        ("com.unity3d.player.", "Unity 引擎"),
        ("com.unity3d.ads.", "Unity Ads"),
        ("com.unity3d.services.", "Unity Ads"),
        ("com.google.firebase.messaging.", "Firebase 云消息"),
        ("com.google.firebase.crashlytics.", "Firebase Crashlytics"),
        ("com.google.firebase.", "Firebase"),
        ("com.google.android.gms.ads.", "Google AdMob"),
        ("com.google.android.gms.measurement.", "Google Analytics"),
        ("com.google.android.gms.auth.", "Google 登录"),
        ("com.google.android.gms.games.", "Google Play 游戏服务"),
        ("com.google.android.gms.", "Google Play 服务"),
        ("com.google.android.play.core.", "Google Play Core"),
        ("com.google.android.play.", "Google Play 库"),
        ("com.android.billingclient.", "Google Play 结算"),
        ("com.google.android.datatransport.", "Google 数据传输"),
        ("com.facebook.", "Facebook SDK"),
        ("com.adjust.sdk.", "Adjust SDK"),
        ("com.appsflyer.", "AppsFlyer"),
        ("com.applovin.", "AppLovin"),
        ("com.ironsource.", "ironSource"),
        ("com.bytedance.", "字节跳动/穿山甲"),
        ("com.tencent.mm.", "微信"),
        ("com.tencent.", "腾讯"),
        ("com.huawei.", "华为 HMS"),
        ("com.xiaomi.", "小米"),
        ("androidx.work.", "AndroidX WorkManager"),
        ("androidx.startup.", "AndroidX App Startup"),
        ("androidx.core.content.FileProvider", "AndroidX 文件共享"),
        ("androidx.profileinstaller.", "AndroidX 基线配置文件安装"),
        ("androidx.room.", "AndroidX Room"),
        ("androidx.lifecycle.", "AndroidX Lifecycle"),
        ("androidx.", "AndroidX"),
    };
}
