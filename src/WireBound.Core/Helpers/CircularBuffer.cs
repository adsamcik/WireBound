namespace WireBound.Core.Helpers;

/// <summary>
/// A fixed-size circular buffer that efficiently overwrites oldest items.
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public int Count => _count;
    public int Capacity => _buffer.Length;

    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        
        if (_count == _buffer.Length)
            _tail = (_tail + 1) % _buffer.Length;
        else
            _count++;
    }

    public void Clear()
    {
        _head = 0;
        _tail = 0;
        _count = 0;
        Array.Clear(_buffer);
    }

    public IEnumerable<T> AsEnumerable()
    {
        for (int i = 0; i < _count; i++)
            yield return _buffer[(_tail + i) % _buffer.Length];
    }

    public T[] ToArray() => AsEnumerable().ToArray();
}
