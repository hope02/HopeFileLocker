using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HopeFileLocker;

public partial class RescueWindow : Window
{
    public string? RecoveredPassword { get; private set; }
    public string? NewRescueCode { get; private set; }
    public bool Success { get; private set; }

    public RescueWindow()
    {
        InitializeComponent();
    }

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RescueOk_Click(sender, e);
    }

    private void RescueOk_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeBox.Text)) return;
        if (SecurityHelper.TryRecover(CodeBox.Text, out var pwd, out var newCode))
        {
            RecoveredPassword = pwd;
            NewRescueCode = newCode;
            Success = true;
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorText.Text = "救援码不正确，请检查后重试。";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
