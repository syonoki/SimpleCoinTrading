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
    private readonly GrpcTradingClient _grpc;
    private readonly GrpcChannel _channel;
    private readonly AlgorithmAdminService.AlgorithmAdminServiceClient _algoAdminClient;

    public AlgorithmViewModel(GrpcTradingClient grpc, GrpcChannel channel)
    {
        _grpc = grpc;
        _channel = channel;
        _algoAdminClient = new AlgorithmAdminService.AlgorithmAdminServiceClient(channel);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var response = await _algoAdminClient.ListAlgorithmsAsync(
                new ListAlgorithmsRequest() { },
                cancellationToken: CancellationToken.None);
            _ui.Invoke((Action)(() =>
            {
                Algorithms.Clear();
                foreach (var algo in response.States)
                {
                    Algorithms.Add(algo);
                }

                if (Algorithms.Count > 0 && SelectedAlgorithm == null)
                {
                    SelectedAlgorithm = Algorithms[0];
                }
            }));

            _ = Task.Run(() => SubscribeEventsLoopAsync());
        }
        catch(Exception e)
        {
            // 오류 처리
            Console.WriteLine(e);
        }
    }

    private async Task SubscribeEventsLoopAsync()
    {
        var backoffMs = 1000;
        while (true)
        {
            try
            {
                using var call = _grpc.SubscribeEvents(0, CancellationToken.None);
                backoffMs = 1000;
                await foreach (var ev in call.ResponseStream.ReadAllAsync())
                {
                    var response = await _grpc.ListAlgorithmsAsync(CancellationToken.None);
                    _ui.Invoke((Action)(() =>
                    {
                        foreach (var algoState in response.States)
                        {
                            var existing = Algorithms.FirstOrDefault(a => a.AlgorithmId == algoState.AlgorithmId);
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
                    }));
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