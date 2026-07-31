using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace HopeFileLocker;

public partial class MainWindow : Window
{
    public ObservableCollection<ManagedFolder> Folders { get; } = new();

    private readonly Progress<FolderCrypto.ProgressInfo> _cryptoProgress;
    private string _cryptoMode = "加密";

    public MainWindow()
    {
        InitializeComponent();
        FolderList.ItemsSource = Folders;
        _cryptoProgress = new Progress<FolderCrypto.ProgressInfo>(OnCryptoProgress);
        UpdateStatus();
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择要管理的文件夹" };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FolderName;
        if (Folders.Any(f => f.Path == path))
        {
            MessageBox.Show("该文件夹已在列表中。");
            return;
        }

        Folders.Add(new ManagedFolder
        {
            Path = path,
            IsHidden = FileHider.IsHidden(path),
            IsEncrypted = FolderCrypto.IsEncrypted(path)
        });
        UpdateStatus();
    }

    private void ToggleHideBtn_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargets();
        if (targets.Count == 0) return;

        foreach (var f in targets)
        {
            try
            {
                if (f.IsHidden) FileHider.Show(f.Path);
                else FileHider.Hide(f.Path);
                f.RefreshState();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"操作失败：{f.Path}\n{ex.Message}");
            }
        }
        RefreshAll();
    }

    private async void ToggleEncBtn_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargets();
        if (targets.Count == 0) return;

        if (string.IsNullOrEmpty(Session.Password))
        {
            MessageBox.Show("会话密码缺失，请重新登录。");
            return;
        }

        ShowBusy("正在加密…");
        try
        {
            await Task.Run(() =>
            {
                foreach (var f in targets)
                {
                    _cryptoMode = f.IsEncrypted ? "解密" : "加密";
                    if (f.IsEncrypted) FolderCrypto.Decrypt(f.Path, Session.Password, _cryptoProgress);
                    else FolderCrypto.Encrypt(f.Path, Session.Password, _cryptoProgress);
                }
            });
            RefreshAll();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"加密/解密失败：{ex.Message}");
        }
        finally
        {
            HideBusy();
        }
    }

    private async void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargets();
        if (targets.Count == 0) return;

        ShowBusy("正在恢复并移除…");
        try
        {
            await Task.Run(() =>
            {
                foreach (var f in targets)
                {
                    RestoreFolder(f);
                }
            });

            foreach (var f in targets.ToList())
            {
                f.RefreshState();
                Folders.Remove(f);
            }
            RefreshAll();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"恢复失败：{ex.Message}\n（该文件夹保留在列表中，未移除）",
                "移除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            HideBusy();
        }
    }

    private async void RowRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ManagedFolder f) return;

        var r = MessageBox.Show(
            $"将从列表移除该文件夹，并先恢复其文件（解密已加密内容、取消隐藏）。\n{f.Path}\n\n继续？",
            "移除并恢复", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        ShowBusy("正在恢复并移除…");
        try
        {
            await Task.Run(() => RestoreFolder(f));
            f.RefreshState();
            Folders.Remove(f);
            RefreshAll();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"恢复失败：{ex.Message}\n（该文件夹保留在列表中，未移除）",
                "移除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            HideBusy();
        }
    }

    /// <summary>
    /// 移除前先“恢复”文件夹：若已加密则解密，若已隐藏则取消隐藏。
    /// 解密依赖登录会话密码；失败会向上抛异常，由调用方决定是否保留记录。
    /// </summary>
    private void RestoreFolder(ManagedFolder f)
    {
        if (f.IsEncrypted)
        {
            if (string.IsNullOrEmpty(Session.Password))
                throw new InvalidOperationException($"会话密码缺失，无法解密：{f.Path}（请重新登录后再移除）");
            _cryptoMode = "解密";
            FolderCrypto.Decrypt(f.Path, Session.Password, _cryptoProgress);
        }
        if (f.IsHidden)
        {
            _cryptoMode = "显示";
            FileHider.Show(f.Path);
        }
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ManagedFolder f && Directory.Exists(f.Path))
            Process.Start("explorer.exe", f.Path);
    }

    private void RowToggleHide_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ManagedFolder f)
        {
            try
            {
                if (f.IsHidden) FileHider.Show(f.Path);
                else FileHider.Hide(f.Path);
                f.RefreshState();
                RefreshAll();
            }
            catch (System.Exception ex) { MessageBox.Show(ex.Message); }
        }
    }

    private async void RowToggleEnc_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ManagedFolder f) return;
        if (string.IsNullOrEmpty(Session.Password)) { MessageBox.Show("会话密码缺失，请重新登录。"); return; }

        _cryptoMode = f.IsEncrypted ? "解密" : "加密";
        ShowBusy($"正在{_cryptoMode}…");
        try
        {
            await Task.Run(() =>
            {
                if (f.IsEncrypted) FolderCrypto.Decrypt(f.Path, Session.Password, _cryptoProgress);
                else FolderCrypto.Encrypt(f.Path, Session.Password, _cryptoProgress);
            });
            f.RefreshState();
            RefreshAll();
        }
        catch (System.Exception ex) { MessageBox.Show(ex.Message); }
        finally { HideBusy(); }
    }

    private List<ManagedFolder> GetTargets()
    {
        var checkedItems = Folders.Where(f => f.IsChecked).ToList();
        if (checkedItems.Count > 0) return checkedItems;

        if (FolderList.SelectedItems.Count > 0)
            return FolderList.SelectedItems.Cast<ManagedFolder>().ToList();

        MessageBox.Show("请先勾选要操作的文件夹（每行左侧的勾选框，可多选），或按住 Ctrl/Shift 在列表中多选，再点击工具栏按钮。",
            "未选择文件夹", MessageBoxButton.OK, MessageBoxImage.Information);
        return new List<ManagedFolder>();
    }

    private void UpdateStatus()
        => StatusText.Text = $"共 {Folders.Count} 个文件夹";

    private void RefreshAll()
    {
        FolderList.Items.Refresh();
        UpdateStatus();
    }

    private void ShowBusy(string text)
    {
        BusyText.Text = text;
        BusyOverlay.Visibility = Visibility.Visible;
        ((Storyboard)BusyOverlay.FindResource("SpinStoryboard")).Begin();
    }

    private void HideBusy()
    {
        ((Storyboard)BusyOverlay.FindResource("SpinStoryboard")).Stop();
        BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnCryptoProgress(FolderCrypto.ProgressInfo p)
    {
        var name = string.IsNullOrEmpty(p.FileName) ? "" : Path.GetFileName(p.FileName);
        var idx = p.Total > 0 ? p.Current + 1 : 1;
        BusyText.Text = $"{_cryptoMode}中… ({idx}/{p.Total})\n{name}";
    }
}
