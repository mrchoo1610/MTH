using System.Windows.Controls;

namespace DiskCleaner.UI.Views;

public partial class AddCustomRuleContent : UserControl
{
    public AddCustomRuleContent()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }
}
