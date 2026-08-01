using System.Windows;

namespace HopeFileLocker;

public partial class RescueCodeWindow : Window
{
    public RescueCodeWindow(string code, string tip)
    {
        InitializeComponent();
        CodeBox.Text = code;
        TipText.Text = tip;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
