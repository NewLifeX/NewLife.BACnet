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
| **属性读写** | 单属性与批量读写，Priority Array 写入优先级（1-16 级） |
| **COV 变更通知** | 订阅属性变化实时推送，替代轮询 |
| **事件与报警** | 事件通知收发、报警摘要、报警确认全链路支持 |
| **文件服务** | AtomicReadFile / AtomicWriteFile 支持设备配置文件备份恢复 |
| **对象管理** | CreateObject / DeleteObject / AddListElement / RemoveListElement |
| **BACnet 服务端** | 基于 XML 设备描述的仿真服务端，支持第三方楼控系统访问 |
| **高级客户端 API** | `BacClient` 面向业务的简洁 API，字符串点位地址格式 `"实例_类型"` |
| **IoT 驱动集成** | 实现 `NewLife.IoT` 驱动接口，与 Modbus/西门子等统一管理 |
| **可观测性** | `ITracer` APM 追踪 + `ILog` 日志，全链路可观测 |
| **纯托管** | 零本地依赖，跨平台运行（Windows / Linux / macOS / ARM） |

## 文档索引

- [需求文档](Doc/需求文档.md) — 愿景、核心目标与功能需求
- [功能清单](Doc/功能清单.md) — 实现/测试/注释三维状态追踪（当前综合完成率 88%）
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

## 新生命项目矩阵

| 项目 | 说明 |
|------|------|
| [NewLife.Core](https://github.com/NewLifeX/X) | 核心库，日志、配置、缓存、网络、序列化、APM 性能追踪 |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | 大数据中间件，支持 MySql/SQLite/SqlServer/Oracle/达梦等 |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | 网络库，单机千万级吞吐率，百万级 Tcp 长连接 |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT 标准库，定义物联网通信协议标准规范 |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | ModbusTcp/RTU/ASCII，基于 IoT 标准库实现 |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | 西门子 PLC 协议，基于 IoT 标准库实现 |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | 物联网消息协议，客户端支持阿里云物联网 |
| [Stardust](https://github.com/NewLifeX/Stardust) | 星尘分布式服务平台，APM 监控、配置中心、注册中心 |
