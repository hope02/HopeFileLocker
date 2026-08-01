using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HopeFileLocker;

/// <summary>
/// 登录密码的本地存储与救援码（密码找回）机制。
///
/// 存储（%LOCALAPPDATA%\HopeFileLocker\pwd.dat，JSON）：
///   - PasswordHash：登录密码的 SHA-256 哈希（仅用于校验，不存明文）。
///   - SealedPassword：用「救援码」派生的密钥对【原登录密码】做 AES 加密的密文。
///                     忘密码时凭救援码解开即得原密码，从而保住此前用该密码加密的文件。
///   - SealedRescue：用【登录密码】派生的密钥对【救援码】做 AES 加密的密文。
///                    登录后可用它把救援码显示给用户查看。
///   - RescueCodeHash / PwSalt / RescueSalt：校验与 KDF 盐。
///
/// 设计要点：找回密码时恢复的是“原密码”，因此旧加密数据仍可解密；救援码本身不存明文，
/// 必须凭它或登录密码才能反解，避免明文泄露。
/// </summary>
public static class SecurityHelper
{
    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HopeFileLocker");
    private static readonly string PwdFile = Path.Combine(StoreDir, "pwd.dat");

    private const int SaltSize = 16, IvSize = 16, KeySize = 32, Iterations = 100_000;

    public static bool HasPassword => File.Exists(PwdFile);

    /// <summary>校验登录密码。</summary>
    public static bool Verify(string password)
    {
        if (!HasPassword) return false;
        var s = ReadStore();
        return string.Equals(ComputeHash(password), s.PasswordHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>首次设置密码，返回需要让用户妥善保存的救援码（明文，仅本次显示）。</summary>
    public static string Setup(string password)
    {
        var rescue = GenerateRescueCode();
        WriteStore(BuildStore(password, rescue));
        return rescue;
    }

    /// <summary>
    /// 用救援码找回密码。成功时返回原密码，并重新生成一份新救援码写入存储（旧码可能失效），
    /// 以保证用户后续仍有可用的找回方式。失败（救援码错误/未设密码）返回 false。
    /// </summary>
    public static bool TryRecover(string rescueCode, out string password, out string newRescueCode)
    {
        password = "";
        newRescueCode = "";
        if (!HasPassword) return false;

        var code = NormalizeRescue(rescueCode);
        if (string.IsNullOrEmpty(code)) return false;

        var s = ReadStore();
        if (!string.Equals(ComputeHash(code), s.RescueCodeHash, StringComparison.OrdinalIgnoreCase))
            return false;

        // 用救援码解开原密码
        var key = DeriveKey(code, Convert.FromBase64String(s.RescueSalt));
        var pwdBytes = AesDecrypt(Convert.FromBase64String(s.SealedPassword), key);
        password = Encoding.UTF8.GetString(pwdBytes);

        // 重新生成救援码并写回，确保后续仍可找回
        newRescueCode = GenerateRescueCode();
        WriteStore(BuildStore(password, newRescueCode));
        return true;
    }

    /// <summary>已登录时查看当前救援码（需要用登录密码解密）。</summary>
    public static string? GetRescueCode(string password)
    {
        if (!HasPassword) return null;
        var s = ReadStore();
        var key = DeriveKey(password, Convert.FromBase64String(s.PwSalt));
        try
        {
            return Encoding.UTF8.GetString(AesDecrypt(Convert.FromBase64String(s.SealedRescue), key));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>修改登录密码（救援码保持不变）。</summary>
    public static void ChangePassword(string oldPassword, string newPassword)
    {
        if (!Verify(oldPassword))
            throw new InvalidOperationException("原密码错误。");
        var s = ReadStore();
        var rescue = GetRescueCode(oldPassword)
                     ?? throw new InvalidOperationException("无法读取救援码，无法安全修改密码。");
        WriteStore(BuildStore(newPassword, rescue));
    }

    // ===== 内部 =====

    private sealed class Store
    {
        public string PasswordHash { get; set; } = "";
        public string PwSalt { get; set; } = "";
        public string SealedRescue { get; set; } = "";
        public string RescueSalt { get; set; } = "";
        public string SealedPassword { get; set; } = "";
        public string RescueCodeHash { get; set; } = "";
    }

    private static Store BuildStore(string password, string rescueCode)
    {
        var pwSalt = RandomNumberGenerator.GetBytes(SaltSize);
        var rescueSalt = RandomNumberGenerator.GetBytes(SaltSize);
        var rescueBytes = Encoding.UTF8.GetBytes(rescueCode);

        var sealedRescue = AesEncrypt(rescueBytes, DeriveKey(password, pwSalt));
        var sealedPassword = AesEncrypt(Encoding.UTF8.GetBytes(password), DeriveKey(rescueCode, rescueSalt));

        return new Store
        {
            PasswordHash = ComputeHash(password),
            PwSalt = Convert.ToBase64String(pwSalt),
            SealedRescue = Convert.ToBase64String(sealedRescue),
            RescueSalt = Convert.ToBase64String(rescueSalt),
            SealedPassword = Convert.ToBase64String(sealedPassword),
            RescueCodeHash = ComputeHash(rescueCode)
        };
    }

    private static Store ReadStore()
    {
        if (!File.Exists(PwdFile))
            throw new InvalidOperationException("密码存储不存在，请先设置登录密码。");

        var text = File.ReadAllText(PwdFile);
        // 当前格式为明文 JSON，应以 '{' 开头。若不是，通常是因为该文件由旧版本
        // （加密存储格式）生成，或已损坏，无法兼容读取。给出清晰提示而非原始 JsonException。
        if (string.IsNullOrWhiteSpace(text) || text[0] != '{')
            throw new InvalidOperationException(
                "密码存储文件格式不兼容或已损坏（期望 JSON，但读到了非 JSON 内容）。\n" +
                "这通常是因为使用了旧版本生成的 pwd.dat。请删除该文件后重新设置密码：\n" +
                PwdFile);

        try
        {
            return JsonSerializer.Deserialize<Store>(text)
                   ?? throw new InvalidOperationException("密码存储已损坏。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "密码存储文件格式不兼容或已损坏，请删除后重新设置密码：\n" + PwdFile, ex);
        }
    }

    private static void WriteStore(Store s)
    {
        Directory.CreateDirectory(StoreDir);
        File.WriteAllText(PwdFile, JsonSerializer.Serialize(s));
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
#pragma warning disable SYSLIB0060
        using var pbkdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
#pragma warning restore SYSLIB0060
        return pbkdf.GetBytes(KeySize);
    }

    private static byte[] AesEncrypt(byte[] plain, byte[] key)
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

    private static byte[] AesDecrypt(byte[] cipher, byte[] key)
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

    private static string ComputeHash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string GenerateRescueCode()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 32 位十六进制

    private static string NormalizeRescue(string code)
        => new(code.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());

}
