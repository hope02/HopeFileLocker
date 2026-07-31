using System.ComponentModel;

namespace HopeFileLocker;

/// <summary>
/// 受管理的文件夹实体，绑定到主界面列表。
/// </summary>
public class ManagedFolder : INotifyPropertyChanged
{
    public string Path { get; set; } = string.Empty;

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (_isHidden == value) return;
            _isHidden = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayStatus));
            OnPropertyChanged(nameof(HideActionText));
        }
    }

    private bool _isEncrypted;
    public bool IsEncrypted
    {
        get => _isEncrypted;
        set
        {
            if (_isEncrypted == value) return;
            _isEncrypted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayStatus));
            OnPropertyChanged(nameof(EncActionText));
        }
    }

    /// <summary>
    /// 操作列“隐藏/显示”按钮的动态文案：已隐藏时显示“显示”，否则“隐藏”。
    /// </summary>
    public string HideActionText => IsHidden ? "显示" : "隐藏";

    /// <summary>
    /// 操作列“加密/解密”按钮的动态文案：已加密时显示“解密”，否则“加密”。
    /// </summary>
    public string EncActionText => IsEncrypted ? "解密" : "加密";

    /// <summary>
    /// 从磁盘重新读取真实的隐藏/加密状态，并触发界面刷新。
    /// </summary>
    public void RefreshState()
    {
        IsHidden = FileHider.IsHidden(Path);
        IsEncrypted = FolderCrypto.IsEncrypted(Path);
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
