# NewLife.BACnet — BACnet 协议

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)

> 基于 .NET 的纯托管 BACnet 协议栈，面向楼宇自动化（HVAC、照明、安防、能源管理）领域，实现完整的 BACnet/IP 协议通信，深度集成 NewLife IoT 物联网平台生态。

**BACnet**（Building Automation and Control Networks）是 ASHRAE/ANSI 135 / ISO 16484-5 标准的楼宇自动化网络协议，广泛应用于约翰逊控制、霍尼韦尔、西门子楼宇、施耐德等主流设备厂商的产品。

源码：https://github.com/NewLifeX/NewLife.BACnet  
Nuget：`NewLife.BACnet` | 目标框架：`netstandard2.0` + `net45`

## 亮点

| 能力域 | 说明 |
|--------|------|
| **传输层** | BACnet/IP (IPv4/IPv6) UDP 传输，BBMD 广播管理与外部设备注册 |
| **协议编解码** | ASN.1 BER / APDU / NPDU / BVLC 各层协议完整编解码 |
| **设备发现** | Who-Is 广播自动发现，定向单播发现，定时重扫感知上下线 |
| **属性读写** | 单属性与批量读写，Priority Array 写入优先级（1-16 级），invoke-id 按设备隔离 |
| **COV 变更通知** | 订阅属性变化实时推送，替代轮询，支持订阅自动续约 |
| **事件与报警** | 事件通知收发、报警摘要、报警确认全链路支持 |
| **文件服务** | AtomicReadFile / AtomicWriteFile 支持设备配置文件备份恢复 |
| **对象管理** | CreateObject / DeleteObject / AddListElement / RemoveListElement |
| **BACnet 服务端** | 基于 XML 设备描述的仿真服务端，支持第三方楼控系统访问 |
| **高级客户端 API** | `BacClient` 面向业务的简洁 API，字符串点位地址格式 `"实例_类型"` |
| **异步 API** | 完整的 `ReadPropertyAsync`/`WritePropertyAsync` 等 Async/Await 方法集 |
| **安全通信** | BACnet/SC (WebSocket + TLS) 安全传输，Hub/Node 与直连双模式 |
| **标准服务补齐** | Who-Has/I-Have、时间同步、设备通信控制等标准 BACnet 服务 |
| **日程表支持** | Schedule/Calendar 数据类型编解码（DailySchedule/SpecialEvent） |
| **IoT 驱动集成** | 实现 `NewLife.IoT` 驱动接口，与 Modbus/西门子等统一管理 |
| **可观测性** | `ITracer` APM 追踪 + `ILog` 日志，全链路可观测 |
| **纯托管** | 零本地依赖，跨平台运行（Windows / Linux / macOS / ARM） |

## 文档索引

- [需求文档](Doc/需求文档.md) — 愿景、核心目标与功能需求
- [功能清单](Doc/功能清单.md) — 实现/测试/注释三维状态追踪（当前综合完成率 90%）
- [架构设计](Doc/架构设计.md) — 五层分层架构、组件与关键流程
- [竞品分析](Doc/竞品分析.md) — 竞品对比矩阵、差距分析与战略建议

## 快速开始

### 安装

```shell
dotnet add package NewLife.BACnet
```

### 客户端：发现设备并读取属性

```csharp
using NewLife.BACnet.Protocols;
using NewLife.Log;

var client = new BacClient
{
    Port = 0xBAC0,          // 默认 47808
    WaitingTime = 3000,     // 等待设备响应超时（毫秒）
    Log = XTrace.Log,
};

client.Open();  // 广播 Who-Is，阻塞等待首个设备响应

foreach (var node in client.Nodes)
{
    // 点位地址格式 "实例_类型"：如 "0_2" = AV[0]
    var value = client.ReadProperty(node.Address, "0_2");
    Console.WriteLine($"[{node.DeviceId}] AV[0] = {value}");

    var results = client.ReadProperties(node.Address, new[] { "0_0", "0_2" });
    foreach (var kv in results)
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
}

client.Close();
```

### 服务端：暴露 BACnet 设备

```csharp
using NewLife.BACnet.Protocols;

var server = new BacServer
{
    DeviceId = 666,
    StorageFile = "DeviceDescriptor.xml",
    Port = 0xBAC0,
    Log = XTrace.Log,
};

server.Open();  // 启动监听，广播 I-Am 宣告自身存在
```

### IoT 驱动集成

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
    new PointModel { Name = "温度", Address = "0_2" },
    new PointModel { Name = "湿度", Address = "1_2" },
});
```

## 配置参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Port` | Int32 | 47808 (0xBAC0) | BACnet/IP UDP 端口 |
| `DeviceId` | Int32 | 0 | 目标设备 ID，0 表示接受所有设备 |
| `WaitingTime` | Int32 | 3000 ms | Open() 等待设备响应超时 |
| `BatchSize` | Int32 | 20 | 批量读取每批属性数量 |
| `Timeout` | Int32 | 1000 ms | 单次请求超时（BacnetClient） |
| `Retries` | Int32 | 3 | 请求失败重试次数 |
| `WritePriority` | UInt32 | 0 | 写操作优先级（1-16，0=默认） |
| `TargetAddress` | String | null | 单播 Who-Is 目标地址（IP:Port），为空则广播 |

## 已知限制

| 问题 | 说明 | 状态 |
|------|------|------|
| 超大设备读取 | 100+ 对象时数据包可能超 APDU 限制，需手动控制 BatchSize | 待优化 |
| MS/TP / PTP 传输 | 串口传输代码已存在但编译排除，跨平台兼容性待解决 | 已禁用 |
| BBMD 广播管理测试覆盖 | TRN-3/TRN-4 功能已实现但缺单元测试 | 待补充测试 |
| IPv6 传输测试覆盖 | TRN-2 功能已实现但缺 IPv6 集成测试 | 待补充测试 |

## 推荐测试工具

[YABE（Yet Another BACnet Explorer）](https://sourceforge.net/projects/yetanotherbacnetexplorer/) — 免费的 BACnet 网络探索工具，可直观查看设备、对象和属性，是调试 NewLife.BACnet 的首选对端验证工具。

## 生态版图

NewLife 生态按依赖分层，各项目独立版本、独立发版。下面的版图在所有 NewLife 仓库中逐字一致；每个项目链接到代码仓库与主 NuGet 包。

| 项目 | 一句话定位 | 下载量 |
| --- | --- | --- |
| **基础底座** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | 日志、配置、缓存、网络、序列化、定时调度与 APM 追踪。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | 数据中间件：单表百亿级、自动分表、读写分离，支持 10 多种数据库。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | 高性能网络库：单机 177 万 pps、14.5 Gbps（TCP 1KB，2026-10 实测），400 万 Tcp 长连接。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | 协议通信与 RPC 框架，面向 CS 应用与物联网设备。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | 把 .NET 应用安装为操作系统守护进程：Windows 服务与 Linux systemd。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new` 项目模板：控制台、Web、WebApi、服务、数据与物联网。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **数据存储与消息** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis 客户端：微秒级延迟、百万级吞吐，内置消息队列。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | 纯托管 RocketMQ 客户端，支持 Apache RocketMQ 与阿里云消息队列。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT 客户端与服务端，客户端支持阿里云物联网。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | C# 混合数据库：嵌入/网络双模，关系型、时序、消息队列与 KV 于一体。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | MySql 驱动与数据访问辅助层，基于 NewLife 数据栈实现。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web、AI 与分布式** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | 魔方快速开发平台：用户权限、SSO、OAuth 服务端与 CRUD 脚手架。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | 星尘分布式服务平台：节点管理、APM、配置中心、注册中心与发布中心。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | 蚂蚁调度：分布式任务与实时数据平台，支持分片、重试与回溯补算。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | 统一接入 46 家大模型服务商，附带开箱即用的 ChatAI Web 对话应用。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **工业与物联网** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT 标准库，定义物联网通信协议契约与设备抽象。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Modbus TCP/RTU/ASCII，基于 NewLife.IoT 实现。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | 西门子 PLC 协议，基于 NewLife.IoT 实现。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | 欧姆龙 PLC 协议，基于 NewLife.IoT 实现。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | 三菱 Melsec PLC 协议，基于 NewLife.IoT 实现。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | OPC 客户端，用于工业数据采集。 | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet 楼宇自控协议栈。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | 开源轻量级 IoT 平台，含网关与设备管理。 | — |
| **车联网** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 车辆定位协议：70+ 消息体，覆盖 2011/2013/2019 三个版本。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T 809 部标平台互联协议，面向运输管理部门。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 车载视频：RTP 解析、FLV/fMP4 转封装与直播推送。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **领域组件** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | 一个包覆盖 Excel、Word、PPT、PDF 等 15+ 文档格式。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | 跨平台音频：编解码、设备 IO、DSP、容器与流式处理。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | 视频编解码、封装格式、帧处理与流媒体。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | 百度/高德/腾讯/天地图统一接口，支持地理编码与坐标转换。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4 归属地：离线内存映射库 + 在线 API 双源。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **工具与产品** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | 码神工具：网络、串口、加解密、正则、Modbus 与 MQTT。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | 自托管 Git 源代码管理系统。 | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | 从低版本宿主启动时，自动安装匹配的 .NET 运行时。 | — |

> 徽章取各项目主包；主题包、扩展包与卫星包见对应 NuGet 页面。未在表中列出的 30 多个包（FTP、LoRa、Office 扩展、各类协议驱动等）见 [NewLifeX 组织](https://github.com/NewLifeX) 与 [newlifex.com](https://newlifex.com)。

## 新生命开发团队

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

新生命团队成立于 2002 年，二十余年专注 .NET 与物联网技术生态，主导开源项目近百个（生态版图收录 34 个主干项目），为电力、物流、医疗、工控、互联网等行业提供软硬件一体化解决方案；以“学无先后达者为师”为信条。

开源组件在 NuGet 累计下载 <!--nuget-total:start-->6.48M<!--nuget-total:end--> 次（全生态口径，截至 2026-10）。

网站：https://newlifex.com · 开源组织：https://github.com/NewLifeX

<details>
<summary>微信公众号：智能大石头（扫码关注）</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>
