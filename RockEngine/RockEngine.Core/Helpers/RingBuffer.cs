namespace RockEngine.Core.Helpers
{
    public class RingBuffer<T>(int capacity)
    {
        private readonly T[] _buffer = new T[capacity];
        private int _index;
        private int _count;

        public void Push(T item)
        {
            _buffer[_index] = item;
            _index = (_index + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
        public T Get()=> _buffer[_index];

        public T Last() => _buffer[(_index - 1 + _buffer.Length) % _buffer.Length];
        public int Count => _count;
        public T[] ToArray()
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[(_index - _count + i + _buffer.Length) % _buffer.Length];
            }
            return result;
        }

        public T? Max()
        {
            var arr = ToArray();
            return arr.Length > 0 ? arr.Max() : default;
        }

        public T? Min()
        {
            var arr = ToArray();
            return arr.Length > 0 ? arr.Min() : default;
        }
        public float Average()
        {
            var list = ToArray();
            if (list.Length == 0) return 0;

            double sum = 0;
            foreach (var item in list)
            {
                sum += Convert.ToDouble(item);
            }
            return (float)(sum / list.Length);
        }
        public void Clear()
        {
            _index = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }
}
