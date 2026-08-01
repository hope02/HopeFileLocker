using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private string _busyKey = "busyEncrypt";

    public MainWindow()
    {
        InitializeComponent();
        FolderList.ItemsSource = Folders;
        LoadFolders();
        _cryptoProgress = new Progress<FolderCrypto.ProgressInfo>(OnCryptoProgress);
        ApplyLocalization();
        Lang.Changed += () => ApplyLocalization();
        Closing += (_, _) => SaveFolders();
        UpdateStatus();
    }

    private void ApplyLocalization()
    {
        Title = Lang.T("appTitle");
        BrandSub.Text = Lang.T("brandSub");
        AddBtn.Content = Lang.T("mainAdd");
        ToggleHideBtn.Content = Lang.T("mainToggleHide");
        ToggleEncBtn.Content = Lang.T("mainToggleEnc");
        RemoveBtn.Content = Lang.T("mainRemove");
        ColSelect.Header = Lang.T("colSelect");
        ColPath.Header = Lang.T("colPath");
        ColStatus.Header = Lang.T("colStatus");
        ColActions.Header = Lang.T("colActions");
        foreach (var f in Folders) f.NotifyLanguageChanged();
        UpdateStatus();
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Lang.T("dlgSelectFolder") };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FolderName;
        if (Folders.Any(f => f.Path == path))
        {
            MessageBox.Show(Lang.T("msgInList"));
            return;
        }

            Folders.Add(new ManagedFolder
            {
                Path = path,
                IsHidden = FileHider.IsHidden(path),
                IsEncrypted = FolderCrypto.IsEncrypted(path)
            });
        SaveFolders();
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
                MessageBox.Show($"{f.Path}\n{ex.Message}");
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
            MessageBox.Show(Lang.T("msgNoSession"));
            return;
        }

        ShowBusy("busyEncrypt");
        try
        {
            await Task.Run(() =>
            {
                foreach (var f in targets)
                {
                    _busyKey = f.IsEncrypted ? "busyDecrypt" : "busyEncrypt";
                    if (f.IsEncrypted) FolderCrypto.Decrypt(f.Path, Session.Password, _cryptoProgress);
                    else FolderCrypto.Encrypt(f.Path, Session.Password, _cryptoProgress);
                }
            });
            RefreshAll();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(string.Format(Lang.T("msgCryptoFail"), ex.Message));
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

        ShowBusy("busyRestore");
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
            SaveFolders();
            RefreshAll();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(string.Format(Lang.T("msgRestoreFail"), ex.Message),
                Lang.T("removeFailTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            string.Format(Lang.T("dlgRestoreMsg"), f.Path),
            Lang.T("dlgRestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        ShowBusy("busyRestore");
        try
        {
            await Task.Run(() => RestoreFolder(f));
            f.RefreshState();
            Folders.Remove(f);
            SaveFolders();
            RefreshAll();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(string.Format(Lang.T("msgRestoreFail"), ex.Message),
                Lang.T("removeFailTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                throw new InvalidOperationException(string.Format(Lang.T("restoreNoPwd"), f.Path));
            _busyKey = "busyDecrypt";
            FolderCrypto.Decrypt(f.Path, Session.Password, _cryptoProgress);
        }
        if (f.IsHidden)
        {
            FileHider.Show(f.Path);
        }
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ManagedFolder f) return;
        if (f.IsFile && File.Exists(f.Path))
            Process.Start("explorer.exe", $"/select,\"{f.Path}\"");
        else if (Directory.Exists(f.Path))
            Process.Start("explorer.exe", f.Path);
    }

    private void FolderList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void FolderList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || Folders.Any(f => f.Path == p)) continue;
            if (!Directory.Exists(p) && !File.Exists(p)) continue;

            Folders.Add(new ManagedFolder
            {
                Path = p,
                IsFile = File.Exists(p),
                IsHidden = FileHider.IsHidden(p),
                IsEncrypted = FolderCrypto.IsEncrypted(p)
            });
            added++;
        }
        if (added > 0) { SaveFolders(); RefreshAll(); }
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
        if (string.IsNullOrEmpty(Session.Password)) { MessageBox.Show(Lang.T("msgNoSession")); return; }

        _busyKey = f.IsEncrypted ? "busyDecrypt" : "busyEncrypt";
        ShowBusy(_busyKey);
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

        MessageBox.Show(Lang.T("msgSelectTarget"),
            Lang.T("appTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return new List<ManagedFolder>();
    }

    private void UpdateStatus()
        => StatusText.Text = string.Format(Lang.T("statusCount"), Folders.Count);

    /// <summary>启动时从 %LOCALAPPDATA%\HopeFileLocker\folders.json 载入托管列表，并重算隐藏/加密状态。</summary>
    private void LoadFolders()
    {
        foreach (var p in FolderListStore.Load())
        {
            Folders.Add(new ManagedFolder
            {
                Path = p,
                IsFile = File.Exists(p),
                IsHidden = FileHider.IsHidden(p),
                IsEncrypted = FolderCrypto.IsEncrypted(p)
            });
        }
    }

    /// <summary>把当前托管路径列表持久化到磁盘。</summary>
    private void SaveFolders()
        => FolderListStore.Save(Folders.Select(f => f.Path));

    private void RefreshAll()
    {
        FolderList.Items.Refresh();
        UpdateStatus();
    }

    private void ShowBusy(string key)
    {
        _busyKey = key;
        BusyText.Text = Lang.T(key);
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
        BusyText.Text = $"{Lang.T(_busyKey)} ({idx}/{p.Total})\n{name}";
    }
}
