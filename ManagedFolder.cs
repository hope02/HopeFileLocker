using System.ComponentModel;

namespace HopeFileLocker;

/// <summary>
/// 受管理的文件夹实体，绑定到主界面列表。
/// 文本（按钮文案、状态）均经 Lang.T 取当前语言。
/// </summary>
public class ManagedFolder : INotifyPropertyChanged
{
    public string Path { get; set; } = string.Empty;

    /// <summary>true 表示这是一个文件（而非文件夹）。</summary>
    public bool IsFile { get; set; }

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

    /// <summary>操作列“隐藏/显示”按钮的动态文案：已隐藏时显示“显示”，否则“隐藏”。</summary>
    public string HideActionText => IsHidden ? Lang.T("actShow") : Lang.T("actHide");

    /// <summary>操作列“加密/解密”按钮的动态文案：已加密时显示“解密”，否则“加密”。</summary>
    public string EncActionText => IsEncrypted ? Lang.T("actDecrypt") : Lang.T("actEncrypt");

    /// <summary>操作列“打开”按钮的固定文案（随语言变化）。</summary>
    public string OpenLabel => Lang.T("actOpen");

    /// <summary>操作列“移除”按钮的固定文案（随语言变化）。</summary>
    public string RemoveLabel => Lang.T("actRemove");

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
            var hidden = IsHidden ? Lang.T("stHidden") : Lang.T("stVisible");
            var enc = IsEncrypted ? Lang.T("stEncrypted") : Lang.T("stNotEnc");
            return $"{hidden} · {enc}";
        }
    }

    /// <summary>
    /// 语言切换时由主界面调用，触发所有文本相关属性刷新，
    /// 使列表中的动态文案（含 DataTemplate 绑定的 OpenLabel/RemoveLabel 等）随之更新。
    /// </summary>
    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(OpenLabel));
        OnPropertyChanged(nameof(RemoveLabel));
        OnPropertyChanged(nameof(HideActionText));
        OnPropertyChanged(nameof(EncActionText));
        OnPropertyChanged(nameof(DisplayStatus));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
