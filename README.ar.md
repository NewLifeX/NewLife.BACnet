# NewLife.BACnet — بروتوكول BACnet

![GitHub top language](https://img.shields.io/github/languages/top/newlifex/NewLife.BACnet?logo=github)
![GitHub License](https://img.shields.io/github/license/newlifex/NewLife.BACnet?logo=github)
![Nuget Downloads](https://img.shields.io/nuget/dt/NewLife.BACnet?logo=nuget)
![Nuget](https://img.shields.io/nuget/v/NewLife.BACnet?logo=nuget)
![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/NewLife.BACnet?label=dev%20nuget&logo=nuget)
[![test](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/NewLifeX/NewLife.BACnet/actions/workflows/test.yml)

<!-- lang:start -->
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md)
<!-- lang:end -->

> حزمة بروتوكول BACnet مُدارة بالكامل لـ .NET، موجّهة إلى أتمتة المباني (HVAC والإضاءة والأمن وإدارة الطاقة). تنفّذ اتصال BACnet/IP الكامل وتتكامل بعمق مع منظومة NewLife IoT.

**BACnet** (Building Automation and Control Networks) هو بروتوكول شبكة أتمتة المباني المحدد في ASHRAE/ANSI 135 وISO 16484-5. يُستخدم على نطاق واسع في منتجات Johnson Controls وHoneywell وSiemens Building Technologies وSchneider Electric وغيرها من الشركات المصنّعة الكبرى.

الكود المصدري: https://github.com/NewLifeX/NewLife.BACnet  
NuGet: `NewLife.BACnet` | أطر العمل المستهدفة: `netstandard2.0` + `net45`

## أبرز الميزات

| المجال | الوصف |
|--------|------|
| **النقل** | نقل UDP عبر BACnet/IP (IPv4/IPv6)، وإدارة بث BBMD وتسجيل الأجهزة الخارجية |
| **ترميز البروتوكول** | ترميز وفك ترميز كامل لـ ASN.1 BER / APDU / NPDU / BVLC |
| **اكتشاف الأجهزة** | اكتشاف تلقائي عبر بث Who-Is، واكتشاف موجّه أحادي الإرسال، وإعادة مسح دورية لرصد تغيّر الاتصال |
| **الوصول إلى الخصائص** | قراءة وكتابة الخصائص الفردية والدفعية، وأولوية الكتابة Priority Array (1-16)، وعزل invoke-id لكل جهاز |
| **إشعارات COV** | دفع تغييرات الخصائص في الوقت الفعلي بدلاً من الاستقصاء، مع تجديد الاشتراكات تلقائيًا |
| **الأحداث والإنذارات** | إشعارات الأحداث وملخص الإنذارات وتأكيد الإنذارات من طرف إلى طرف |
| **خدمات الملفات** | AtomicReadFile / AtomicWriteFile لنسخ إعدادات الجهاز احتياطيًا واستعادتها |
| **إدارة الكائنات** | CreateObject / DeleteObject / AddListElement / RemoveListElement |
| **خادم BACnet** | خادم محاكاة قائم على وصف XML للجهاز، متاح لأنظمة المباني الخارجية |
| **واجهة عميل عالية المستوى** | `BacClient`، واجهة مبسّطة موجّهة للأعمال بعناوين نقاط نصية بصيغة `"المعرّف_النوع"` |
| **واجهة غير متزامنة** | مجموعة كاملة من أساليب Async/Await مثل `ReadPropertyAsync`/`WritePropertyAsync` |
| **نقل آمن** | BACnet/SC (WebSocket + TLS) مع وضعي Hub/Node والاتصال المباشر |
| **الخدمات القياسية** | Who-Has/I-Have ومزامنة الوقت والتحكم في اتصال الأجهزة وغيرها من خدمات BACnet القياسية |
| **الجداول الزمنية** | ترميز أنواع Schedule/Calendar (DailySchedule/SpecialEvent) |
| **تكامل برنامج تشغيل IoT** | ينفّذ عقود برامج تشغيل `NewLife.IoT` ويُدار مع برامج تشغيل Modbus/Siemens |
| **قابلية المراقبة** | تتبع APM عبر `ITracer` + تسجيل `ILog` لرؤية شاملة من طرف إلى طرف |
| **مُدار بالكامل** | بلا تبعيات أصلية؛ يعمل على Windows / Linux / macOS / ARM |

## الوثائق

- [المتطلبات](Doc/需求文档.md) — الرؤية والأهداف الأساسية والمتطلبات الوظيفية
- [قائمة الميزات](Doc/功能清单.md) — تتبع حالة التنفيذ/الاختبار/التعليقات (نسبة الإنجاز الحالية 90%)
- [البنية](Doc/架构设计.md) — بنية من خمس طبقات والمكوّنات والمسارات الرئيسية
- [التحليل التنافسي](Doc/竞品分析.md) — مصفوفة مقارنة وتحليل الفجوات وتوصيات استراتيجية

## البدء السريع

### التثبيت

```shell
dotnet add package NewLife.BACnet
```

### العميل: اكتشاف الأجهزة وقراءة الخصائص

```csharp
using NewLife.BACnet.Protocols;
using NewLife.Log;

var client = new BacClient
{
    Port = 0xBAC0,          // الافتراضي 47808
    WaitingTime = 3000,     // مهلة انتظار استجابة الجهاز (مللي ثانية)
    Log = XTrace.Log,
};

client.Open();  // بث Who-Is والانتظار حتى تستجيب أول جهاز

foreach (var node in client.Nodes)
{
    // صيغة عنوان النقطة "المعرّف_النوع": "0_2" = AV[0]
    var value = client.ReadProperty(node.Address, "0_2");
    Console.WriteLine($"[{node.DeviceId}] AV[0] = {value}");

    var results = client.ReadProperties(node.Address, new[] { "0_0", "0_2" });
    foreach (var kv in results)
        Console.WriteLine($"  {kv.Key}: {kv.Value}");
}

client.Close();
```

### الخادم: عرض جهاز BACnet

```csharp
using NewLife.BACnet.Protocols;

var server = new BacServer
{
    DeviceId = 666,
    StorageFile = "DeviceDescriptor.xml",
    Port = 0xBAC0,
    Log = XTrace.Log,
};

server.Open();  // بدء الاستماع وبث I-Am للإعلان عن نفسه
```

### تكامل برنامج تشغيل IoT

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
    new PointModel { Name = "درجة الحرارة", Address = "0_2" },
    new PointModel { Name = "الرطوبة", Address = "1_2" },
});
```

## معاملات التكوين

| المعامل | النوع | القيمة الافتراضية | الوصف |
|------|------|--------|------|
| `Port` | Int32 | 47808 (0xBAC0) | منفذ UDP لـ BACnet/IP |
| `DeviceId` | Int32 | 0 | معرّف الجهاز الهدف؛ 0 يعني قبول جميع الأجهزة |
| `WaitingTime` | Int32 | 3000 ms | مهلة Open() لانتظار استجابات الأجهزة |
| `BatchSize` | Int32 | 20 | عدد الخصائص المقروءة في كل دفعة |
| `Timeout` | Int32 | 1000 ms | مهلة الطلب الواحد (BacnetClient) |
| `Retries` | Int32 | 3 | عدد المحاولات بعد فشل الطلب |
| `WritePriority` | UInt32 | 0 | أولوية الكتابة (1-16؛ 0 = الافتراضي) |
| `TargetAddress` | String | null | هدف Who-Is أحادي الإرسال (IP:Port)؛ عند الفراغ يُستخدم البث |

## القيود المعروفة

| المشكلة | الوصف | الحالة |
|------|------|------|
| الأجهزة الكبيرة جدًا | مع أكثر من 100 كائن قد تتجاوز الحزمة حد APDU؛ تحكّم في BatchSize يدويًا | بحاجة إلى تحسين |
| نقل MS/TP / PTP | كود النقل التسلسلي موجود لكنه مستبعد من التجميع؛ توافق الأنظمة المتعددة غير محلول | معطّل |
| اختبارات إدارة بث BBMD | تم تنفيذ TRN-3/TRN-4 لكن دون اختبارات وحدة | بحاجة إلى اختبارات |
| اختبارات نقل IPv6 | تم تنفيذ TRN-2 لكن دون اختبارات تكامل IPv6 | بحاجة إلى اختبارات |

## الأدوات الموصى بها

[YABE (Yet Another BACnet Explorer)](https://sourceforge.net/projects/yetanotherbacnetexplorer/) — مستكشف شبكات BACnet مجاني يعرض الأجهزة والكائنات والخصائص. وهو الطرف المفضّل للتحقق من NewLife.BACnet أثناء التصحيح.

## خريطة المنظومة

NewLife منظومة متعدّدة الطبقات من مشاريع مفتوحة المصدر تُصدَر بإصدارات مستقلة. الخريطة أدناه مشتركة حرفيًا بين جميع مستودعات NewLife؛ وكل مشروع يرتبط بمستودعه وبحزمته الرئيسية على NuGet.

| المشروع | الوظيفة | التنزيلات |
| --- | --- | --- |
| **الأساس** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | التسجيل والتهيئة والتخزين المؤقت والشبكات والتسلسل والجدولة وتتبّع أداء APM. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | وسيط بيانات: جداول واحدة بمستوى عشرات المليارات من الصفوف، تقسيم تلقائي للجداول، فصل القراءة عن الكتابة، وأكثر من 10 قواعد بيانات. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | مكتبة شبكات عالية الأداء: 1.77 مليون pps / 14.5 Gbps على جهاز واحد (TCP 1KB، 2026-10)، و4 ملايين اتصال TCP طويل الأمد. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | إطار اتصال بروتوكولي وRPC لتطبيقات العميل/الخادم وأجهزة إنترنت الأشياء. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | يثبّت تطبيقات .NET كخدمات حارسة لنظام التشغيل: خدمات Windows وsystemd في Linux. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | قوالب مشاريع `dotnet new` للطرفية وweb وAPI والخدمات والبيانات وإنترنت الأشياء. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **البيانات والمراسلة** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | عميل Redis بزمن استجابة بالميكروثانية، وملايين العمليات في الثانية، وقوائم رسائل مدمجة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | عميل RocketMQ مُدار بالكامل لـ Apache RocketMQ وخدمة رسائل Alibaba Cloud. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | عميل وخادم MQTT، بما في ذلك الاتصال بـ Alibaba Cloud IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | قاعدة بيانات هجينة بلغة C#: مدمجة أو شبكية، وعلائقية، وسلاسل زمنية، وقائمة رسائل، وKV في واحد. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | برنامج تشغيل MySql وطبقة مساعدة مبنية على مكدّس بيانات NewLife. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web والذكاء الاصطناعي والأنظمة الموزّعة** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | إطار منصة إدارة سريعة: الصلاحيات، والدخول الموحّد SSO، وخادم OAuth، وتوليد واجهات CRUD. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | منصة خدمات موزّعة: إدارة العقد، وAPM، ومركز الإعدادات، ومركز التسجيل، ومركز النشر. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | منصة موزّعة للمهام وجدولة البيانات الفورية، مع التقسيم وإعادة المحاولة وإعادة التعبئة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | عميل موحّد لـ 46 مزوّد LLM بالإضافة إلى ChatAI، تطبيق ويب جاهز للمحادثة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **الصناعة وإنترنت الأشياء** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | مكتبة معايير إنترنت الأشياء التي تعرّف عقود البروتوكولات وتجريدات الأجهزة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | تنفيذ Modbus TCP/RTU/ASCII فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | بروتوكول Siemens PLC فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | بروتوكول Omron PLC فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | بروتوكول Mitsubishi Melsec PLC فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | عميل OPC للوصول إلى البيانات الصناعية. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | مكدّس بروتوكول BACnet لأتمتة المباني. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | منصة إنترنت أشياء مفتوحة المصدر وخفيفة، مع إدارة البوابات والأجهزة. | — |
| **تليماتيكا المركبات** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | بروتوكول تحديد مواقع المركبات JT/T 808: أكثر من 70 جسم رسالة، 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | بروتوكول JT/T 809 بين المنصات لجهات إدارة النقل. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | فيديو داخل المركبة JT/T 1078: تحليل RTP وإعادة التجميع إلى FLV/fMP4 والبث المباشر. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **مكوّنات المجال** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | حزمة واحدة لـ Excel وWord وPPT وPDF وأكثر من 15 صيغة مستندات. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | صوت عبر المنصات: مرمّزات، وإدخال/إخراج الأجهزة، وDSP، وحاويات، وبث. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | مرمّزات الفيديو وصيغ الحاويات ومعالجة الإطارات والبث. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | واجهة موحّدة فوق Baidu وAMap وTencent وTianditu مع الترميز الجغرافي وتحويل الإحداثيات. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | تحديد الموقع الجغرافي لعناوين IPv4 بقاعدة بيانات غير متصلة مُعيّنة في الذاكرة، وواجهة API عبر الإنترنت كبديل. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **الأدوات والمنتجات** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | صندوق أدوات المطوّر: الشبكات والمنفذ التسلسلي والتشفير والتعبيرات النمطية وModbus وMQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | نظام استضافة ذاتية لإدارة الشيفرة المصدرية Git. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | يثبّت بيئة تشغيل .NET المطابقة تلقائيًا عند التشغيل من مضيف أقدم. | — |

> تعرض الشارات الحزمة الرئيسية لكل مشروع؛ أما حزم السمات والإضافات والحزم التابعة فمذكورة في صفحاتها على NuGet. الحزم التي تتجاوز 30 حزمة وغير المذكورة هنا (FTP وLoRa وإضافات Office وبرامج تشغيل البروتوكولات وغيرها) متاحة في [منظمة NewLifeX](https://github.com/NewLifeX) وعلى [newlifex.com](https://newlifex.com).

## الفريق

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

تأسّست NewLife في عام 2002، وأمضت أكثر من عقدين في منظومة .NET وإنترنت الأشياء، وتدير ما يقارب 100 مشروع مفتوح المصدر (34 منها مشاريع رئيسية)، وتقدّم حلولًا متكاملة للبرمجيات والأجهزة لقطاعات الكهرباء والخدمات اللوجستية والرعاية الصحية والتحكّم الصناعي والإنترنت. ونلتزم بمبدأ "لا أسبقية في التعلّم — الإتقان يكسب الاحترام".

نُزّلت المكوّنات مفتوحة المصدر <!--nuget-total:start-->6.48M<!--nuget-total:end--> مرة من NuGet (على مستوى المنظومة بأكملها، حتى 2026-10).

الموقع: https://newlifex.com · المنظمة مفتوحة المصدر: https://github.com/NewLifeX

<details>
<summary>حساب WeChat الرسمي: 智能大石头 (امسح للاشتراك)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## الترخيص

ترخيص MIT — انظر [LICENSE](LICENSE).
