using Google.Protobuf.WellKnownTypes;
using SimpleCoinTrading.Core.Broker;

namespace SimpleCoinTrading.Server.Services;


public static class ServerEventFactory
{
    // ---- System -------------------------------------------------

    public static ServerEvent FromSystem(
        long seq,
        string level,
        string message,
        DateTime? utc = null)
    {
        return new ServerEvent
        {
            Seq = seq,
            TimeUtc = ToTimestamp(utc ?? DateTime.UtcNow),
            System = new SystemEvent
            {
                Level = level,
                Message = message
            }
        };
    }

    // ---- Order --------------------------------------------------

    public static ServerEvent FromOrder(
        long seq,
        OrderUpdatedEvent orderUpdate,
        DateTime? utc = null)
    {
        return new ServerEvent
        {
            Seq = seq,
            TimeUtc = ToTimestamp(utc ?? DateTime.UtcNow),
            Order = new OrderEvent
            {
                Order = ToProto(orderUpdate.Order)
            }
        };
    }

    // ---- Fill ---------------------------------------------------

    public static ServerEvent FromFill(
        long seq,
        SimpleCoinTrading.Core.Broker.FillEvent fillUpdate,
        DateTime? utc = null)
    {
        return new ServerEvent
        {
            Seq = seq,
            TimeUtc = ToTimestamp(utc ?? DateTime.UtcNow),
            Fill = new FillEvent
            {
                Fill = ToProto(fillUpdate.Fill)
            }
        };
    }

    // ---- Converters --------------------------------------------

    private static Order ToProto(OrderState s)
    {
        return new Order
        {
            OrderId = s.OrderId,
            Symbol = s.Symbol,
            Side = s.Side.ToString().ToUpperInvariant(),
            Type = s.Type.ToString().ToUpperInvariant(),
            Status = s.Status.ToString().ToUpperInvariant(),
            Quantity = (double)s.Quantity,
            FilledQuantity = (double)s.FilledQuantity,
            LimitPrice = (double)(s.LimitPrice ?? 0),
            AvgFillPrice = (double)(s.AvgFillPrice ?? 0),
            UpdatedUtc = ToTimestamp(s.UpdatedUtc ?? s.CreatedUtc)
        };
    }

    private static Fill ToProto(SimpleCoinTrading.Core.Broker.Fill f)
    {
        return new Fill
        {
            OrderId = f.OrderId,
            Symbol = f.Symbol,
            Side = f.Side.ToString().ToUpperInvariant(),
            Price = (double)f.Price,
            Quantity = (double)f.Quantity,
            Fee = (double)f.Fee,
            FeeCurrency = f.FeeCurrency,
            TimeUtc = ToTimestamp(f.TimeUtc),
            TradeId = f.TradeId ?? ""
        };
    }

    // ---- helpers -----------------------------------------------

    private static Timestamp ToTimestamp(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return Timestamp.FromDateTime(utc);
    }
}
