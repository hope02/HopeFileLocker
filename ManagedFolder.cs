using System.ComponentModel;

namespace HopeFileLocker;

/// <summary>
/// 受管理的文件夹实体，绑定到主界面列表。
/// </summary>
public class ManagedFolder : INotifyPropertyChanged
{
    public string Path { get; set; } = string.Empty;

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set { _isHidden = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayStatus)); }
    }

    private bool _isEncrypted;
    public bool IsEncrypted
    {
        get => _isEncrypted;
        set { _isEncrypted = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayStatus)); }
    }

    /// <summary>
    /// 列表“状态”列展示用的聚合文本。
    /// </summary>
    public string DisplayStatus
    {
        get
        {
            var hidden = IsHidden ? "已隐藏" : "可见";
            var enc = IsEncrypted ? "已加密" : "未加密";
            return $"{hidden} · {enc}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
