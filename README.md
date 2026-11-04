# ApkInfos

跨平台（Windows / macOS）的 APK / AAB 信息查看工具，C# + .NET 9 + Avalonia 实现，解析逻辑为纯 C#，不依赖 JDK / Android SDK。

## 功能

- 打开或**拖拽** `.apk` / `.aab` 文件（也可把文件拖到 exe 图标上、或作为命令行参数传入）
- **APK 信息**：包名、名称（优先中文）、证书 MD5、版本号、内部版本号、Min SDK、权限列表、应用图标
- **构建信息**：Target SDK、Compile SDK、debuggable、allowBackup、ABI 架构、AAB 模块列表
- **签名详情**：签名方案（v1 / v2 / v3 / v3.1）、SHA1、SHA256、主题、颁发者、有效期、序列号、签名算法
- **Keystore 比对**：加载 `.jks` / `.keystore`（JKS、JCEKS）或 `.p12` / `.pfx`（PKCS12），列出所有 alias 及指纹，自动比对是否与当前包签名一致（也可直接把 keystore 文件拖进窗口）
- **生成 AndroidManifest.xml**：把 APK 的二进制 AXML / AAB 的 protobuf XML 还原为可读 XML，可复制或另存为
- **文件信息**：文件名、路径、类型、MD5、大小

## 目录结构

```
src/ApkInfos.Core/     解析库（AXML、resources.arsc、AAB protobuf、签名块、keystore）
src/ApkInfos.App/      Avalonia 桌面界面
tests/                 xUnit 单元测试（Fixtures 内为自签名的测试样本与 keystore）
tools/make-icons.ps1   生成应用图标（png / ico / icns）
tools/make-fixtures.sh 用 Android SDK 重新生成测试样本
publish.ps1            一键发布
```

## 构建与测试

```bash
dotnet build ApkInfos.sln -c Release
dotnet test tests/ApkInfos.Core.Tests -c Release
```

## 发布

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```

输出到 `dist/`（自包含 + 单文件 + 部分裁剪 + 压缩，使用者无需安装 .NET）：

| 产物 | 说明 |
|---|---|
| `ApkInfos-win-x64.exe` | Windows 单文件 exe |
| `ApkInfos-osx-arm64.zip` | macOS Apple 芯片（M 系列），解压得到 `ApkInfos.app` |
| `ApkInfos-osx-x64.zip` | macOS Intel 芯片 |

### macOS 首次运行

mac 版在 Windows 上交叉编译，未经 Apple 签名与公证。解压后在「终端」执行一次：

```bash
xattr -cr ApkInfos.app
codesign --force --deep -s - ApkInfos.app
```

第一条去掉下载隔离标记，第二条做本机 ad-hoc 签名（Apple 芯片要求可执行文件必须带签名）。之后即可双击打开。如需正式分发，请在 Mac 上用开发者证书 `codesign` 并 `notarytool` 公证。
