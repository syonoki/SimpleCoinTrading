using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

public class Sample2ViewModel : INotifyPropertyChanged
{
    public AlgorithmViewModel AlgorithmViewModel { get; }
    public LogViewModel LogViewModel { get; }

    public Sample2ViewModel()
    {
        var channel = GrpcChannel.ForAddress("http://localhost:5200");
        AlgorithmViewModel = new AlgorithmViewModel(channel);
        LogViewModel = new LogViewModel(channel);

        AlgorithmViewModel.SelectedAlgorithmChanged += (s, algo) =>
        {
            if (algo != null)
            {
                _ = LogViewModel.SetAlgorithmAsync(algo.Name);
            }
        };
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}