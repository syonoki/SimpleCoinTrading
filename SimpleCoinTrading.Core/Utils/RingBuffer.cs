namespace SimpleCoinTrading.Core.Utils;

public sealed class RingBuffer<T>
{
    private readonly T[] _buf;
    private int _count;
    private int _head; // next write index
    private readonly object _lock = new();

    public RingBuffer(int capacity) => _buf = new T[capacity];

    public void Add(in T item)
    {
        lock (_lock)
        {
            _buf[_head] = item;
            _head = (_head + 1) % _buf.Length;
            _count = Math.Min(_count + 1, _buf.Length);
        }
    }

    public IReadOnlyList<T> Tail(int size)
    {
        lock (_lock)
        {
            int n = Math.Min(size, _count);
            var arr = new T[n];
            int start = (_head - n + _buf.Length) % _buf.Length;
            for (int i = 0; i < n; i++)
                arr[i] = _buf[(start + i) % _buf.Length];
            return arr; // 복사본(안전)
        }
    }

    public T? LastOrDefault()
    {
        lock (_lock)
        {
            if (_count == 0) return default;
            int idx = (_head - 1 + _buf.Length) % _buf.Length;
            return _buf[idx];
        }
    }
}
