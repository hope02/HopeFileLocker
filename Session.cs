namespace HopeFileLocker;

/// <summary>
/// 进程内会话，保存登录密码（用于派生 AES 密钥）。
/// 仅存在于内存中，关闭程序即失效。
/// </summary>
public static class Session
{
    public static string? Password { get; set; }
}
