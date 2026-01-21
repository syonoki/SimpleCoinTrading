// See https://aka.ms/new-console-template for more information

using Grpc.Core;
using Grpc.Net.Client;
using SimpleCoinTrading.Server;

class Program
{
    static async Task Main(string[] args)
    {
        var input = new HelloRequest(){Name = "World"};
        var channel = GrpcChannel.ForAddress("http://localhost:5200");
        var client = new TradingControl.TradingControlClient(channel);
        
        var request = new GetSnapshotRequest();
        
        var cts = new CancellationTokenSource();
        var snapshot = client.GetSnapshotAsync(request, cancellationToken: cts.Token);
        
        Console.WriteLine(snapshot.ResponseAsync.Result.Seq);
        
        var eventRequest = new SubscribeEventsRequest();
        using var call = client.SubscribeEvents(new SubscribeEventsRequest
        {
            AfterSeq = snapshot.ResponseAsync.Result.Seq
        });
        
        await foreach (var ev in call.ResponseStream.ReadAllAsync())
        {
            Console.WriteLine($"EVENT seq={ev.Seq} type={ev.PayloadCase}");
        }
        
        Console.WriteLine();
        Console.ReadLine();
    }
}