# OkEventApp 技术文档

| 项目 | 内容 |
| --- | --- |
| 项目名称 | OkEventApp |
| 解决方案 | `OkEventApp.slnx`（单工程，指向 `OkEventApp/OkEventApp.csproj`） |
| 应用类型 | 会议/活动日程移动端 App（日程浏览 + 电子入场凭证 + 扫码签到核销） |
| 技术框架 | .NET 10 MAUI（Multi-target，`SingleProject`） |
| 当前版本 | `ApplicationDisplayVersion = 1.0`、`ApplicationVersion = 1` |
| 应用标识 | `com.companyname.okeventapp`（模板默认占位，未改为正式标识） |
| 文档日期 | 2026-09-28（第四次修订：全文与当前源码逐行对齐；修复色板键名不匹配导致的运行时崩溃回归；资源键校验固化为 CI 一步） |
| 文档依据 | 本仓库全部源码（模型 / 服务 / ViewModel / View / 平台配置 / 项目文件）逐文件通读 |
| 代码规模 | 应用层 14 个 C# 文件 + 4 个 XAML 页面 + 2 个样式字典，合计约 1138 行 |

> 说明：本文档基于**当前仓库快照**编写，不含任何未落地的设想。所有结论均标注证据位置（文件名:行号），
> 涉及"风险"的条目均已区分「确定事实」与「需真机验证」。

---

## 1. 项目概览

### 1.1 定位

OkEventApp 是一个跨平台的**会议日程与会务签到**应用，围绕一条完整闭环展开：

```
拉取日程 → 本地缓存 → 搜索筛选 → 查看详情 → 生成电子入场凭证(二维码) → 扫码核销 / 手动签到
```

### 1.2 已实现的功能

| 编号 | 功能 | 实现位置 | 完成度 |
| --- | --- | --- | --- |
| F1 | 日程列表加载（网络优先 + 本地降级） | `Services/EventService.cs:18-34` | ✅ 可用（数据源为公开 mock API） |
| F2 | 下拉刷新 | `Views/MainPage.xaml:28-30` + `MainViewModel.LoadEventsAsync` | ✅ 可用 |
| F3 | 关键词搜索（标题/讲师/地点） | `ViewModels/MainViewModel.cs:93-106` | ✅ 可用 |
| F4 | 议程详情页 | `Views/EventDetailPage.xaml` | ✅ 可用 |
| F5 | 电子入场凭证二维码（QRCoder 生成 PNG） | `ViewModels/EventDetailViewModel.cs:62-73` | ✅ 可用（内容走 `CheckInCode` 契约） |
| F6 | 手动签到并持久化 | `ViewModels/EventDetailViewModel.cs:75-99` | ✅ 可用（业务校验偏弱，见 §9.3） |
| F7 | 相机扫码核销 | `Views/ScanPage.xaml(.cs)` + `ViewModels/ScanViewModel.cs` | ✅ 代码链路完整（⚠️ 仅 Windows TFM 编译验证，真机待验） |
| F8 | Shell 路由跳转（列表 → 详情 → 扫码） | `AppShell.xaml.cs:9-10` | ✅ 三个页面均已注册 DI |
| F9 | 收藏（模型内已含 `IsBookmarked` 字段） | `Models/EventItem.cs:52-56` | ❌ 未接 UI，字段当前无写入方（模型已有 INPC，接 UI 时状态天然同步） |

### 1.3 目标平台

工程通过条件编译展开多目标框架（`.csproj:3-6`）：

| 平台 TFM | 最低支持版本 | 说明 |
| --- | --- | --- |
| `net10.0-android` | Android 21（API 21+） | 当前主力调试目标 |
| `net10.0-ios` | iOS 15.0 | 已配置 `NSCameraUsageDescription`；运行时代码只对移动端请求权限 |
| `net10.0-maccatalyst` | Mac Catalyst 15.0 | 同上（`IsMobilePlatform()` 覆盖 MacCatalyst） |
| `net10.0-windows10.0.19041.0` | Windows 10.0.17763.0 | `WindowsPackageType=None`（非商店打包） |

---

## 2. 技术栈

| 领域 | 选型 | 版本 | 用途 |
| --- | --- | --- | --- |
| 语言 / 运行时 | C# / .NET 10 | 10.0 | 语言特性：`partial` 属性、源生成器、`ImplicitUsings`、可空引用类型 |
| UI 框架 | .NET MAUI（`Microsoft.Maui.Controls`） | `$(MauiVersion)` | 跨端 UI + Shell 导航 + 容器（DI、日志） |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 | `[ObservableObject]`、`[ObservableProperty]`、`[RelayCommand]`、`partial void OnXxxChanged` 钩子 |
| XAML 编译 | `MauiXamlInflator=SourceGen` | — | XAML 在编译期生成 C#，替代运行时反射膨胀，提升启动性能 |
| 本地存储 | `sqlite-net-pcl` | 1.11.285 | SQLite 异步封装（`SQLiteAsyncConnection`） |
| 条码生成 | `QRCoder` | 1.8.0 | 生成二维码 PNG 字节流 |
| 条码识别 | `ZXing.Net.Maui` + `.Controls` | 0.10.4 | 相机扫码（`CameraBarcodeReaderView`） |
| 日志 | `Microsoft.Extensions.Logging.Debug` + 自建 `LoggingService` | 10.0.0 | 前者仅 DEBUG 断点输出（`MauiProgram.cs:45-47`，Release 下被编译器丢弃）；后者把网络降级、核销异常等关键路径追加写入 `AppData/logs/app.log` |
| 凭证协议 | 自建 `CheckInCode` 契约 | — | 二维码内容的编解码单一来源，生成端与解析端共同引用（`Services/CheckInCode.cs`） |
| 网络 | `System.Net.Http.Json` | 运行时内置 | `GetFromJsonAsync<T>` 直接反序列化 |

**未使用但项目已具备条件的能力**：`Preferences`（用户偏好）、`SecureStorage`、`Connectivity`
（网络状态判断）、`MediaElement`、多语言资源、`Connectivity` 检查等 MAUI Essentials 能力。
当前代码中 `Connectivity` 未被调用——离线降级依赖"请求失败/返回空列表"来判断，而非主动探测网络。

---

## 3. 工程结构

```
OkEventApp/
├── OkEventApp.slnx                      # 解决方案文件（单工程）
└── OkEventApp/                          # 主工程（SingleProject）
    ├── MauiProgram.cs                   # 应用入口 + DI 容器装配
    ├── App.xaml / App.xaml.cs           # Application 子类，合并样式资源字典
    ├── AppShell.xaml / .cs              # Shell 宿主 + 路由注册
    ├── Models/
    │   └── EventItem.cs                 # 领域模型（SQLite 实体）
    ├── Services/
    │   ├── ApiService.cs                # 远程接口 + DTO 映射
    │   ├── CheckInCode.cs               # 参会凭证编解码契约（生成端 / 解析端共用）
    │   ├── DatabaseService.cs           # SQLite 读写 + 签到状态保护
    │   ├── EventService.cs              # 业务编排（网络优先 + 离线降级）
    │   └── LoggingService.cs            # 本地落盘日志
    ├── ViewModels/
    │   ├── MainViewModel.cs             # 列表 / 搜索 / 导航
    │   ├── EventDetailViewModel.cs      # 详情 / 二维码 / 签到
    │   └── ScanViewModel.cs             # 扫码核销
    ├── Views/
    │   ├── MainPage.xaml(.cs)           # 日程列表（搜索 + 下拉刷新）
    │   ├── EventDetailPage.xaml(.cs)    # 议程详情 + 凭证二维码
    │   └── ScanPage.xaml(.cs)           # 相机扫码
    ├── Platforms/
    │   ├── Android/                     # MainActivity / MainApplication / Manifest
    │   ├── iOS/                         # AppDelegate / Info.plist
    │   ├── MacCatalyst/                 # AppDelegate / Info.plist
    │   └── Windows/                     # App.xaml / app.manifest / Package.appxmanifest
    ├── Properties/launchSettings.json   # Windows 调试配置
    ├── Resources/
    │   ├── AppIcon/ Splash/ Images/ Fonts/ Raw/
    │   └── Styles/Colors.xaml + Styles.xaml   # 模板默认样式 + 业务深浅双色板
    └── bin / obj                         # 构建产物（已被 .gitignore 排除）
```

**目录约定**：标准的 MVVM 分层（`Models / Services / ViewModels / Views`）+ MAUI 单工程多平台目录
（`Platforms/{Android,iOS,MacCatalyst,Windows}`）。没有为各平台拆子工程，符合 `SingleProject` 范式。

---

## 4. 架构设计

### 4.1 分层与依赖方向

```
        ┌─────────────────────────────────────┐
  UI    │  Views (XAML + code-behind)         │  ← 只依赖 ViewModel 与 Shell
        │  MainPage ─ EventDetailPage ─ ScanPage│
        └──────────────┬──────────────────────┘
                       │ 数据绑定 / 命令
        ┌──────────────▼──────────────────────┐
  VM    │  ViewModels (CommunityToolkit.Mvvm) │  ← 业务逻辑(UserCase)，依赖 Services
        │  MainVM / EventDetailVM / ScanVM    │
        └──────────────┬──────────────────────┘
                       │ 接口依赖（编译期具体类型）
        ┌──────────────▼──────────────────────┐
  Biz   │  EventService                       │  ← 编排层：网络 + 本地缓存策略
        └──────────────┬──────────────────────┘
                       │
        ┌──────────────▼──────────────────────┐
  Data  │  ApiService            DatabaseService│
        │  (HTTP + DTO 映射)      (SQLite)      │
        └─────────────────────────────────────┘
                       │
                  Model：EventItem / ApiPostModel
```

* 依赖方向单向向下，View ↔ ViewModel 通过 `INotifyPropertyChanged` / `ICommand` 通信。
* `EventService` 是唯一的业务入口：`ViewModels` 从不直接访问 `ApiService` / `DatabaseService`
  （`MainViewModel.cs:11`、`EventDetailViewModel.cs:12`、`ScanViewModel.cs:8` 均只持有 `EventService`）。
* `ScanPage.xaml.cs` 只承担「相机事件 → ViewModel 命令」的转发（`ProcessScanCommand`），
  去重、协议解析、核销与提示全部收敛在 `ScanViewModel`，code-behind 不持有业务状态 ✅
  （早期版本把 `_isProcessing` 去抖标志与核销逻辑留在 code-behind，属于中间态，现已收口。）

### 4.2 依赖注入配置（`MauiProgram.CreateMauiApp`）

| 服务 | 生命周期 | 说明 |
| --- | --- | --- |
| `LoggingService` | Singleton | 本地落盘日志，写入 `AppData/logs/app.log`；构造时建目录，写失败静默 |
| `DatabaseService` | Singleton | 全局唯一 SQLite 连接（构造注入 `LoggingService`） |
| `ApiService` | Singleton | 全局唯一 `HttpClient`（在构造函数中 `new`） |
| `EventService` | Singleton | 编排层唯一入口。早期在 `MauiProgram` 里重复注册过两次，现已合并为一行（`MauiProgram.cs:27`） |
| `MainViewModel` / `MainPage` | Transient | 每次解析新建 |
| `EventDetailViewModel` / `EventDetailPage` | Transient | — |
| `ScanViewModel` / `ScanPage` | Transient | ✅ 已注册（`MauiProgram.cs:42-43`）。缺这一行时 Shell 路由解析不出页面类型，扫码页打不开 |

生命周期选择合理：数据库与 HTTP 客户端为单例（避免重复连接与 socket 耗尽），页面与页面级
ViewModel 为 Transient（每次导航新建，符合页面生命周期）。

**待改进**：`ApiService` 直接 `new HttpClient()`（`ApiService.cs:12-15`），未走 DI 注入 `IHttpClientFactory`。
对当前单例用法可行，但 `HttpClient` 的 DNS 生命周期问题在频繁切换接口时会出现；建议改为构造注入
`HttpClient`，由 MAUI 的 `AddHttpClient()` 或手动管理。

### 4.3 MVVM 数据流动

以"详情页生成二维码"为例，展示完整数据流：

```
Shell.GoToAsync("EventDetailPage", {"Event": item})
        │
        ▼
Shell 从 DI 解析 EventDetailPage
        │
        ├── 构造注入 EventDetailViewModel（EventService 来自单例容器）
        ├── ViewModel 构造函数执行完毕
        ├── 页面构造函数：BindingContext = viewModel
        └── Shell 将 query 参数 "Event" 写入 BindingContext 上标了
            [QueryProperty(nameof(Item), "Event")] 的 Item 属性
                       │
                       ▼
        partial void OnItemChanged(value) 被触发
                       │
                       ▼
        GenerateQRCode(Item) → CheckInCode.Build(id, title, speaker)
            → QRCoder 生成 PNG 字节 → ImageSource.FromStream
                       │
                       ▼
        XAML: <Image Source="{Binding QrCodeImage}"> 自动刷新
```

反向链路（扫码核销）与上面对称：相机回调把原始文本交给 `ScanViewModel.ProcessScanCommand`，
由 `CheckInCode.TryParse` 解析出 `EventId`，再按 ID 匹配议程 —— **编解码两端共用同一个契约类，
格式只在一处定义**（见 §6.4）。

其中 `[QueryProperty]` 标注在 **ViewModel** 上是 MAUI 官方支持的用法——文档明确说明：
"表示要导航到的页面的类，或页面的 **BindingContext**，可以标注 `QueryPropertyAttribute`"
（MAUI Shell 导航文档）。因此参数能正确落到 ViewModel。

> ⚠️ 官方同时提示：`QueryPropertyAttribute` **不保证对裁剪(NativeAOT)安全**，
> 需要接收导航参数的类型应实现 `IQueryAttributable`。当前工程未开启完整裁剪/NativeAOT，
> 暂无阻塞；若后续做 AOT 发布，需改造。

---

## 5. 数据层

### 5.1 领域模型 `Models/EventItem.cs`

```csharp
public class EventItem : ObservableObject        // CommunityToolkit.Mvvm，即 INPC
{
    [PrimaryKey] public string Id { get; set; } = Guid.NewGuid().ToString();

    private string _title = string.Empty;
    // ... 其余字段同样是「后备字段 + SetProperty」形式

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public bool IsCheckedIn { get => _isCheckedIn; set => SetProperty(ref _isCheckedIn, value); }

    public string CheckInStatus => IsCheckedIn ? "已签到" : "未签到";
}
```

设计要点与问题：

| 项 | 评价 |
| --- | --- |
| `[PrimaryKey] string Id` | 主键用 GUID 字符串，MAUI/Sqlite 里 GUID 主键可正常索引；不使用自增整数，利于与云端 ID 对齐 ✅ |
| 字段默认值 | `Title/Speaker/Location` 默认空串、`Category` 默认 `"General"`，避免反序列化空值 ✅ |
| `IsBookmarked` | 目前**没有 UI 也没有服务写入**，属于预留字段 |
| 模型职责 | 该模型同时是「领域模型」和「SQLite 表实体」和「网络 DTO 的目标类型」——职责混在一起 |
| `INotifyPropertyChanged` | ✅ 已实现（继承 `ObservableObject`）。签到/收藏状态变更后，列表与详情页绑定自动刷新；sqlite-net 只按属性映射列，改成后备字段不影响建表读写 |
| 派生属性 `CheckInStatus` | 把「状态 → 文案」收在模型上，XAML 只需 `{Binding CheckInStatus}`，不必再引入 BoolToText 转换器 |
| 时区 | `StartTime` 为 `DateTime`，未指定 `Kind`；跨时区展示需明确约定（建议 `DateTimeOffset` 或 UTC+Offset） |

### 5.2 SQLite 存储 `Services/DatabaseService.cs`

```csharp
// 真正的赋值发生在首次 InitAsync，这里用 null! 跳过编译器的"字段必须初始化"检查
private SQLiteAsyncConnection _database = null!;
private readonly LoggingService _logger;

private async Task InitAsync()
{
    if (_database != null) return;                                   // 非线程安全（见下）
    var dbPath = Path.Combine(FileSystem.AppDataDirectory, "okevent.db3");
    _database = new SQLiteAsyncConnection(dbPath);
    await _database.CreateTableAsync<EventItem>();
}
```

| 方法 | 行为 | 评价 |
| --- | --- | --- |
| `GetEventsAsync` | 全表 `ToListAsync()` | 数据量小时够用；议程规模上千后应加分页/条件查询 |
| `FindByIdAsync` | `Where(e => e.Id == id).FirstOrDefaultAsync()` | 单条查询；已具备但当前无调用方（核销仍走全量查询，见 §9.2 #8） |
| `SaveEventsAsync` | 逐条「先回捞本地状态 → 再 `InsertOrReplaceAsync`」 | 见下方「状态保护」 |
| `UpdateEventAsync` | `UpdateAsync(item)` + 写入 INFO 日志 | 用于签到状态持久化 |

**状态保护（本轮修复的 P0-4）**：

```csharp
var existing = await _database.Table<EventItem>()
                              .Where(e => e.Id == item.Id)
                              .FirstOrDefaultAsync();

if (existing != null)
{
    item.IsCheckedIn = existing.IsCheckedIn;      // 新造对象默认 false，必须回捞
    item.IsBookmarked = existing.IsBookmarked;
}

await _database.InsertOrReplaceAsync(item);
```

`InsertOrReplaceAsync` 是**整行覆盖**：网络侧 `ApiService` 造出来的 `EventItem` 只填了 6 个日程字段，
`IsCheckedIn` / `IsBookmarked` 都是 `false`。若不回捞，每次下拉刷新都会把用户刚签到的记录静默重置。

存在的技术债：

| # | 问题 | 状态 | 说明 |
| --- | --- | --- | --- |
| 1 | 无迁移机制 | | `CreateTableAsync` 不带 `CreateFlags`、不检测列变化。一旦 `EventItem` 新增字段，老用户设备上的旧库不会自动加列，会抛 "no such column"。需引入 sqlite-net 的 `CheckMigration` 或自建 `user_version` 迁移表 |
| 2 | 无锁 / 无并发保护 | | `InitAsync` 的 `if (_database != null) return;` 不是原子操作，并发首次访问可能创建两条连接 |
| 3 | 缺异常处理 | 部分 | DB 被占用、磁盘满等情况仍会向上冒泡；但 `MainViewModel.LoadEventsAsync` 已有 try/catch 兜住，用户能看到「加载失败」弹窗而不是只看到空白列表 |
| 4 | 逐条写入无事务 | | 5 条数据无感；全量同步上千条时应包事务（`RunInTransactionAsync`） |
| 5 | 路径固定 | | `okevent.db3` 写死在代码里（未从配置读取），符合小项目约定，但属硬编码 |

### 5.3 网络层 `Services/ApiService.cs`

```csharp
var url = "https://jsonplaceholder.typicode.com/posts?_limit=5";
var posts = await _httpClient.GetFromJsonAsync<List<ApiPostModel>>(url);
```

* 当前接的是**公开 mock 服务**（JSONPlaceholder 的 posts 接口），并非真实会务后端。
  代码注释（`ApiService.cs:23-24`）已说明"实际项目中替换为公司后端 API"，属于明确的 TODO。
* 失败路径分两条：`TaskCanceledException`（10s 超时，属可预期降级路径）→ `WarnAsync`；
  其余异常 → `ErrorAsync`（连同堆栈）→ 两者都返回**空 List**。日志落到 `AppData/logs/app.log`。
* DTO 与模型映射（`ApiService.cs:32-40`）目前是"测试性质"的映射：
  `Speaker = $"讲师 {index+1}"`、`Location = $"线上分会场 {index+1}"`、`StartTime = DateTime.Now.AddDays(index)`。
  —— 也就是说当前 UI 上看到的讲师/地点/时间是**本地生成的占位数据**。

设计问题：

| 问题 | 影响 | 建议 |
| --- | --- | --- |
| DTO（`ApiPostModel`）与领域模型混用 | 网络结构与业务耦合，字段一变全受影响 | 保留 DTO → 显式映射函数（当前是内联 LINQ `Select`，尚可） |
| 异常吞掉后返回空集合 | 调用方无法区分「网络失败」和「服务端返回 0 条」 | 部分缓解：超时与异常已在日志里分开记录，但返回值仍是空集合；建议返回 `bool Try` 或抛领域异常 |
| `HttpClient` 在构造函数 new | 见 §4.2 | 改 DI 注入 |
| 无取消令牌 / 无重试 | 弱网体验 | 引入 `CancellationToken`、`Polly` 重试 |
| 超时 10s 硬编码 | 不可配置 | 走配置 |

### 5.4 编排层与离线降级策略 `Services/EventService.cs`

```csharp
public async Task<List<EventItem>> GetEventsAsync()
{
    var remoteEvents = await _apiService.FetchRemoteEventsAsync();   // 1. 先打网络
    if (remoteEvents != null && remoteEvents.Count > 0)
    {
        await _databaseService.SaveEventsAsync(remoteEvents);        // 2. 回写本地缓存（保留签到状态）
        return remoteEvents;                                        // 3. 返回网络数据
    }

    var cached = await _databaseService.GetEventsAsync();             // 4. 降级读本地
    await _logger.WarnAsync($"网络数据不可用，降级使用本地缓存 {cached.Count} 条");
    return cached;
}
```

这是一段**典型的 "Network-First, Offline-Last" 缓存策略**，思路正确、可读性好，值得作为项目的
正面设计记录下来。时序如下：

```
   UI ── GetEventsAsync ──▶ ApiService ──HTTP──▶ 服务端
                                 │ 成功(有数据)
                                 ▼
                           DatabaseService.SaveEvents  (覆盖本地缓存)
                                 │
                                 ▼  返回网络数据给 UI
                                 │
                           (若 HTTP 失败/空)
                                 ▼
                           DatabaseService.GetEvents   (本地缓存兜底)
```

已知问题：

1. ~~**覆盖写会丢失本地用户状态**~~ ✅ **已修复**：`SaveEventsAsync` 在覆盖前按 `Id` 回捞本地的
   `IsCheckedIn` / `IsBookmarked`（见 §5.2）。原实现用新造对象整体替换，每次刷新都会把
   用户刚签到的记录静默清零 —— 这是本轮修复的 P0-4。
2. **"空"与"失败"语义重合**（同 §5.3）：`FetchRemoteEventsAsync` 返回空 List 时，编排层
   无法区分「网络失败」与「服务端确实 0 条」，只能一律降级读缓存。
3. 没有设置 `Connectivity` 预检，弱网时会白等 10 秒。
4. **编排层多了一个"只读缓存"入口**：`GetCachedEventsAsync()`（`:40`）不触网，
   供「从扫码页 / 详情页返回主页」时校准签到状态使用 —— 避免为了一个状态字段拉一次完整网络请求。

补充说明（待改进项，对应 §9.2 #8）：`GetEventsAsync` 在**扫码核销路径上被当作"查一条记录"**使用
（`ScanViewModel.cs:49`），代价是触发一次完整网络请求 + 全表缓存覆盖。`DatabaseService.FindByIdAsync`
已经写好，把核销路径切过去即可解除。

---

## 6. 业务功能实现详解

### 6.1 日程列表与实时搜索 `ViewModels/MainViewModel.cs`

* **双集合设计**：`_allEvents`（`List<EventItem>`，源数据）与 `Events`（`ObservableCollection<EventItem>`，UI 绑定）。
  搜索时不修改源数据，只重建视图集合，避免丢失原始数据 ✅
* **搜索钩子**：`[ObservableProperty] public partial string SearchText` 由源生成器生成
  `OnSearchTextChanged` 部分方法，开发者只需写 `partial void OnSearchTextChanged(string value)` 即可响应，
  无需手写 `INotifyPropertyChanged` 事件 —— 这是 CommunityToolkit.Mvvm 的标准用法 ✅
* **过滤逻辑**（`MainViewModel.cs:96-100`）：对 `Title / Speaker / Location` 三字段做
  `StringComparison.OrdinalIgnoreCase` 的 `Contains`（内存 LINQ，无第三方搜索库，合理）。
* **下拉刷新**：`RefreshView` 双向绑定 `IsRefreshing`，`LoadEventsCommand` 绑定刷新动作 ✅
* **跳转**：`GoToDetailAsync` 通过 `Shell.Current.GoToAsync(nameof(EventDetailPage), new Dictionary{...})`
  传对象参数 ✅；`GoToScanAsync` 跳扫码页 ✅
* **返回主页时的状态校准**：`OnAppearing` 判断列表非空则调 `SyncCheckInStateAsync()`
  （`MainViewModel.cs:67-90`），只从本地缓存回填有差异的签到/收藏字段，不触网、不整体重载 ✅
* **副作用**：`Events.Clear()` 在 `LoadEventsAsync`（`:37`）和 `ApplyFilter`（`:95`）中各调用一次，
  前者是必要的，后者等价于"先清空再加回"，无害但冗余。
* **异常处理**：`LoadEventsAsync` 用 try/catch 包住并弹「加载失败」提示，`finally` 里复位
  `IsRefreshing` ✅ —— 早期版本没有这一层，DB 或网络异常时用户只会看到一片空白。

### 6.2 议程详情与电子入场凭证 `ViewModels/EventDetailViewModel.cs`

* `[QueryProperty(nameof(Item), "Event")]` 接收导航参数，配合 `OnItemChanged` 回调在**对象赋值的瞬间**
  生成二维码，免去了"页面加载后手动初始化"的样板代码 ✅
* 二维码生成链路：
  ```csharp
  QRCodeGenerator() → CreateQrCode(text, ECCLevel.Q) → PngByteQRCode().GetGraphic(20)
      → byte[] → ImageSource.FromStream(() => new MemoryStream(bytes))
  ```
  用 `using` 释放生成器与码数据，字符编码正确 ✅；`ImageSource.FromStream` 传的是 **lambda 工厂**，
  由 MAUI 在需要时创建流，避免长期持有大数组 ✅
* ECC 等级 `Q`（约 25% 容错），在 160×160 的展示尺寸下对摄像头识别是合适的。
* `GetGraphic(20)` 的 20 是像素缩放倍数（每 QR module 20px）。

### 6.3 手动签到

```csharp
if (Item == null || Item.IsCheckedIn) return;   // 幂等保护
Item.IsCheckedIn = true;
await _eventService.UpdateEventStatusAsync(Item);
// EventItem 自带 INPC，绑定路径上的状态标签 / 按钮状态会自动刷新，无需再手动通知
if (Shell.Current != null) await Shell.Current.DisplayAlert(...);
```

* 有幂等保护（重复点击被 `IsCheckedIn` 拦住）✅
* 落库通过 `EventService.UpdateEventStatusAsync → DatabaseService.UpdateEventAsync` ✅
* 按钮的文案与可用状态由 `CheckInButtonText` / `CanCheckIn` 两个派生属性驱动，
  它们订阅了模型的 `PropertyChanged`，签到后按钮自动置灰并改文案 —— 不再需要调用方手动补通知（见 §5.1）。

### 6.4 扫码核销 `Views/ScanPage` + `ViewModels/ScanViewModel.cs`

**协议层**（`Services/CheckInCode.cs`）：二维码内容的编解码只在此处定义一次。

| 成员 | 职责 |
| --- | --- |
| `CheckInCode.Scheme` | 协议标识 `OKEEVENT:CHECKIN`，同时用作「这张码是不是本系统的」判定依据 |
| `Build(id, title, speaker)` | 生成 `OKEEVENT:CHECKIN\|<id>\|<title>\|<speaker>` |
| `TryParse(raw, out payload)` | 解析四种输入：完整四段 / 短格式两段 / 早期中文格式 / 裸邀请 ID |

> 本轮修复的核心（P0-2）就在这里：此前**生成端**在 `EventDetailViewModel` 里拼一段中文文本，
> **解析端**在 `ScanViewModel` 里按 `e.Id == scannedResult` 精确匹配整串 —— 扫码结果永远不可能
> 等于一个裸 GUID，因此自扫自必然弹「无效二维码」。现在两端共同引用 `CheckInCode`，
> 格式只在一处定义，且解析器向后兼容旧版本生成的码。

**扫描侧**（`ScanPage.xaml.cs`）：只做「相机事件 → 命令」的转发。

```csharp
private void CameraReader_BarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
{
    var firstResult = e.Results?.FirstOrDefault();
    if (firstResult == null) return;

    // 页面不持有业务状态，去重与核销都在 ScanViewModel 里
    MainThread.BeginInvokeOnMainThread(() =>
        _viewModel.ProcessScanCommand.Execute(firstResult.Value));
}
```

* 正确地把相机回调派发到主线程（`MainThread.BeginInvokeOnMainThread`）—— ZXing 回调在后台线程，
  不切线程直接操作 `Shell.Current.DisplayAlertAsync` 会崩 ✅
* 相机参数在 `Loaded` 事件中设置（`QrCode` 格式 + `AutoRotate`）✅
* 去抖标志已从 code-behind 移入 ViewModel，页面不再持有 `_isProcessing` ✅

**核销侧**（`ScanViewModel.ProcessScanAsync`，由 `[RelayCommand]` 生成 `ProcessScanCommand`）：

```csharp
if (!await _gate.WaitAsync(0)) return;          // ① 上一张还没处理完，丢弃这一帧
IsProcessing = true;
try
{
    if (!CheckInCode.TryParse(scannedResult, out var payload)) { "无效二维码"; return; }

    var events = await _eventService.GetEventsAsync();                    // ② 走完整编排（含网络）
    var targetEvent = events.FirstOrDefault(e => e.Id == payload.EventId);

    if (targetEvent == null)     { "未找到该议程，请确认凭证是否属于本会议"; return; }
    if (targetEvent.IsCheckedIn) { "重复核销"; 返回上一页; return; }

    targetEvent.IsCheckedIn = true;
    await _eventService.UpdateEventStatusAsync(targetEvent);
    "扫码成功"; 返回上一页;
}
catch (Exception ex) { await _logger.ErrorAsync(原始内容, ex); "核销失败"; }
finally { IsProcessing = false; _gate.Release(); }
```

实现要点：

* **并发去重**：`SemaphoreSlim(1,1)` + 非阻塞 `WaitAsync(0)`，同一时刻只处理一张码；
  相机高频回调下不会堆叠弹窗 ✅（早期是 code-behind 里的 `bool _isProcessing`，且跨线程读写不安全）
* **失败路径各有明确文案**：非本系统二维码 / 议程 ID 不存在 / 重复核销。重复核销提示后直接
  返回上一页，不再走写库 ✅
* **异常兜底**：catch 中把原始扫码内容连同堆栈落盘，用户侧只看到可读提示；
  `finally` 一定释放信号量，不会把摄像头卡死 ✅
* 解析器**兼容四种输入格式**，旧版 App 生成的凭证在新版上仍可识别 ✅

仍然存在的问题：

* **核销路径用 `GetEventsAsync()` 查一条记录**（`ScanViewModel.cs:49`）：会触发一次完整网络请求 +
  全表缓存覆盖（见 §5.4）。`DatabaseService.FindByIdAsync` 已就绪，切换过去即可解除 —— 对应 §9.2 #8。
* **无服务端校验**：扫到**别人**的二维码也会给本机签到成功，且没有服务端记录。本机 `IsCheckedIn`
  只能防住重复点击，防不住多设备重复提交 —— 详见 §9.3。
* **凭证可复制重放**：二维码是明文契约串，截图转发即可在另一台设备上核销成功 —— 详见 §9.3。

**相机权限（三处补齐，本轮 P0-3）**：

| 位置 | 内容 |
| --- | --- |
| `Platforms/Android/AndroidManifest.xml` | `<uses-permission android:name="android.permission.CAMERA" />`，并把 `uses-feature` 设为 `required="false"` |
| `Platforms/iOS/Info.plist` | `NSCameraUsageDescription` 用途描述 |
| `Platforms/MacCatalyst/Info.plist` | `NSCameraUsageDescription` 用途描述 |
| `Views/ScanPage.xaml.cs` | `OnAppearing` 只调 `CheckStatusAsync`；授权弹窗由「授予相机权限」按钮触发 `RequestAsync` |

> ⚠️ **不要在 `OnAppearing` 里直接 `RequestAsync`**：Android 上此时 Activity 尚未完全恢复，
> 会静默返回 `Unknown` 且不弹系统框。正确拆法是「进页面查状态 → 未授权则显示按钮 →
> 用户点击才弹框」，这样用户在设置里授权后回到本页也能重新走通。

---

## 7. 页面、样式与导航

### 7.1 Shell 导航结构

`AppShell.xaml` 只有**一个** `ShellContent`（日程主页），详情页和扫码页通过路由跳转：

| 路由名 | 页面 | 创建方式 | 注册位置 | 传递参数 |
| --- | --- | --- | --- | --- |
| *（初始页）* | `MainPage` | `ContentTemplate`，由 DI 解析构造函数 | `AppShell.xaml`（ContentTemplate） | — |
| `EventDetailPage` | `EventDetailPage` | Shell 路由，由 DI 解析 | `AppShell.xaml.cs:9` `Routing.RegisterRoute` | `{"Event": EventItem}` |
| `ScanPage` | `ScanPage` | Shell 路由，由 DI 解析 | `AppShell.xaml.cs:10` `Routing.RegisterRoute` | 无 |

> **关于「初始页算不算一条路由」**：早期 `ShellContent` 上同时写了 `Route="MainPage"` 和
> `ContentTemplate`，而 `MainPage` 从未 `RegisterRoute`，这条路由是空的 —— 一旦有人
> `GoToAsync("MainPage")` 就会抛「找不到路由」。现已移除该 `Route`：
> 全工程只有 **DI 容器**一种页面创建方式（初始页经 `ContentTemplate`、路由页经 `Routing`），
> 两条路最终都从 `MauiProgram` 注册的容器取实例。Shell 的返回栈由 Shell 自身维护，
> 移除这条路由不影响 `GoToAsync("..")` 回退。

`App.CreateWindow()` 返回 `new Window(new AppShell())`（`App.xaml.cs:12-15`），
未使用 `Application.MainPage` 的旧式赋值，写法现代 ✅

返回上一页用 `GoToAsync("..")`（`ScanViewModel.cs:67`、`:75`），是 Shell 的相对路由语法 ✅

### 7.2 `MainPage` 布局与交互

```
Grid (RowDefinitions: Auto, *)
├─ Row0：Grid(ColumnDefinitions: *, Auto)
│    ├─ SearchBar   Text={Binding SearchText, Mode=TwoWay}
│    └─ Button「📷 扫码核销」→ GoToScanCommand
└─ Row1：RefreshView（IsRefreshing 双向绑定 + Command=LoadEventsCommand）
     └─ CollectionView（ItemsSource=Events，Single 选择模式）
          └─ ItemTemplate：Border + Grid（`x:DataType="models:EventItem"`）
                ├─ 左：标题 + 讲师/地点
                └─ 右：Category 彩色标签 + CheckInStatus 签到状态文字
```

交互细节做得到位的地方：

* 根节点声明 `x:DataType="vm:MainViewModel"`，`ItemTemplate` 内再声明 `x:DataType="models:EventItem"`
  切换回模型作用域，**整页开启编译期绑定**，绑定路径写错会在编译期报错 ✅
* `SelectionChangedCommand` + `SelectionChangedCommandParameter="{Binding SelectedItem,
  Source={RelativeSource Self}}"` 把选中项作为参数传给命令 ✅
* `MainPage.OnAppearing` 里主动 `EventsCollectionView.SelectedItem = null`（`MainPage.xaml.cs:31`），
  解决"返回列表后点同一项不触发跳转"的经典 Shell 坑 ✅（注释也写清楚了原因）
* `OnAppearing` 按列表是否为空分流：空则整体加载，非空则只做签到状态校准（`MainPage.xaml.cs:21-28`）✅
* 所有颜色走 `AppThemeBinding` + 深浅两套资源键，无硬编码色值 ✅

### 7.3 `EventDetailPage`

`ScrollView + VerticalStackLayout`：标题 → 讲师/地点 → 分隔线 → 二维码卡片（160×160）
→ 签到状态文字 → 签到按钮（48 高，圆角 8）。

该页设置了 `x:DataType="vm:EventDetailViewModel"`，开启**编译期绑定**（XamlC），
拼写错误的绑定路径会在编译期报错而不是运行时静默失败 ✅ —— 这是很好的实践，值得保留与推广。

两个由 INPC 驱动的细节：

* 状态标签直接绑 `{Binding Item.CheckInStatus}`，签到后文案从「未签到」自动变「已签到」；
* 按钮绑 `Text="{Binding CheckInButtonText}"` + `IsEnabled="{Binding CanCheckIn}"`，
  签到后**自动置灰并改文案**。这两个派生属性订阅了模型的 `PropertyChanged`
  （`EventDetailViewModel.cs:50-60`），因此不需要调用方手动补通知。

### 7.4 `ScanPage`

`Grid` 内叠放三层：

1. `ContentView(ScannerContainer)` → `CameraBarcodeReaderView` + 250×250 取景框 `Border`（主题色描边）
2. 底部提示文字（`Margin="0,320,0,0"` 定位；**固定白字**，因为它压在相机画面上，
   不跟随主题 —— 这是全工程唯一的刻意硬编码色值）
3. 「授予相机权限」按钮，`IsVisible="False"`，由 code-behind 按权限状态控制显隐

平台适配说明：相机视图对所有平台都会加载（`ZXing.Net.Maui` 在 Windows 上走另一套后端），
但 `OnAppearing` 里用 `IsMobilePlatform()` 判断，**非移动端直接隐藏权限按钮**并跳过权限检查 ——
注释已改为描述真实行为（早期注释声称"非安卓端不加载、不渲染"，与实现不符，属 §9.2 #13 的典型项）。

### 7.5 样式与资源

* `Resources/Styles/Colors.xaml`（72 行）= 模板默认色板 + **业务深浅双色板**（7 组共 14 个
  `Light*` / `Dark*` 键）；`Styles.xaml`（434 行）为模板默认控件样式。
* 页面**零硬编码色值**，统一写成官方推荐的内联形式：

  ```xaml
  TextColor="{AppThemeBinding Light={StaticResource LightTextPrimary}, Dark={StaticResource DarkTextPrimary}}"
  ```

  唯一的例外是 `ScanPage` 底部提示文字写死 `TextColor="White"` —— 它压在相机取景画面上，
  需要固定高对比度，不随主题切换（代码里已注明理由）。
* ⚠️ **XamlC 与 SourceGen 都不校验 `StaticResource` 的 key 是否存在。** 键名写错编译期不报错，
  运行时解析页面时才抛 `XamlParseException`，表现为「页面打不开」。本轮确实踩到过一次
  （见 §9.5 的回归记录）。批量改键名后**必须**跑一次定义 / 引用双向差集核对：

  ```bash
  bash scripts/check-xaml-resource-keys.sh
  ```

  脚本以仓库根目录为 cwd，扫描 `OkEventApp/Views/*.xaml`（引用侧）与
  `OkEventApp/Resources/Styles/*.xaml`（定义侧），**先剥离跨行 `<!-- -->` 注释再抽取** ——
  否则 `Colors.xaml` 里那些「键名必须与页面引用一致」的防复发注释会被误判成真实引用。

  两个方向的口径不同，这点很重要：

  | 方向 | 含义 | 严重度 | 退出码 |
  | --- | --- | --- | --- |
  | 引用了但未定义 | `XamlParseException`，页面打不开 | 致命 | **1（阻断构建）** |
  | 定义了但未引用 | 冗余定义，不影响运行 | 无害 | 0（仅打印清单） |

  ②之所以不阻断：资源字典里本就有 34 个 MAUI 模板自带的键（`Gray100` / `PrimaryBrush` /
  `Headline` 等）暂时没被用到，真要阻断，CI 会被这些无害条目淹没，反而盖住 ① 类真实故障。
  当前基线：定义 48 个 / 引用 14 个，方向①为空，方向② 34 个。

  脚本里还加了一道自锁：若抽取出的引用集合为空（例如路径或正则被改坏），直接以退出码 2 失败，
  而不是让「差集为空」被误读成「校验通过」—— 空差集是最危险的假阳性。

  该校验已固化为 CI 一步（`.github/workflows/xaml-resource-keys.yml`），见 §10 第二阶段第 6 条。
* 字体：OpenSans Regular / Semibold（`MauiProgram.cs:17-18`）。

---

## 8. 构建、运行与调试

### 8.1 依赖列表（`.csproj:68-76`）

| 包 | 版本 | 用途 |
| --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 源生成器 |
| Microsoft.Maui.Controls | `$(MauiVersion)` | 框架本体 |
| Microsoft.Extensions.Logging.Debug | 10.0.0 | 调试日志 |
| QRCoder | 1.8.0 | 二维码生成 |
| sqlite-net-pcl | 1.11.285 | SQLite |
| ZXing.Net.Maui | 0.10.4 | 条码识别核心 |
| ZXing.Net.Maui.Controls | 0.10.4 | 相机控件 |

### 8.2 XAML 编译策略

* 项目级 `<MauiXamlInflator>SourceGen</MauiXamlInflator>`（`.csproj:28`）：XAML 在编译期生成 C#，
  启动更快、反射更少。
* 但同时为 `Views/EventDetailPage.xaml` 与 `Views/ScanPage.xaml` 单独指定
  `<Generator>MSBuild:Compile</Generator>`（`.csproj:79-84`），会让这两页退回传统 XamlC 编译路径，
  与项目级 SourceGen 策略不一致。
  *建议*：移除这两行，统一走 SourceGen（移除后需重新构建验证是否报重复生成）。
  *实测补充*：这两页走 XamlC 路径也好、其余页面走 SourceGen 也好，**对 `StaticResource`
  的 key 都不做校验** —— 别指望编译器帮你兜住资源键写错的问题（见 §7.5、§9.5）。

### 8.3 调试方式

* 当前 `ActiveDebugFramework = net10.0-android`，调试设备为 `Pixel 7 - API 36` 模拟器
  （`OkEventApp.csproj.user`）。
* Windows 调试：`Properties/launchSettings.json` 定义了 "Windows Machine" 配置；
  但 Windows 目标下扫码依赖相机能力，实际体验有限。

**本次修订采用的验证组合**（本机未安装 Android SDK，无法出真机包）：

| 手段 | 能覆盖 | 覆盖不到 |
| --- | --- | --- |
| `dotnet build -f net10.0-windows10.0.19041.0 -c Debug` | C# 类型检查、`x:DataType` 编译期绑定、XAML 语法、资源字典语法 | 平台差异、权限申请、相机、SQLite 实际行为 |
| XAML 资源键定义 / 引用双向差集校验（`scripts/check-xaml-resource-keys.sh`，已进 CI） | `StaticResource` 键名写错（**编译期不报的那一类**） | 键值本身的视觉效果 |
| 源码逐文件对照 | 文档描述与实现是否一致 | 运行时行为 |

CI 现状：目前只有「XAML 资源键校验」一个 workflow，还没有 `dotnet build` / 单测 job ——
也就是说上面表格里第一行的编译验证仍靠本地手动跑。

> 这套组合是**刻意补上编译期盲区**的：上一轮就是「0 错误 / 0 警告」但首页打不开（见 §9.5）。
> 真机验证清单见 §10 第一阶段末尾。

### 8.4 工程环境观察

项目根目录有三个 JVM 崩溃日志 `hs_err_pid19160.log` / `hs_err_pid29596.log` / `hs_err_pid34212.log`
以及 `replay_pid34212.log`。`hs_err_pid34212.log` 的内容是：

```
There is insufficient memory for the Java Runtime Environment to continue.
Native memory allocation (malloc) failed to allocate 1624336 bytes.
Out of Memory Error (arena.cpp:168)
JRE: OpenJDK Runtime Environment Temurin-21.0.12.1+1
```

即 **Android 构建/打包阶段 JVM 因内存不足崩溃**，发生在 R8 代码压缩阶段（replay 日志中大量
`com.android.tools.r8` 相关类）。常见缓解手段：

* 关闭 R8（Android 工程 `<AndroidEnableSGenConcurrent>` 之类，或在构建时加 `-Djava.awt.headless=true`)
* 提高 MSBuild 的 JVM 参数（`-p:AndroidSdkDirectory` 无关，实际是给 `OutOfMemory` 加 `-Xmx`）
* 关闭 XamlC / 减少链接（`-p:AndroidLinkMode=None` 仅调试期）
* 或拆分构建目标、清理 `bin/obj` 重新构建

这类日志文件已加入 `.gitignore`（`hs_err_pid*.log` / `replay_pid*.log`），不入库。

---

## 9. 代码质量评估

### 9.1 做得好的地方（值得保留）

| # | 优点 | 证据 |
| --- | --- | --- |
| 1 | 分层清晰，`ViewModels` 只依赖 `EventService`，数据访问未渗透到 UI | 全部 ViewModel；`EventService.cs` |
| 2 | 正确使用 CommunityToolkit.Mvvm 源生成器（`ObservableProperty`/`RelayCommand`/partial 钩子） | `MainViewModel.cs:18-22,46-49` |
| 3 | 生命周期与依赖注入配置合理 | `MauiProgram.cs` |
| 4 | 网络优先 + 本地降级的缓存策略完整且有注释 | `EventService.cs` |
| 5 | 二维码在 `OnItemChanged` 中惰性生成，链路干净、资源释放正确 | `EventDetailViewModel.cs` |
| 6 | 扫码回调正确切主线程；去抖下沉到 ViewModel（信号量非阻塞获取） | `ScanPage.xaml.cs`、`ScanViewModel.cs` |
| 7 | 详情页启用 `x:DataType` 编译期绑定 | `EventDetailPage.xaml:6` |
| 8 | 处理了 Shell `SelectedItem` 需手动清空导致"重复点击无响应"的坑 | `MainPage.xaml.cs:31` |
| 9 | 页面功能注释完整，读者能快速知道意图 | 各文件头部中文注释 |
| 10 | 协议收口：生成端与解析端共用 `CheckInCode`，改格式不会只改一边 | `Services/CheckInCode.cs` |
| 11 | 主题色单一事实来源：深浅两套色板集中在 `Colors.xaml` | `Resources/Styles/Colors.xaml` |
| 12 | 模型自带 INPC + 派生文案属性，状态变更自动贯穿列表与详情页 | `Models/EventItem.cs`、`EventDetailViewModel.cs:50-60` |
| 13 | 关键失败路径（网络降级、核销异常、签到落库）都有落盘日志 | `Services/LoggingService.cs` |

### 9.2 缺陷清单（按优先级）

**P0 — 阻断功能，建议尽快修复**

> 状态列：✅ = 已修复（2026-09-28）；「部分」= 已缓解但未根治；空白 = 仍待处理。

| # | 问题 | 位置 | 状态 | 说明 |
| --- | --- | --- | --- | --- |
| 1 | `ScanPage` / `ScanViewModel` 未注册进 DI，但路由已注册 | `MauiProgram.cs` vs `AppShell.xaml.cs:10` | ✅ | Shell 解析路由时需从容器解析页面类型，未注册会导致导航抛异常 |
| 2 | 二维码内容格式与核销匹配逻辑不一致 | `EventDetailViewModel` vs `ScanViewModel` | ✅ | 扫码结果永远匹配不上，自扫自必定失败 |
| 3 | 相机权限缺失 | `AndroidManifest.xml`、iOS/MacCatalyst `Info.plist`、无运行时申请 | ✅ | 清单/plist 补齐 + 运行时 `CheckStatusAsync`/`RequestAsync` |
| 4 | 刷新覆盖写会清零签到/收藏状态 | `DatabaseService.SaveEventsAsync` | ✅ | `InsertOrReplaceAsync` 用新造对象整体替换 |

**P1 — 影响正确性 / 可维护性**

| # | 问题 | 位置 | 状态 | 建议 |
| --- | --- | --- | --- | --- |
| 5 | `EventItem` 无 `INotifyPropertyChanged`，状态变化不自通知 | `Models/EventItem.cs` | ✅ | 改为继承 `ObservableObject`，状态变更由模型自通知 |
| 6 | 无数据库迁移机制 | `DatabaseService.InitAsync` | | 字段变更会让老用户崩溃 |
| 7 | "网络失败"与"返回空"无法区分 | `ApiService.FetchRemoteEventsAsync` | 部分 | 已加超时分支区分超时与异常；仍建议返回可判空类型或抛领域异常 |
| 8 | 扫码核销走 `GetEventsAsync()` 触发完整网络请求 | `ScanViewModel.ProcessScanAsync:49` | | `DatabaseService.FindByIdAsync` 已就绪，把核销路径切过去即可 |
| 9 | `EventService` 重复注册 | `MauiProgram.cs` | ✅ | 已删除重复行 |
| 10 | 无网络状态预检，弱网白等 10s | `ApiService` 构造 | | 接入 `Connectivity` |
| 11 | 逐条 `InsertOrReplace` 无事务 | `DatabaseService.SaveEventsAsync` | | 包事务 |
| 12 | 无全局异常处理 / 用户可见错误信息 | 全工程 | 部分 | 已有：`MainViewModel.LoadEventsAsync` 与 `ScanViewModel` 各自 try/catch + 弹窗 + 落盘日志。缺全局兜底（`DispatcherUnhandledException`）与统一错误态 UI |
| 13 | `ScanPage.xaml` 注释与实现不符（平台条件未落地） | `ScanPage.xaml:9` | ✅ | 已改为描述真实行为 |
| 14 | 路由 + `ContentTemplate` 双路径创建 `MainPage` | `AppShell.xaml` vs `MauiProgram.cs` | ✅ | 移除未注册的 `Route="MainPage"`，页面只由 DI 创建 |
| 15 | 依赖版本陈旧（ZXing 0.10.4）与 .NET 10 兼容性待验证 | `.csproj` | | 升级并验证 |

**P2 — 工程化 / 体验**

| # | 问题 | 状态 | 建议 |
| --- | --- | --- | --- |
| 16 | `ApplicationId` 仍是模板占位 `com.companyname.okeventapp` | | 改为正式包名 |
| 17 | 无 `.gitignore`、`README` | ✅ | `.gitignore` 与根目录 `README.md` 已补齐 |
| 18 | 无单元测试 / UI 测试 | | 至少为 `EventService` 的降级策略、`ApplyFilter` 写 xUnit 用例 |
| 19 | 无结构化日志 | 部分 | 已自建 `LoggingService` 落盘到 AppData；正式项目建议换 `ILogger` + `Serilog` |
| 20 | 硬编码色值散落在页面 | ✅ | 已收敛到 `Resources/Styles/Colors.xaml` 的深浅双色板 |
| 21 | 无空状态/加载失败态 UI | `CollectionView EmptyView` |
| 22 | 无多语言资源 | `Resources/Strings/resx` |
| 23 | 未使用 `Preferences` 保存登录态/最后同步时间 | 便于增量同步 |
| 24 | 无 CI / 打包脚本 | GitHub Actions + Android/iOS 打包 |
| 25 | 无 AOT/裁剪友好性准备 | 若需商店版，需处理 `QueryProperty` 等反射依赖 |

### 9.3 业务与安全风险

即便不考虑代码缺陷，当前设计在**业务可信度**上存在三类问题，产品化前必须处理：

1. **核销不校验**：任何拿到他人二维码的人扫码即可让本机显示"核销成功"，且没有服务端记录。
   真实会务中"签到"通常需要服务端确认场次、时间窗口与名额。
2. **重复核销无服务端幂等**：本机 `IsCheckedIn` 只能防住重复点击，防不住多设备重复提交。
3. **凭证无防伪造**：二维码内容是明文契约串，可被随意复制重放（截图转发即可绕过）。

> 本轮的协议收口、信号量去重、重复核销提示只解决了**单机可用性**（能扫、能认、不重复写），
> 并没有改变上面这个可信度模型 —— 它需要服务端参与（核销接口 + 幂等键 + 时效签名）才能根治。

> 对求职/作品集而言：这三点是很好的"我在当前架构下识别出的产品级风险"，比罗列技术栈更有说服力。
> 建议在简历或技术分享中按"当前实现 → 风险 → 我的改造方案"的方式表述。

### 9.4 已落地的改动清单（第二、三次修订，2026-09-28）

编译基线：`dotnet build -f net10.0-windows10.0.19041.0 -c Debug` → **0 错误 / 0 警告**。
（注意：Android 目标本次未能验证 —— 本机未安装 Android SDK，`dotnet build -f net10.0-android` 在 SDK 探测阶段即以 `XA5300` 失败，与代码无关。）
（**注意**：这个基线并**不能**证明 XAML 资源键正确 —— 同一批次里就漏掉了一个页面上打不开的
回归，见 §9.5。）

| # | 类别 | 改动 | 涉及文件 |
| --- | --- | --- | --- |
| 1 | P0 | 注册 `ScanViewModel` / `ScanPage`，去掉 `EventService` 重复注册 | `MauiProgram.cs` |
| 2 | P0 | 新建 `CheckInCode` 契约类，生成端与解析端共用 | `Services/CheckInCode.cs`（新增） |
| 3 | P0 | 二维码改为 `OKEEVENT:CHECKIN\|{id}\|{title}\|{speaker}` | `ViewModels/EventDetailViewModel.cs` |
| 4 | P0 | 核销解析兼容四种输入，并识别重复核销 | `ViewModels/ScanViewModel.cs` |
| 5 | P0 | `AndroidManifest` 加 `CAMERA` + `uses-feature(required=false)` | `Platforms/Android/AndroidManifest.xml` |
| 6 | P0 | iOS / MacCatalyst `Info.plist` 加 `NSCameraUsageDescription` | 两个 `Info.plist` |
| 7 | P0 | 运行时权限：检查与请求分离，请求由按钮触发 | `Views/ScanPage.xaml(.cs)` |
| 8 | P0 | `SaveEventsAsync` 保存前回捞本地 `IsCheckedIn`/`IsBookmarked` | `Services/DatabaseService.cs` |
| 9 | 整改 | `MainPage.xaml` 补 `x:DataType`，ItemTemplate 内声明 `models:EventItem` | `Views/MainPage.xaml` |
| 10 | 整改 | `ScanViewModel` 命令化，去抖用 `SemaphoreSlim` 收敛到 VM | `ViewModels/ScanViewModel.cs`、`Views/ScanPage.xaml.cs` |
| 11 | 整改 | 色值收敛到深浅双色板，深色模式可用 | `Resources/Styles/Colors.xaml` + 三个页面 XAML |
| 12 | 整改 | 新建 `LoggingService`，替换 `Debug.WriteLine`，关键异常落盘 | `Services/LoggingService.cs`（新增）+ 各服务 |
| 13 | 整改 | `DisplayAlert` → `DisplayAlertAsync` | 三个 ViewModel |
| 14 | 整改 | `[ObservableProperty]` 改 partial property，消除 WinRT AOT 警告 | `EventDetailViewModel`、`ScanViewModel` |
| 15 | 整改 | `MainPage` 只保留 DI 一条创建路径，移除未注册的 `Route="MainPage"` | `AppShell.xaml`、`MauiProgram.cs` |
| 16 | 整改 | `EventItem` 继承 `ObservableObject`，模型自通知状态变更 | `Models/EventItem.cs` |
| 17 | 整改 | 详情页派生属性 `CanCheckIn` / `CheckInButtonText` 订阅模型 INPC | `ViewModels/EventDetailViewModel.cs` |
| 18 | 整改 | 列表新增签到状态标签；详情页新增状态行，按钮按签到状态置灰 | `Views/MainPage.xaml`、`Views/EventDetailPage.xaml` |
| 19 | 整改 | 返回主页时用本地缓存校准签到状态，避免为单个字段走一次网络 | `EventService.cs`、`MainViewModel.cs`、`MainPage.xaml.cs` |

**四个值得记下来的决策点：**

1. **不在 `OnAppearing` 里直接调 `Permissions.RequestAsync`。** Android 上此时 Activity 尚未完全恢复，会静默返回 `Unknown` 且不弹系统框。最终做法是 `OnAppearing` 只 `CheckStatusAsync` 查状态，授权弹窗改由页面上的「授予相机权限」按钮触发。

2. **MAUI 10 里没有 `Permissions.CheckAsync<T>()`，正确 API 是 `CheckStatusAsync<T>()`**（命名空间 `Microsoft.Maui.ApplicationModel`）。

3. **主题色不能用 `{AppThemeBinding Key=X}` 引用资源，也不能在 `<Color>` 上写 `Light=`/`Dark=`。** XamlC（SourceGen）两种写法都报 `MAUIX2002`。唯一可行的是官方形式：
   ```xaml
   TextColor="{AppThemeBinding Light={StaticResource LightTextPrimary}, Dark={StaticResource DarkTextPrimary}}"
   ```
   即先在资源字典里定义两套 `<Color x:Key="LightXxx">` / `<Color x:Key="DarkXxx">`，再在控件属性上内联引用。

4. **「初始页由谁创建」要写清楚。** 之前 `AppShell.ShellContent` 上同时有 `Route="MainPage"` 和
   `ContentTemplate`，但 `MainPage` 从未 `RegisterRoute` —— 这条路由是空的，谁 `GoToAsync("MainPage")`
   谁就会抛异常。最终做法是去掉这条无效路由、保留 `ContentTemplate`，并在 `MauiProgram` 里注明：
   `ContentTemplate` 创建页面时依然从 DI 容器解析构造函数参数，**这一行注册不能删**。

5. **模型要不要吃 INPC，取决于它是不是直接当绑定数据源。** `EventItem` 同时是 SQLite 实体、
   `MainViewModel` 集合元素、详情页的绑定上下文，签到状态一变而 UI 不知道，就只能到处手动补
   `OnPropertyChanged`。改成本模型继承 `ObservableObject` 之后，派生属性（如 `CheckInStatus`、
   按钮的 `CanCheckIn`）只要订阅模型的 `PropertyChanged` 就能自动重算，调用方不用再兜。
   代价是模型混入了 UI 框架依赖 —— 后续若要拆领域模型，把状态抽成独立的 `EventItemState` 即可。

### 9.5 第四次修订：色板键名回归修复（2026-09-28）

**问题**：`Colors.xaml` 里定义的键名与页面引用的键名不一致，共 3 组 6 个：

| 页面引用（不存在） | 资源字典实际定义 |
| --- | --- |
| `LightOnAccentText` / `DarkOnAccentText` | `LightOnAccent` / `DarkOnAccent` |
| `LightDividerColor` / `DarkDividerColor` | `LightDivider` / `DarkDivider` |
| `LightSurfaceBackground` / `DarkSurfaceBackground` | `LightSurface` / `DarkSurface` |

**为什么编译没拦住**：XamlC 与 SourceGen **都不校验 `StaticResource` 的 key 是否存在**。
所以上一次修订的结论「0 错误 / 0 警告」是**不充分的** —— 这个问题只在运行时解析页面时
抛 `XamlParseException`，而首页 `MainPage` 用到了 `LightOnAccentText` / `LightDividerColor`，
**症状是首页直接打不开**，属最严重的一档。

**根因**：上一轮用 `sed` 批量重写主题绑定语法时，替换目标（`Light\1`）是从页面上原有的键名
推导出来的，而随后重写 `Colors.xaml` 时凭印象用了更短的键名，两边没做一致性核对。

**修复与验证**：统一为页面使用的长名（只改 `Colors.xaml` 6 行，页面零改动），
然后做**定义 / 引用双向差集**校验 —— 「引用但未定义」与「定义了但未引用」两个方向都必须为空。
校验命令见 §7.5。

**教训（已固化为流程）**：

1. **「编译通过」≠「XAML 资源键正确」。** 凡是用脚本批量改 XAML 的键名，
   改完必须跑一次差集核对，不能靠编译器兜底。
2. **批量替换的目标字符串必须来自资源字典的真实定义**，不能凭印象手写。
3. 验证结论要写清**验证了什么、没验证什么**。「0 错误 0 警告」只覆盖语法与类型，
   不覆盖运行时资源解析、真机行为与平台差异 —— 这三块必须单列。

---

## 10. 改进路线图

### 第一阶段：打通主流程（1~2 天）— ✅ 已于 2026-09-28 全部完成

1. ~~`MauiProgram` 补注册 `ScanViewModel` + `ScanPage`。~~
2. ~~统一二维码协议，生成端与解析端同源。~~
3. ~~申请相机权限：清单 / plist / 运行时三处补齐。~~
4. ~~修复签到状态被刷新覆盖。~~
5. ~~统一 `MainPage` 的创建路径为「仅 DI」。~~
6. ~~`EventItem` 实现 `INotifyPropertyChanged`，状态同步自洽。~~
7. ~~修复色板键名不一致导致的「页面打不开」（见 §9.5）。~~

> 本阶段七项均已在 §9.4 / §9.5 中落地。
>
> **剩下的是真机验证（本机无 Android SDK，Windows TFM 只能验证到语法与类型这一层）**：
> ① Android 与 iOS 各跑一次扫码核销，确认权限弹窗、核销弹窗与返回上一页的行为；
> ② 返回列表后签到标签是否就地刷新（不依赖重新加载）；
> ③ 系统深浅色切换后，列表与详情页的文字对比度是否正常（本轮刚从硬编码色值切到双色板）。

### 第二阶段：质量与健壮性（1 周）

1. 引入数据库迁移（`user_version` + 迁移表）。
2. 抽取 `IEventRepository` / `IEventApi` 接口，改为构造注入，为单测铺路。
3. 全局异常处理 + 空态 / 错误态 UI（`CollectionView.EmptyView` 目前仍为空）。
4. `EventService` 同步事务化；网络层接入 `Connectivity` 预检与 `CancellationToken`。
5. 核销路径改用 `DatabaseService.FindByIdAsync`，去掉一次无谓的完整网络请求（§9.2 #8）。
6. ~~把 §7.5 的资源键差集校验固化成 CI 一步，防止 §9.5 类问题复发。~~
   **已于 2026-09-28 落地**：`scripts/check-xaml-resource-keys.sh` +
   `.github/workflows/xaml-resource-keys.yml`。push / PR 且路径命中
   `OkEventApp/Views/**`、`OkEventApp/Resources/Styles/**` 时自动执行，
   也可在 GitHub 页面上手动触发（`workflow_dispatch`）。当前 CI 只有这一步，
   `dotnet build` / 单测 job 仍待补（见 §8.3「验证组合」）。

### 第三阶段：工程化与产品化（2~3 周）

7. 补 xUnit 测试：`ApiService` 超时/异常降级、`MainViewModel.ApplyFilter` 过滤、
   `CheckInCode.TryParse` 四种输入格式（这组是纯函数，最容易先补上）。
8. 登录态（`SecureStorage`）+ 用户维度日程隔离。
9. 服务端核销接口对接，引入幂等键与核销记录（解决 §9.3 的三条可信度风险）。
10. 主题 / 多语言 / 可访问性（字号、`SemanticScreenReader`）。
11. CI：构建 + 单元测试 + 自动打包 APK/IPA。

---

## 11. 附录

### 11.1 关键文件索引

| 文件 | 行数 | 职责 |
| --- | --- | --- |
| `OkEventApp/MauiProgram.cs` | 51 | 应用入口、DI 装配 |
| `OkEventApp/AppShell.xaml` / `.cs` | 24 / 11 | Shell 结构与路由注册 |
| `OkEventApp/App.xaml` / `.cs` | 14 / 16 | 合并资源字典、创建 `Window(new AppShell())` |
| `OkEventApp/Models/EventItem.cs` | 69 | 领域模型 / SQLite 实体 / 绑定数据源（INPC） |
| `OkEventApp/Services/EventService.cs` | 45 | 网络优先 + 离线降级编排、只读缓存入口 |
| `OkEventApp/Services/ApiService.cs` | 64 | HTTP + DTO 映射 + 超时/异常分类日志 |
| `OkEventApp/Services/DatabaseService.cs` | 67 | SQLite CRUD + 签到状态保护 |
| `OkEventApp/Services/CheckInCode.cs` | 84 | 参会凭证编解码契约 |
| `OkEventApp/Services/LoggingService.cs` | 62 | 本地落盘日志 |
| `OkEventApp/ViewModels/MainViewModel.cs` | 124 | 列表、搜索、返回校准、导航 |
| `OkEventApp/ViewModels/EventDetailViewModel.cs` | 99 | 详情、二维码、签到 |
| `OkEventApp/ViewModels/ScanViewModel.cs` | 88 | 扫码核销（命令化 + 信号量去重） |
| `OkEventApp/Views/MainPage.xaml` / `.cs` | 65 / 32 | 日程列表 UI（含 `x:DataType`） |
| `OkEventApp/Views/EventDetailPage.xaml` / `.cs` | 48 / 11 | 详情 + 凭证 + 签到状态 UI |
| `OkEventApp/Views/ScanPage.xaml` / `.cs` | 44 / 78 | 相机扫码 UI、权限申请、回调转发 |
| `OkEventApp/Resources/Styles/Colors.xaml` | 72 | 模板色板 + 业务深浅双色板（键名须与页面引用一致） |
| `OkEventApp/Resources/Styles/Styles.xaml` | 434 | 模板默认控件样式 |
| `scripts/check-xaml-resource-keys.sh` | 173 | 资源键定义/引用双向差集校验，CI 与本地共用 |
| `.github/workflows/xaml-resource-keys.yml` | 42 | 上述校验的 GitHub Actions 编排（push / PR / 手动） |
| 仓库根 `.gitignore` | 55 | 版本控制忽略规则（构建产物、IDE、JVM 崩溃日志） |
| 仓库根 `README.md` | 95 | 仓库门面（定位 / 快速开始 / 结构 / 已知限制） |

应用层合计约 1138 行（14 个 C# 文件 + 4 个 XAML 页面 + 2 个样式字典），不含平台项目与生成代码。

### 11.2 核心代码片段速查

> 片段均为当前实现的节选（省略了日志与注释），行号会随改动变化，定位以方法名为准。

**离线降级（正面示例）**

```csharp
public async Task<List<EventItem>> GetEventsAsync()
{
    var remoteEvents = await _apiService.FetchRemoteEventsAsync();
    if (remoteEvents != null && remoteEvents.Count > 0)
    {
        await _databaseService.SaveEventsAsync(remoteEvents);
        return remoteEvents;
    }

    var cached = await _databaseService.GetEventsAsync();
    await _logger.WarnAsync($"网络数据不可用，降级使用本地缓存 {cached.Count} 条");
    return cached;
}
```

**MVVM 源生成器 + partial 钩子**

```csharp
[ObservableProperty]
public partial string SearchText { get; set; } = string.Empty;

partial void OnSearchTextChanged(string value) => ApplyFilter();
```

**二维码生成（走协议契约）**

```csharp
var checkInText = CheckInCode.Build(item.Id, item.Title, item.Speaker);

using var qrGenerator = new QRCodeGenerator();
using var qrCodeData = qrGenerator.CreateQrCode(checkInText, QRCodeGenerator.ECCLevel.Q);
using var qrCode = new PngByteQRCode(qrCodeData);
byte[] bytes = qrCode.GetGraphic(20);
QrCodeImage = ImageSource.FromStream(() => new MemoryStream(bytes));
```

**扫码回调：页面只转发，去抖在 ViewModel**

```csharp
// ScanPage.xaml.cs
private void CameraReader_BarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
{
    var firstResult = e.Results?.FirstOrDefault();
    if (firstResult == null) return;

    MainThread.BeginInvokeOnMainThread(() =>
        _viewModel.ProcessScanCommand.Execute(firstResult.Value));
}

// ScanViewModel.cs
if (!await _gate.WaitAsync(0)) return;   // 上一张还没处理完就丢弃这一帧
```

**签到状态不被刷新覆盖**

```csharp
var existing = await _database.Table<EventItem>()
                              .Where(e => e.Id == item.Id)
                              .FirstOrDefaultAsync();

if (existing != null)
{
    item.IsCheckedIn = existing.IsCheckedIn;
    item.IsBookmarked = existing.IsBookmarked;
}

await _database.InsertOrReplaceAsync(item);
```

**模型自带 INPC，派生属性订阅后自动重算**

```csharp
// Models/EventItem.cs —— 状态一变，绑在 CheckInStatus 上的标签自动刷新
public string CheckInStatus => IsCheckedIn ? "已签到" : "未签到";

// ViewModels/EventDetailViewModel.cs —— 派生属性订阅模型的 PropertyChanged
private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    OnPropertyChanged(nameof(CanCheckIn));
    OnPropertyChanged(nameof(CheckInButtonText));
}
```

**XAML 资源键差集校验（编译期拦不住，已固化进 CI）**

```bash
# 以仓库根目录为 cwd；退出码 1 = 有「引用了但未定义」的键，会被 CI 判失败
bash scripts/check-xaml-resource-keys.sh
```

等价的手写一次性命令（调试脚本行为时用，日常与 CI 都直接跑脚本）：

```bash
comm -23 \
  <(grep -oh "StaticResource [A-Za-z]*" OkEventApp/Views/*.xaml | awk '{print $2}' | sort -u) \
  <(grep -oh 'x:Key="[A-Za-z]*"' OkEventApp/Resources/Styles/*.xaml | sed 's/x:Key="//;s/"//' | sort -u)
```

注意上面的路径带 `OkEventApp/` 前缀 —— 本项目是单工程布局，源码不在仓库根。
早期版本漏了这层，搬到 CI 上会 glob 匹配为空、输出空差集，反而「假通过」。

### 11.3 术语表

| 术语 | 含义 |
| --- | --- |
| TFM | Target Framework Moniker，.NET 目标框架标识 |
| Shell | MAUI 的导航与视觉结构宿主，提供路由、标签页、栈式导航 |
| Route / 路由 | Shell 中用于导航的字符串标识，通过 `Routing.RegisterRoute` 注册 |
| QueryProperty | 通过导航参数自动填充属性的特性 |
| MVVM | Model-View-ViewModel 分层架构 |
| INPC | `INotifyPropertyChanged`，属性变更通知机制（此处由 `ObservableObject` 提供） |
| StaticResource | XAML 中对资源字典键的静态引用；**键名写错编译期不报，运行时抛 `XamlParseException`** |
| XamlC | XAML 编译期绑定校验（配合 `x:DataType` 生效），但**不校验资源键** |
| 源生成器（SourceGen） | 编译期生成代码的特性，此处指 XAML 编译与 MVVM 属性生成 |
| 降级（Fallback） | 网络不可用时改用本地缓存数据的策略 |
| ECC | Error Correction Level，二维码容错等级（此处为 Q，约 25%） |
| R8 | Android 的代码压缩与混淆工具（构建期 OOM 来源） |
