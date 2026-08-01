using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace HopeFileLocker;

/// <summary>
/// 文件夹 / 单个文件的 AES-256 加密 / 解密。
///
/// 密钥：由登录密码经 PBKDF2（SHA-256，100,000 次迭代）派生，使用固定的应用盐。
/// 密文格式（每个 .locked 文件头部）：
///   [IV 16 字节][原相对路径长度 4 字节][原相对路径 UTF-8][密文]
/// 这样无需再写明文清单即可在解密时还原原始文件名与目录结构，既支持文件夹也支持单文件。
///
/// 向后兼容：若目标文件夹下仍存在旧版 .hope_manifest.json，则按旧格式（IV + 密文，
/// 文件名映射在清单中）解密，保证此前已加密的数据仍可还原。
///
/// 性能：分块流式读写（81920 字节缓冲），内存占用恒定；支持 IProgress 进度回报，
/// 可在后台线程执行以不卡界面。
/// </summary>
public static class FolderCrypto
{
    private const string LockedExt = ".locked";
    private const string LegacyManifestName = ".hope_manifest.json";
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;          // AES-256
    private const int Iterations = 100_000;
    private const int BufferSize = 81920;

    // 固定应用盐（非机密，仅用于 KDF 唯一性）
    private static readonly byte[] AppSalt = Encoding.UTF8.GetBytes("HopeFileLocker!");

    /// <summary>进度回报：当前已处理文件数、总文件数、当前文件名。</summary>
    public sealed class ProgressInfo
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string? FileName { get; set; }
    }

    public static void Encrypt(string path, string password,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        var key = DeriveKey(password, AppSalt);

        if (File.Exists(path))
        {
            // 单文件
            var baseDir = Path.GetDirectoryName(path)!;
            var relName = Path.GetFileName(path);
            EncryptItem(path, baseDir, relName, key, progress, ct);
            return;
        }

        // 文件夹
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        var files = dir.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Extension.Equals(LockedExt, StringComparison.OrdinalIgnoreCase)
                        && f.Name != LegacyManifestName)
            .ToList();

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressInfo { Current = done, Total = files.Count, FileName = file.FullName });
            var relName = Path.GetRelativePath(dir.FullName, file.FullName);
            EncryptItem(file.FullName, dir.FullName, relName, key, progress, ct);
            done++;
        }
    }

    public static void Decrypt(string path, string password,
        IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        // 旧版清单格式：整文件夹向后兼容
        if (Directory.Exists(path) && File.Exists(Path.Combine(path, LegacyManifestName)))
        {
            DecryptLegacy(path, password, progress, ct);
            return;
        }

        var key = DeriveKey(password, AppSalt);

        if (File.Exists(path) && path.EndsWith(LockedExt, StringComparison.OrdinalIgnoreCase))
        {
            // 单文件
            var baseDir = Path.GetDirectoryName(path)!;
            DecryptItem(path, baseDir, key, progress, ct);
            return;
        }

        // 文件夹（新格式）
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        var locked = dir.GetFiles("*" + LockedExt, SearchOption.AllDirectories).ToList();

        int done = 0;
        foreach (var file in locked)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressInfo { Current = done, Total = locked.Count, FileName = file.FullName });
            DecryptItem(file.FullName, dir.FullName, key, progress, ct);
            done++;
        }
    }

    public static bool IsEncrypted(string path)
    {
        if (File.Exists(path))
            return path.EndsWith(LockedExt, StringComparison.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(path);
        return dir.Exists &&
               (File.Exists(Path.Combine(path, LegacyManifestName)) ||
                dir.GetFiles("*" + LockedExt, SearchOption.AllDirectories).Length > 0);
    }

    // ===== 新格式：单文件处理 =====

    private static void EncryptItem(string inputPath, string baseDir, string relName,
        byte[] key, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var encPath = Path.Combine(baseDir, relName + LockedExt);
        Directory.CreateDirectory(Path.GetDirectoryName(encPath)!);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var inFs = File.OpenRead(inputPath);
        using var outFs = new FileStream(encPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.SequentialScan);

        outFs.Write(aes.IV, 0, IvSize);
        var nameBytes = Encoding.UTF8.GetBytes(relName);
        outFs.Write(BitConverter.GetBytes(nameBytes.Length), 0, 4);
        outFs.Write(nameBytes, 0, nameBytes.Length);

        using var cs = new CryptoStream(outFs, aes.CreateEncryptor(), CryptoStreamMode.Write);
        inFs.CopyTo(cs, BufferSize);
        cs.FlushFinalBlock();

        // 与解密同理：先释放对原文件 inputPath 的读句柄，再删除，避免被自身占用而抛 IOException。
        inFs.Dispose();
        SafeDelete(inputPath);
    }

    private static void DecryptItem(string encPath, string baseDir, byte[] key,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var inFs = File.OpenRead(encPath);

        var iv = new byte[IvSize];
        if (ReadExact(inFs, iv, 0, IvSize) != IvSize)
            throw new InvalidOperationException("加密文件头损坏，无法读取 IV。");
        var lenBytes = new byte[4];
        if (ReadExact(inFs, lenBytes, 0, 4) != 4)
            throw new InvalidOperationException("加密文件头损坏，无法读取文件名长度。");
        var nameLen = BitConverter.ToInt32(lenBytes, 0);
        if (nameLen < 0 || nameLen > 8192)
            throw new InvalidOperationException("加密文件头损坏，文件名长度异常。");
        var nameBytes = new byte[nameLen];
        if (ReadExact(inFs, nameBytes, 0, nameLen) != nameLen)
            throw new InvalidOperationException("加密文件头损坏，无法读取文件名。");

        var relName = Encoding.UTF8.GetString(nameBytes);
        var outPath = Path.Combine(baseDir, relName);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.SequentialScan);
        using var cs = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read);
        cs.CopyTo(outFs, BufferSize);

        // inFs 以默认 FileShare（不含 Delete）打开；若不先释放，当前进程仍持有 encPath 句柄，
        // File.Delete 会抛 IOException: "The process cannot access the file ... used by another process"（被自身占用）。
        inFs.Dispose();
        SafeDelete(encPath);
    }

    // ===== 旧版清单格式（向后兼容解密） =====

    private static void DecryptLegacy(string folderPath, string password,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var dir = new DirectoryInfo(folderPath);
        var manifestPath = Path.Combine(folderPath, LegacyManifestName);
        var manifest = JsonSerializer.Deserialize<LegacyManifest>(
            File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException("加密清单已损坏。");

        var key = DeriveKey(password, Convert.FromBase64String(manifest.Salt!));
        var entries = manifest.Files ?? new List<LegacyEntry>();
        int done = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressInfo { Current = done, Total = entries.Count, FileName = entry.Orig });
            var encPath = Path.Combine(folderPath, entry.Enc!);
            if (!File.Exists(encPath)) { done++; continue; }

            var outPath = Path.Combine(folderPath, entry.Orig!);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            LegacyDecryptFile(encPath, outPath, key);
            File.Delete(encPath);
            done++;
        }
        File.Delete(manifestPath);
    }

    private static void LegacyDecryptFile(string encPath, string outPath, byte[] key)
    {
        using var inFs = File.OpenRead(encPath);
        var iv = new byte[IvSize];
        if (ReadExact(inFs, iv, 0, IvSize) != IvSize)
            throw new InvalidOperationException("加密文件头损坏，无法读取 IV。");
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.SequentialScan);
        using var cs = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read);
        cs.CopyTo(outFs, BufferSize);
    }

    /// <summary>从流中精确读取 count 字节，返回实际读取数（提前 EOF 时小于 count）。
    /// 自带此方法以规避 Stream.ReadExactly 在不同重载下返回 void 导致的编译歧义。</summary>
    private static int ReadExact(Stream s, byte[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buffer, offset + read, count - read);
            if (n == 0) break; // 提前到达 EOF
            read += n;
        }
        return read;
    }

    /// <summary>删除文件，对瞬时外部占用（如杀毒软件、索引器）做少量重试；仍失败则原样抛出。</summary>
    private static void SafeDelete(string path, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (i < retries - 1)
            {
                Thread.Sleep(120);
            }
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
#pragma warning disable SYSLIB0060
        using var pbkdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
#pragma warning restore SYSLIB0060
        return pbkdf.GetBytes(KeySize);
    }

    private sealed class LegacyManifest
    {
        public string? Salt { get; set; }
        public List<LegacyEntry>? Files { get; set; }
    }

    private sealed class LegacyEntry
    {
        public string? Orig { get; set; }
        public string? Enc { get; set; }
    }
}
