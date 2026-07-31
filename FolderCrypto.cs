using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace HopeFileLocker;

/// <summary>
/// 文件夹 AES-256 加密 / 解密。
/// 策略：遍历文件夹内所有文件，用从登录密码派生的密钥（PBKDF2 + 随机盐）进行
/// AES-CBC 加密，重命名为 “原名.locked”，并写入一个（隐藏的）清单文件记录
/// 原始路径映射，便于精确还原。解密时反向操作并删除清单。
///
/// 实现要点（性能）：
///  - 采用「分块流式」读写（81920 字节缓冲），无论文件多大内存占用都恒定，不会整文件读进 RAM。
///  - 密码派生（PBKDF2）只在每个文件夹开始时做一次，不随文件数放大。
///  - 支持 IProgress 进度回报，便于 UI 显示进度 / 转圈，且全程可在后台线程执行，不卡界面。
/// </summary>
public static class FolderCrypto
{
    private const string ManifestName = ".hope_manifest.json";
    private const string LockedExt = ".locked";
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;          // AES-256
    private const int Iterations = 100_000;
    private const int BufferSize = 81920;     // 分块流式读写缓冲

    /// <summary>进度回报：当前已处理文件数、总文件数、当前文件名。</summary>
    public sealed class ProgressInfo
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string? FileName { get; set; }
    }

    public static void Encrypt(string folderPath, string password,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);

        var manifest = new Manifest
        {
            Salt = Convert.ToBase64String(salt),
            Files = new List<FileEntry>()
        };

        var dir = new DirectoryInfo(folderPath);
        var files = dir.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => f.Name != ManifestName &&
                        !f.Extension.Equals(LockedExt, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressInfo
            {
                Current = done,
                Total = files.Count,
                FileName = file.FullName
            });

            var rel = Path.GetRelativePath(dir.FullName, file.FullName);
            var encName = rel + LockedExt;
            var encPath = Path.Combine(dir.FullName, encName);
            Directory.CreateDirectory(Path.GetDirectoryName(encPath)!);

            // 流式：边读边加密写入 .locked 临时文件，避免在内存里整文件展开
            using (var inFs = file.OpenRead())
            using (var outFs = new FileStream(encPath, FileMode.Create, FileAccess.Write,
                       FileShare.None, BufferSize, FileOptions.SequentialScan))
            {
                EncryptStream(inFs, outFs, key);
            }

            file.Delete();
            manifest.Files.Add(new FileEntry { Orig = rel, Enc = encName });
            done++;
        }

        var manifestPath = Path.Combine(dir.FullName, ManifestName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        new FileInfo(manifestPath).Attributes |= FileAttributes.Hidden;
    }

    public static void Decrypt(string folderPath, string password,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var dir = new DirectoryInfo(folderPath);
        var manifestPath = Path.Combine(dir.FullName, ManifestName);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("未找到加密清单，该文件夹可能未加密或清单已丢失。");

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidOperationException("加密清单已损坏。");

        var key = DeriveKey(password, Convert.FromBase64String(manifest.Salt!));

        var entries = manifest.Files ?? new List<FileEntry>();
        int done = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressInfo
            {
                Current = done,
                Total = entries.Count,
                FileName = entry.Orig
            });

            var encPath = Path.Combine(dir.FullName, entry.Enc!);
            if (!File.Exists(encPath)) { done++; continue; }

            var outPath = Path.Combine(dir.FullName, entry.Orig!);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            using (var inFs = File.OpenRead(encPath))
            using (var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write,
                       FileShare.None, BufferSize, FileOptions.SequentialScan))
            {
                DecryptStream(inFs, outFs, key);
            }

            File.Delete(encPath);
            done++;
        }

        File.Delete(manifestPath);
    }

    public static bool IsEncrypted(string folderPath)
        => File.Exists(Path.Combine(folderPath, ManifestName));

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        // .NET 10 起 Rfc2898DeriveBytes 构造函数已过时，改用静态 Pbkdf2 方法
        return Rfc2898DeriveBytes.Pbkdf2(
            password, salt, HashAlgorithmName.SHA256, Iterations, KeySize);
    }

    /// <summary>流式加密：先写 IV，再从输入流分块加密写入输出流。</summary>
    private static void EncryptStream(Stream input, Stream output, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        output.Write(aes.IV, 0, IvSize);
        using var cs = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        input.CopyTo(cs, BufferSize);
        cs.FlushFinalBlock();
    }

    /// <summary>流式解密：从输入流读 IV，再分块解密写入输出流。</summary>
    private static void DecryptStream(Stream input, Stream output, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var iv = new byte[IvSize];
        if (input.ReadExactly(iv, 0, IvSize) != IvSize)
            throw new InvalidOperationException("加密文件头损坏，无法读取 IV。");

        aes.IV = iv;
        using var cs = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        cs.CopyTo(output, BufferSize);
    }

    private sealed class Manifest
    {
        public string? Salt { get; set; }
        public List<FileEntry>? Files { get; set; }
    }

    private sealed class FileEntry
    {
        public string? Orig { get; set; }
        public string? Enc { get; set; }
    }
}
