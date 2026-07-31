using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HopeFileLocker;

/// <summary>
/// 登录密码的本地哈希存储（SHA-256）。
/// 首次运行时输入任意密码即被设为登录密码。
/// </summary>
public static class SecurityHelper
{
    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HopeFileLocker");
    private static readonly string HashFile = Path.Combine(StoreDir, "pwd.dat");

    public static bool HasPassword => File.Exists(HashFile);

    /// <summary>
    /// 校验密码；若尚未设置密码，则把本次输入的密码设为密码并返回 true。
    /// </summary>
    public static bool VerifyOrSet(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        if (!HasPassword)
        {
            SetPassword(password);
            return true;
        }

        var stored = File.ReadAllText(HashFile).Trim();
        return string.Equals(ComputeHash(password), stored, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetPassword(string password)
    {
        Directory.CreateDirectory(StoreDir);
        File.WriteAllText(HashFile, ComputeHash(password));
    }

    private static string ComputeHash(string password)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}
