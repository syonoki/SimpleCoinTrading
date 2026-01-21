using Grpc.Core;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

public sealed class SampleViewModel : INotifyPropertyChanged
{
    private readonly GrpcTradingClient _grpc;
    private CancellationTokenSource? _cts;

    public ObservableCollection<Order> Orders { get; } = new();
    public ObservableCollection<Fill> Fills { get; } = new();
    public ObservableCollection<Position> Positions { get; } = new();
    public ObservableCollection<AlgorithmState> Algorithms { get; } = new();

    private long _seq;
    public long Seq
    {
        get => _seq;
        private set { _seq = value; OnPropertyChanged(); }
    }

    private string _status = "Disconnected";
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    private bool _killSwitchEnabled;
    public bool KillSwitchEnabled
    {
        get => _killSwitchEnabled;
        set { _killSwitchEnabled = value; 
            OnPropertyChanged(nameof(KillSwitchEnabled)); 
        }
    }

    public ICommand ToggleKillSwitchCommand { get; }
    
    public SampleViewModel()
    {
        // 주소는 네 서버 주소로 변경
        _grpc = new GrpcTradingClient("http://localhost:5200");

        ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => _cts is null);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => _cts is not null);
        ToggleKillSwitchCommand = new RelayCommand(async _ =>
        {
            var newValue = !KillSwitchEnabled;
            var resp = await _grpc.SetKillSwitchAsync(newValue, cancelAll: newValue, _cts!.Token);
            KillSwitchEnabled = resp.Enabled;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private async Task ConnectAsync()
    {
        _cts = new CancellationTokenSource();
        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();

        try
        {
            Status = "Connecting...";

            // 1) Snapshot 1회
            var snap = await _grpc.GetSnapshotAsync(_cts.Token);

            Application.Current.Dispatcher.Invoke(() =>
            {
                Orders.Clear();
                foreach (var o in snap.Orders) Orders.Add(o);

                Fills.Clear();
                foreach (var f in snap.Fills) Fills.Add(f);

                Positions.Clear();
                foreach (var p in snap.Positions) Positions.Add(p);

                Algorithms.Clear();
                foreach (var a in snap.Algorithms) Algorithms.Add(a);

                Seq = snap.Seq;
                Status = snap.MarketDataOk ? "Connected (Market OK)" : "Connected (Market NOT OK)";
                KillSwitchEnabled = snap.KillSwitchEnabled;
            });

            // 2) Streaming 시작 (백그라운드)
            _ = Task.Run(() => RunEventStreamAsync(snap.Seq, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            Disconnect();
        }
    }

    private async Task RunEventStreamAsync(long afterSeq, CancellationToken ct)
    {
        var cursor = afterSeq;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var call = _grpc.SubscribeEvents(cursor, ct);

                await foreach (var ev in call.ResponseStream.ReadAllAsync(ct))
                {
                    cursor = Math.Max(cursor, ev.Seq);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Seq = cursor;
                        ApplyEvent(ev);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 끊기면: snapshot으로 복구 후 재구독
                await Task.Delay(500, ct);

                try
                {
                    var snap = await _grpc.GetSnapshotAsync(ct);
                    cursor = snap.Seq;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Orders.Clear();
                        foreach (var o in snap.Orders) Orders.Add(o);

                        Fills.Clear();
                        foreach (var f in snap.Fills) Fills.Add(f);

                        Positions.Clear();
                        foreach (var p in snap.Positions) Positions.Add(p);

                        Algorithms.Clear();
                        foreach (var a in snap.Algorithms) Algorithms.Add(a);

                        Seq = cursor;
                        //Status = snap.MarketDataOk ? "Reconnected (Market OK)" : "Reconnected (Market NOT OK)";
                        KillSwitchEnabled = snap.KillSwitchEnabled;
                    });
                }
                catch
                {
                    // snapshot도 실패하면 다음 루프에서 재시도
                }
            }
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

            case ServerEvent.PayloadOneofCase.Fill:
            {
                var f = ev.Fill.Fill;
                ProcessFill(f.Symbol, f.OrderId, f.Quantity, f.Price);
                break;
            }

            case ServerEvent.PayloadOneofCase.System:
                Status = $"{ev.System.Level}: {ev.System.Message}";
                if (ev.System.Message.Contains("KillSwitch ON")) KillSwitchEnabled = true;
                if (ev.System.Message.Contains("KillSwitch OFF")) KillSwitchEnabled = false;
                break;
        }
    }

    private void ProcessFill(string symbol, string orderId, double quantity, double price)
    {
        var f = new Fill
        {
            Symbol = symbol,
            OrderId = orderId,
            Quantity = quantity,
            Price = price,
            TimeUtc = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            Side = "UNKNOWN" // 상세 정보가 부족하면 보완 필요
        };
        
        Fills.Insert(0, f);
        while (Fills.Count > 200) Fills.RemoveAt(Fills.Count - 1);

        var pos = Positions.FirstOrDefault(p => p.Symbol == symbol);
        if (pos == null)
        {
            pos = new Position { Symbol = symbol, Quantity = 0, AvgPrice = 0 };
            Positions.Add(pos);
        }

        // 간단 포지션 계산 (Side를 알 수 없는 경우 일단 누적하거나 무시)
        // 실제로는 OrderEventPayload에서 Side 정보를 주거나 기존 Order 상태를 참조해야 함
        
        int idx = Positions.IndexOf(pos);
        if (idx >= 0) Positions[idx] = pos;
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

    private void Disconnect()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        Status = "Disconnected";

        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
