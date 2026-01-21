using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

public class AlgorithmViewModel : INotifyPropertyChanged
{
    public ObservableCollection<AlgorithmState> Algorithms { get; } = new();
    
    private AlgorithmState? _selectedAlgorithm;
    public AlgorithmState? SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set
        {
            if (SetField(ref _selectedAlgorithm, value))
            {
                SelectedAlgorithmChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<AlgorithmState?>? SelectedAlgorithmChanged;

    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly TradingControl.TradingControlClient _tradingControl;

    public AlgorithmViewModel(GrpcChannel channel)
    {
        _tradingControl = new TradingControl.TradingControlClient(channel);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var snapshot = await _tradingControl.GetSnapshotAsync(new GetSnapshotRequest());
            _ui.Invoke(() =>
            {
                Algorithms.Clear();
                foreach (var algo in snapshot.Algorithms)
                {
                    Algorithms.Add(algo);
                }

                if (Algorithms.Count > 0 && SelectedAlgorithm == null)
                {
                    SelectedAlgorithm = Algorithms[0];
                }
            });

            _ = Task.Run(() => SubscribeEventsLoopAsync());
        }
        catch
        {
            // 오류 처리
        }
    }

    private async Task SubscribeEventsLoopAsync()
    {
        var backoffMs = 1000;
        while (true)
        {
            try
            {
                using var call = _tradingControl.SubscribeEvents(new SubscribeEventsRequest());
                backoffMs = 1000;
                await foreach (var ev in call.ResponseStream.ReadAllAsync())
                {
                    var snapshot = await _tradingControl.GetSnapshotAsync(new GetSnapshotRequest());
                    _ui.Invoke(() =>
                    {
                        foreach (var algoState in snapshot.Algorithms)
                        {
                            var existing = Algorithms.FirstOrDefault(a => a.Name == algoState.Name);
                            if (existing != null)
                            {
                                existing.Status = algoState.Status;
                                existing.Message = algoState.Message;
                            }
                            else
                            {
                                Algorithms.Add(algoState);
                            }
                        }
                    });
                }
            }
            catch
            {
                await Task.Delay(backoffMs);
                backoffMs = Math.Min(backoffMs * 2, 10000);
            }
        }
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
