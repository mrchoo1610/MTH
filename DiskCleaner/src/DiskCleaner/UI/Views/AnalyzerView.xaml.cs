using System.Windows;
using System.Windows.Controls;
using DiskCleaner.UI.ViewModels;

namespace DiskCleaner.UI.Views;

public partial class AnalyzerView : UserControl
{
    public AnalyzerView() => InitializeComponent();

    /// <summary>Tính lại trạng thái "Thêm vào Dọn dẹp" của node ngay trước khi menu chuột phải mở.</summary>
    private void OnNodeContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TreeNodeViewModel node) node.RefreshAddState();
    }
}
