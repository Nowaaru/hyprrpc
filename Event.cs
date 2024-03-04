namespace HyprRPC
{
    namespace Events
    {
        public class Event<EventType>
        {
            public readonly EventType Type;
            private readonly Action _storedAction;

            public Event(EventType EventType, Action action)
            {
                this._storedAction = action;
                this.Type = EventType;
            }

            public void Fire()
            {
                this._storedAction();
                return;
            }
        }

        public class EventFactory<T>
        {
            public List<Event<T>> Events = new List<Event<T>>();

            public Event<T> ConnectEvent(T eventType, Action action)
            {
                Event<T> newEvent = new(eventType, action);
                this.Events.Add(newEvent);

                return newEvent;
            }

            public Event<T> ConnectEvent(Event<T> premadeEvent)
            {
                this.Events.Add(premadeEvent);
                return premadeEvent;
            }

            protected async Task<bool> FireEvent(T eventType)
            {
                TaskFactory factory = new();
                List<Task> tasks = new();

                foreach (Event<T> i in this.Events.FindAll((e) => e.Type?.Equals(eventType) ?? false))

                    tasks.Add(factory.StartNew(i.Fire));

                return await factory.ContinueWhenAll(tasks.ToArray(), (tasks) => true);
            }
        }

        public enum RPCEventType
        {
            APPLICATION_IN,
            APPLICATION_OUT,
            ENDANGERED,
            UPDATE,
        }

        public class RPCEvent : Event<RPCEventType>
        {
            public RPCEvent(RPCEventType EventType, Action action) : base(EventType, action)
            {}
        }
    }
}
