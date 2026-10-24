# NewLife.BACnet — BACnet 協定

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)
[![test](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml)

<!-- lang:start -->
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

> 基於 .NET 的純受控 BACnet 協定堆疊，面向樓宇自動化（HVAC、照明、安防、能源管理）領域，實作完整的 BACnet/IP 協定通訊，深度整合 NewLife IoT 物聯網平台生態。

**BACnet**（Building Automation and Control Networks）是 ASHRAE/ANSI 135 / ISO 16484-5 標準的樓宇自動化網路協定，廣泛應用於江森自控、霍尼韋爾、西門子樓宇、施耐德等主流設備廠商的產品。

原始碼：https://github.com/NewLifeX/NewLife.BACnet  
NuGet：`NewLife.BACnet` | 目標框架：`netstandard2.0` + `net45`

## 亮點

| 能力領域 | 說明 |
|--------|------|
| **傳輸層** | BACnet/IP (IPv4/IPv6) UDP 傳輸，BBMD 廣播管理與外部裝置註冊 |
| **協定編解碼** | ASN.1 BER / APDU / NPDU / BVLC 各層協定完整編解碼 |
| **裝置探索** | Who-Is 廣播自動探索，定向單播探索，定時重掃感知上下線 |
| **屬性讀寫** | 單屬性與批次讀寫，Priority Array 寫入優先級（1-16 級），invoke-id 依裝置隔離 |
| **COV 變更通知** | 訂閱屬性變化即時推送，取代輪詢，支援訂閱自動續約 |
| **事件與警報** | 事件通知收發、警報摘要、警報確認全鏈路支援 |
| **檔案服務** | AtomicReadFile / AtomicWriteFile 支援裝置設定檔備份還原 |
| **物件管理** | CreateObject / DeleteObject / AddListElement / RemoveListElement |
| **BACnet 伺服端** | 基於 XML 裝置描述的模擬伺服端，支援第三方樓控系統存取 |
| **高階用戶端 API** | `BacClient` 面向業務的簡潔 API，字串點位地址格式 `"實例_類型"` |
| **非同步 API** | 完整的 `ReadPropertyAsync`/`WritePropertyAsync` 等 Async/Await 方法集 |
| **安全通訊** | BACnet/SC (WebSocket + TLS) 安全傳輸，Hub/Node 與直連雙模式 |
| **標準服務補齊** | Who-Has/I-Have、時間同步、裝置通訊控制等標準 BACnet 服務 |
| **排程表支援** | Schedule/Calendar 資料類型編解碼（DailySchedule/SpecialEvent） |
| **IoT 驅動整合** | 實作 `NewLife.IoT` 驅動介面，與 Modbus/西門子等統一管理 |
| **可觀測性** | `ITracer` APM 追蹤 + `ILog` 日誌，全鏈路可觀測 |
| **純受控** | 零原生相依，跨平台執行（Windows / Linux / macOS / ARM） |

## 文件索引

- [需求文件](Doc/需求文档.md) — 願景、核心目標與功能需求
- [功能清單](Doc/功能清单.md) — 實作/測試/註解三維狀態追蹤（目前綜合完成率 90%）
- [架構設計](Doc/架构设计.md) — 五層分層架構、元件與關鍵流程
- [競品分析](Doc/竞品分析.md) — 競品對比矩陣、差距分析與策略建議

## 快速開始

### 安裝

```shell
dotnet add package NewLife.BACnet
```

### 用戶端：探索裝置並讀取屬性

```csharp
using NewLife.BACnet.Protocols;
using NewLife.Log;

var client = new BacClient
{
    Port = 0xBAC0,          // 預設 47808
    WaitingTime = 3000,     // 等待裝置回應逾時（毫秒）
    Log = XTrace.Log,
};

client.Open();  // 廣播 Who-Is，阻塞等待首個裝置回應

foreach (var node in client.Nodes)
{
    // 點位地址格式 "實例_類型"：如 "0_2" = AV[0]
    var value = client.ReadProperty(node.Address, "0_2");
    Console.WriteLine($"[{node.DeviceId}] AV[0] = {value}");

    var results = client.ReadProperties(node.Address, new[] { "0_0", "0_2" });
    foreach (var kv in results)
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
}

client.Close();
```

### 伺服端：提供 BACnet 裝置

```csharp
using NewLife.BACnet.Protocols;

var server = new BacServer
{
    DeviceId = 666,
    StorageFile = "DeviceDescriptor.xml",
    Port = 0xBAC0,
    Log = XTrace.Log,
};

server.Open();  // 啟動監聽，廣播 I-Am 宣告自身存在
```

### IoT 驅動整合

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
    new PointModel { Name = "溫度", Address = "0_2" },
    new PointModel { Name = "濕度", Address = "1_2" },
});
```

## 設定參數

| 參數 | 類型 | 預設值 | 說明 |
|------|------|--------|------|
| `Port` | Int32 | 47808 (0xBAC0) | BACnet/IP UDP 連接埠 |
| `DeviceId` | Int32 | 0 | 目標裝置 ID，0 表示接受所有裝置 |
| `WaitingTime` | Int32 | 3000 ms | Open() 等待裝置回應逾時 |
| `BatchSize` | Int32 | 20 | 批次讀取每批屬性數量 |
| `Timeout` | Int32 | 1000 ms | 單次請求逾時（BacnetClient） |
| `Retries` | Int32 | 3 | 請求失敗重試次數 |
| `WritePriority` | UInt32 | 0 | 寫入優先級（1-16，0=預設） |
| `TargetAddress` | String | null | 單播 Who-Is 目標地址（IP:Port），為空則廣播 |

## 已知限制

| 問題 | 說明 | 狀態 |
|------|------|------|
| 超大裝置讀取 | 100+ 物件時資料包可能超過 APDU 限制，需手動控制 BatchSize | 待最佳化 |
| MS/TP / PTP 傳輸 | 串列傳輸程式碼已存在但編譯排除，跨平台相容性待解決 | 已停用 |
| BBMD 廣播管理測試覆蓋 | TRN-3/TRN-4 功能已實作但缺單元測試 | 待補充測試 |
| IPv6 傳輸測試覆蓋 | TRN-2 功能已實作但缺 IPv6 整合測試 | 待補充測試 |

## 推薦測試工具

[YABE（Yet Another BACnet Explorer）](https://sourceforge.net/projects/yetanotherbacnetexplorer/) — 免費的 BACnet 網路探索工具，可直觀查看裝置、物件和屬性，是除錯 NewLife.BACnet 的首選對端驗證工具。

## 生態版圖

NewLife 生態按依賴分層，各專案獨立版本、獨立發版。下面的版圖在所有 NewLife 儲存庫中逐字一致；每個專案連結到程式碼儲存庫與主 NuGet 套件。

| 專案 | 一句話定位 | 下載量 |
| --- | --- | --- |
| **基礎底座** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | 日誌、設定、快取、網路、序列化、排程與 APM 追蹤。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | 資料中介層：單表百億級、自動分表、讀寫分離，支援 10 多種資料庫。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | 高效能網路函式庫：單機 177 萬 pps、14.5 Gbps（TCP 1KB，2026-10 實測），400 萬 Tcp 長連線。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | 協定通訊與 RPC 框架，面向 CS 應用與物聯網裝置。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | 把 .NET 應用安裝為作業系統守護行程：Windows 服務與 Linux systemd。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new` 專案範本：主控台、Web、WebApi、服務、資料與物聯網。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **資料儲存與訊息** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis 用戶端：微秒級延遲、百萬級輸送量，內建訊息佇列。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | 純受控 RocketMQ 用戶端，支援 Apache RocketMQ 與阿里雲訊息佇列。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT 用戶端與伺服端，用戶端支援阿里雲物聯網。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | C# 混合資料庫：嵌入/網路雙模，關聯式、時序、訊息佇列與 KV 於一體。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | MySql 驅動與資料存取輔助層，基於 NewLife 資料堆疊實作。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web、AI 與分散式** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | 魔方快速開發平台：使用者權限、SSO、OAuth 伺服端與 CRUD 腳手架。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | 星塵分散式服務平台：節點管理、APM、設定中心、註冊中心與發布中心。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | 螞蟻排程：分散式任務與即時資料平台，支援分片、重試與回溯補算。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | 統一接入 46 家大模型服務商，附帶開箱即用的 ChatAI Web 對話應用。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **工業與物聯網** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT 標準函式庫，定義物聯網通訊協定契約與裝置抽象。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Modbus TCP/RTU/ASCII，基於 NewLife.IoT 實作。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | 西門子 PLC 協定，基於 NewLife.IoT 實作。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | 歐姆龍 PLC 協定，基於 NewLife.IoT 實作。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | 三菱 Melsec PLC 協定，基於 NewLife.IoT 實作。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | OPC 用戶端，用於工業資料採集。 | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet 樓宇自控協定堆疊。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | 開源輕量級 IoT 平台，含閘道與裝置管理。 | — |
| **車聯網** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 車輛定位協定：70+ 訊息體，覆蓋 2011/2013/2019 三個版本。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T 809 部標平台互聯協定，面向運輸管理部門。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 車載視訊：RTP 解析、FLV/fMP4 轉封裝與直播推送。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **領域元件** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | 一個套件涵蓋 Excel、Word、PPT、PDF 等 15+ 文件格式。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | 跨平台音訊：編解碼、裝置 IO、DSP、容器與串流處理。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | 視訊編解碼、封裝格式、幀處理與串流媒體。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | 百度/高德/騰訊/天地圖統一介面，支援地理編碼與座標轉換。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4 歸屬地：離線記憶體映射函式庫 + 線上 API 雙來源。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **工具與產品** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | 碼神工具：網路、序列埠、加解密、正規表示式、Modbus 與 MQTT。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | 自託管 Git 原始碼管理系統。 | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | 從低版本宿主啟動時，自動安裝相符的 .NET 執行階段。 | — |

> 徽章取各專案主套件；主題套件、擴充套件與衛星套件見對應 NuGet 頁面。未在表中列出的 30 多個套件（FTP、LoRa、Office 擴充、各類協定驅動等）見 [NewLifeX 組織](https://github.com/NewLifeX) 與 [newlifex.com](https://newlifex.com)。

## 新生命開發團隊

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

新生命團隊成立於 2002 年，二十餘年專注 .NET 與物聯網技術生態，主導開源專案近百個（生態版圖收錄 34 個主幹專案），為電力、物流、醫療、工控、網際網路等行業提供軟硬體一體化解決方案；以「學無先後達者為師」為信條。

開源元件在 NuGet 累計下載 <!--nuget-total:start-->6.48M<!--nuget-total:end--> 次（全生態口徑，截至 2026-10）。

網站：https://newlifex.com · 開源組織：https://github.com/NewLifeX

<details>
<summary>微信公眾號：智能大石頭（掃碼關注）</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## 授權條款

MIT 授權條款 — 詳見 [LICENSE](LICENSE)。
