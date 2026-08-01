using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HopeFileLocker;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        PwdBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryLogin(); };

        if (!SecurityHelper.HasPassword)
        {
            HintText.Text = "首次使用：输入任意密码即设为登录密码，并会生成救援码。";
        }
        else
        {
            HintText.Text = "请输入登录密码以解锁主界面。";
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void Forgot_Click(object sender, RoutedEventArgs e)
    {
        if (!SecurityHelper.HasPassword)
        {
            ErrorText.Text = "本机尚未设置密码，请直接输入密码登录。";
            return;
        }

        var dlg = new RescueWindow { Owner = this };
        if (dlg.ShowDialog() != true || !dlg.Success) return;

        Session.Password = dlg.RecoveredPassword!;
        ShowRescueCode(dlg.NewRescueCode!, "密码已找回！请保存这串新的救援码（旧的可能会失效）：");
        OpenMain();
    }

    private void TryLogin()
    {
        var pw = PwdBox.Password;
        if (string.IsNullOrEmpty(pw))
        {
            ErrorText.Text = "请输入密码。";
            return;
        }

        if (!SecurityHelper.HasPassword)
        {
            var rescue = SecurityHelper.Setup(pw);
            Session.Password = pw;
            ShowRescueCode(rescue, "请保存你的救援码：忘记密码时凭它找回，且已加密的文件不会丢失。");
            OpenMain();
            return;
        }

        if (SecurityHelper.Verify(pw))
        {
            Session.Password = pw;
            OpenMain();
        }
        else
        {
            ErrorText.Text = "密码错误，请重试。";
            PwdBox.Clear();
        }
    }

    private void OpenMain()
    {
        var main = new MainWindow();
        main.Show();
        Close();
    }

    private void ShowRescueCode(string code, string tip)
    {
        var w = new RescueCodeWindow(code, tip) { Owner = this };
        w.ShowDialog();
    }
}
