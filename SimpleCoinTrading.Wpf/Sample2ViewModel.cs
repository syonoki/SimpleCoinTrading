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
    public ObservableCollection<AlgoLogDto> Logs { get; } = new();
    public ObservableCollection<AlgorithmState> Algorithms { get; } = new();

    private AlgorithmState? _selectedAlgorithm;
    public AlgorithmState? SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set
        {
            if (SetField(ref _selectedAlgorithm, value))
            {
                if (value != null)
                {
                    _ = SelectAlgorithmAsync(value.Name);
                }
            }
        }
    }

    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly TradingControl.TradingControlClient _tradingControl;
    private readonly AlgoLogService.AlgoLogServiceClient _logClient;

    private readonly HashSet<string> _seen = new();  // dedup
    private CancellationTokenSource? _cts;
    private string _currentAlgoId = "UNKNOWN";

    public Sample2ViewModel()
    {
        var channel = GrpcChannel.ForAddress("http://localhost:5200");
        _tradingControl = new TradingControl.TradingControlClient(channel);
        _logClient = new AlgoLogService.AlgoLogServiceClient(channel);

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
        catch (Exception ex)
        {
            // 로그 처리 등
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

    public async Task SelectAlgorithmAsync(string algorithmId)
    {
        _currentAlgoId = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;

        // 기존 스트림 중단
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        // UI 초기화 + 최근 로그 채우기
        await LoadRecentAsync(_currentAlgoId, limit: 500, _cts.Token);

        // 스트림 시작(백그라운드)
        _ = Task.Run(() => RunStreamLoopAsync(_currentAlgoId, _cts.Token));
    }
    
    
    private async Task LoadRecentAsync(string algoId, int limit, CancellationToken ct)
    {
        var resp = await _logClient.GetRecentAsync(new GetAlgoLogsRequest
        {
            AlgorithmId = algoId,
            Limit = limit
        }, cancellationToken: ct);

        _ui.Invoke(() =>
        {
            Logs.Clear();
            _seen.Clear();

            foreach (var l in resp.Logs)
                AddIfNew(l, maxLines: 2000);
        });
    }

    private async Task RunStreamLoopAsync(string algoId, CancellationToken ct)
    {
        var backoffMs = 500;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // (선택) 재연결 직전에 최근 로그로 보정
                await ReconcileRecentAsync(algoId, limit: 200, ct);

                using var call = _logClient.Subscribe(new SubscribeAlgoLogsRequest
                {
                    AlgorithmId = algoId
                }, cancellationToken: ct);

                backoffMs = 500; // 성공적으로 붙었으니 backoff 리셋

                await foreach (var l in call.ResponseStream.ReadAllAsync(ct))
                {
                    _ui.Invoke(() => AddIfNew(l, maxLines: 2000));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                // 서버 재시작/네트워크 문제: 재연결
            }
            catch
            {
                // 기타 오류도 일단 재연결 시도
            }

            await Task.Delay(backoffMs, ct);
            backoffMs = Math.Min(backoffMs * 2, 5000);
        }
    }
    private async Task ReconcileRecentAsync(string algoId, int limit, CancellationToken ct)
    {
        var resp = await _logClient.GetRecentAsync(new GetAlgoLogsRequest
        {
            AlgorithmId = algoId,
            Limit = limit
        }, cancellationToken: ct);

        _ui.Invoke(() =>
        {
            foreach (var l in resp.Logs)
                AddIfNew(l, maxLines: 2000);
        });
    }
    private void AddIfNew(AlgoLogDto l, int maxLines)
    {
        var key = MakeKey(l);
        if (_seen.Contains(key))
            return;

        _seen.Add(key);
        Logs.Add(l);

        // 라인 제한: 오래된 것 삭제
        while (Logs.Count > maxLines)
            Logs.RemoveAt(0);

        // _seen도 무한히 커지지 않게 간단히 제한
        if (_seen.Count > maxLines * 2)
        {
            // Logs 기반으로 다시 구성(간단, O(n)지만 maxLines 작으면 OK)
            _seen.Clear();
            foreach (var x in Logs)
                _seen.Add(MakeKey(x));
        }
    }
    private static string MakeKey(AlgoLogDto l)
        => $"{l.TimeUnixMs}:{l.Message}";
    
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