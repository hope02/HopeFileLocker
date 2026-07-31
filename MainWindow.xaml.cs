using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HopeFileLocker;

public partial class MainWindow : Window
{
    public ObservableCollection<ManagedFolder> Folders { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        FolderList.ItemsSource = Folders;
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
                f.IsHidden = FileHider.IsHidden(f.Path);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"操作失败：{f.Path}\n{ex.Message}");
            }
        }
    }

    private void ToggleEncBtn_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetTargets();
        if (targets.Count == 0) return;

        if (string.IsNullOrEmpty(Session.Password))
        {
            MessageBox.Show("会话密码缺失，请重新登录。");
            return;
        }

        foreach (var f in targets)
        {
            try
            {
                if (f.IsEncrypted) FolderCrypto.Decrypt(f.Path, Session.Password);
                else FolderCrypto.Encrypt(f.Path, Session.Password);
                f.IsEncrypted = FolderCrypto.IsEncrypted(f.Path);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"加密/解密失败：{f.Path}\n{ex.Message}");
            }
        }
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in GetTargets().ToList())
            Folders.Remove(f);
        UpdateStatus();
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
                f.IsHidden = FileHider.IsHidden(f.Path);
            }
            catch (System.Exception ex) { MessageBox.Show(ex.Message); }
        }
    }

    private void RowToggleEnc_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ManagedFolder f)
        {
            if (string.IsNullOrEmpty(Session.Password)) { MessageBox.Show("会话密码缺失，请重新登录。"); return; }
            try
            {
                if (f.IsEncrypted) FolderCrypto.Decrypt(f.Path, Session.Password);
                else FolderCrypto.Encrypt(f.Path, Session.Password);
                f.IsEncrypted = FolderCrypto.IsEncrypted(f.Path);
            }
            catch (System.Exception ex) { MessageBox.Show(ex.Message); }
        }
    }

    private List<ManagedFolder> GetTargets()
    {
        if (FolderList.SelectedItems.Count > 0)
            return FolderList.SelectedItems.Cast<ManagedFolder>().ToList();
        return Folders.ToList();
    }

    private void UpdateStatus()
        => StatusText.Text = $"共 {Folders.Count} 个文件夹";
}
