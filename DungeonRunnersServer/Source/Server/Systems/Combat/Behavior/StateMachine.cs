using System;
using System.Collections.Generic;

namespace DungeonRunners.Combat.Behavior
{
    public readonly struct StateMachineMessageSnapshot
    {
        public int DueTick { get; }
        public int Id { get; }
        public int Param { get; }
        public int Interval { get; }
        public bool Consumed { get; }

        internal StateMachineMessageSnapshot(int dueTick, int id, int param, int interval, bool consumed)
        {
            DueTick = dueTick;
            Id = id;
            Param = param;
            Interval = interval;
            Consumed = consumed;
        }
    }

    public sealed class StateMachineSnapshot
    {
        public int Clock { get; }
        public IReadOnlyList<StateMachineMessageSnapshot> Messages { get; }

        internal StateMachineSnapshot(int clock, StateMachineMessageSnapshot[] messages)
        {
            Clock = clock;
            Messages = Array.AsReadOnly(messages);
        }
    }

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
            for (int dueIndex = 0; dueIndex < _due.Count; dueIndex++)
                _due[dueIndex].Consumed = true;
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
            for (int dueIndex = 0; dueIndex < _due.Count; dueIndex++)
            {
                Message due = _due[dueIndex];
                if (due.Id == id && due.Param == param)
                    due.Consumed = true;
            }
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

        public StateMachineSnapshot GetSnapshot()
        {
            var messages = new StateMachineMessageSnapshot[_messages.Count];
            for (int messageIndex = 0; messageIndex < _messages.Count; messageIndex++)
            {
                Message message = _messages[messageIndex];
                messages[messageIndex] = new StateMachineMessageSnapshot(
                    message.DueTick,
                    message.Id,
                    message.Param,
                    message.Interval,
                    message.Consumed);
            }
            return new StateMachineSnapshot(Clock, messages);
        }

        public void DeliverMessages(Action<int, int> onMessage)
        {
            if (_messages.Count == 0)
                return;
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
                if (message.Consumed)
                    continue;
                onMessage?.Invoke(message.Id, message.Param);
                if (message.Interval != 0 && !message.Consumed)
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
