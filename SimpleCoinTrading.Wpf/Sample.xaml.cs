using System.Windows.Controls;

namespace SimpleCoinTrading.Wpf;

public partial class Sample : UserControl
{
    public Sample()
    {
        InitializeComponent();
        DataContext = new SampleViewModel();
    }
}