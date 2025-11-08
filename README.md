# DayZ Server Manager

Ein umfassendes Tool zum Verwalten und Automatisieren von DayZ-Servern.

Kurzbeschreibung

DayZ Server Manager ist eine in C# implementierte Anwendung zum Starten, Stoppen und Verwalten von DayZ Dedicated Server-Instanzen. Das Projekt bietet sowohl CLI- als auch GUI-Optionen (sofern implementiert), automatische Backups, Log-Überwachung und einfache Konfigurationsmöglichkeiten.

Features

- Starten / Stoppen / Neustarten von DayZ-Serverinstanzen
- Verwaltung mehrerer Serverprofile
- Geplante Backups und manuelle Wiederherstellung
- Protokollanzeige (Logs) und einfache Fehlersuche
- Benachrichtigungen via Webhook (z. B. Discord) (optional)
- Scheduler für Wartungsaufgaben

Voraussetzungen

- .NET 6 SDK (oder neuer)
- Windows (empfohlen) — Linux-Unterstützung kann eingeschränkt sein
- DayZ Dedicated Server installiert und lauffähig

Installation (Entwicklung)

1. Repository klonen:
   git clone https://github.com/ChrisZeroTwo/DayZServerManager.git
2. Ins Verzeichnis wechseln:
   cd DayZServerManager
3. Abhängigkeiten wiederherstellen und Projekt bauen:
   dotnet restore
   dotnet build
4. Anwendung starten (Entwicklung):
   dotnet run --project Pfad/Zu/DeinemProjekt.csproj

Installation (Produktiv / Release)

- Erzeuge ein Release-Build:
  dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
- Optional: Anwendung als Windows-Dienst oder Systemd-Service (Linux) einrichten.

Konfiguration

Die Anwendung liest Einstellungen aus appsettings.json oder Umgebungsvariablen. Beispiel appsettings.json:

{
  "DayZ": {
    "ServerExePath": "C:\\Path\\to\\DayZServer_x64.exe",
    "ServerArgs": "-config=serverDZ.cfg -port=2302",
    "WorkingDirectory": "C:\\Path\\to\\server\\directory"
  },
  "Backup": {
    "Enabled": true,
    "BackupPath": "C:\\DayZBackups",
    "Schedule": "0 3 * * *"
  },
  "Notifications": {
    "DiscordWebhook": "",
    "Email": {
      "SmtpHost": "",
      "From": "",
      "To": ""
    }
  }
}

Ersetze die Pfade und Werte durch deine eigenen Einstellungen. Vermeide es, Tokens oder Passwörter im Repository zu speichern; nutze Umgebungsvariablen oder ein Secrets-Management.

Nutzung / Beispiele

- Starten eines Servers (GUI): Menü -> Server auswählen -> Start
- Starten eines Servers (CLI):
  DayZServerManager.exe start --profile "MyServer"
- Stoppen eines Servers (CLI):
  DayZServerManager.exe stop --profile "MyServer"
- Backup auslösen (CLI):
  DayZServerManager.exe backup --profile "MyServer"
- Logs anzeigen (CLI):
  DayZServerManager.exe logs --profile "MyServer" --tail 200

Build & Entwicklung

- Feature-Branch erstellen: git checkout -b feature/<kurzbeschreibung>
- Änderungen committen und pushen
- Pull Request erstellen
- Tests (falls vorhanden): dotnet test Pfad/Zu/Testprojekt
- Formatierung: dotnet format

Mitwirken

- Issues für Bugs und Feature-Requests öffnen
- Pull Requests willkommen — bitte beschreibe die Änderung und mögliche Breaking Changes
- Nutze einen konsistenten Code-Style (.editorconfig / StyleCop nach Bedarf)

Sicherheit

- Keine Tokens oder Zugangsdaten im Repo ablegen
- Logs vor dem Teilen auf sensible Daten prüfen

Bekannte Einschränkungen / TODO

- Multi-OS-Support noch nicht vollständig getestet
- Integrierte Update-Funktion fehlt
- Weitere Features: feinere Rechteverwaltung, mehr Benachrichtigungs-Kanäle

Lizenz

Dieses Projekt steht unter der MIT-Lizenz. Siehe LICENSE für Details.

Kontakt

Projekt-Inhaber: ChrisZeroTwo (https://github.com/ChrisZeroTwo)

Changelog

Siehe ggf. CHANGELOG.md oder Git-History für Versionshinweise.
