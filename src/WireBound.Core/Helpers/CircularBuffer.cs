namespace WireBound.Core.Helpers;

/// <summary>
/// A fixed-size circular buffer that efficiently overwrites oldest items.
/// Thread-safe for concurrent reads and writes.
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _lock = new();
    private int _head;
    private int _tail;
    private int _count;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public int Capacity => _buffer.Length;

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;

            if (_count == _buffer.Length)
                _tail = (_tail + 1) % _buffer.Length;
            else
                _count++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }

    /// <summary>
    /// Returns a snapshot of the buffer contents as an enumerable.
    /// The snapshot is taken atomically to ensure thread safety.
    /// </summary>
    public IEnumerable<T> AsEnumerable()
    {
        // Take a snapshot under lock to ensure consistency
        T[] snapshot;
        lock (_lock)
        {
            snapshot = new T[_count];
            for (int i = 0; i < _count; i++)
                snapshot[i] = _buffer[(_tail + i) % _buffer.Length];
        }
        return snapshot;
    }

    public T[] ToArray()
    {
        lock (_lock)
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _buffer[(_tail + i) % _buffer.Length];
            return result;
        }
    }
}
