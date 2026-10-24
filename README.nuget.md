<!-- generated from README.md by sync-readme-package.ps1; do not edit -->

# NewLife.BACnet — BACnet Protocol

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)
[![test](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml)

<!-- lang:start -->
🌐 [简体中文](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.zh-CN.md) | [繁體中文](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.zh-TW.md) | [Español](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.es.md) | [Deutsch](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.de.md) | [日本語](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.ja.md) | [Français](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.fr.md) | [Português](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.pt.md) | [Русский](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.ru.md) | [한국어](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.ko.md) | [العربية](https://github.com/NewLifeX/NewLife.BACnet/blob/master/README.ar.md)
<!-- lang:end -->

> A pure-managed BACnet protocol stack for .NET, built for building automation (HVAC, lighting, security and energy management). It implements complete BACnet/IP communication and integrates deeply with the NewLife IoT ecosystem.

**BACnet** (Building Automation and Control Networks) is the building automation network protocol defined by ASHRAE/ANSI 135 and ISO 16484-5. It is widely used in products from Johnson Controls, Honeywell, Siemens Building Technologies, Schneider Electric and other major vendors.

Source: https://github.com/NewLifeX/NewLife.BACnet  
NuGet: `NewLife.BACnet` | Target frameworks: `netstandard2.0` + `net45`

## Highlights

| Capability | Description |
|--------|------|
| **Transport** | BACnet/IP (IPv4/IPv6) UDP transport, BBMD broadcast management and foreign-device registration |
| **Protocol codec** | Complete ASN.1 BER / APDU / NPDU / BVLC encoding and decoding |
| **Device discovery** | Automatic Who-Is broadcast discovery, directed unicast discovery, periodic rescans to detect online/offline changes |
| **Property access** | Single and batch property read/write, Priority Array write priority (1-16), per-device invoke-id isolation |
| **COV notifications** | Real-time property-change push instead of polling, with automatic subscription renewal |
| **Events & alarms** | End-to-end event notification, alarm summary and alarm acknowledgement |
| **File services** | AtomicReadFile / AtomicWriteFile for device configuration backup and restore |
| **Object management** | CreateObject / DeleteObject / AddListElement / RemoveListElement |
| **BACnet server** | Simulation server driven by an XML device description, accessible to third-party building systems |
| **High-level client API** | `BacClient`, a business-oriented API with string point addresses in `"instance_type"` form |
| **Async API** | Complete `ReadPropertyAsync`/`WritePropertyAsync` and other Async/Await methods |
| **Secure transport** | BACnet/SC (WebSocket + TLS) secure transport with Hub/Node and direct modes |
| **Standard services** | Who-Has/I-Have, time synchronization, device communication control and other standard BACnet services |
| **Schedules** | Schedule/Calendar data type codec (DailySchedule/SpecialEvent) |
| **IoT driver integration** | Implements the `NewLife.IoT` driver contracts and shares management with Modbus/Siemens drivers |
| **Observability** | `ITracer` APM tracing + `ILog` logging for end-to-end visibility |
| **Pure managed** | Zero native dependencies; runs on Windows / Linux / macOS / ARM |

## Documentation

- [Requirements](https://github.com/NewLifeX/NewLife.BACnet/blob/master/Doc/需求文档.md) — vision, core goals and functional requirements
- [Feature list](https://github.com/NewLifeX/NewLife.BACnet/blob/master/Doc/功能清单.md) — implementation/test/comment status tracking (currently 90% complete)
- [Architecture](https://github.com/NewLifeX/NewLife.BACnet/blob/master/Doc/架构设计.md) — five-layer architecture, components and key flows
- [Competitive analysis](https://github.com/NewLifeX/NewLife.BACnet/blob/master/Doc/竞品分析.md) — comparison matrix, gap analysis and strategic recommendations

## Quick start

### Install

```shell
dotnet add package NewLife.BACnet
```

### Client: discover devices and read properties

```csharp
using NewLife.BACnet.Protocols;
using NewLife.Log;

var client = new BacClient
{
    Port = 0xBAC0,          // default 47808
    WaitingTime = 3000,     // device response timeout (ms)
    Log = XTrace.Log,
};

client.Open();  // broadcast Who-Is and block until the first device responds

foreach (var node in client.Nodes)
{
    // Point address format "instance_type": "0_2" = AV[0]
    var value = client.ReadProperty(node.Address, "0_2");
    Console.WriteLine($"[{node.DeviceId}] AV[0] = {value}");

    var results = client.ReadProperties(node.Address, new[] { "0_0", "0_2" });
    foreach (var kv in results)
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
}

client.Close();
```

### Server: expose a BACnet device

```csharp
using NewLife.BACnet.Protocols;

var server = new BacServer
{
    DeviceId = 666,
    StorageFile = "DeviceDescriptor.xml",
    Port = 0xBAC0,
    Log = XTrace.Log,
};

server.Open();  // start listening and broadcast I-Am to announce itself
```

### IoT driver integration

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
    new PointModel { Name = "Temperature", Address = "0_2" },
    new PointModel { Name = "Humidity", Address = "1_2" },
});
```

## Configuration

| Parameter | Type | Default | Description |
|------|------|--------|------|
| `Port` | Int32 | 47808 (0xBAC0) | BACnet/IP UDP port |
| `DeviceId` | Int32 | 0 | Target device ID; 0 accepts all devices |
| `WaitingTime` | Int32 | 3000 ms | Timeout for Open() to wait for device responses |
| `BatchSize` | Int32 | 20 | Number of properties read per batch |
| `Timeout` | Int32 | 1000 ms | Single-request timeout (BacnetClient) |
| `Retries` | Int32 | 3 | Number of retries after a failed request |
| `WritePriority` | UInt32 | 0 | Write priority (1-16; 0 = default) |
| `TargetAddress` | String | null | Unicast Who-Is target (IP:Port); broadcast when empty |

## Known limitations

| Issue | Description | Status |
|------|------|------|
| Very large devices | With 100+ objects a packet may exceed the APDU limit; control BatchSize manually | To optimize |
| MS/TP / PTP transport | Serial transport code exists but is excluded from compilation; cross-platform compatibility is unresolved | Disabled |
| BBMD broadcast management tests | TRN-3/TRN-4 are implemented but lack unit tests | Tests needed |
| IPv6 transport tests | TRN-2 is implemented but lacks IPv6 integration tests | Tests needed |

## Recommended tools

[YABE (Yet Another BACnet Explorer)](https://sourceforge.net/projects/yetanotherbacnetexplorer/) — a free BACnet network explorer that shows devices, objects and properties. It is the preferred peer for validating NewLife.BACnet during debugging.

## Ecosystem Map

NewLife is a layered ecosystem of independently versioned open-source projects. The map below is shared verbatim by every NewLife repository; each project links to its repository and its primary NuGet package.

| Project | What it does | Downloads |
| --- | --- | --- |
| **Foundation** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Logging, configuration, caching, networking, serialization, scheduling and APM tracing. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Data middleware: single tables at the tens-of-billions scale, automatic sharding, read-write splitting, 10+ databases. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | High-performance network library: 1.77M pps / 14.5 Gbps on a single machine (TCP 1KB, 2026-10), 4M long-lived TCP connections. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Protocol communication and RPC framework for client/server applications and IoT devices. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Installs .NET applications as OS daemons: Windows services and Linux systemd. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new` project templates for console, web, API, service, data and IoT projects. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Data & Messaging** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis client with microsecond latency, millions of ops/s and built-in message queues. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Pure managed RocketMQ client for Apache RocketMQ and Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT client and server, including Alibaba Cloud IoT connectivity. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Hybrid database in C#: embedded or networked, relational, time-series, message queue and KV in one. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | MySql driver and helper layer built on the NewLife data stack. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, AI & Distributed** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Rapid admin-platform framework: permissions, SSO, OAuth server and CRUD scaffolding. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Distributed service platform: node management, APM, configuration center, registry and release center. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Distributed task and real-time data scheduling platform with sharding, retries and backfill. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Unified client for 46 LLM providers plus ChatAI, a ready-to-run web chat application. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industrial & IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT standard library defining protocol contracts and device abstractions. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Modbus TCP/RTU/ASCII implementation on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Siemens PLC protocol on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Omron PLC protocol on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Mitsubishi Melsec PLC protocol on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | OPC client for industrial data access. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet building-automation protocol stack. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Lightweight open-source IoT platform with gateway and device management. | — |
| **Vehicle Telematics** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 vehicle positioning protocol: 70+ message bodies, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T 809 platform-to-platform protocol for transport authorities. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 in-vehicle video: RTP parsing, FLV/fMP4 remuxing and live streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Domain Components** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | One package for Excel, Word, PPT, PDF and 15+ document formats. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Cross-platform audio: codecs, device I/O, DSP, containers and streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Video codecs, container formats, frame processing and streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Unified interface over Baidu, AMap, Tencent and Tianditu with geocoding and coordinate conversion. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4 geolocation with an offline memory-mapped database and an online API fallback. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Tools & Products** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Developer toolbox: network, serial port, crypto, regex, Modbus and MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Self-hosted Git source code management system. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Installs the matching .NET runtime automatically when launched from an older host. | — |

> Badges show the primary package of each project; theme, extension and satellite packages are listed on their NuGet pages. The 30+ packages not listed here (FTP, LoRa, Office extensions, protocol drivers and more) are available under the [NewLifeX organization](https://github.com/NewLifeX) and on [newlifex.com](https://newlifex.com).

## Team

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Founded in 2002, the NewLife team has spent more than two decades on the .NET and IoT ecosystem, maintains nearly 100 open-source projects (34 of them core to this map), and delivers integrated software and hardware solutions for power, logistics, healthcare, industrial control and internet industries. We live by the motto "learning has no seniority — mastery earns respect".

Open-source components have been downloaded <!--nuget-total:start-->6.48M<!--nuget-total:end--> times from NuGet (ecosystem-wide, as of 2026-10).

Website: https://newlifex.com · Open source organization: https://github.com/NewLifeX

<details>
<summary>WeChat Official Account: 智能大石头 (scan to follow)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## License

MIT License — see [LICENSE](https://github.com/NewLifeX/NewLife.BACnet/blob/master/LICENSE).
