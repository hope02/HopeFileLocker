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

        HintText.Text = SecurityHelper.HasPassword
            ? "请输入登录密码以解锁主界面。"
            : "首次使用：输入任意密码即设为登录密码。";
    }

    private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void TryLogin()
    {
        var pw = PwdBox.Password;
        if (SecurityHelper.VerifyOrSet(pw))
        {
            Session.Password = pw;
            var main = new MainWindow();
            main.Show();
            Close();
        }
        else
        {
            ErrorText.Text = "密码错误，请重试。";
            PwdBox.Clear();
        }
    }
}
