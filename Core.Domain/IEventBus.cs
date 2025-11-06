// Path: Core/Domain/IEventBus.cs
// File: IEventBus.cs
// Purpose: Zentrales Publish/Subscribe-Interface für lose gekoppelte Modulkommunikation.

namespace Core.Domain;

public interface IEventBus
{
    void Publish<TEvent>(TEvent ev);
    void Subscribe<TEvent>(Action<TEvent> handler);
}
