# NewLife.BACnet - BACnet协议

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)

BACnet楼宇自动化  
Building Automation Control network  

源码： https://github.com/NewLifeX/NewLife.BACnet  
Nuget：NewLife.BACnet  

## 主要特性

- **纯托管实现**：完全基于 .NET 托管代码，无需本地依赖或 P/Invoke，跨平台运行
- **多框架支持**：同时支持 `netstandard2.0` 和 `net45`，覆盖 .NET 4.5、.NET Core 2.0+、.NET 5/6/8/9
- **完整协议栈**：实现 ASN.1 BER 编解码、APDU/NPDU/BVLC 各层协议，支持 30 种确认型服务和 15 种未确认型服务
- **设备发现**：广播 Who-Is 自动发现网络中所有 BACnet 设备，定时重扫感知设备上下线
- **高级客户端 API**：`BacClient` 提供设备扫描、属性读写、批量操作等面向业务的简洁接口
- **BACnet 服务端**：`BacServer` 可将 .NET 应用暴露为标准 BACnet 设备，支持 XML 描述文件配置对象和属性
- **COV 变更通知**：支持订阅属性变化通知，实时推送替代轮询
- **事件与报警**：支持事件通知、报警摘要、报警确认等完整报警管理服务
- **IoT 驱动框架集成**：实现 `NewLife.IoT` 驱动接口，可作为 IoT 边缘网关的可插拔驱动
- **可观测性**：内置 `ITracer` APM 追踪和 `ILog` 日志，全链路可观测

## 快速入门

### 安装

```shell
dotnet add package NewLife.BACnet
```

### 客户端：发现设备并读取属性

```csharp
using NewLife.BACnet.Protocols;
using NewLife.Log;

// 创建客户端，自动发现网络中所有 BACnet 设备
var client = new BacClient
{
    DeviceId = 0,       // 0 表示接受所有设备；指定数值则只处理该设备
    Log = XTrace.Log,   // 可选：注入日志
};

client.Open();          // 广播 Who-Is，等待 3 秒收集响应

// 获取已发现的设备节点
foreach (var node in client.Nodes)
{
    Console.WriteLine($"设备 {node.DeviceId} 地址：{node.Address}");

    // 读取单个属性（模拟值对象 0 的当前值）
    var objectId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);
    if (client.ReadProperty(node, objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, out var values))
    {
        Console.WriteLine($"  当前值：{values[0].Value}");
    }
}

client.Close();
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
    StorageFile = "DeviceDescriptor.xml",   // 设备对象描述文件
    Log = XTrace.Log,
};

server.Open();  // 启动监听，广播 I-Am 宣告自身存在

var objectId = new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, 0);

// 定时更新属性值（模拟传感器数据）
var timer = new TimerX(_ =>
{
    lock (server.Storage)
    {
        var value = new[] { new BacnetValue((Single)(DateTime.Now.Second)) };
        server.Storage.WriteProperty(objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, 1, value, true);
    }
}, null, 0, 5_000) { Async = true };

Console.ReadLine();
```

### IoT 驱动框架集成

```csharp
// BACnetDriver 已注册 [Driver("BACnet")] 特性，IoT 框架自动发现
// 点位地址格式："实例_类型"，例如 "0_2" = 实例0 类型2(OBJECT_ANALOG_VALUE)
// 参见 NewLife.IoT 框架文档了解驱动注册和点位配置方式
```

> **推荐测试工具**：使用 [YABE（Yet Another BACnet Explorer）](https://sourceforge.net/projects/yetanotherbacnetexplorer/) 作为对端验证工具，可直观查看设备、对象和属性。

## 知识
BACnet支持七种局域网类型，通过以下方式进行通信:
* IP(IPv4 and IPv6);
* Ethernet(ISO 8802-3);
* Master Slave/Token Passing MS/TP(RS485)
* Point-to-Point (PTP) (RS 232)
* ZigBee
* LonTalk
* ARCNET (4 min)

目前使用的两种最普遍的局域网类型是BACnet IP和BACnet MSTP。BACnet IP允许跨多个子网、多区控制系统进行通信，利用光纤和千兆以太网的优点，并为设备分配IP地址。

常见的局域网类型：
* BACnet IP
* BACnet MSTP
* BACnet IP与MSTP相比有许多优点。
多个子网和楼宇高速Web访问

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
