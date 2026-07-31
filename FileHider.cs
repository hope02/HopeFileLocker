using System.IO;

namespace HopeFileLocker;

/// <summary>
/// 通过 Windows 文件属性（底层为 Win32 SetFileAttributes）实现文件夹的
/// 隐藏 / 显示。隐藏时同时设置 Hidden 与 System 属性，使其在资源管理器中彻底不可见。
/// </summary>
public static class FileHider
{
    private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

    /// <summary>设置文件夹及其内容的 Hidden + System 属性。</summary>
    public static void Hide(string folderPath)
    {
        var dir = new DirectoryInfo(folderPath);
        if (!dir.Exists) return;
        Apply(dir, add: true);
    }

    /// <summary>移除文件夹及其内容的 Hidden + System 属性。</summary>
    public static void Show(string folderPath)
    {
        var dir = new DirectoryInfo(folderPath);
        if (!dir.Exists) return;
        Apply(dir, add: false);
    }

    public static bool IsHidden(string folderPath)
    {
        var dir = new DirectoryInfo(folderPath);
        return dir.Exists && dir.Attributes.HasFlag(FileAttributes.Hidden);
    }

    private static void Apply(DirectoryInfo dir, bool add)
    {
        SetFlag(dir, add);
        foreach (var file in dir.GetFiles())
            SetFlag(file, add);
        foreach (var sub in dir.GetDirectories())
            Apply(sub, add);
    }

    private static void SetFlag(FileSystemInfo info, bool add)
    {
        if (add)
            info.Attributes |= HiddenSystem;
        else
            info.Attributes &= ~HiddenSystem;
    }
}
