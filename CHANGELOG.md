# 更新日志

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-11-04

首个版本。

### 新增

- 支持 APK 与 AAB，打开文件或拖入窗口即可解析
- APK 信息：包名、名称、证书 MD5、版本号、内部版本号、Min SDK、权限列表、应用图标（PNG / WebP / 自适应图标）
- 构建信息：Target SDK、Compile SDK、debuggable、allowBackup、ABI 架构、AAB 模块列表
- 签名详情：v1 / v2 / v3 / v3.1 签名方案，SHA1、SHA256、主题、颁发者、有效期、序列号、签名算法
- Keystore 比对：支持 JKS / JCEKS / PKCS12，列出 alias 及指纹，自动匹配并比对当前包签名
- Firebase 配置：从包内资源还原 google-services.json 关键字段，可导出最小可用的 `google-services.json`
- 第三方 SDK：Facebook 配置读取与检查；Adjust 集成情况、原生与 Unity 插件版本识别
- 生成 AndroidManifest.xml，默认附带中文注解，可复制 / 另存为
- 发布 Windows（x64）单文件 exe 与 macOS（Apple 芯片 / Intel）.app

[1.0.0]: https://github.com/meleke/ApkInfos/releases/tag/v1.0.0
