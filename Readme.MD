# NewLife.BACnet - BACnet协议

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)

NewLife.BACnet 是一个基于 .NET 的纯托管 BACnet 协议栈，面向楼宇自动化（HVAC、照明、安防、能源管理）领域，实现完整的 BACnet/IP 协议通信，并深度集成 NewLife IoT 物联网平台生态。

**BACnet**（Building Automation and Control Networks）是 ASHRAE/ANSI 135 / ISO 16484-5 标准的楼宇自动化网络协议，广泛应用于约翰逊控制、霍尼韦尔、西门子楼宇、施耐德等主流设备厂商的产品。

源码：https://github.com/NewLifeX/NewLife.BACnet  
Nuget：`NewLife.BACnet`

## 主要特性

- **纯托管实现**：完全基于 .NET 托管代码，无需本地依赖或 P/Invoke，跨平台运行（Windows / Linux / macOS / ARM）
- **多框架支持**：同时支持 `netstandard2.0` 和 `net45`，覆盖 .NET 4.5、.NET Core 2.0+、.NET 5/6/7/8/9
- **完整协议栈**：实现 ASN.1 BER 编解码、APDU/NPDU/BVLC 各层协议，支持 30 种确认型服务和 15 种未确认型服务
- **双模式运行**：`BacClient` 作为客户端发现/读写设备，`BacServer` 作为服务端将应用数据暴露为 BACnet 设备
- **设备自动发现**：广播 Who-Is 自动发现网络中所有 BACnet 设备，定时重扫感知设备上下线
- **属性读写**：单属性 ReadProperty/WriteProperty、批量 ReadPropertyMultiple/WritePropertyMultiple，支持 Priority Array 写优先级（1-16 级）
- **COV 变更通知**：订阅属性变化实时推送，替代轮询降低网络负载
- **事件与报警**：完整的事件通知收发、报警摘要、报警确认全链路支持
- **文件服务**：AtomicReadFile/AtomicWriteFile 支持设备配置文件备份与恢复
- **对象管理**：CreateObject/DeleteObject/AddListElement/RemoveListElement 动态管理设备对象
- **BBMD 支持**：跨子网广播管理（BACnet Broadcast Management Device）和外部设备注册
- **IoT 驱动集成**：实现 `NewLife.IoT` 驱动接口，可作为 IoT 边缘网关可插拔驱动，与 Modbus/西门子等统一管理
- **可观测性**：内置 `ITracer` APM 追踪和 `ILog` 日志，全链路可观测，兼容 Stardust、OpenTelemetry

## 安装

```shell
dotnet add package NewLife.BACnet
```

## 快速入门

### 客户端：发现设备并读取属性

```csharp
using System.IO.BACnet;
using NewLife.BACnet.Protocols;
using NewLife.Log;

// 创建客户端，自动发现网络中所有 BACnet 设备
var client = new BacClient
{
    Port = 0xBAC0,       // 默认 47808（0xBAC0）
    DeviceId = 0,        // 0 = 接受所有设备；指定数值则只处理该 DeviceId
    WaitingTime = 3000,  // 等待设备响应的超时时间（毫秒）
    BatchSize = 20,      // 批量读取时每批的属性数量
    Log = XTrace.Log,
};

client.Open();  // 广播 Who-Is，阻塞等待首个设备响应或超时

Console.WriteLine($"发现 {client.Nodes.Count} 台设备");

foreach (var node in client.Nodes)
{
    Console.WriteLine($"设备 {node.DeviceId} 地址：{node.Address}");

    // 高级 API：用点位地址格式读取（"实例_类型"）
    var value = client.ReadProperty(node.Address, "0_2");   // AV[0] 当前值
    Console.WriteLine($"  AV[0] 当前值：{value}");

    // 批量读取多个点位
    var results = client.ReadProperties(node.Address, new[] { "0_0", "0_2", "1_0" });
    foreach (var kv in results)
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
}

client.Close();
```

### 点位地址格式

```
"{实例号}_{对象类型代码}"
  "0_0"  → 实例 0，类型 0（OBJECT_ANALOG_INPUT，  模拟输入）
  "0_2"  → 实例 0，类型 2（OBJECT_ANALOG_VALUE，  模拟值）
  "0_4"  → 实例 0，类型 4（OBJECT_BINARY_INPUT，  二进制输入）
  "0_5"  → 实例 0，类型 5（OBJECT_BINARY_VALUE，  二进制值）
  "0_8"  → 实例 0，类型 8（OBJECT_DEVICE，        设备对象）
```

### 底层 API：精确指定属性

```csharp
using System.IO.BACnet;
using System.IO.BACnet.Serialize;

// 读取单个属性
var objectId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_INPUT, 0);
if (client.ReadProperty(node.Address, objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, out var values))
    Console.WriteLine($"当前值（float）：{(float)values[0].Value}");

// 批量写入
var props = new List<BacnetPropertyValue>
{
    new()
    {
        property = new BacnetPropertyReference
            { propertyIdentifier = (uint)BacnetPropertyIds.PROP_PRESENT_VALUE },
        value = new[] { new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, 25.5f) }
    }
};
client.WritePropertyMultiple(node.Address, objectId, props);
```

### 服务端：暴露 BACnet 设备

```csharp
using System.IO.BACnet;
using System.IO.BACnet.Storage;
using NewLife.BACnet.Protocols;
using NewLife.Threading;

// 创建服务端，设备 ID 为 666，从 XML 文件加载对象描述
var server = new BacServer
{
    DeviceId = 666,
    StorageFile = "DeviceDescriptor.xml",   // 设备对象描述文件（XML 格式）
    Port = 0xBAC0,
    Log = XTrace.Log,
};

server.Open();  // 启动监听，广播 I-Am 宣告自身存在

var objectId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);

// 定时更新属性值（模拟传感器采集）
var timer = new TimerX(_ =>
{
    lock (server.Storage)
    {
        var value = new[] { new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL,
                                            (Single)DateTime.Now.Second) };
        server.Storage.WriteProperty(objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, value, true);
    }
}, null, 0, 5_000) { Async = true };

Console.ReadLine();
```

### DeviceDescriptor.xml 格式

```xml
<?xml version="1.0" encoding="utf-8"?>
<DeviceDescriptor>
  <!-- Type=8 = OBJECT_DEVICE -->
  <Object Type="8" Instance="666">
    <Property Id="77">  <!-- PROP_OBJECT_NAME -->
      <Value Tag="7">MyBACnetDevice</Value>
    </Property>
  </Object>
  <!-- Type=2 = OBJECT_ANALOG_VALUE -->
  <Object Type="2" Instance="0">
    <Property Id="85">  <!-- PROP_PRESENT_VALUE -->
      <Value Tag="4">0.0</Value>
    </Property>
    <Property Id="117">  <!-- PROP_UNITS = degrees-celsius -->
      <Value Tag="9">62</Value>
    </Property>
  </Object>
</DeviceDescriptor>
```

### IoT 驱动框架集成

```csharp
using NewLife.BACnet.Drivers;
using NewLife.IoT.ThingModels;

// BACnetDriver 已注册 [Driver("BACnet")]，IoT 框架自动发现
var driver = new BACnetDriver { Log = XTrace.Log };

var parameter = new BACnetParameter
{
    Port = 0xBAC0,
    DeviceId = 666,  // 目标设备 ID
};

// 一个 Driver 实例对应一条 BACnet 网络连接，多设备节点共享
var node = driver.Open(device, parameter);

// 批量读取点位
var points = new[]
{
    new PointModel { Name = "室内温度", Address = "0_2" },   // AV[0]
    new PointModel { Name = "室内湿度", Address = "1_2" },   // AV[1]
};
var values = driver.Read(node, points);
// => { "室内温度": 25.5, "室内湿度": 60.0 }

// 写入单个点位（自动根据属性类型转换值）
driver.Write(node, points[0], 26.0f);

driver.Close(node);
```

### COV 订阅（变更通知）

```csharp
var objectId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);

// 订阅对象级 COV（属性变化时触发）
_client.SubscribeCOV(node.Address, objectId,
    subscriberProcessId: 1,
    issueConfirmedNotifications: false,
    lifetime: 300);

// 注册 COV 通知事件处理器
_client.OnCOVNotification += (sender, address, subscriberProcessId,
    initiatingDeviceIdentifier, monitoredObjectIdentifier,
    initiatingDeviceAddress, notificationData) =>
{
    foreach (var item in notificationData)
        Console.WriteLine($"COV: {item.property.propertyIdentifier} = {item.value[0].Value}");
};
```

## 配置参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Port` | Int32 | 47808 (0xBAC0) | BACnet/IP UDP 端口 |
| `DeviceId` | Int32 | 0 | 目标设备 ID，0 表示接受所有设备 |
| `WaitingTime` | Int32 | 3000 ms | Open() 等待设备响应的超时时间 |
| `BatchSize` | Int32 | 20 | 批量读取每批属性数量 |
| `Timeout` | Int32 | 1000 ms | 单次请求超时（BacnetClient） |
| `Retries` | Int32 | 3 | 请求失败重试次数（BacnetClient） |
| `WritePriority` | UInt32 | 0 | 写操作优先级（1-16，0=默认优先级） |
| `VendorId` | UInt16 | 260 | BACnet 厂商 ID |

## 已知限制

| 问题 | 说明 | 状态 |
|------|------|------|
| invoke-id 全局共享 | 多设备高并发场景响应可能混淆 | 待修复 |
| 批量读取偶发 Bug | ReadPropertyMultiple 有时只返回 1 个点位，已加 lock 临时绕过 | 待根本修复 |
| 超大设备读取 | 100+ 对象时数据包可能超 APDU 限制，需手动控制 BatchSize | 待优化 |
| MS/TP / PTP 传输 | 串口传输代码已存在于 `Transport/`，跨平台兼容性待解决 | 已禁用 |
| BACnet/SC 安全通信 | TLS/WebSocket 加密传输，符合网络安全合规 | 规划中 |
| COV 订阅自动续约 | 订阅有效期到期后需手动重新订阅 | 待实现 |

## 推荐测试工具

[YABE（Yet Another BACnet Explorer）](https://sourceforge.net/projects/yetanotherbacnetexplorer/)：免费的 BACnet 网络探索工具，可直观查看设备、对象和属性，是调试 NewLife.BACnet 的首选对端验证工具。

## BACnet 基础知识

BACnet 支持多种物理/网络媒体：

| 类型 | 说明 | 当前支持 |
|------|------|---------|
| BACnet/IP (IPv4) | UDP 47808 端口，以太网传输 | ✅ |
| BACnet/IP (IPv6) | UDP 47808 端口，IPv6 网络 | ✅ |
| MS/TP (RS485) | 主从/令牌传递，串行现场总线 | 🚫 已禁用 |
| PTP (RS232) | 点对点串行连接 | 🚫 已禁用 |
| Ethernet 802.3 | 以太网直连（需原始套接字权限） | 🚫 已禁用 |
| BACnet/SC | TLS/WebSocket 安全连接（ASHRAE 135.1） | ❌ 规划中 |

BACnet/IP 与 BACnet MS/TP 是目前最广泛使用的两种网络类型。BACnet/IP 允许跨多子网通信，利用现有以太网和 IP 基础设施，无需额外硬件；BACnet MS/TP 基于 RS485 总线，适合现场设备互联的低成本场景。

## 新生命项目矩阵
各项目默认支持net9.0/netstandard2.1/netstandard2.0/net4.62/net4.5，旧版（2024.0801）支持net4.0/net2.0  

|                               项目                               | 年份  | 说明                                                                                        |
| :--------------------------------------------------------------: | :---: | ------------------------------------------------------------------------------------------- |
|                             基础组件                             |       | 支撑其它中间件以及产品项目                                                                  |
|          [NewLife.Core](https://github.com/NewLifeX/X)           | 2002  | 核心库，日志、配置、缓存、网络、序列化、APM性能追踪                                         |
|    [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode)    | 2005  | 大数据中间件，单表百亿级，MySql/SQLite/SqlServer/Oracle/PostgreSql/达梦，自动分表，读写分离 |
|      [NewLife.Net](https://github.com/NewLifeX/NewLife.Net)      | 2005  | 网络库，单机千万级吞吐率（2266万tps），单机百万级连接（400万Tcp长连接）                     |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | 2011  | 协议通信库，提供CS应用通信框架，支持Http/RPC通信框架，高吞吐，物联网设备低开销易接入        |
|     [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube)     | 2010  | 魔方快速开发平台，集成了用户权限、SSO登录、OAuth服务端等，单表100亿级项目验证               |
|    [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent)    | 2008  | 服务管理组件，把应用安装成为操作系统守护进程，Windows服务、Linux的Systemd                   |
|     [NewLife.Zero](https://github.com/NewLifeX/NewLife.Zero)     | 2020  | Zero零代脚手架，基于NewLife组件生态的项目模板NewLife.Templates，Web、WebApi、Service        |
|                              中间件                              |       | 对接知名中间件平台                                                                          |
|    [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis)    | 2017  | Redis客户端，微秒级延迟，百万级吞吐，丰富的消息队列，百亿级数据量项目验证                   |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | 2018  | RocketMQ纯托管客户端，支持Apache RocketMQ和阿里云消息队列，十亿级项目验                     |
|     [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT)     | 2019  | 物联网消息协议，MqttClient/MqttServer，客户端支持阿里云物联网                               |
|      [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT)      | 2022  | IoT标准库，定义物联网领域的各种通信协议标准规范                                             |
|   [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus)   | 2022  | ModbusTcp/ModbusRTU/ModbusASCII，基于IoT标准库实现，支持ZeroIoT平台和IoTEdge网关            |
|  [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens)  | 2022  | 西门子PLC协议，基于IoT标准库实现，支持IoT平台和IoTEdge                                      |
|      [NewLife.Map](https://github.com/NewLifeX/NewLife.Map)      | 2022  | 地图组件库，封装百度地图、高德地图、腾讯地图、天地图                                        |
|    [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio)    | 2023  | 音频编解码库，PCM/ADPCMA/G711A/G722U/WAV/AAC                                                |
|                             产品平台                             |       | 产品平台级，编译部署即用，个性化自定义                                                      |
|         [Stardust](https://github.com/NewLifeX/Stardust)         | 2018  | 星尘，分布式服务平台，节点管理、APM监控中心、配置中心、注册中心、发布中心                   |
|           [AntJob](https://github.com/NewLifeX/AntJob)           | 2019  | 蚂蚁调度，分布式大数据计算平台（实时/离线），蚂蚁搬家分片思想，万亿级数据量项目验证         |
|      [NewLife.ERP](https://github.com/NewLifeX/NewLife.ERP)      | 2021  | 企业ERP，产品管理、客户管理、销售管理、供应商管理                                           |
|         [CrazyCoder](https://github.com/NewLifeX/XCoder)         | 2006  | 码神工具，众多开发者工具，网络、串口、加解密、正则表达式、Modbus、MQTT                      |
|           [EasyIO](https://github.com/NewLifeX/EasyIO)           | 2023  | 简易文件存储，支持分布式系统中文件集中存储。                                                |
|           [XProxy](https://github.com/NewLifeX/XProxy)           | 2005  | 产品级反向代理，NAT代理、Http代理                                                           |
|        [HttpMeter](https://github.com/NewLifeX/HttpMeter)        | 2022  | Http压力测试工具                                                                            |
|         [GitCandy](https://github.com/NewLifeX/GitCandy)         | 2015  | Git源代码管理系统                                                                           |
|          [SmartOS](https://github.com/NewLifeX/SmartOS)          | 2014  | 嵌入式操作系统，完全独立自主，支持ARM Cortex-M芯片架构                                      |
|          [SmartA2](https://github.com/NewLifeX/SmartA2)          | 2019  | 嵌入式工业计算机，物联网边缘网关，高性能.NET8主机，应用于工业、农业、交通、医疗             |
|                          FIoT物联网平台                          | 2020  | 物联网整体解决方案，建筑、环保、农业，软硬件及大数据分析一体化，单机十万级点位项目验证      |
|                        UWB高精度室内定位                         | 2020  | 厘米级（10~20cm）高精度室内定位，软硬件一体化，与其它系统联动，大型展厅项目验证             |



## 新生命开发团队
![XCode](https://newlifex.com/logo.png)  

新生命团队（NewLife）成立于2002年，是新时代物联网行业解决方案提供者，致力于提供软硬件应用方案咨询、系统架构规划与开发服务。  
团队主导的80多个开源项目已被广泛应用于各行业，Nuget累计下载量高达400余万次。  
团队开发的大数据中间件NewLife.XCode、蚂蚁调度计算平台AntJob、星尘分布式平台Stardust、缓存队列组件NewLife.Redis以及物联网平台FIoT，均成功应用于电力、高校、互联网、电信、交通、物流、工控、医疗、文博等行业，为客户提供了大量先进、可靠、安全、高质量、易扩展的产品和系统集成服务。  

我们将不断通过服务的持续改进，成为客户长期信赖的合作伙伴，通过不断的创新和发展，成为国内优秀的IoT服务供应商。  

`新生命团队始于2002年，部分开源项目具有20年以上漫长历史，源码库保留有2010年以来所有修改记录`  
网站：https://newlifex.com  
开源：https://github.com/newlifex  
QQ群：1600800/1600838  
微信公众号：  
![智能大石头](https://newlifex.com/stone.jpg)  
