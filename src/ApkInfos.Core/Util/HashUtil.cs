// ----------------------------------------------------------------------------
// ApkInfos - APK / AAB 信息查看工具
// 文件：HashUtil.cs
// 说明：哈希计算与十六进制格式化工具
// 作者：Wade.liuyu (meleke_ly@126.com)
// 创建：2026-11-04
// 许可：Apache License 2.0
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace ApkInfos.Core.Util;

/// <summary>
/// 哈希计算与十六进制格式化工具。
/// </summary>
public static class HashUtil
{
    public static string Md5(byte[] data) => ToHex(MD5.HashData(data));

    public static string Sha1(byte[] data) => ToHex(SHA1.HashData(data));

    public static string Sha256(byte[] data) => ToHex(SHA256.HashData(data));

    /// <summary>
    /// 流式计算文件 MD5，适用于大文件。
    /// </summary>
    public static string FileMd5(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return ToHex(MD5.HashData(stream));
    }

    /// <summary>
    /// 转为大写十六进制；colon 为 true 时以冒号分隔（与 keytool 输出一致）。
    /// </summary>
    public static string ToHex(ReadOnlySpan<byte> bytes, bool colon = false)
    {
        if (!colon)
        {
            return Convert.ToHexString(bytes);
        }

        var sb = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(':');
            }

            sb.Append(bytes[i].ToString("X2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 把连续的十六进制串转为冒号分隔形式。
    /// </summary>
    public static string WithColons(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return hex;
        }

        var sb = new StringBuilder(hex.Length * 3 / 2);
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (i > 0)
            {
                sb.Append(':');
            }

            sb.Append(hex, i, Math.Min(2, hex.Length - i));
        }

        return sb.ToString();
    }
}
