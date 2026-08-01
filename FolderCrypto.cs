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
///
/// 密文格式（.locked 文件头部）：
///   [Magic "HLK1" 4 字节][文件名 IV 16][加密文件名长度 4][加密文件名][内容 IV 16][密文]
/// 文件名与内容均用同一派生密钥加密；磁盘上的 .locked 文件名本身是「原相对路径的 SHA-256 哈希」（不透明名），
/// 原始文件名绝不以明文出现在文件系统中，仅在 .locked 文件头内以密文保存，解密时还原。
///
/// 向后兼容：
///   - 若头部前 4 字节不是 "HLK1"，视为旧版（明文文件名）格式 [IV 16][名称长度 4][名称 UTF-8][密文]。
///   - 若目标文件夹下仍存在旧版 .hope_manifest.json，则按清单格式（IV + 密文，映射在清单中）解密。
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

    // 新格式标识（4 字节）。旧格式首字节是随机 IV，几乎不可能恰好等于 "HLK1"。
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("HLK1");

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

        if (File.Exists(path))
        {
            string lockedPath;
            if (path.EndsWith(LockedExt, StringComparison.OrdinalIgnoreCase))
            {
                // 传入的就是 .locked 文件本身
                lockedPath = path;
            }
            else
            {
                // 传入的是原始文件路径（单文件）：解析其确定性的 .locked 名称后解密
                lockedPath = Path.Combine(Path.GetDirectoryName(path)!, OpaqueName(Path.GetFileName(path)));
            }
            var baseDir = Path.GetDirectoryName(lockedPath)!;
            if (File.Exists(lockedPath))
            {
                DecryptItem(lockedPath, baseDir, key, progress, ct);
                return;
            }
        }

        // 文件夹（新格式 / 旧明文文件名格式）
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
        {
            if (path.EndsWith(LockedExt, StringComparison.OrdinalIgnoreCase)) return true;
            // 原始文件路径：检查其确定性的 .locked 兄弟文件是否存在
            var lockedPath = Path.Combine(Path.GetDirectoryName(path)!, OpaqueName(Path.GetFileName(path)));
            return File.Exists(lockedPath);
        }
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
        // 磁盘文件名使用不透明（哈希）名，避免原始文件名暴露在文件系统中；
        // 原始相对路径仅以密文形式保存在 .locked 文件头内，解密时还原。
        var encPath = Path.Combine(baseDir, OpaqueName(relName));
        Directory.CreateDirectory(Path.GetDirectoryName(encPath)!);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // 文件名加密，避免明文暴露原始文件名
        var nameBytes = Encoding.UTF8.GetBytes(relName);
        var nameIv = RandomNumberGenerator.GetBytes(IvSize);
        var encName = EncryptBytes(nameBytes, key, nameIv);

        using var inFs = File.OpenRead(inputPath);
        using var outFs = new FileStream(encPath, FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.SequentialScan);

        outFs.Write(Magic, 0, Magic.Length);
        outFs.Write(nameIv, 0, IvSize);
        outFs.Write(BitConverter.GetBytes(encName.Length), 0, 4);
        outFs.Write(encName, 0, encName.Length);
        outFs.Write(aes.IV, 0, IvSize);

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

        var magic = new byte[Magic.Length];
        if (ReadExact(inFs, magic, 0, Magic.Length) != Magic.Length)
            throw new InvalidOperationException("加密文件头损坏，无法读取标识。");

        string relName;
        byte[] contentIv;

        if (magic.AsSpan().SequenceEqual(Magic))
        {
            // 新格式：文件名已加密
            var nameIv = new byte[IvSize];
            if (ReadExact(inFs, nameIv, 0, IvSize) != IvSize)
                throw new InvalidOperationException("加密文件头损坏，无法读取文件名 IV。");
            var lenBytes = new byte[4];
            if (ReadExact(inFs, lenBytes, 0, 4) != 4)
                throw new InvalidOperationException("加密文件头损坏，无法读取文件名长度。");
            var encNameLen = BitConverter.ToInt32(lenBytes, 0);
            if (encNameLen < 0 || encNameLen > 8192)
                throw new InvalidOperationException("加密文件头损坏，文件名长度异常。");
            var encName = new byte[encNameLen];
            if (ReadExact(inFs, encName, 0, encNameLen) != encNameLen)
                throw new InvalidOperationException("加密文件头损坏，无法读取文件名。");
            var nameBytes = DecryptBytes(encName, key, nameIv);
            relName = Encoding.UTF8.GetString(nameBytes);

            contentIv = new byte[IvSize];
            if (ReadExact(inFs, contentIv, 0, IvSize) != IvSize)
                throw new InvalidOperationException("加密文件头损坏，无法读取内容 IV。");
        }
        else
        {
            // 旧格式（明文文件名）：已读的 magic 实际是 IV 的前 4 字节
            var iv = new byte[IvSize];
            Array.Copy(magic, 0, iv, 0, Magic.Length);
            if (ReadExact(inFs, iv, Magic.Length, IvSize - Magic.Length) != IvSize - Magic.Length)
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
            relName = Encoding.UTF8.GetString(nameBytes);
            contentIv = iv;
        }

        var outPath = Path.Combine(baseDir, relName);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = contentIv;
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

    /// <summary>用给定密钥与 IV 加密一段明文（用于加密文件名）。</summary>
    private static byte[] EncryptBytes(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    /// <summary>用给定密钥与 IV 解密一段密文（用于还原文件名）。</summary>
    private static byte[] DecryptBytes(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var ms = new MemoryStream(data);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var outMs = new MemoryStream();
        cs.CopyTo(outMs);
        return outMs.ToArray();
    }

    /// <summary>
    /// 生成确定性的不透明文件名：SHA-256(relName) + .locked。
    /// 同一 relName 永远映射到同一磁盘名，便于在无外部清单的情况下反向定位 .locked 文件，
    /// 同时不暴露原始文件名（注意：任何人都能看到哈希对应的 .locked 文件，但拿不到原文件名）。
    /// </summary>
    private static string OpaqueName(string relName)
        => Convert.ToHexString(
               SHA256.HashData(Encoding.UTF8.GetBytes(relName + "\0HLK")))
           + LockedExt;

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
