using System.Windows.Controls;

namespace SimpleCoinTrading.Wpf;

public partial class Sample2 : UserControl
{
    public Sample2()
    {
        InitializeComponent();
        DataContext = new Sample2ViewModel();
    }
}