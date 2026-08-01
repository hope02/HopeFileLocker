using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HopeFileLocker;

/// <summary>
/// 持久化「托管目录 / 文件」列表，使其在程序重启 / 重新编译后仍然存在。
///
/// 仅保存路径字符串；隐藏 / 加密状态在加载时由 <see cref="FileHider"/> 与
/// <see cref="FolderCrypto"/> 重新探测（不依赖会话密码）。
///
/// 存储位置：%LOCALAPPDATA%\HopeFileLocker\folders.json（与 pwd.dat 同目录）。
/// 写文件采用「临时文件 + 覆盖移动」以避免半截写入导致文件损坏。
/// </summary>
public static class FolderListStore
{
    /// <summary>
    /// 优先存软件（exe）目录：满足「便携 / 存软件目录」诉求，zip 解压版可写即生效。
    /// 若 exe 目录不可写（如安装到 C:\Program Files 普通用户无权限），回退到每用户
    /// %LOCALAPPDATA%\HopeFileLocker，避免静默保存失败导致列表再次丢失。
    /// </summary>
    private static string Dir
    {
        get
        {
            try
            {
                var exeDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir) && IsWritable(exeDir))
                    return exeDir;
            }
            catch
            {
                // 探测失败，走下方回退
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HopeFileLocker");
        }
    }

    private static string FilePath => Path.Combine(Dir, "folders.json");

    /// <summary>探测目录是否可写（创建 + 写测试文件 + 删除）。不可写返回 false。</summary>
    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".writetest_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读取已保存的托管路径；跳过不存在 / 重复项。文件缺失或损坏时返回空列表。</summary>
    public static List<string> Load()
    {
        if (!File.Exists(FilePath)) return new List<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
            return (list ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
                .Distinct()
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>保存托管路径列表（去重）。持久化失败不影响主流程。</summary>
    public static void Save(IEnumerable<string> paths)
    {
        try
        {
            var dir = Dir;
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "folders.json");
            var json = JsonSerializer.Serialize(paths.Distinct().ToList());
            var tmp = filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, filePath, overwrite: true);
        }
        catch
        {
            // 忽略：列表持久化失败不应中断加密 / 隐藏等核心操作
        }
    }
}
