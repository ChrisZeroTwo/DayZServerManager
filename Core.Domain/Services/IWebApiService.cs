// Path: Core/Domain/Services/IWebApiService.cs
// File: IWebApiService.cs
// Purpose: Start/Stop-Schnittstelle für die eingebettete Web-API (Minimal API über Kestrel).

namespace Core.Domain.Services;

public interface IWebApiService
{
    /// <summary>Startet die WebAPI, wenn in manager.json aktiviert.</summary>
    void Start();

    /// <summary>Stoppt die WebAPI (graceful).</summary>
    void Stop();

    /// <summary>True, wenn der HTTP-Server läuft.</summary>
    bool IsRunning { get; }
}
