using System.Collections.Generic;

namespace DungeonRunners.Networking
{
    public class MessageQueue
    {
        private readonly Queue<byte[]> _queue = new Queue<byte[]>();
        private readonly object _lock = new object();

        public void Enqueue(byte[] data)
        {
            if (data == null || data.Length == 0)
                return;
            lock (_lock)
                _queue.Enqueue(data);
        }

        public bool IsEmpty()
        {
            lock (_lock)
                return _queue.Count == 0;
        }

        public List<byte[]> DequeueAll()
        {
            lock (_lock)
            {
                var messages = new List<byte[]>(_queue);
                _queue.Clear();
                return messages;
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return _queue.Count;
            }
        }

        public void Clear()
        {
            lock (_lock)
                _queue.Clear();
        }
    }
}
