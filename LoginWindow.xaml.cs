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
        ApplyLocalization();
        Lang.Changed += () => ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        BrandSub.Text = Lang.T("brandSub");
        PwdLabel.Text = Lang.T("loginPwd");
        LoginBtn.Content = Lang.T("loginBtn");
        ForgotBtn.Content = Lang.T("loginForgot");
        LangLabel.Text = Lang.T("langLabel");
        LangZh.IsChecked = Lang.Current == LangCode.Zh;
        LangEn.IsChecked = Lang.Current == LangCode.En;
        HintText.Text = SecurityHelper.HasPassword
            ? Lang.T("loginHintNormal")
            : Lang.T("loginHintFirst");
    }

    private void LangZh_Checked(object sender, RoutedEventArgs e) => Lang.Set(LangCode.Zh);
    private void LangEn_Checked(object sender, RoutedEventArgs e) => Lang.Set(LangCode.En);

    private void Login_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void Forgot_Click(object sender, RoutedEventArgs e)
    {
        if (!SecurityHelper.HasPassword)
        {
            ErrorText.Text = Lang.T("loginErrNoPwd");
            return;
        }

        var dlg = new RescueWindow { Owner = this };
        if (dlg.ShowDialog() != true || !dlg.Success) return;

        Session.Password = dlg.RecoveredPassword!;
        ShowRescueCode(dlg.NewRescueCode!, Lang.T("rescueTipRecovered"));
        OpenMain();
    }

    private void TryLogin()
    {
        var pw = PwdBox.Password;
        if (string.IsNullOrEmpty(pw))
        {
            ErrorText.Text = Lang.T("loginErrEmpty");
            return;
        }

        if (!SecurityHelper.HasPassword)
        {
            var rescue = SecurityHelper.Setup(pw);
            Session.Password = pw;
            ShowRescueCode(rescue, Lang.T("rescueTipFirst"));
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
            ErrorText.Text = Lang.T("loginErrWrong");
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
