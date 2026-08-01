using System.IO;

namespace HopeFileLocker;

/// <summary>
/// 通过 Windows 文件属性（底层为 Win32 SetFileAttributes）实现文件 / 文件夹的
/// 隐藏 / 显示。隐藏时同时设置 Hidden 与 System 属性，使其在资源管理器中彻底不可见。
///
/// 既支持文件夹（递归处理其内容），也支持单个文件（仅设置该文件自身属性）。
/// </summary>
public static class FileHider
{
    private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

    /// <summary>设置文件/文件夹的 Hidden + System 属性。</summary>
    public static void Hide(string path)
    {
        if (Directory.Exists(path))
            Apply(new DirectoryInfo(path), add: true);
        else if (File.Exists(path))
            SetFlag(new FileInfo(path), add: true);
    }

    /// <summary>移除文件/文件夹的 Hidden + System 属性。</summary>
    public static void Show(string path)
    {
        if (Directory.Exists(path))
            Apply(new DirectoryInfo(path), add: false);
        else if (File.Exists(path))
            SetFlag(new FileInfo(path), add: false);
    }

    public static bool IsHidden(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryInfo(path).Attributes.HasFlag(HiddenSystem);
        if (File.Exists(path))
            return new FileInfo(path).Attributes.HasFlag(HiddenSystem);
        return false;
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
