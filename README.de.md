# NewLife.BACnet — BACnet-Protokoll

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)
[![test](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml)

<!-- lang:start -->
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

> Ein vollständig verwalteter BACnet-Protokollstapel für .NET, entwickelt für die Gebäudeautomation (HVAC, Beleuchtung, Sicherheit und Energiemanagement). Er implementiert die vollständige BACnet/IP-Kommunikation und ist eng in das NewLife-IoT-Ökosystem integriert.

**BACnet** (Building Automation and Control Networks) ist das von ASHRAE/ANSI 135 und ISO 16484-5 definierte Netzwerkprotokoll für die Gebäudeautomation. Es wird in Produkten von Johnson Controls, Honeywell, Siemens Building Technologies, Schneider Electric und anderen großen Herstellern eingesetzt.

Quellcode: https://github.com/NewLifeX/NewLife.BACnet  
NuGet: `NewLife.BACnet` | Zielframeworks: `netstandard2.0` + `net45`

## Highlights

| Fähigkeit | Beschreibung |
|--------|------|
| **Transport** | BACnet/IP-UDP-Transport (IPv4/IPv6), BBMD-Broadcast-Verwaltung und Registrierung externer Geräte |
| **Protokoll-Codec** | Vollständige Codierung und Decodierung von ASN.1 BER / APDU / NPDU / BVLC |
| **Geräteerkennung** | Automatische Erkennung per Who-Is-Broadcast, gerichtete Unicast-Erkennung und periodische Neusuche zur Erkennung von Online-/Offline-Wechseln |
| **Eigenschaftszugriff** | Einzel- und Batch-Lesen/Schreiben von Eigenschaften, Priority-Array-Schreibpriorität (1-16) und invoke-id-Isolierung pro Gerät |
| **COV-Benachrichtigungen** | Echtzeit-Übertragung von Eigenschaftsänderungen statt Polling, mit automatischer Verlängerung der Abonnements |
| **Ereignisse und Alarme** | Ende-zu-Ende-Ereignisbenachrichtigung, Alarmzusammenfassung und Alarmbestätigung |
| **Dateidienste** | AtomicReadFile / AtomicWriteFile für Sicherung und Wiederherstellung der Gerätekonfiguration |
| **Objektverwaltung** | CreateObject / DeleteObject / AddListElement / RemoveListElement |
| **BACnet-Server** | Simulationsserver auf Basis einer XML-Gerätebeschreibung, zugänglich für Fremdsysteme |
| **High-Level-Client-API** | `BacClient`, eine geschäftsorientierte API mit Punktadressen im Format `"Instanz_Typ"` |
| **Asynchrone API** | Vollständige Async/Await-Methoden wie `ReadPropertyAsync`/`WritePropertyAsync` |
| **Sicherer Transport** | BACnet/SC (WebSocket + TLS) mit Hub/Node- und Direktmodus |
| **Standarddienste** | Who-Has/I-Have, Zeitsynchronisierung, Gerätekommunikationssteuerung und weitere BACnet-Standarddienste |
| **Zeitpläne** | Codec für Schedule/Calendar-Datentypen (DailySchedule/SpecialEvent) |
| **IoT-Treiberintegration** | Implementiert die Treiberverträge von `NewLife.IoT` und wird gemeinsam mit Modbus-/Siemens-Treibern verwaltet |
| **Beobachtbarkeit** | `ITracer`-APM-Tracing + `ILog`-Protokollierung für Ende-zu-Ende-Sichtbarkeit |
| **Vollständig verwaltet** | Keine nativen Abhängigkeiten; läuft unter Windows / Linux / macOS / ARM |

## Dokumentation

- [Anforderungen](Doc/需求文档.md) — Vision, Kernziele und funktionale Anforderungen
- [Funktionsliste](Doc/功能清单.md) — Statusverfolgung für Implementierung/Tests/Kommentare (derzeit 90 % abgeschlossen)
- [Architektur](Doc/架构设计.md) — fünfschichtige Architektur, Komponenten und zentrale Abläufe
- [Wettbewerbsanalyse](Doc/竞品分析.md) — Vergleichsmatrix, Lückenanalyse und strategische Empfehlungen

## Schnellstart

### Installation

```shell
dotnet add package NewLife.BACnet
```

### Client: Geräte erkennen und Eigenschaften lesen

```csharp
using NewLife.BACnet.Protocols;
using NewLife.Log;

var client = new BacClient
{
    Port = 0xBAC0,          // Standard 47808
    WaitingTime = 3000,     // Timeout für Geräteantworten (ms)
    Log = XTrace.Log,
};

client.Open();  // Who-Is senden und warten, bis das erste Gerät antwortet

foreach (var node in client.Nodes)
{
    // Punktadressformat "Instanz_Typ": "0_2" = AV[0]
    var value = client.ReadProperty(node.Address, "0_2");
    Console.WriteLine($"[{node.DeviceId}] AV[0] = {value}");

    var results = client.ReadProperties(node.Address, new[] { "0_0", "0_2" });
    foreach (var kv in results)
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
}

client.Close();
```

### Server: ein BACnet-Gerät bereitstellen

```csharp
using NewLife.BACnet.Protocols;

var server = new BacServer
{
    DeviceId = 666,
    StorageFile = "DeviceDescriptor.xml",
    Port = 0xBAC0,
    Log = XTrace.Log,
};

server.Open();  // Lauschen starten und I-Am senden, um sich anzukündigen
```

### IoT-Treiberintegration

```csharp
using NewLife.BACnet.Drivers;

var driver = new BACnetDriver { Log = XTrace.Log };
var node = driver.Open(device, new BACnetParameter
{
    Port = 0xBAC0,
    DeviceId = 666,
    TargetAddress = "192.168.1.100:47808",
});

var values = driver.Read(node, new[] {
    new PointModel { Name = "Temperatur", Address = "0_2" },
    new PointModel { Name = "Feuchtigkeit", Address = "1_2" },
});
```

## Konfiguration

| Parameter | Typ | Standardwert | Beschreibung |
|------|------|--------|------|
| `Port` | Int32 | 47808 (0xBAC0) | BACnet/IP-UDP-Port |
| `DeviceId` | Int32 | 0 | Zielgeräte-ID; 0 akzeptiert alle Geräte |
| `WaitingTime` | Int32 | 3000 ms | Timeout für Open() beim Warten auf Geräteantworten |
| `BatchSize` | Int32 | 20 | Anzahl der pro Batch gelesenen Eigenschaften |
| `Timeout` | Int32 | 1000 ms | Timeout einer einzelnen Anforderung (BacnetClient) |
| `Retries` | Int32 | 3 | Anzahl der Wiederholungen nach einer fehlgeschlagenen Anforderung |
| `WritePriority` | UInt32 | 0 | Schreibpriorität (1-16; 0 = Standard) |
| `TargetAddress` | String | null | Unicast-Ziel für Who-Is (IP:Port); bei leerem Wert wird Broadcast verwendet |

## Bekannte Einschränkungen

| Problem | Beschreibung | Status |
|------|------|------|
| Sehr große Geräte | Bei mehr als 100 Objekten kann ein Paket das APDU-Limit überschreiten; BatchSize manuell steuern | Zu optimieren |
| MS/TP-/PTP-Transport | Der serielle Transportcode existiert, ist aber von der Kompilierung ausgeschlossen; die plattformübergreifende Kompatibilität ist ungeklärt | Deaktiviert |
| Tests der BBMD-Broadcast-Verwaltung | TRN-3/TRN-4 sind implementiert, aber ohne Unit-Tests | Tests fehlen |
| Tests des IPv6-Transports | TRN-2 ist implementiert, aber ohne IPv6-Integrationstests | Tests fehlen |

## Empfohlene Werkzeuge

[YABE (Yet Another BACnet Explorer)](https://sourceforge.net/projects/yetanotherbacnetexplorer/) — ein kostenloser BACnet-Netzwerkexplorer, der Geräte, Objekte und Eigenschaften anzeigt. Er ist der bevorzugte Peer zur Validierung von NewLife.BACnet beim Debuggen.

## Ökosystem-Landkarte

NewLife ist ein geschichtetes Ökosystem unabhängig versionierter Open-Source-Projekte. Die folgende Landkarte ist in allen NewLife-Repositories wortgleich; jedes Projekt verlinkt sein Repository und sein Haupt-NuGet-Paket.

| Projekt | Kurzbeschreibung | Downloads |
| --- | --- | --- |
| **Fundament** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Protokollierung, Konfiguration, Caching, Netzwerk, Serialisierung, Zeitplanung und APM-Tracing. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Datenmiddleware: einzelne Tabellen mit zig Milliarden Zeilen, automatisches Sharding, Lese-/Schreibtrennung, über 10 Datenbanken. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | Hochleistungs-Netzwerkbibliothek: 1,77 Mio. pps / 14,5 Gbps auf einem Rechner (TCP 1 KB, 2026-10), 4 Mio. langlebige TCP-Verbindungen. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Protokollkommunikation und RPC-Framework für Client-Server-Anwendungen und IoT-Geräte. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Installiert .NET-Anwendungen als Betriebssystemdienste: Windows-Dienste und Linux-systemd. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new`-Projektvorlagen für Konsole, Web, API, Dienst, Daten und IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Daten & Messaging** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis-Client mit Mikrosekunden-Latenz, Millionen Operationen pro Sekunde und eingebauten Nachrichtenwarteschlangen. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Reiner Managed-RocketMQ-Client für Apache RocketMQ und Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT-Client und -Server, einschließlich Alibaba-Cloud-IoT-Anbindung. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Hybriddatenbank in C#: eingebettet oder im Netzwerk, relational, Zeitreihen, Nachrichtenwarteschlange und KV in einem. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | MySql-Treiber und Hilfsschicht auf dem NewLife-Datenstack. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, KI & Verteiltes** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Schnelle Admin-Plattform: Berechtigungen, SSO, OAuth-Server und CRUD-Gerüst. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Verteiltes Dienstplatform: Knotenverwaltung, APM, Konfigurationscenter, Registry und Release-Center. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Verteiltes Aufgaben- und Echtzeit-Datenplatform mit Sharding, Wiederholungen und Backfill. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Einheitlicher Client für 46 LLM-Anbieter plus ChatAI, eine sofort nutzbare Web-Chat-Anwendung. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industrie & IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT-Standardbibliothek mit Protokollverträgen und Geräteabstraktionen. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Modbus TCP/RTU/ASCII auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Siemens-SPS-Protokoll auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Omron-SPS-Protokoll auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Mitsubishi-Melsec-SPS-Protokoll auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | OPC-Client für industriellen Datenzugriff. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet-Protokollstack für die Gebäudeautomation. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Leichtgewichtige Open-Source-IoT-Plattform mit Gateway und Geräteverwaltung. | — |
| **Fahrzeug-Telematik** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 Fahrzeugortungsprotokoll: über 70 Nachrichtentypen, Versionen 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T 809 Plattform-zu-Plattform-Protokoll für Verkehrsbehörden. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 Fahrzeugvideo: RTP-Parsing, FLV/fMP4-Remuxing und Live-Streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Domänenkomponenten** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Ein Paket für Excel, Word, PPT, PDF und über 15 Dokumentformate. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Plattformübergreifendes Audio: Codecs, Geräte-I/O, DSP, Container und Streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Videocodecs, Containerformate, Frame-Verarbeitung und Streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Einheitliche Schnittstelle für Baidu, AMap, Tencent und Tianditu mit Geokodierung und Koordinatentransformation. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4-Geolokalisierung mit offline speicherabgebildeter Datenbank und Online-API-Fallback. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Werkzeuge & Produkte** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Entwicklerwerkzeug: Netzwerk, serielle Schnittstelle, Kryptografie, Regex, Modbus und MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Selbst gehostetes Git-Quellcodeverwaltungssystem. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Installiert bei Start aus einem älteren Host automatisch die passende .NET-Runtime. | — |

> Die Badges zeigen das Hauptpaket jedes Projekts; Theme-, Erweiterungs- und Satellitenpakete sind auf ihren NuGet-Seiten zu finden. Die über 30 hier nicht gelisteten Pakete (FTP, LoRa, Office-Erweiterungen, Protokolltreiber und mehr) finden Sie in der [NewLifeX-Organisation](https://github.com/NewLifeX) und auf [newlifex.com](https://newlifex.com).

## Team

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Das NewLife-Team wurde 2002 gegründet und arbeitet seit mehr als zwei Jahrzehnten am .NET- und IoT-Ökosystem. Es betreut fast 100 Open-Source-Projekte (34 davon sind Kernprojekte dieser Landkarte) und liefert integrierte Software- und Hardwarelösungen für Energie, Logistik, Gesundheitswesen, Industrieautomation und Internetbranchen. Unser Leitspruch: „Lernen kennt keine Reihenfolge — wer es beherrscht, wird geachtet“.

Open-Source-Komponenten wurden <!--nuget-total:start-->6.48M<!--nuget-total:end--> Mal von NuGet heruntergeladen (ökosystemweit, Stand 2026-10).

Website: https://newlifex.com · Open-Source-Organisation: https://github.com/NewLifeX

<details>
<summary>WeChat-Kanal: 智能大石头 (zum Folgen scannen)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## Lizenz

MIT-Lizenz — siehe [LICENSE](LICENSE).
