using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

public class Sample2ViewModel : INotifyPropertyChanged
{
    public AlgorithmViewModel AlgorithmViewModel { get; }
    public LogViewModel LogViewModel { get; }
    
    public OrdersViewModel OrdersViewModel { get; }
    public PositionsViewModel PositionsViewModel { get; }

    private bool _killSwitchEnabled;
    private readonly GrpcTradingClient _grpc;
    private readonly CancellationTokenSource _cts;

    public bool KillSwitchEnabled
    {
        get => _killSwitchEnabled;
        set { _killSwitchEnabled = value; 
            OnPropertyChanged(nameof(KillSwitchEnabled)); 
        }
    }
    
    public ICommand ToggleKillSwitchCommand { get; }
    
    public Sample2ViewModel()
    {
        var channel = GrpcChannel.ForAddress("http://localhost:5200");
        _grpc = new GrpcTradingClient("http://localhost:5200");
        AlgorithmViewModel = new AlgorithmViewModel(_grpc, channel);
        LogViewModel = new LogViewModel(channel);
        PositionsViewModel = new PositionsViewModel(channel);
        OrdersViewModel = new OrdersViewModel(_grpc);

        AlgorithmViewModel.SelectedAlgorithmChanged += (s, algo) =>
        {
            if (algo != null)
            {
                _ = LogViewModel.SetAlgorithmAsync(algo.AlgorithmId);
                _ = PositionsViewModel.SelectAlgorithmAsync(algo.AlgorithmId);
                _ = OrdersViewModel.SelectAlgorithmAsync(algo.AlgorithmId);
            }
        };
        
        _cts = new CancellationTokenSource();
        
        ToggleKillSwitchCommand = new RelayCommand(async _ =>
        {
            var newValue = !KillSwitchEnabled;
            var resp = await _grpc.SetKillSwitchAsync(newValue, cancelAll: newValue, _cts!.Token);
            KillSwitchEnabled = resp.Enabled;
        });
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