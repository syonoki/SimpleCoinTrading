using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

public class LogViewModel : INotifyPropertyChanged
{
    public ObservableCollection<AlgoLogDto> Logs { get; } = new();
    
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly AlgoLogService.AlgoLogServiceClient _logClient;
    private readonly HashSet<string> _seen = new();
    private CancellationTokenSource? _cts;
    private string _currentAlgoId = "UNKNOWN";

    public LogViewModel(GrpcChannel channel)
    {
        _logClient = new AlgoLogService.AlgoLogServiceClient(channel);
    }

    public async Task SetAlgorithmAsync(string algorithmId)
    {
        _currentAlgoId = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        await LoadRecentAsync(_currentAlgoId, limit: 500, _cts.Token);
        _ = Task.Run(() => RunStreamLoopAsync(_currentAlgoId, _cts.Token));
    }

    private async Task LoadRecentAsync(string algoId, int limit, CancellationToken ct)
    {
        try
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
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task RunStreamLoopAsync(string algoId, CancellationToken ct)
    {
        var backoffMs = 500;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReconcileRecentAsync(algoId, limit: 200, ct);

                using var call = _logClient.Subscribe(new SubscribeAlgoLogsRequest
                {
                    AlgorithmId = algoId
                }, cancellationToken: ct);

                backoffMs = 500;

                await foreach (var l in call.ResponseStream.ReadAllAsync(ct))
                {
                    _ui.Invoke(() => AddIfNew(l, maxLines: 2000));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // 재연결 대기
            }

            await Task.Delay(backoffMs, ct);
            backoffMs = Math.Min(backoffMs * 2, 5000);
        }
    }

    private async Task ReconcileRecentAsync(string algoId, int limit, CancellationToken ct)
    {
        try
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
        catch { }
    }

    private void AddIfNew(AlgoLogDto l, int maxLines)
    {
        var key = MakeKey(l);
        if (_seen.Contains(key))
            return;

        _seen.Add(key);
        Logs.Add(l);

        while (Logs.Count > maxLines)
            Logs.RemoveAt(0);

        if (_seen.Count > maxLines * 2)
        {
            _seen.Clear();
            foreach (var x in Logs)
                _seen.Add(MakeKey(x));
        }
    }

    private static string MakeKey(AlgoLogDto l) => $"{l.TimeUnixMs}:{l.Message}";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
