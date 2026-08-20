# Pulpit — 开发计划

> 教会直播用经文投屏工具。副屏以透明叠加层显示经文，不干扰正在放映的 PPT。
> 工作名 `Pulpit`，改名只需替换命名空间与 assembly name。

---

## 0. 一句话定义

操作员在主屏输入 `约3:16`，按 F9，经文淡入到副屏 PPT 之上；按 F12 淡出。
全程不夺取焦点，不打断 WPS/PowerPoint 放映，现场观众与直播观众同时可见。

---

## 1. 锁定决策（Locked Decisions）

代理不得擅自更改以下任何一条。若实现中发现某条不可行，**停止并报告**，不要自行替换方案。

| # | 决策 | 理由 |
|---|---|---|
| L1 | Windows 桌面应用，.NET 8 + WPF，C# 12 | 需要 Win32 窗口样式与全局热键；WPF 的 Viewbox 排版最省事 |
| L2 | 双窗口：`ControlWindow`（主屏）+ `OverlayWindow`（副屏） | 控制与显示分离 |
| L3 | `OverlayWindow` 只覆盖屏幕**下三分之一带状区域**，不全屏 | WPF `AllowsTransparency=true` 会关闭该窗口的硬件加速；全屏 1080p 软件渲染会导致淡入淡出卡顿。带状区域约 1920×324，软件渲染无压力 |
| L4 | `OverlayWindow` 生命周期与进程一致，**从不 Close** | 反复创建窗口会引发 Z 序抖动。"清屏"= 内容置空 + 透明，窗口仍在 |
| L5 | 扩展样式必须为 `WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE` | 依次实现：透明、鼠标穿透、不进 Alt+Tab、永不获取焦点。`NOACTIVATE` 缺失会导致放映软件误判失焦而退出全屏 |
| L6 | 2 秒心跳 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE\|SWP_NOMOVE\|SWP_NOSIZE)` | 对抗放映软件的 Z 序抢占。已实测 WPS 不独占全屏，但心跳成本近零，必须保留 |
| L7 | 全局热键仅注册 **F7 F8 F9 F10 F12** | `RegisterHotKey` 是全局独占，注册即从放映软件手里夺走该键。方向键、PgUp/PgDn、Esc、B、F5 **一律不得注册**，它们属于 PPT |
| L8 | 送出键**不是 Enter** | 中文输入法用 Enter 确认候选词，会导致半截内容上屏。Enter 在输入框内不触发任何投放行为 |
| L9 | 单语上屏，不做中英双语同屏 | 双语会让字号减半，后排与直播画面都看不清 |
| L10 | 超长内容按**并节组**分页，一页一节，字号恒定 | 自动缩小字号会造成副屏观感忽大忽小 |
| L11 | 不做预备区 / 播放队列 | 用户明确舍弃 |
| L12 | 数据库为交付的 `bible_cuv.db`，只读打开 | schema 见 `SCHEMA.md` |
| L13 | v1 仅中文；英文译本 v1.1 补 | 但 F10 代码路径 v1 必须存在（见 P0-9） |
| L14 | 应用清单声明 `PerMonitorV2` DPI 感知 | 防止换屏后副屏字号被系统拉伸 |
| L15 | 单实例运行（Mutex） | 两份实例会争抢全局热键，第二份注册失败且无提示 |

---

## 2. 功能分级

### P0 — v1 必须完成，缺一不可

| ID | 功能 | 说明 |
|---|---|---|
| P0-1 | 副屏透明叠加显示 | 下三分之一带状；半透明黑底 + 白字；PPT 正常可见 |
| P0-2 | 经文引用解析 | `约3:16` `约3:16-18` `yh3:16` `john3:16` `约翰福音3:16`；全角输入、空格容错 |
| P0-3 | 经文查询与并节解析 | `民1:21` 必须出文本，出处显示「民数记 1:20-21」 |
| P0-4 | 自由文本模式 | 输入不匹配引用格式时，原样上屏。中文输入法正常可用 |
| P0-5 | F9 送出 | 淡入 250ms |
| P0-6 | F12 清屏 | 淡出 250ms，窗口保留 |
| P0-7 | F7 / F8 翻页 | 多节内容按并节组分页，页码指示器 |
| P0-8 | 出处标签 | 屏上显示「约翰福音 3:16」，位置与正文分离 |
| P0-9 | F10 键位占位 | v1 无英文库时弹提示「英文译本未安装」，**不得让键位缺失**——志愿者的肌肉记忆要在 v1 就建立 |
| P0-10 | 输入校验与友好报错 | `约3:99` → 「约翰福音 3 章只有 36 节」；报错只出现在主屏，**绝不上副屏** |
| P0-11 | 主屏预览 | 控制窗口实时显示副屏当前内容 |
| P0-12 | 副屏选择与记忆 | 多屏时可指定目标屏；写入配置 |
| P0-13 | 显示器变更容错 | `DisplaySettingsChanged` 时重新定位；副屏拔出不崩溃 |
| P0-14 | 单实例 | Mutex + 已运行时提示 |

### P1 — v1.1

| ID | 功能 | 状态（2026-08-20） |
|---|---|---|
| P1-1 | 英文译本（NIV 授权确认后，或改用 ESV/KJV/WEB），F10 原地换语言 | ⏸ **被授权阻塞**，见 §8。F10 键位与提示已在 v1 就位（P0-9） |
| P1-2 | 历史记录：本次聚会已投过的引用，可点击复投 | ✅ 代码已交付 |
| P1-3 | 外观设置面板：字号上限、底色不透明度、带状高度与垂直位置、字体 | ⬜ |
| P1-4 | 原文/清洗版切换（`text_raw` ↔ `text_display`，DB 已就绪） | ✅ 代码已交付 |
| P1-5 | 连续引用 `约3:16;罗8:28` | ✅ 代码已交付 |

**P1-2 的两个取舍**（原文未定义，此处记明）：

- **只记经文，不记自由文本。** 本条写的是「已投过的**引用**」；自由文本多是一次性通告
  （「今晚 7:30 祷告会」），混进来会把真正想复投的经文淹掉。
- **去重按解析出来的引用，不按输入串。** `约3:16` 与 `约翰福音3:16` 是同一处经文，
  列表里不该占两行——只比较输入串分不出来。连续引用的**顺序**参与去重（页序不同即两条）。
- 复投走**双击**而非单击：单击即投在直播中太危险，操作员想选中看一眼就会把内容甩上副屏。
- 只在内存里，刻意不落盘。「本次聚会」就是本次进程。

**P1-5 的分隔符**：只认 `;`（NFKC 会把全角 `；` 折过来），**刻意不认 `,` `，` `、`**——
中文正文里逗号顿号满地跑，当分隔符会让大量自由文本被误判成引用格式。
任何一段不像引用则**整串**当自由文本，不做混合投放（可预期比聪明重要）。

### P2 — 以后再说

| ID | 功能 |
|---|---|
| P2-1 | 关键词反查（「神爱世人」→ 出处），需补 FTS5 |
| P2-2 | 多行歌词模式 |
| P2-3 | OBS WebSocket 联动（直播画面与现场投影分别控制） |
| P2-4 | 副屏第二区域（角标、聚会主题常驻） |

---

## 3. 里程碑

### M0 — 环境验证与技术尖刺 ⚠️ 硬性 go/no-go 门

> **状态（2026-08-20 修订）：尖刺已交付，9 项真机验收经操作员决定暂时搁置，开发继续往下走。**
> 门没有撤销，只是延后——随时可以补做，清单见 `docs/M0-验收清单.md`。
> 搁置带来的唯一悬空风险是验收第 7 项（淡入帧率），缓解见 §8 风险登记。

原定要求：**必须在写任何业务代码之前完成，且必须在真实直播机上验证。**

产出：一个最小 WPF 程序，副屏下三分之一显示一条固定文字「测试」，白字半透明黑底，2 秒心跳置顶。

验收标准（**全部通过才能进 M1**）：

1. WPS 全屏放映时，文字可见
2. 连续翻页 20 次，文字始终可见
3. 播放带动画的页面，文字不闪烁、不消失
4. 鼠标点击文字区域，事件落到 WPS 而非本程序（穿透生效）
5. Alt+Tab 列表中不出现本程序
6. 主屏切换到记事本并打字，WPS **未退出全屏**，文字仍在
7. 淡入淡出 250ms 视觉流畅，无卡顿撕裂
8. OBS 显示器采集能抓到文字
9. 连续运行 60 分钟，内存无明显增长

**任一项失败即停止，报告具体现象，等待人工决策。** 特别是第 7 项——若软件渲染导致淡入卡顿，需回报实测帧感，可能要降级为无动画直切。

### M1 — 数据层与引用解析

产出：`Pulpit.Core` 类库 + 单元测试。无 UI。

- `BibleRepository`（只读 SQLite，连接串带 `Mode=ReadOnly`）
- `ReferenceParser`
- 归一化：NFKC → 去 `空格 . - _ ·` → 小写

验收标准：

- 第 6 节全部测试用例通过
- 冷启动首次查询 < 50ms
- 数据库文件缺失/损坏时抛出明确异常，不是 `NullReferenceException`

### M2 — 叠加层渲染

产出：`OverlayWindow` 完整实现，由测试用的按钮驱动。

- 带状容器 + **二分搜索最大可容字号**自适应排版（**2026-08-20 修订：原定 `Viewbox`，实测不可用**）
- 出处标签固定在正文下方右对齐，字号为正文的 40%
- 页码指示器（多页时才显示，如 `2/3`）
- 淡入淡出 250ms（`DoubleAnimation` on `Opacity`）
- `Clear()` 只做淡出，不 Hide、不 Close

> **为什么不是 `Viewbox`（2026-08-20 修订）**
>
> `Viewbox` 给子元素无限宽度，`TextWrapping` 永不生效；改成给 `TextBlock` 固定宽度再让
> `Viewbox` 等比缩，则换行点是在 `MaxFontSize` 下算出来的，缩小后行数偏多、字号偏小。
> 实测最坏情况（申 30:9-10，106 字）在 1920×324 带子里：`Viewbox` 方案约 **35px**，
> 按最优换行可到约 **46px**。30% 的字号差，副屏后排能不能看清就在这里。
>
> 改为：二分搜索能把正文完整放进正文区的最大字号，上限 `MaxFontSize`、下限 `MinFontSize`，
> 用 `FormattedText` 离线测量（不进视觉树，无布局重入风险），行高系数测量与呈现共用同一常量。
>
> 连带约束：**页脚行高必须固定预留**（`MaxFontSize × LabelScale × 1.6`），不能用 `Auto`。
> 出处标签字号 = 正文字号 × 40%，而正文字号又由「总高 − 页脚高」算出来，`Auto` 会形成循环。

验收标准：

- **最长节（申 30:9-10，`text_display` 106 字，且本身是并节组）单页显示完整，不溢出、不截断**
  （**2026-08-20 修订**：原写「太 23:13，119 字」。119 字是 `text_raw` 的长度——那一节含
  `（有古卷加：14…）`，清洗后的 `text_display` 只有 60 字，拿它验排版验不出问题。
  全库 `text_display` 最长的是申 30:9 / 30:10 / 耶 33:11，均 106 字；其中申 30:9-10 是并节组，
  最长文本 + 强制单页，正好是最坏情况。）
- 最短内容（2 字）字号被 `MaxFontSize` 限制，不会撑满整条带
- 反复 Show/Clear 200 次，窗口句柄不变，Z 序不丢

### M3 — 控制窗口与输入

产出：主屏 UI。

- 输入框 + 模式指示（`经文` / `自由文本`，实时判定并显示）
- 预览区：所见即副屏
- 状态栏：目标屏、当前页、DB 版本
- **IME 安全**：Enter 在输入框内仅换行/无操作；监听 `TextCompositionManager` 确认组合态

验收标准：

- 用微软拼音输入「神爱世人」，全程按 Enter 选词，副屏无任何变化
- 输入 `约3:16` 时模式指示为「经文」，输入 `欢迎新朋友` 时为「自由文本」
- 报错信息只出现在控制窗口，副屏无变化

### M4 — 全局热键与分页

产出：热键子系统 + 分页逻辑。

- `RegisterHotKey` 挂在本程序自建的隐藏窗口上（`HwndSource`，0×0，带 `WS_EX_TOOLWINDOW`）
  （**2026-08-20 修订**：原写「message-only 窗口」。改用普通隐藏窗口是为了走
  `HwndSource` 那个久经使用的七参构造，避开 `HwndSourceParameters.ParentWindow=HWND_MESSAGE`
  与 `WindowStyle` 的组合细节。效果一致：不参与绘制、不进 Alt+Tab、生命周期自持。
  关键点不变——**不能挂在控制窗口上**，控制窗口一旦关闭或重建，热键就跟着失效。）
- 注册失败（键位被占）时在状态栏明确告警，列出失败的键
- 分页：范围查询按 `merge_head` 去重，一组一页

验收标准：

- 焦点在 WPS 上时，F9/F12 生效
- 焦点在 WPS 上时，**方向键与 PgUp/PgDn 仍能翻 PPT**（证明未误注册）
- `诗23:1-6` 分 6 页，F8 逐页前进，末页再按 F8 无动作（不循环）
- `诗8:6-8` 只出 1 页，出处显示「诗篇 8:6-8」

### M5 — 配置与健壮性

- 配置 JSON 存 `%LOCALAPPDATA%\Pulpit\config.json`
- 单实例 Mutex
- `DisplaySettingsChanged` 重定位
- 全局异常捕获 → 写日志文件，**绝不弹未处理异常对话框**（直播中弹窗是事故）

验收标准：

- 运行中拔掉副屏 HDMI 再插回，程序不崩，叠加层自动回到副屏
- 强制抛异常，程序继续运行，日志有记录，副屏无变化
- 第二次启动被拒绝并提示

### M6 — 打包与实战彩排

- 单文件发布（`PublishSingleFile`，self-contained，win-x64）
- DB 随包（**嵌入为程序集资源**，`LogicalName=Pulpit.App.Assets.bible_cuv.db`），
  首次运行解出到 `%LOCALAPPDATA%\Pulpit\`
  （**2026-08-20 补充**：必须嵌入而不能放在 exe 旁边——单文件发布时 exe 旁边就没有别的文件了，
  而 SQLite 需要真实文件路径、不能从内存流打开。是否重新解出按**文件长度**比对。）
- 快速上手卡（一页 A4，给志愿者）

验收标准：

- 在一台**未装 .NET 运行时**的干净 Windows 上双击可用
- 完成一次完整主日彩排：真实 PPT + 真实 OBS + 真实投影，连续 90 分钟
- 志愿者在无人指导下完成 10 次投放，0 次误操作影响 PPT

---

## 4. 项目结构

```
Pulpit/
├─ Pulpit.sln
├─ Directory.Build.props           # LangVersion / Nullable / 关闭隐式 using
├─ publish.cmd                     # 单文件发布（先跑测试再打包）
├─ CLAUDE.md · DEVELOPMENT_PLAN.md · SCHEMA.md · README.md
├─ bible_cuv.db                    # 唯一一份；App 以嵌入资源方式引用它
├─ docs/
│  ├─ M0-验收清单.md
│  └─ 快速上手卡.md                 # 一页 A4，给志愿者
├─ src/
│  ├─ Pulpit.Core/                 # 纯 net8.0，无 UI 依赖，可单测
│  │  ├─ Data/
│  │  │  ├─ Models.cs              # VerseRef / VerseText
│  │  │  ├─ IBibleRepository.cs
│  │  │  ├─ BibleRepository.cs
│  │  │  └─ BibleDatabaseException.cs
│  │  ├─ Parsing/
│  │  │  ├─ TextNormalizer.cs
│  │  │  ├─ IReferenceParser.cs
│  │  │  └─ ReferenceParser.cs
│  │  ├─ Content/
│  │  │  ├─ DisplayContent.cs      # ContentKind / Page / DisplayContent
│  │  │  └─ ContentBuilder.cs      # VerseText[] -> Page[]
│  │  └─ Config/
│  │     ├─ AppConfig.cs           # Band / Typography / Animation / Text
│  │     ├─ HotkeyConfig.cs        # + HotkeyWhitelist（L7 的机器化）
│  │     └─ ConfigStore.cs         # config.json 读写，永不抛异常
│  ├─ Pulpit.App/                  # WPF，net8.0-windows
│  │  ├─ App.xaml(.cs)             # 组装 + 全局异常 + 热键分派 + 显示器变更
│  │  ├─ app.manifest              # PerMonitorV2
│  │  ├─ SingleInstanceGuard.cs    # L15
│  │  ├─ Diagnostics/AppLog.cs     # 写 %LOCALAPPDATA%\Pulpit\logs\
│  │  ├─ Data/DatabaseProvisioner.cs   # 嵌入资源 -> %LOCALAPPDATA%
│  │  ├─ Views/
│  │  │  ├─ ControlWindow.xaml(.cs)
│  │  │  ├─ OverlayWindow.xaml(.cs)
│  │  │  ├─ IOverlayController.cs  # 需要 Screen，故留在 App 层
│  │  │  └─ OverlayTheme.cs        # AppConfig -> WPF 类型
│  │  └─ Interop/
│  │     ├─ NativeMethods.cs       # P/Invoke 集中于此
│  │     ├─ OverlayWindowStyler.cs # 扩展样式 + 心跳 + 物理像素定位
│  │     └─ GlobalHotkey.cs
└─ tests/
   └─ Pulpit.Core.Tests/           # xUnit，跑在真库上
```

**约束**：所有 P/Invoke 声明必须集中在 `NativeMethods.cs`，其他文件不得直接写 `DllImport`。

**与原计划的差异（2026-08-20 修订）**：

- **没有 `ViewModels/`**。两个窗口都用 code-behind。CLAUDE.md 规定未经确认不得添加 MVVM 框架，
  而手写 `INotifyPropertyChanged` 样板在这个体量上只是纯负担——控制窗口的状态刷新是
  一个 1 秒轮询加几个事件回调，不值得为它引入一层。
- 新增 `Diagnostics/`、`Data/`（App 侧）、`SingleInstanceGuard.cs`、`Views/OverlayTheme.cs`、
  `Views/IOverlayController.cs` —— 分别对应全局异常日志、经文库就位、L15、配置到 WPF 类型的映射、
  以及 §5 那个需要 `Screen` 因而必须留在 App 层的接口。
- `Assets/bible_cuv.db` **不作为文件存在**。仓库根只放一份 `bible_cuv.db`，csproj 用
  `<EmbeddedResource>` 嵌进程序集，运行时解出到 `%LOCALAPPDATA%\Pulpit\`（见 §3 M6）。

---

## 5. 关键契约

```csharp
// ---- 模型 ----
public sealed record VerseRef(int BookId, int Chapter, int Verse, int? EndVerse);

public sealed record VerseText(
    int    BookId,
    string BookNameZh,
    int    Chapter,
    int    MergeHead,
    int    MergeLast,
    string TextDisplay,
    string TextRaw)
{
    /// 「约翰福音 3:16」或「民数记 1:20-21」
    public string Label => MergeLast != MergeHead
        ? $"{BookNameZh} {Chapter}:{MergeHead}-{MergeLast}"
        : $"{BookNameZh} {Chapter}:{MergeHead}";
}

public sealed record Page(string Label, string Body);

public enum ContentKind { Scripture, FreeText }

public sealed class DisplayContent
{
    public ContentKind Kind { get; init; }
    public IReadOnlyList<Page> Pages { get; init; } = [];
    public int Index { get; set; }
    public VerseRef? Source { get; init; }   // 供 F10 换语言时重查
}

// ---- 解析 ----
public interface IReferenceParser
{
    /// 返回 false 时 error 为空表示"不是引用格式，走自由文本"；
    /// error 非空表示"是引用格式但有错"（如书卷未知、章节越界），应向操作员报错。
    bool TryParse(string input, out VerseRef reference, out string? error);
}

// ---- 数据 ----
public interface IBibleRepository
{
    int? ResolveBook(string normalizedAlias);
    (int Chapters, string NameZh)? GetBookInfo(int bookId);
    int? GetVerseCount(int bookId, int chapter);
    IReadOnlyList<VerseText> Lookup(VerseRef reference, int transId = 1);
}

// ---- 叠加层 ----
public interface IOverlayController
{
    void Show(DisplayContent content);
    void Clear();
    bool NextPage();      // 已在末页返回 false
    bool PrevPage();
    void MoveToScreen(System.Windows.Forms.Screen screen);
}
```

**`Lookup` 实现要点**：范围查询后按 `merge_head` 去重，否则 `民1:20-21` 会返回两条相同文本。
（实现落点：去重发生在 SQL 里的 `GROUP BY v.merge_head`，不在 C# 侧再去一遍。）

**`TryParse` 的三态语义很重要**：解析失败但"看起来不像引用"时必须静默走自由文本，不能报错——否则操作员想投「欢迎新朋友」会被拦下。

---

### 5.1 三态语义的一个已知缺口（2026-08-20 新增）

`abc3:16` → 报「未知书卷「abc」」是 §6 明确要求的。但 **`晚上7:30` 的结构与它完全相同**
（书卷片段 + 章号 + 冒号 + 节号，且整串匹配完），因此也会得到「未知书卷「晚上」」，
操作员想把这行字当自由文本投出去就会被拦下——与 P0-4「输入不匹配引用格式时原样上屏」相抵。

`今晚 7:30 祷告会` **不受影响**（尾部有非数字，整串匹配不上，静默走自由文本），§6 那条用例是绿的。
受影响的只有「中文常用词 + 数字:数字」且后面不带任何非数字字符的输入。

**当前取舍：按本节字面语义实现，即报错。** 依据是本项目自己的价值排序——§8 风险登记里
「志愿者输错书卷 → 报错只在主屏，副屏保持原状」，报错是安全的一侧；而错误文本上副屏才是事故。
代价是操作员遇到这类输入需要改写（加个字、或用 `晚上 7:30 祷告会`）。

**若要改成静默走自由文本**，改动点只有一处：`ReferenceParser.TryParse` 中
「书卷片段解析不出书卷」那个分支。这是一个待定的产品决定，不是缺陷。

---

### 5.2 契约的实际实现差异（2026-08-20 修订）

以下是实现时对本节签名做的调整，都是**加强而非削弱**：

| 契约 | 实际 | 原因 |
|---|---|---|
| `bool TryParse(string input, out VerseRef reference, out string? error)` | `TryParse(string? input, [MaybeNullWhen(false)] out VerseRef reference, out string? error)` | 空输入是合法的自由文本路径；`MaybeNullWhen` 让「失败时 reference 为 null」这条契约被编译器检查 |
| `DisplayContent` 只有 `Kind / Pages / Index / Source` | 另加 `PageCount` `HasMultiplePages` `IsEmpty` `Current` `PageIndicator` `TryNext()` `TryPrevious()` | 「末页再按 F8 无动作、不循环」（M4 验收）是可单测的纯逻辑，放在 Core 才测得到；放在 `IOverlayController` 实现里就只能靠人工验 |
| `IOverlayController` 未指定所属项目 | 放在 **`Pulpit.App`** | 它的 `MoveToScreen` 需要 `System.Windows.Forms.Screen`，而 CLAUDE.md 规定 `Screen` 仅在 App 层使用 |
| `IBibleRepository` 四个成员 | 未改动；`BibleRepository` 另加 `SchemaVersion` / `DatabasePath` 两个具体类属性 | 状态栏要显示 DB 版本，但没必要把它塞进接口 |
| — | 新增 `HotkeyWhitelist`（`IsAllowed` / `Canonicalize` / `All` / `AllowedList`） | L7 的机器化。配置文件不是可信输入，白名单必须在 Core 里且可单测 |

---

## 6. 测试用例（M1 验收清单）

代理必须把下列全部写成单元测试并通过。这些是已在数据库上实测过的真实结果。

> **核实结论（2026-08-20）：本节全部 34 条已在 `bible_cuv.db` 上逐条重跑，无一条有误，未作改动。**
> 同批核实的其他事实也都正确：31103 节 / 478 条别名 / 1189 行 `chapter_info` / 66 书卷；
> 并节 **81 组**、涉及 163 节、被合并掉 **82** 个节号。
>
> 顺带查明的两个「最坏情况」，已用于 M2/M4 的验收与样例按钮：
>
> | 最坏情况 | 值 | 用途 |
> |---|---|---|
> | `text_display` 最长 | **申 30:9-10 / 耶 33:11，106 字**（申 30:9-10 还是并节组 → 最长文本 + 强制单页） | M2 排版验收 |
> | `text_raw` 最长 | 太 23:13 / 23:14，119 字（清洗后仅 60 字） | 仅供参考，**不用于排版验收** |
> | 单章最多节 | **诗篇 119，176 节** | 范围分页的上界（`诗119:1-176` → 176 页） |

### 解析成功

| 输入 | 期望出处 | 考点 |
|---|---|---|
| `约3:16` | 约翰福音 3:16 | 基本 |
| `约翰福音3:16` | 约翰福音 3:16 | 全称 |
| `yh3:16` | 约翰福音 3:16 | 拼音码 |
| `john3:16` | 约翰福音 3:16 | 英文全称 |
| `jhn3:16` | 约翰福音 3:16 | 英文缩写 |
| `约３：１６` | 约翰福音 3:16 | **全角数字与冒号** |
| `罗 8 : 28` | 罗马书 8:28 | 空格容错 |
| `约1:1` | **约翰福音 1:1** | ⚠️ 绝不能解析成约翰壹书 |
| `约一3:16` | 约翰壹书 3:16 | 数字书卷 |
| `约翰一书3:16` | 约翰壹书 3:16 | 同上全称 |
| `1jn3:16` | 约翰壹书 3:16 | 英文数字前缀 |
| `门1:6` | 腓利门书 1:6 | 门/腓 不混 |
| `该2:9` | 哈该书 2:9 | 单字简称 |
| `撒上17:45` | 撒母耳记上 17:45 | 上下卷 |
| `民1:21` | **民数记 1:20-21** | ⚠️ 并节，文本非空 |
| `诗8:7` | **诗篇 8:6-8** | ⚠️ 三节并一 |
| `约5:4` | 约翰福音 5:3-4 | 古卷异文并节 |
| `诗23:1-3` | 3 页 | 范围分页 |
| `民1:20-21` | **1 页** | ⚠️ 并节去重，不得出 2 页 |

### 解析报错（应提示，不上屏）

| 输入 | 期望提示 |
|---|---|
| `约3:99` | 约翰福音 3 章只有 36 节 |
| `约99:1` | 约翰福音只有 21 章 |
| `abc3:16` | 未知书卷「abc」 |

### 走自由文本（静默，不报错）

`欢迎新朋友` / `今晚 7:30 祷告会` / `` (空) / `2026年感恩节`

> 注意 `今晚 7:30 祷告会` 含冒号数字，必须不被误判为引用。

### 文本清洗回归

| 引用 | 期望 `TextDisplay` 特征 |
|---|---|
| `创1:1` | 以「起初，神」开头，**神字前无全角空格** |
| `约3:15` | 不含「或译」 |
| `诗3:2` | 不含「细拉」 |
| `太18:10` | 不含「有古卷加」 |
| `歌2:10` | 不含「〔新郎〕」 |
| `出16:35` | 不以「（」结尾 |
| `创48:14` | **保留**「（以法莲乃是次子）」——经文插入语不剥 |

---

## 7. 配置文件

`%LOCALAPPDATA%\Pulpit\config.json`

```json
{
  "targetScreenDeviceName": "\\\\.\\DISPLAY2",
  "band": {
    "heightPercent": 0.30,
    "verticalAnchor": "bottom",
    "backgroundOpacity": 0.72,
    "paddingPercent": 0.06
  },
  "typography": {
    "fontFamily": "Microsoft YaHei UI",
    "fontWeight": "SemiBold",
    "maxFontSize": 96,
    "minFontSize": 24,
    "labelScale": 0.40,
    "foreground": "#FFFFFFFF"
  },
  "animation": { "fadeMs": 250 },
  "hotkeys": {
    "sendZh": "F9", "sendEn": "F10",
    "prevPage": "F7", "nextPage": "F8",
    "clear": "F12"
  },
  "text": { "useRawText": false }
}
```

热键在 v1 从配置读取但**不提供 UI 修改**（P1-3 再做）。配置缺失或字段非法时用内置默认值，并写日志，不弹窗。

**2026-08-20 修订/补充**：

- 新增 `typography.minFontSize`（默认 24）。二分字号需要一个下限——低于它即使溢出也不再缩，
  因为缩到看不见等于没投。连带行为：真放不下时按下限渲染并**在日志留痕**，尾部被裁切。
- **`animation.fadeMs: 0` 表示无动画直切**，不是「立即完成的动画」。这是 M0 验收第 7 项的
  逃生口：`AllowsTransparency=true` 关闭了叠加层窗口的硬件加速，若软件渲染下淡入帧率过低，
  把这里设成 0 即可退化为直切，**不需要改任何代码**。
- `hotkeys.*` 的取值会过白名单（只许 `F7 F8 F9 F10 F12`）。写入白名单外的键位——尤其是
  方向键、`PageUp`/`PageDown`、`Space`、`Enter`、`Escape`——会被**拒绝并退回默认值**，
  同时写日志。配置文件不是可信输入（L7）。

---

## 8. 风险登记

状态列于 2026-08-20 更新。

| 风险 | 影响 | 缓解 | 状态 |
|---|---|---|---|
| **M0 九项真机验收被搁置** | 整套窗口行为假设未在真实直播机上证实 | 门未撤销，清单在 `docs/M0-验收清单.md`；上线前必须补做 | 🔴 **当前最大悬空项** |
| **全部 WPF 代码未经编译** | 首次构建可能大量报错 | Core 侧可在任意平台 `dotnet test`；App 侧只能在 Windows 上验 | 🔴 待首次 `dotnet build` |
| WPS 抢占 Z 序 | 字不可见 | 心跳置顶；M0 已用红块实测通过 | ✅ 已验证 |
| `AllowsTransparency` 关闭硬件加速导致淡入卡顿 | 观感差 | L3 限定为带状区域；**逃生口已实现**：`animation.fadeMs=0` 即无动画直切，改配置不改代码；控制窗口直接显示实测帧率 | ⚠️ 缓解就位，实测待 M0 第 7 项 |
| 全局热键被其他软件占用 | 键无反应且无提示 | 注册失败时状态栏**点名到具体键位**告警 | ✅ 已实现 |
| **误注册 PPT 按键**（本项目最严重的回归） | 操作员再也翻不了 PPT | 两道闸：`HotkeyWhitelist` 过键名 + `ToVirtualKey` 映射表只有那五个键；配置文件视为不可信输入；27 条单测盯着 | ✅ 已实现 |
| 输入法 Enter 误触发 | 半截字上屏 | `AcceptsReturn=False` + **全窗口无任何 `IsDefault` 按钮** + 组合态跟踪（组合中拒绝 F9 送出） | ✅ 已实现，行为待真机验 |
| 志愿者输错书卷 | 屏上出错 | 报错只在主屏；副屏保持原状。**代价见 §5.1**：结构像引用的中文短语也会被拦下 | ✅ 已实现 |
| 直播中程序崩溃 | 事故 | 三通道全局异常捕获 → 写日志 → 继续运行；叠加层与控制窗解耦，控制窗崩了叠加层内容仍在；诊断区有「强制抛异常」按钮可现场自证 | ✅ 已实现 |
| `FormattedText` 与 `TextBlock` 行高模型不完全一致 | 字号算偏，可能差一行 | 两侧共用同一行高常量 + `LineStackingStrategy=BlockLineHeight` 强行对齐 | ⚠️ 理论一致，待真机核 |
| 单文件发布时 SQLite 原生库加载失败 | 经文查询全废 | `IncludeNativeLibrariesForSelfExtract=true` | ⚠️ 待首次发布验 |
| NIV 1984 授权 | 法务风险 | v1 不含英文；上线前由 Crossmap 确认，或改用 ESV/KJV/WEB | ⏸ 阻塞 P1-1 |

---

## 9. 明确的非目标

- 不播放 PPT，不接管 PPT，不与 PowerPoint/WPS 做任何进程间通信
- 不做歌词/敬拜投影（那是 FreeShow / OpenLP 的领域）
- 不联网，不自动更新，不做遥测
- 不支持 macOS / Linux
- v1 不做主题、不做多套外观预设

---

## 10. 给代理的执行顺序

1. 读 `SCHEMA.md`，理解 `merge_head` / `merge_last` 语义
2. 建解决方案骨架 → **先做 M0 尖刺并等待人工验收结果**
3. M0 通过后：M1 → M2 → M3 → M4 → M5 → M6，逐个里程碑提交
4. 每个里程碑结束时输出：完成项、验收自测结果、未决问题
5. 遇到与锁定决策冲突的情况：停止，报告，不自行变通

### 实际执行记录（2026-08-20）

第 2、3 步**没有按原定顺序走**：M0 尖刺交付后，操作员决定暂时搁置九项真机验收，
让开发先往下推进。因此 M1–M6 是在 M0 门未过的情况下完成的。

| 提交 | 内容 | 编译/测试状态 |
|---|---|---|
| M0 | 透明叠加层尖刺 + 解决方案骨架 | 未编译 |
| M1 | `Pulpit.Core` 数据层与引用解析 | 语义已用 Python 原型在真库上验证（§6 全 34 条绿）；C# 未编译 |
| M2 | 叠加层渲染 | 未编译 |
| M3 | 控制窗口与输入 | 未编译 |
| M4 | 全局热键 | 未编译 |
| M5 | 配置与健壮性 | `ConfigStore` 有单测；未编译 |
| M6 | 打包 + 志愿者上手卡 | 未编译，未发布 |

开发在 macOS 上进行，而 WPF（`net8.0-windows`）在 macOS 上**连编译都不支持**，
`Pulpit.App` 因此完全没有经过编译器检查。`Pulpit.Core` 与其 75 个测试方法是纯 `net8.0`，
在任意平台都能 `dotnet test`。

**补做顺序建议**：`dotnet test`（Core 转绿）→ `dotnet build`（App 首次编译）→
M0 九项真机验收 → M2/M3/M4 各自的真机验收项 → M6 彩排。

---

## 11. 计划书修订记录

### 2026-08-20 —— 首轮实现后的事实校正

计划书是本项目的契约，改它必须留痕。本轮改动全部源于**在真库上核实**或**实现中发现原方案不可行**，
**§1 锁定决策未作任何改动**（核实后未发现事实错误）。

| # | 位置 | 原文 | 改为 | 依据 |
|---|---|---|---|---|
| 1 | §3 M2 验收 | 最长节（太 23:13，**119 字**） | 最长节（申 30:9-10，**106 字**） | 119 字是 `text_raw` 长度；M2 渲染 `text_display`，太 23:13 清洗后只有 60 字，验不出溢出。全库 `text_display` 最长为 106 字，且申 30:9-10 本身是并节组（最长文本 + 强制单页 = 最坏情况） |
| 2 | §3 M2 实现 | 带状容器 + `Viewbox`（`Stretch="Uniform"`）自适应字号 | 二分搜索最大可容字号，`FormattedText` 离线测量 | `Viewbox` 给子元素无限宽度，`TextWrapping` 永不生效；退而给固定宽度则换行点在 `MaxFontSize` 下算出，缩小后字号偏小。最坏情况实测 35px vs 46px，差 30% |
| 3 | §3 M2 | （无） | 补「页脚行高必须固定预留，不能用 `Auto`」 | 出处标签字号 = 正文字号 × 40%，正文字号又由「总高 − 页脚高」算出，`Auto` 形成循环 |
| 4 | §3 M4 | 挂在 message-only 窗口上 | 挂在自建 0×0 隐藏窗口（带 `WS_EX_TOOLWINDOW`） | 避开 `HwndSourceParameters.ParentWindow=HWND_MESSAGE` 与 `WindowStyle` 的组合细节；效果一致 |
| 5 | §3 M6 | DB 随包，首次运行复制 | DB **嵌入为程序集资源**，首次运行解出 | 单文件发布时 exe 旁边没有别的文件，而 SQLite 需要真实文件路径 |
| 6 | §3 M0 | （无状态标注） | 标注九项真机验收已搁置 | 操作员 2026-08-20 决定 |
| 7 | §4 | 含 `ViewModels/`、`Assets/bible_cuv.db` | 改为实际结构，并列出差异与原因 | 未使用 MVVM（CLAUDE.md 禁止未经确认引入 MVVM 框架）；DB 改嵌入资源 |
| 8 | §5 | （无） | 新增 §5.1 三态语义的已知缺口、§5.2 契约实现差异表 | `晚上7:30` 与 `abc3:16` 结构同形，会被报错拦下——这是原契约未定义的情形，非缺陷，待产品决定 |
| 9 | §6 | （无） | 标注 34 条全部核实无误，补三个「最坏情况」数据 | 逐条在 `bible_cuv.db` 上重跑 |
| 10 | §7 | 缺 `minFontSize`；`fadeMs` 语义未定义 0 | 补字段；明确 `fadeMs=0` 为无动画直切；补热键白名单行为 | 二分字号需要下限；`fadeMs=0` 是 M0 第 7 项的逃生口 |
| 11 | §8 | 状态列停留在设计期 | 全表更新，新增 5 行风险 | M0 搁置与「全部 WPF 代码未编译」是当前两个最大悬空项，必须写进风险登记 |
| 12 | §10 | 仅有原定顺序 | 补「实际执行记录」与补做顺序建议 | 实际未按原定顺序执行，需留痕 |

**核实后确认无误、未作改动的事实**：§1 全部 15 条锁定决策 · §2 功能分级 · §6 全部 34 条测试用例 ·
并节 81 组 / 82 个被合并节号 · 31103 节 / 478 条别名 / 1189 行 `chapter_info` / 66 书卷 ·
L3 的「带状区域约 1920×324」（1080 × 0.30 = 324）。
