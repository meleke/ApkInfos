using System.Text;
using ApkInfos.Core.Models;
using ApkInfos.Core.Parsing;
using ApkInfos.Core.Util;

namespace ApkInfos.Core.Tests;

public class HashUtilTests
{
    [Fact]
    public void Md5_And_Colon_Format()
    {
        string md5 = HashUtil.Md5(Encoding.ASCII.GetBytes("abc"));
        Assert.Equal("900150983CD24FB0D6963F7D28E17F72", md5);
        Assert.Equal("90:01:50", HashUtil.WithColons("900150"));
    }
}

public class ProtoReaderTests
{
    [Fact]
    public void Reads_Varint_And_String()
    {
        // field 1 varint 300；field 2 字符串 "hi"
        byte[] data = { 0x08, 0xAC, 0x02, 0x12, 0x02, (byte)'h', (byte)'i' };
        var reader = new ProtoReader(data);
        Assert.True(reader.TryReadTag(out int f1, out int w1));
        Assert.Equal(1, f1);
        Assert.Equal(ProtoReader.WireVarint, w1);
        Assert.Equal(300UL, reader.ReadVarint());
        Assert.True(reader.TryReadTag(out int f2, out _));
        Assert.Equal(2, f2);
        Assert.Equal("hi", reader.ReadString());
        Assert.False(reader.TryReadTag(out _, out _));
    }
}

public class ResourceTableTests
{
    [Theory]
    [InlineData("res/mipmap-xxxhdpi-v4/ic.png", 640)]
    [InlineData("res/mipmap-mdpi/ic.png", 160)]
    [InlineData("res/mipmap-anydpi-v26/ic.xml", ResourceTable.DensityAny)]
    [InlineData("res/drawable/ic.png", 0)]
    public void Density_From_Path(string path, int expected)
    {
        Assert.Equal(expected, ResourceTable.DensityFromPath(path));
    }

    [Theory]
    [InlineData("zh-rCN", "zh-cn")]
    [InlineData("zh_CN", "zh-cn")]
    [InlineData("b+zh+Hans", "zh-hans")]
    [InlineData("", "")]
    public void Normalize_Locale(string input, string expected)
    {
        Assert.Equal(expected, ResourceTable.NormalizeLocale(input));
    }
}

public class KeystoreReaderTests
{
    private const string ReleaseSha256 = "E2A702020D3079A8C99882031FFBDB44BB00C9055ADBE2259679C48E66C07FAF";
    private const string DebugSha256 = "67290E017C65E35D86E3789624EADE714E5A9707847486A4F2EF9A943BB9C47D";
    private const string UploadSha256 = "05E068845C96F559D0D1B0E39D44EA1A343955E24B260EA4A56E08AD5A7DEC51";

    [Fact]
    public void Jks_Lists_Aliases_With_Fingerprints()
    {
        var ks = KeystoreReader.Load(Fixture.Path("test.jks"), "123456");
        Assert.Equal("JKS", ks.Format);
        Assert.True(ks.IntegrityVerified);
        Assert.Equal(2, ks.Entries.Count);
        Assert.Equal(ReleaseSha256, ks.Entries.Single(e => e.Alias == "release").Certificate!.Sha256);
        Assert.Equal(DebugSha256, ks.Entries.Single(e => e.Alias == "debug").Certificate!.Sha256);
        Assert.All(ks.Entries, e => Assert.Equal("PrivateKeyEntry", e.EntryType));
    }

    [Fact]
    public void Jks_Without_Password_Skips_Integrity_Check()
    {
        var ks = KeystoreReader.Load(Fixture.Path("test.jks"), "");
        Assert.False(ks.IntegrityVerified);
        Assert.Equal(2, ks.Entries.Count);
    }

    [Fact]
    public void Jks_Wrong_Password_Throws()
    {
        Assert.Throws<KeystorePasswordException>(() => KeystoreReader.Load(Fixture.Path("test.jks"), "wrong"));
    }

    [Fact]
    public void Pkcs12_Lists_Alias_With_Fingerprint()
    {
        var ks = KeystoreReader.Load(Fixture.Path("test.p12"), "654321");
        Assert.Equal("PKCS12", ks.Format);
        var entry = Assert.Single(ks.Entries);
        Assert.Equal("upload", entry.Alias);
        Assert.Equal(UploadSha256, entry.Certificate!.Sha256);
    }

    [Fact]
    public void Pkcs12_Wrong_Password_Throws()
    {
        Assert.Throws<KeystorePasswordException>(() => KeystoreReader.Load(Fixture.Path("test.p12"), "wrong"));
    }
}

public class ApkInfoReaderTests
{
    [Fact]
    public void Reads_Sample_Apk()
    {
        var info = ApkInfoReader.Read(Fixture.Path("sample.apk"));
        Assert.Equal("APK", info.PackageType);
        AssertCommonFields(info);
        Assert.Equal(new[] { "v1", "v2", "v3" }, info.SignatureSchemes);
        Assert.Equal("res/mipmap-xxxhdpi-v4/ic_launcher.png", info.IconPath);

        // 与 keystore 中 release 条目比对
        var ks = KeystoreReader.Load(Fixture.Path("test.jks"), "123456");
        Assert.True(info.PrimaryCertificate!.SameAs(ks.Entries.Single(e => e.Alias == "release").Certificate));
        Assert.False(info.PrimaryCertificate!.SameAs(ks.Entries.Single(e => e.Alias == "debug").Certificate));
        Assert.Contains("android:versionCode=\"42\"", info.ManifestXml);
    }

    [Fact]
    public void Reads_Sample_Aab()
    {
        var info = ApkInfoReader.Read(Fixture.Path("sample.aab"));
        Assert.Equal("AAB", info.PackageType);
        AssertCommonFields(info);
        Assert.Equal(new[] { "v1" }, info.SignatureSchemes);
        Assert.Equal(new[] { "base" }, info.Modules);
        Assert.Equal(new[] { "arm64-v8a", "armeabi-v7a" }, info.Abis);
        Assert.Equal("base/res/mipmap-xxxhdpi-v4/ic_launcher.png", info.IconPath);

        var ks = KeystoreReader.Load(Fixture.Path("test.p12"), "654321");
        Assert.True(info.PrimaryCertificate!.SameAs(ks.Entries[0].Certificate));
        Assert.Contains("android:label=\"@string/app_name\"", info.ManifestXml);
    }

    private static void AssertCommonFields(ApkInfo info)
    {
        Assert.Equal("com.apkinfos.sample", info.PackageName);
        Assert.Equal("示例应用", info.AppLabel);
        Assert.Equal("4.2.0", info.VersionName);
        Assert.Equal("42", info.VersionCode);
        Assert.Equal("21", info.MinSdk);
        Assert.Equal("34", info.TargetSdk);
        Assert.Equal("true", info.Debuggable);
        Assert.Equal("false", info.AllowBackup);
        Assert.Equal(new[] { "android.permission.INTERNET", "android.permission.CAMERA" }, info.Permissions);
        Assert.NotNull(info.IconBytes);
        Assert.Equal(HashUtil.FileMd5(info.FilePath), info.FileMd5);
    }
}

internal static class Fixture
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
