// Path: Core/Host/EventBus.cs
// File: EventBus.cs
// Purpose: In-Memory EventBus (Publish/Subscribe) für Events zwischen Modulen.

using Core.Domain;

namespace Core.Host;

public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<TEvent>(TEvent ev)
    {
        var t = typeof(TEvent);
        if (_handlers.TryGetValue(t, out var list))
        {
            foreach (var h in list.ToArray())
                ((Action<TEvent>)h).Invoke(ev);
        }
    }

    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        var t = typeof(TEvent);
        if (!_handlers.TryGetValue(t, out var list))
        {
            list = new List<Delegate>();
            _handlers[t] = list;
        }
        list.Add(handler);
    }
}
