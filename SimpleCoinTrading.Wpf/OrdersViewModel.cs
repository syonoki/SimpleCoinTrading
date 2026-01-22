using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

public class OrdersViewModel : INotifyPropertyChanged
{
    private readonly Dispatcher _ui = Application.Current.Dispatcher;

    private readonly GrpcTradingClient _grpc;
    private string _currentAlgoId;
    private CancellationTokenSource _cts;
    public ObservableCollection<Order> Orders { get; set; } = new();

    public OrdersViewModel(GrpcTradingClient grpc)
    {
        _grpc = grpc;
    }

    public async Task SelectAlgorithmAsync(string algorithmId)
    {
        _currentAlgoId = string.IsNullOrWhiteSpace(algorithmId) ? "" : algorithmId;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        await LoadSnapshotAsync(_currentAlgoId, _cts.Token);
        _ = Task.Run(() => RunStreamLoopAsync(_currentAlgoId, _cts.Token));
    }

    private async Task? RunStreamLoopAsync(string currentAlgoId, CancellationToken ct)
    {
        var backoffMs = 500;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await LoadSnapshotAsync(currentAlgoId, ct);

                using var call = _grpc.SubscribeEvents(1 ,ct);

                backoffMs = 500;

                await foreach (var se in call.ResponseStream.ReadAllAsync(ct))
                {
                    _ui.Invoke(() => ApplyEvent(se));
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
        }    }

    private async Task LoadSnapshotAsync(string currentAlgoId, CancellationToken ctsToken)
    {
        try
        {
            var snap = await _grpc.GetSnapshotAsync(ctsToken);

            _ui.Invoke(() =>
            {
                Orders.Clear();
                foreach (var order in snap.Orders)
                    Orders.Add(order);
            });
        }
        catch (Exception e)
        {
        }
    }

    private void ApplyEvent(ServerEvent ev)
    {
        switch (ev.PayloadCase)
        {
            case ServerEvent.PayloadOneofCase.Order:
            {
                var payload = ev.Order;
                // UI 로그나 상태 업데이트에 활용 가능
                // 기존 Order 갱신 로직을 위해 dummy Order 객체를 만들어 UpsertOrder 호출하거나
                UpsertOrder(payload.Order);
                break;
            }
        }
    }

    private void UpsertOrder(Order o)
    {
        for (int i = 0; i < Orders.Count; i++)
        {
            if (Orders[i].OrderId == o.OrderId)
            {
                Orders[i] = o; // DataGrid가 갱신되도록 통째 교체(간단 MVP)
                return;
            }
        }
        Orders.Insert(0, o);
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