using Google.Protobuf.Collections;
using Grpc.Core;
using SimpleCoinTrading.Core.Algorithms;

namespace SimpleCoinTrading.Server.Services;

public class AlgorithmAdminGrpcService : AlgorithmAdminService.AlgorithmAdminServiceBase
{
    private readonly IAlgorithmEngine _engine;
    private readonly ILogger<AlgorithmAdminGrpcService> _logger;

    public AlgorithmAdminGrpcService(IAlgorithmEngine engine,
        ILogger<AlgorithmAdminGrpcService> logger
    )
    {
        _engine = engine;
        _logger = logger;
    }

    public override Task<ListAlgorithmsResponse> ListAlgorithms(ListAlgorithmsRequest request,
        ServerCallContext context)
    {   
        var algorithms = _engine.Algorithms;
        var retval = new ListAlgorithmsResponse();
        var algoStats = algorithms.Select(a =>
            new AlgorithmState()
            {
                AlgorithmId = a.Key,
                Status = a.Value.Status.ToString(),
                Message = a.Value.LastError?.ToString() ?? string.Empty
            }).ToList();
        retval.States.AddRange(algoStats);
        
        return Task.FromResult(retval);
    }

    public override Task<StartAlgorithmResponse> StartAlgorithm(StartAlgorithmRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("Starting algorithm {AlgorithmId}", request.AlgorithmId);
        try
        {
            _engine.StartAlgorithm(request.AlgorithmId);
            return Task.FromResult(new StartAlgorithmResponse
            {
                Success = true,
                Message = ""
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to start algorithm {AlgorithmId}", request.AlgorithmId);
            
            return Task.FromResult(new StartAlgorithmResponse
            {
                Success = false,
                Message = e.Message
            });
        }
        
    }

    public override Task<StopAlgorithmResponse> StopAlgorithm(StopAlgorithmRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Stopping algorithm {AlgorithmId}", request.AlgorithmId);

        try
        {
            _engine.StopAlgorithm(request.AlgorithmId);
            return Task.FromResult(new StopAlgorithmResponse
            {
                Success = true,
                Message = ""
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to stop algorithm {AlgorithmId}", request.AlgorithmId);
            return Task.FromResult(new StopAlgorithmResponse
            {
                Success = false,
                Message = e.Message
            });
        }
    }
}