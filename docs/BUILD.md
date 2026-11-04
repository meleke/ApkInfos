# 开发与发布

[← 返回首页](../README.md)

## 环境要求

| 用途 | 需要 |
|---|---|
| 编译、运行、测试 | [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)（Windows / macOS / Linux 均可） |
| 发布脚本 `publish.ps1` | Windows PowerShell 5.1 或 PowerShell 7 |
| 重新生成测试样本（可选） | Android SDK build-tools、JDK（`keytool` / `jarsigner` / `jar`）、Git Bash |
| 重新生成图标（可选） | Windows PowerShell（使用 System.Drawing） |

IDE 推荐 JetBrains Rider 或 Visual Studio 2022，并安装 Avalonia 插件以获得 XAML 预览。

## 常用命令

```bash
dotnet build ApkInfos.sln -c Release
```

```bash
dotnet test tests/ApkInfos.Core.Tests -c Release
```

```bash
dotnet run --project src/ApkInfos.App
```

也可以带文件参数启动：

```bash
dotnet run --project src/ApkInfos.App -- path/to/app.apk
```

## 项目结构

```
ApkInfos.sln
Directory.Build.props            公共属性（目标框架、版本号、作者、许可证）
src/ApkInfos.Core/               解析库，无 UI 依赖
  Parsing/
    ApkInfoReader.cs             总入口：判断 APK / AAB，汇总所有信息
    AxmlParser.cs                二进制 XML（AXML）
    ArscParser.cs                resources.arsc
    ProtoReader.cs               极简 protobuf 读取器
    ProtoXmlParser.cs            AAB 的 protobuf XML
    ProtoResourceParser.cs       AAB 的 resources.pb
    ResourceTable.cs             统一资源表（label 语言优先级、图标按密度选取）
    ManifestModel.cs             统一 Manifest 模型 + 系统属性 ID 表
    ManifestXmlWriter.cs         输出 XML
    ManifestAnnotator.cs         中文注解词典与规则
    ApkSignatureReader.cs        v1 / v2 / v3 / v3.1 签名
    KeystoreReader.cs            JKS / JCEKS / PKCS12
    SdkDetector.cs               Facebook / Adjust 识别
  Models/                        ApkInfo、CertInfo、FirebaseConfig、SdkInfo、KeystoreEntry
  Util/HashUtil.cs
src/ApkInfos.App/                Avalonia 11 桌面应用（code-behind）
tests/ApkInfos.Core.Tests/       xUnit 测试
  Fixtures/                      测试样本（见下文）
tools/make-icons.ps1             生成 app.png / app.ico / app.icns
tools/make-fixtures.sh           生成测试用 APK / AAB
publish.ps1                      发布脚本
```

## 测试样本

`tests/ApkInfos.Core.Tests/Fixtures/` 中的文件均为自行生成、仅供测试：

| 文件 | 说明 |
|---|---|
| `test.jks` | JKS，storepass / keypass 均为 `123456`，alias：`release`、`debug` |
| `test.p12` | PKCS12，密码 `654321`，alias：`upload` |
| `sample.apk` | 最小 APK，v1 + v2 + v3 签名（`test.jks` / `release`），含虚构的 Firebase、Facebook 配置与 Adjust 组件 |
| `sample.aab` | 最小 AAB，jarsigner 签名（`test.p12` / `upload`） |

> 这些 keystore 和配置值都是公开的测试数据，切勿用于正式签名。

修改 `tools/make-fixtures.sh` 后重新生成（需要 Android SDK 与 JDK）：

```bash
bash tools/make-fixtures.sh
```

## 发布

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```

只发布部分平台：

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1 -Runtimes win-x64
```

指定版本号（写入程序集与 macOS Info.plist）：

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1 -Version 1.1.0
```

发布方式为**自包含 + 单文件 + 部分裁剪 + 压缩**，产物输出到 `dist/`：

| 文件 | 约大小 |
|---|---|
| `ApkInfos-win-x64.exe`（本地使用，不上传） | 20 MB |
| `ApkInfos-win-x64.zip`（内含 `ApkInfos-win-x64.exe`） | 14 MB |
| `ApkInfos-osx-arm64.zip`（内含 `ApkInfos.app`） | 16 MB |
| `ApkInfos-osx-x64.zip`（内含 `ApkInfos.app`） | 16 MB |

mac 的 zip 由脚本手动打包：会写入 Unix 权限位（可执行文件为 0755），否则 macOS 解压后程序无法运行。请直接上传脚本生成的 zip，不要用 Windows 资源管理器重新压缩。

## 发布到 GitHub Releases

1. 修改 `Directory.Build.props` 中的 `<Version>`，并在 `CHANGELOG.md` 中补充更新内容。
2. 运行 `publish.ps1 -Version x.y.z` 生成 `dist/` 下的产物。
3. 在 GitHub 仓库页面 **Releases → Draft a new release**：
   - Tag 填 `vx.y.z`，标题如 `ApkInfos vx.y.z`
   - 说明可打开本地 `CHANGELOG.md`，复制对应版本的 Markdown 原文（不要从网页复制渲染后的文字，否则标题和列表格式会丢失）
   - 把 `dist/` 下的 `ApkInfos-win-x64.zip`、`ApkInfos-osx-arm64.zip`、`ApkInfos-osx-x64.zip` 拖进说明框**下方**的附件区（拖进说明框会提示不支持的文件类型）
4. 发布。

> 请保持附件文件名不变：README 中的下载链接使用 `releases/latest/download/<文件名>`，始终指向最新版本。
>
> Windows 上传的是 zip 而不是 exe：直接上传 exe 容易被网络代理或安全软件拦截，用户下载 exe 也更容易被浏览器拦截。

## 代码风格

- 花括号独占一行（Allman），控制语句一律带花括号，一行一条语句；单表达式成员可写成箭头形式
- 注释使用中文；每个源文件带统一文件头（作者、许可证）
- 新增 Manifest 注解：在 `ManifestAnnotator.cs` 的词典（权限、Feature、Action、meta-data、SDK 归属）中补充即可
