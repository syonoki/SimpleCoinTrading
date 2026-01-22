using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

public sealed class PositionsViewModel
{
    public ObservableCollection<PositionDto> Positions { get; } = new();

    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private readonly PositionStateService.PositionStateServiceClient _client;

    private CancellationTokenSource? _cts;
    private string _currentAlgoId = ""; // "" = 전체

    public PositionsViewModel(GrpcChannel channel)
    {
        _client = new PositionStateService.PositionStateServiceClient(channel);
    }

    public async Task SelectAlgorithmAsync(string? algorithmId)
    {
        _currentAlgoId = string.IsNullOrWhiteSpace(algorithmId) ? "" : algorithmId;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        await LoadSnapshotAsync(_currentAlgoId, _cts.Token);
        _ = Task.Run(() => RunStreamLoopAsync(_currentAlgoId, _cts.Token));
    }

    private async Task LoadSnapshotAsync(string algoId, CancellationToken ct)
    {
        var snap = await _client.GetSnapshotAsync(new GetPositionsRequest
        {
            AlgorithmId = algoId
        }, cancellationToken: ct);

        _ui.Invoke(() =>
        {
            Positions.Clear();
            foreach (var p in snap.Positions)
                Positions.Add(p);
        });
    }

    private async Task RunStreamLoopAsync(string algoId, CancellationToken ct)
    {
        var backoffMs = 500;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var call = _client.Subscribe(new GetPositionsRequest
                {
                    AlgorithmId = algoId
                }, cancellationToken: ct);

                backoffMs = 500;

                await foreach (var upd in call.ResponseStream.ReadAllAsync(ct))
                {
                    _ui.Invoke(() => Apply(upd));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                // 서버 재시작/네트워크 끊김 → 재연결
            }
            catch
            {
                // 기타 오류도 재연결 시도
            }

            // 끊긴 동안 놓친 업데이트 보정: Snapshot 재로딩이 제일 간단하고 안전(MVP)
            try { await LoadSnapshotAsync(algoId, ct); } catch { /* ignore */ }

            await Task.Delay(backoffMs, ct);
            backoffMs = Math.Min(backoffMs * 2, 5000);
        }
    }

    private void Apply(PositionUpdate upd)
    {
        switch (upd.PayloadCase)
        {
            case PositionUpdate.PayloadOneofCase.Upserted:
                Upsert(upd.Upserted.Position);
                break;

            case PositionUpdate.PayloadOneofCase.Removed:
                Remove(upd.Removed.AlgorithmId, upd.Removed.Symbol);
                break;
        }
    }

    private void Upsert(PositionDto p)
    {
        var idx = FindIndex(p.AlgorithmId, p.Symbol);
        if (idx < 0) Positions.Add(p);
        else Positions[idx] = p; // 교체 (Grid 갱신 쉬움)
    }

    private void Remove(string algorithmId, string symbol)
    {
        var idx = FindIndex(algorithmId, symbol);
        if (idx >= 0) Positions.RemoveAt(idx);
    }

    private int FindIndex(string algorithmId, string symbol)
    {
        for (int i = 0; i < Positions.Count; i++)
        {
            var x = Positions[i];
            if (x.AlgorithmId == algorithmId && x.Symbol == symbol)
                return i;
        }
        return -1;
    }
}