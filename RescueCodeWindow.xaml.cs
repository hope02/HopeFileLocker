using System.Windows;

namespace HopeFileLocker;

public partial class RescueCodeWindow : Window
{
    public RescueCodeWindow(string code, string tip)
    {
        InitializeComponent();
        CodeBox.Text = code;
        TipText.Text = tip;
        ApplyLocalization();
        Lang.Changed += () => ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = Lang.T("codeTitle");
        HeaderText.Text = Lang.T("codeHeader");
        SaveHint.Text = Lang.T("codeSaveHint");
        SavedBtn.Content = Lang.T("codeSaved");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
