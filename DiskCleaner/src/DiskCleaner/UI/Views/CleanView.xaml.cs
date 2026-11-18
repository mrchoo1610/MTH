using System.Windows.Controls;
using System.Windows.Threading;
using DiskCleaner.UI.ViewModels;

namespace DiskCleaner.UI.Views;

public partial class CleanView : UserControl
{
    public CleanView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Vừa thêm rule tùy chỉnh từ tab Phân tích: cuộn xuống nhóm "Tùy chỉnh" (nằm cuối danh sách).
            if (DataContext is CleanViewModel { ScrollToCustomRequested: true } vm)
            {
                vm.ScrollToCustomRequested = false;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => PageScroll.ScrollToEnd());
            }
        };
    }
}
