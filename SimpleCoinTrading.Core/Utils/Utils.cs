namespace SimpleCoinTrading.Core.Utils;

// =========================
// Minimal event stream (Rx 없이 IObservable 구현)
// =========================
public sealed class SimpleSubject<T> : IObservable<T>
{
    private readonly object _lock = new();
    private List<IObserver<T>> _observers = new();

    public IDisposable Subscribe(IObserver<T> observer)
    {
        if (observer == null) throw new ArgumentNullException(nameof(observer));
        lock (_lock) _observers.Add(observer);
        return new Unsub(this, observer);
    }

    public void OnNext(T value)
    {
        IObserver<T>[] snap;
        lock (_lock) snap = _observers.ToArray();
        foreach (var o in snap) o.OnNext(value);
    }

    public void OnError(Exception ex)
    {
        IObserver<T>[] snap;
        lock (_lock) snap = _observers.ToArray();
        foreach (var o in snap) o.OnError(ex);
    }

    public void OnCompleted()
    {
        IObserver<T>[] snap;
        lock (_lock) snap = _observers.ToArray();
        foreach (var o in snap) o.OnCompleted();
    }

    private sealed class Unsub : IDisposable
    {
        private SimpleSubject<T>? _s;
        private IObserver<T>? _o;

        public Unsub(SimpleSubject<T> s, IObserver<T> o) { _s = s; _o = o; }

        public void Dispose()
        {
            var s = _s; var o = _o;
            if (s == null || o == null) return;
            lock (s._lock) s._observers.Remove(o);
            _s = null; _o = null;
        }
    }
}

// 편의: Action 기반 구독
public sealed class ActionObserver<T> : IObserver<T>
{
    private readonly Action<T> _onNext;
    private readonly Action<Exception>? _onError;
    private readonly Action? _onCompleted;

    public ActionObserver(Action<T> onNext, Action<Exception>? onError = null, Action? onCompleted = null)
    {
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
    }

    public void OnNext(T value) => _onNext(value);
    public void OnError(Exception error) => _onError?.Invoke(error);
    public void OnCompleted() => _onCompleted?.Invoke();
}
