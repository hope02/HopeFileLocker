using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace HopeFileLocker;

/// <summary>
/// 文件夹 AES-256 加密 / 解密。
/// 策略：遍历文件夹内所有文件，用从登录密码派生的密钥（PBKDF2）进行
/// AES-CBC 加密，重命名为 “原名.locked”，并写入一个（隐藏的）清单文件记录
/// 原始路径映射，便于精确还原。解密时反向操作并删除清单。
/// </summary>
public static class FolderCrypto
{
    private const string ManifestName = ".hope_manifest.json";
    private const string LockedExt = ".locked";
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;          // AES-256
    private const int Iterations = 100_000;

    public static void Encrypt(string folderPath, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);

        var manifest = new Manifest
        {
            Salt = Convert.ToBase64String(salt),
            Files = new List<FileEntry>()
        };

        var dir = new DirectoryInfo(folderPath);
        foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            if (file.Name == ManifestName) continue;
            if (file.Extension.Equals(LockedExt, StringComparison.OrdinalIgnoreCase)) continue;

            var rel = Path.GetRelativePath(dir.FullName, file.FullName);
            var encName = rel + LockedExt;
            var encPath = Path.Combine(dir.FullName, encName);
            Directory.CreateDirectory(Path.GetDirectoryName(encPath)!);

            var plain = File.ReadAllBytes(file.FullName);
            File.WriteAllBytes(encPath, EncryptBytes(plain, key));
            file.Delete();

            manifest.Files.Add(new FileEntry { Orig = rel, Enc = encName });
        }

        var manifestPath = Path.Combine(dir.FullName, ManifestName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        new FileInfo(manifestPath).Attributes |= FileAttributes.Hidden;
    }

    public static void Decrypt(string folderPath, string password)
    {
        var dir = new DirectoryInfo(folderPath);
        var manifestPath = Path.Combine(dir.FullName, ManifestName);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("未找到加密清单，该文件夹可能未加密或清单已丢失。");

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidOperationException("加密清单已损坏。");

        var key = DeriveKey(password, Convert.FromBase64String(manifest.Salt!));

        foreach (var entry in manifest.Files ?? new List<FileEntry>())
        {
            var encPath = Path.Combine(dir.FullName, entry.Enc!);
            if (!File.Exists(encPath)) continue;

            var cipher = File.ReadAllBytes(encPath);
            var plain = DecryptBytes(cipher, key);
            var outPath = Path.Combine(dir.FullName, entry.Orig!);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllBytes(outPath, plain);
            File.Delete(encPath);
        }

        File.Delete(manifestPath);
    }

    public static bool IsEncrypted(string folderPath)
        => File.Exists(Path.Combine(folderPath, ManifestName));

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf.GetBytes(KeySize);
    }

    private static byte[] EncryptBytes(byte[] plain, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, IvSize);
        using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        cs.Write(plain, 0, plain.Length);
        cs.FlushFinalBlock();
        return ms.ToArray();
    }

    private static byte[] DecryptBytes(byte[] cipher, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var iv = new byte[IvSize];
        Array.Copy(cipher, 0, iv, 0, IvSize);
        aes.IV = iv;

        using var ms = new MemoryStream(cipher, IvSize, cipher.Length - IvSize);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var outMs = new MemoryStream();
        cs.CopyTo(outMs);
        return outMs.ToArray();
    }

    private class Manifest
    {
        public string? Salt { get; set; }
        public List<FileEntry>? Files { get; set; }
    }

    private class FileEntry
    {
        public string? Orig { get; set; }
        public string? Enc { get; set; }
    }
}
