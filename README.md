# OkEventApp

会议日程 + 扫码签到核销的移动应用，基于 **.NET MAUI** 单工程多端构建。

参会者在日程列表里浏览议程，进详情页看到自己的电子入场凭证（二维码）；现场由工作人员用「扫码核销」扫描凭证完成签到。

## 技术栈

| 层次 | 选型 |
| --- | --- |
| 目标框架 | .NET 10（MAUI，net10.0-android / -ios / -maccatalyst / -windows / -tizen） |
| UI 架构 | MVVM（CommunityToolkit.Mvvm，源生成器） |
| 导航 | .NET MAUI Shell + 路由注册（`Routing.RegisterRoute`） |
| 本地存储 | SQLite-net-pcl（`FileSystem.AppDataDirectory` 下 `okevent.db3`） |
| 网络 | `HttpClient` + `System.Net.Http.Json` |
| 二维码 | QRCoder（生成）、ZXing.Net.Maui（相机扫码） |
| 日志 | 自建轻量落盘日志（`Services/LoggingService.cs`） |

工程结构是典型的分层单工程：`Models / Services / ViewModels / Views / Platforms / Resources`。
依赖方向单向 —— `Views → ViewModels → Services → Models`，数据访问不渗透到 UI 层。

## 环境依赖

构建前需要装齐：

1. **.NET 10 SDK**
2. **MAUI 工作负载**：`dotnet workload install maui`
3. **Android SDK**（`net10.0-android` 目标）：JDK 17、Android SDK Platform 35+
4. **Xcode + Command Line Tools**（`net10.0-ios` / `-maccatalyst` 目标）
5. **Visual Studio 2022 17.8+ 或 VS Code + .NET MAUI 扩展**

## 构建与运行

在 PowerShell 中执行（不要用 bash 语法）：

```powershell
# 确认 MAUI 工作负载就绪
dotnet workload install maui

# 还原 + 构建 Android Debug 包
cd D:\02.Project\02.GitHub\OkEventApp\OkEventApp
dotnet build -f net10.0-android -c Debug

# 部署到已连接的模拟器或真机
dotnet build -f net10.0-android -c Debug -t:Run
```

其他平台把 `-f net10.0-android` 换成 `net10.0-ios`、`net10.0-maccatalyst` 或 `net10.0-windows10.0.19041.0`。

> 构建日志里出现 `hs_err_pid*.log` / `replay_pid*.log` 是 JVM 在 R8 压缩阶段内存不足的产物，属于构建期现象，不是 App 运行时崩溃。

## 功能

- **日程列表**：网络优先加载，失败自动降级读本地 SQLite 缓存
- **关键字搜索**：标题 / 讲师 / 地点三路匹配，`SearchText` 变化即过滤
- **下拉刷新**：`RefreshView` + `LoadEventsCommand`
- **议程详情**：展示议程信息与电子入场凭证二维码
- **手动签到**：详情页按钮签到，状态持久化到 SQLite
- **扫码核销**：`CameraBarcodeReaderView` 识别 QR Code，校验后写入签到状态并自动返回

## 二维码约定

二维码内容与解析共用 `Services/CheckInCode.cs` 一个契约类，格式：

```
OKEEVENT:CHECKIN|<议程ID>|<议程名称>|<讲师>
```

解析端按 `|` 切分取议程 ID 去本地库匹配；兼容只有 ID 的短格式、早期中文格式以及纯 ID 输入。

> 当前二维码是**离线凭证**，核销只在 App 本地落库，没有服务端校验，凭证被拍照转发可以重复使用。接入真实活动需要补服务端。

## 已知限制

- 网络层当前指向 [JSONPlaceholder](https://jsonplaceholder.typicode.com) 占位接口，返回的是模拟数据，上线前要替换成真实后端。
- 扫码核销无服务端校验，见上一节。
- 相机权限已在 Android 清单、iOS plist 与运行时申请三处补齐，但 iOS 真机首次授权弹窗系统控制，无法跳过。
- 深色模式适配：页面色值已收敛到 `Resources/Styles/Colors.xaml` 的主题绑定资源，跟随系统深浅色切换（唯一例外是扫码页压在相机画面上的提示文字，固定白色以保证取景对比度）。
- 编译验证目前只覆盖 Windows 目标（C# 类型检查 + XAML 编译期绑定 + 资源键差集校验三件套）；Android / iOS 真机上的权限弹窗、扫码核销与深浅色表现仍待实机确认。
- CI 目前只有「XAML 资源键差集校验」一步，还没有 `dotnet build` 与单测 job。

## CI

`XAML 资源键校验` 这个 workflow 会在 push / PR 且改动命中 `OkEventApp/Views/**` 或
`OkEventApp/Resources/Styles/**` 时自动执行，也可以在 GitHub 页面上手动触发。

之所以需要它：MAUI 的 XamlC 与源生成器**都不校验 `StaticResource` 的 key 是否存在**，
键名写错时编译期照样 0 错误 / 0 警告，直到运行时解析页面才抛 `XamlParseException`
——直接表现为「页面打不开」。独立建一道检查，才不会误以为「编译通过 = 资源键正确」。

本地想同样跑一遍：

```bash
bash scripts/check-xaml-resource-keys.sh
```

退出码 1 表示存在「引用了但未定义」的键（阻断），只为冗余定义告警但放行。

## 项目结构

```
.github/workflows/  xaml-resource-keys.yml（资源键校验）
scripts/            check-xaml-resource-keys.sh（校验脚本）
OkEventApp/
├─ Models/         EventItem（议程模型，SQLite 主键）
├─ Services/       ApiService / DatabaseService / EventService / CheckInCode / LoggingService
├─ ViewModels/     MainViewModel / EventDetailViewModel / ScanViewModel
├─ Views/          MainPage / EventDetailPage / ScanPage（.xaml + .xaml.cs）
├─ Platforms/      Android / iOS / MacCatalyst / Windows 平台专属配置
├─ Resources/      Styles（Colors.xaml / Styles.xaml）、字体
└─ Properties/     launchSettings.json
```

## 详细技术文档

架构拆解、数据层时序、功能实现细节、缺陷清单与改进路线图见 **[docs/OkEventApp-技术文档.md](docs/OkEventApp-技术文档.md)**。
