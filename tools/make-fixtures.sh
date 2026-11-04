#!/usr/bin/env bash
# 生成单元测试用的最小 APK / AAB 样本（需 Android SDK build-tools 与 JDK）。
# 用法（Git Bash）：bash tools/make-fixtures.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FIX="$ROOT/tests/ApkInfos.Core.Tests/Fixtures"
SDK="${ANDROID_HOME:-$LOCALAPPDATA/Android/Sdk}"
BT="$(ls -d "$SDK"/build-tools/* | sort -V | tail -1)"
JAR="$(ls -d "$SDK"/platforms/android-* | sort -V | tail -1)/android.jar"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/res/values" "$WORK/res/values-zh-rCN" "$WORK/res/mipmap-mdpi" "$WORK/res/mipmap-xxxhdpi"
cp "$ROOT/src/ApkInfos.App/Assets/app.png" "$WORK/res/mipmap-mdpi/ic_launcher.png"
cp "$ROOT/src/ApkInfos.App/Assets/app.png" "$WORK/res/mipmap-xxxhdpi/ic_launcher.png"

cat > "$WORK/res/values/strings.xml" <<'EOF'
<resources><string name="app_name">Sample App</string></resources>
EOF
cat > "$WORK/res/values-zh-rCN/strings.xml" <<'EOF'
<resources><string name="app_name">示例应用</string></resources>
EOF
cat > "$WORK/AndroidManifest.xml" <<'EOF'
<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.apkinfos.sample">
    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.CAMERA" />
    <application android:label="@string/app_name" android:icon="@mipmap/ic_launcher" android:allowBackup="false" />
</manifest>
EOF

LINK_ARGS=(-I "$JAR" --manifest "$WORK/AndroidManifest.xml" --min-sdk-version 21 --target-sdk-version 34 --version-code 42 --version-name 4.2.0 --debug-mode)

"$BT/aapt2.exe" compile --dir "$WORK/res" -o "$WORK/compiled.zip"

# ---------- APK：v1 + v2 + v3 签名（test.jks / release）----------
"$BT/aapt2.exe" link -o "$WORK/unsigned.apk" "${LINK_ARGS[@]}" "$WORK/compiled.zip"
"$BT/zipalign.exe" -f -p 4 "$WORK/unsigned.apk" "$WORK/aligned.apk"
cmd //c "$(cygpath -w "$BT/apksigner.bat")" sign --ks "$(cygpath -w "$FIX/test.jks")" --ks-pass pass:123456 --ks-key-alias release \
    --v1-signing-enabled true --v2-signing-enabled true --v3-signing-enabled true \
    --out "$(cygpath -w "$FIX/sample.apk")" "$(cygpath -w "$WORK/aligned.apk")"
rm -f "$FIX/sample.apk.idsig"

# ---------- AAB：proto 格式 + jarsigner v1 签名（test.p12 / upload）----------
"$BT/aapt2.exe" link --proto-format -o "$WORK/proto.zip" "${LINK_ARGS[@]}" "$WORK/compiled.zip"
mkdir -p "$WORK/proto" "$WORK/aab/base/manifest" "$WORK/aab/base/lib/arm64-v8a" "$WORK/aab/base/lib/armeabi-v7a"
(cd "$WORK/proto" && unzip -q ../proto.zip)
mv "$WORK/proto/AndroidManifest.xml" "$WORK/aab/base/manifest/AndroidManifest.xml"
mv "$WORK/proto/resources.pb" "$WORK/aab/base/resources.pb"
mv "$WORK/proto/res" "$WORK/aab/base/res"
printf '\x7fELF' > "$WORK/aab/base/lib/arm64-v8a/libsample.so"
printf '\x7fELF' > "$WORK/aab/base/lib/armeabi-v7a/libsample.so"
: > "$WORK/aab/BundleConfig.pb"
rm -f "$FIX/sample.aab"
(cd "$WORK/aab" && jar cMf "$(cygpath -w "$FIX/sample.aab")" .)
jarsigner -keystore "$(cygpath -w "$FIX/test.p12")" -storetype PKCS12 -storepass 654321 "$(cygpath -w "$FIX/sample.aab")" upload > /dev/null

ls -la "$FIX"
