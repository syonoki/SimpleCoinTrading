using SimpleCoinTrading;
using SimpleCoinTrading.Core.Utils;
using Xunit;

namespace SimpleCoinTrading.Core.Tests;

public class UtilsTests
{
    [Fact]
    public void SimpleSubject_ShouldPropagateValues()
    {
        var subject = new SimpleSubject<int>();
        int lastValue = 0;
        var observer = new ActionObserver<int>(v => lastValue = v);
        
        using var sub = subject.Subscribe(observer);
        
        subject.OnNext(10);
        Assert.Equal(10, lastValue);
        
        subject.OnNext(20);
        Assert.Equal(20, lastValue);
    }

    [Fact]
    public void SimpleSubject_ShouldStopPropagationAfterUnsubscribe()
    {
        var subject = new SimpleSubject<int>();
        int lastValue = 0;
        var observer = new ActionObserver<int>(v => lastValue = v);
        
        var sub = subject.Subscribe(observer);
        subject.OnNext(10);
        Assert.Equal(10, lastValue);
        
        sub.Dispose();
        subject.OnNext(20);
        Assert.Equal(10, lastValue);
    }

    [Fact]
    public void SimpleSubject_ShouldPropagateError()
    {
        var subject = new SimpleSubject<int>();
        Exception? receivedError = null;
        var observer = new ActionObserver<int>(_ => { }, ex => receivedError = ex);
        
        using var sub = subject.Subscribe(observer);
        var error = new Exception("Test Error");
        subject.OnError(error);
        
        Assert.Equal(error, receivedError);
    }

    [Fact]
    public void SimpleSubject_ShouldPropagateCompletion()
    {
        var subject = new SimpleSubject<int>();
        bool completed = false;
        var observer = new ActionObserver<int>(_ => { }, _ => { }, () => completed = true);
        
        using var sub = subject.Subscribe(observer);
        subject.OnCompleted();
        
        Assert.True(completed);
    }
}
