using System;
using System.Collections.Generic;

namespace DungeonRunners.Combat.Behavior
{
    public sealed class StateMachine
    {
        public sealed class Message
        {
            public int DueTick;
            public int Id;
            public int Param;
            public int Interval;
            public bool Consumed;
        }

        public int Clock;

        private readonly List<Message> _messages = new List<Message>();
        private readonly List<Message> _due = new List<Message>();

        public void Reset()
        {
            Clock = 0;
            _messages.Clear();
        }

        public void SendMessageA(int id, int delay, int interval, int param = 0xffff)
        {
            for (int messageIndex = 0; messageIndex < _messages.Count; messageIndex++)
            {
                Message existing = _messages[messageIndex];
                if (!existing.Consumed && existing.Id == id && existing.Param == param)
                    return;
            }

            var message = new Message
            {
                DueTick = Clock + 1 + delay,
                Id = id,
                Param = param,
                Interval = interval,
            };
            Insert(message);
        }

        public void CancelMessage(int id, int param = 0xffff)
        {
            for (int messageIndex = _messages.Count - 1; messageIndex >= 0; messageIndex--)
            {
                Message message = _messages[messageIndex];
                if (!message.Consumed && message.Id == id && message.Param == param)
                    _messages.RemoveAt(messageIndex);
            }
        }

        public int GetMessageETA(int id, int param = 0xffff)
        {
            for (int messageIndex = 0; messageIndex < _messages.Count; messageIndex++)
            {
                Message message = _messages[messageIndex];
                if (!message.Consumed && message.Id == id && message.Param == param)
                    return message.DueTick - Clock;
            }
            return -1;
        }

        public void DeliverMessages(Action<int, int> onMessage)
        {
            Clock++;

            _due.Clear();
            for (int messageIndex = 0; messageIndex < _messages.Count; messageIndex++)
            {
                if (_messages[messageIndex].DueTick == Clock)
                    _due.Add(_messages[messageIndex]);
            }
            if (_due.Count == 0) return;

            for (int dueIndex = 0; dueIndex < _due.Count; dueIndex++)
                _messages.Remove(_due[dueIndex]);

            for (int dueIndex = 0; dueIndex < _due.Count; dueIndex++)
            {
                Message message = _due[dueIndex];
                onMessage?.Invoke(message.Id, message.Param);
                if (message.Interval != 0)
                {
                    message.DueTick = Clock + message.Interval;
                    Insert(message);
                }
            }
        }

        private void Insert(Message message)
        {
            int insertIndex = 0;
            while (insertIndex < _messages.Count && _messages[insertIndex].DueTick <= message.DueTick)
                insertIndex++;
            _messages.Insert(insertIndex, message);
        }
    }
}
