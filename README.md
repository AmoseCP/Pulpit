# Pulpit

<img src="docs/logo.png" width="120" align="right" alt="Pulpit 标志：红十字与翻开的圣经" />

教会直播用经文投屏工具。副屏以**透明叠加层**显示经文，**不干扰正在放映的 PPT**。

操作员在主屏输入 `约3:16`，按 F9，经文淡入到副屏 PPT 之上；按 F12 淡出。
全程不夺取焦点，不打断 WPS/PowerPoint 放映，现场观众与直播观众同时可见。

---

## 当前状态

| 里程碑 | 内容 | 状态 |
|---|---|---|
| — | 数据库 `bible_cuv.db`（和合本简体，31103 节，478 条书卷别名，81 组并节已解析） | ✅ 已就绪 |
| **M0** | 透明叠加层技术尖刺 | 🟢 **双屏 + WPS 全屏放映共存已实测**（2026-08-20）：投放不夺焦、放映不退全屏、跨 DPI（125%/100%）定位正确。剩三项人工（见下） |
| **M1** | 数据层与引用解析（`Pulpit.Core` + 单测） | ✅ **282 条用例全绿**（真库；首测修掉一处解析器缺陷与一处测试预期） |
| **M2** | 叠加层渲染 | ✅ 已编译并真机投放实测：106 字最长节三行无裁切、并节标签（申30:9 → 30:9-10）正确 |
| **M3** | 控制窗口与输入（IME 安全） | 🟡 已编译运行；**IME 组合态需微软拼音人工复验**（handledEventsToo 修复后） |
| **M4** | 全局热键与分页 | ✅ F7–F10 真机实测（含 F12 被占用时的降级路径）；多页翻页与页码正确 |
| **M5** | 配置与健壮性 | ✅ 已编译；新增配置节为 null 的防护（曾是启动崩溃 / 直播中延迟崩溃两条路径） |
| **M6** | 打包与实战彩排 | 🟢 单文件发布 + 全新首启已实测（嵌入库解出、e_sqlite3 自解压均正常）；实战彩排待现场 |

**2026-08-20 真机首测**：WPF 层首次编译一次通过（0 错误，检查清单预警的 12 处 API 风险
零命中）；首启抓出并修复了 9 处缺陷（详见当日三个提交）。M0 九项验收仅剩三项必须人工：

1. **微软拼音组合态**——打「神爱世人」到一半按 F9 应被拒绝（验证 handledEventsToo 修复）；
2. **淡入帧率读数**（原第 7 项悬空风险）：过低就把 `animation.fadeMs` 设 **0**（无动画直切），
   这条逃生口已实现，是改配置而不是改代码；
3. **投影线物理拔插**——拔线带子退回主屏、插回应自动回到扩展屏（验证回退修复）。

---

## 构建

需要 **.NET 8 SDK**，**仅 Windows x64**。WPF 无法在 macOS/Linux 上构建或运行
（这是 L1 锁定决策的直接结果，不是疏漏；见 `DEVELOPMENT_PLAN.md` §9 明确的非目标）。

> ℹ 2026-08-20 已在 Windows 上首次编译（一次通过，0 错误）并真机运行。
> [`docs/首次编译检查清单.md`](docs/首次编译检查清单.md) 预警的 12 处 API 风险零命中，
> 清单保留作历史与方法论参考；其中「四级：行为风险」的前三条也已实测通过
> （行高无裁切、VisualBrush 预览可用、单文件 e_sqlite3 自解压正常）。

```powershell
dotnet build src\Pulpit.Core\Pulpit.Core.csproj    # 先把 Core 排除掉
dotnet test  tests\Pulpit.Core.Tests

dotnet build Pulpit.sln -c Debug
dotnet test tests\Pulpit.Core.Tests                      # Core 验收（§6 全部用例 + 配置 + 热键白名单）
dotnet run --project src\Pulpit.App\Pulpit.App.csproj    # 跑起来
```

发布单文件（未装 .NET 运行时的干净 Windows 上双击可用）：

```powershell
.\publish.cmd
```

产物 `publish\win-x64\Pulpit.exe`。经文库嵌在 exe 里，首次运行解出到
`%LOCALAPPDATA%\Pulpit\bible_cuv.db`。

### 分发打包（两个版本）

```powershell
.\tools\package.ps1
```

先跑全部单测（不过不发布），然后一次产出：

| 产物 | 形态 |
|---|---|
| `publish\Pulpit-<版本>-portable.zip` | **Portable**：解压后双击 `Pulpit.exe` 即用，附快速上手卡与说明 |
| `publish\Pulpit-Setup-<版本>.exe` | **安装版**：按用户安装、**无需管理员**（教会机器上的操作员账号可自行安装），含开始菜单/桌面快捷方式与卸载器；卸载保留 `%LOCALAPPDATA%\Pulpit` 的配置、经文库与日志，重装不丢设置 |

安装版的编译需要 Inno Setup 6（`winget install --id JRSoftware.InnoSetup -e`），
缺了会跳过安装版、Portable 照常产出。版本号唯一来源是 `Pulpit.App.csproj` 的 `<Version>`。

两个版本都是**零外部依赖**：自包含 .NET 运行时，SQLite 原生库为静态 CRT，
WPF 所需的 VC 运行库随包自解压——目标机只需 Windows 10 x64 (1607) 或更新。
唯一的现场提示：exe 未做代码签名，**经网络下载**的副本首次运行会弹 SmartScreen
（「更多信息」→「仍要运行」即可，说明.txt 里有写）；U 盘拷贝通常不触发。

`Pulpit.Core` 与它的测试是**纯 `net8.0`**（不引用任何 WPF/WinForms 类型），
所以 `dotnet test` 在 macOS / Linux 上也能跑；只有 `Pulpit.App` 必须在 Windows 上构建。

---

## 文档

| 文件 | 内容 |
|---|---|
| `DEVELOPMENT_PLAN.md` | 锁定决策、功能分级、里程碑与验收标准、关键契约、测试用例 |
| `SCHEMA.md` | `bible_cuv.db` 表结构、并节语义、文本清洗规则、常用查询 |
| `CLAUDE.md` | 给 AI 代理的项目约定；「最容易做错的五件事」 |
| `docs/M0-验收清单.md` | M0 现场验收表，含帧率判读标准与失败回报格式 |
| `docs/快速上手卡.md` | **给志愿者的一页 A4**：五个键、怎么打经文、出错怎么看 |
| `docs/首次编译检查清单.md` | **首次 `dotnet build` 前先读这个**：按可疑度排序的 12 处外部 API 风险点，每条带位置与退路 |

---

## 三条锁定决策在代码里的落点

改动这些地方前先读 `DEVELOPMENT_PLAN.md` §1。

| 决策 | 落点 | 怎么被守住 |
|---|---|---|
| **L7** 只许注册 F7–F12 六键（F11 仅作 F12 被占时的清屏降级，2026-08-21 修订） | `HotkeyWhitelist` + `GlobalHotkeyService.ToVirtualKey` | 两道闸：白名单过键名，映射表里只有那六个键。加键位必须同时改两处 |
| **L8** 送出键不是 Enter | `ControlWindow.xaml` | `AcceptsReturn=False` + **全窗口无任何 `IsDefault="True"` 按钮**。加一个默认按钮就等于把送出键绑回 Enter |
| **L4** 叠加层从不 Close | `OverlayWindow.OnClosing` | 拦掉一切关闭请求，只有 `AllowCloseOnShutdown()` 之后才放行 |

---

## M0 尖刺已实现的东西

- `OverlayWindow` — 副屏底部 30% 带状透明叠加层，半透明黑底 + 白字，250ms 淡入淡出
- `OverlayWindowStyler` — 四个必需扩展样式（`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`）+ 2 秒置顶心跳 + 按**物理像素**定位（跨 DPI 不算错）
- `NativeMethods` — 全项目唯一的 P/Invoke 声明处
- `ControlWindow` — 屏幕选择、投放按钮、以及**实时自检**：扩展样式位逐项核对、带状区域物理像素、淡入实测帧率、内存增量
- `AppLog` + 三通道全局异常捕获 — 直播中绝不弹未处理异常对话框
- `app.manifest` — PerMonitorV2 DPI 感知（L14）

M0 **刻意不含**：全局热键（M4）、单实例 Mutex（M5）、任何经文查询逻辑（M1）。

---

## M1 已实现的东西

| 类型 | 职责 |
|---|---|
| `TextNormalizer` | 两级归一化。整串只做 NFKC + 去空白（**保留 `-`**，否则 `诗23:1-3` 会变成 `诗23:13`）；书卷片段才做 SCHEMA.md 的完整规则（去 `. - _ ·` + 小写） |
| `ReferenceParser` | 惰性正则切分书卷/章/节，让「紧跟其后必须是章号+冒号」这个结构自己决定切点——所以 `约1:1` 落在约翰福音、`约翰1书3:16` 与 `1jn3:16` 都能切对 |
| `BibleRepository` | 只读 SQLite（`Mode=ReadOnly`，持久连接）。范围查询在 SQL 里 `GROUP BY merge_head` 去重 |
| `ContentBuilder` / `DisplayContent` | `VerseText[]` → `Page[]`，一个并节组一页；翻页**不循环**（M4 验收要求） |
| `BibleDatabaseException` | 库缺失/损坏/结构不符时的明确异常（M1 验收要求，不能是 `NullReferenceException`） |

测试跑在**真库**上（`bible_cuv.db` 由 csproj 复制到测试输出目录）。用假库测等于什么都没测——
§6 的每一条期望值都是在这个库上实测得来的。
