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

| ID | 功能 |
|---|---|
| P1-1 | 英文译本（NIV 授权确认后，或改用 ESV/KJV/WEB），F10 原地换语言 |
| P1-2 | 历史记录：本次聚会已投过的引用，可点击复投 |
| P1-3 | 外观设置面板：字号上限、底色不透明度、带状高度与垂直位置、字体 |
| P1-4 | 原文/清洗版切换（`text_raw` ↔ `text_display`，DB 已就绪） |
| P1-5 | 连续引用 `约3:16;罗8:28` |

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

**必须在写任何业务代码之前完成，且必须在真实直播机上验证。**

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

- 带状容器 + `Viewbox`（`Stretch="Uniform"`）自适应字号
- 出处标签固定在正文下方右对齐，字号为正文的 40%
- 页码指示器（多页时才显示，如 `2/3`）
- 淡入淡出 250ms（`DoubleAnimation` on `Opacity`）
- `Clear()` 只做淡出，不 Hide、不 Close

验收标准：

- 最长节（太 23:13，119 字）单页显示完整，不溢出、不截断
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

- `RegisterHotKey` 挂在一个 message-only 窗口上（`HwndSource`）
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
- DB 随包，首次运行复制到 `%LOCALAPPDATA%\Pulpit\`
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
├─ CLAUDE.md
├─ src/
│  ├─ Pulpit.Core/                 # 无 UI 依赖，可单测
│  │  ├─ Data/
│  │  │  ├─ BibleRepository.cs
│  │  │  └─ Models.cs              # VerseRef / VerseText / Page
│  │  ├─ Parsing/
│  │  │  ├─ ReferenceParser.cs
│  │  │  └─ TextNormalizer.cs
│  │  ├─ Content/
│  │  │  ├─ DisplayContent.cs
│  │  │  └─ ContentBuilder.cs      # VerseText[] -> Page[]
│  │  └─ Config/AppConfig.cs
│  ├─ Pulpit.App/                  # WPF
│  │  ├─ App.xaml(.cs)
│  │  ├─ app.manifest              # PerMonitorV2
│  │  ├─ Views/
│  │  │  ├─ ControlWindow.xaml(.cs)
│  │  │  └─ OverlayWindow.xaml(.cs)
│  │  ├─ ViewModels/
│  │  ├─ Interop/
│  │  │  ├─ NativeMethods.cs       # P/Invoke 集中于此
│  │  │  ├─ OverlayWindowStyler.cs # 扩展样式 + 心跳
│  │  │  └─ GlobalHotkey.cs
│  │  └─ Assets/bible_cuv.db
└─ tests/
   └─ Pulpit.Core.Tests/
```

**约束**：所有 P/Invoke 声明必须集中在 `NativeMethods.cs`，其他文件不得直接写 `DllImport`。

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

**`TryParse` 的三态语义很重要**：解析失败但"看起来不像引用"时必须静默走自由文本，不能报错——否则操作员想投「欢迎新朋友」会被拦下。

---

## 6. 测试用例（M1 验收清单）

代理必须把下列全部写成单元测试并通过。这些是已在数据库上实测过的真实结果。

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

---

## 8. 风险登记

| 风险 | 影响 | 缓解 | 状态 |
|---|---|---|---|
| WPS 抢占 Z 序 | 字不可见 | 心跳置顶；M0 已用红块实测通过 | ✅ 已验证 |
| `AllowsTransparency` 关闭硬件加速导致淡入卡顿 | 观感差 | L3 限定为带状区域；M0 验收第 7 项实测 | ⚠️ M0 待验 |
| 全局热键被其他软件占用 | 键无反应且无提示 | 注册失败时状态栏明确告警 | 设计已覆盖 |
| 输入法 Enter 误触发 | 半截字上屏 | L8 + M3 验收 | 设计已覆盖 |
| 志愿者输错书卷 | 屏上出错 | 报错只在主屏；副屏保持原状 | 设计已覆盖 |
| 直播中程序崩溃 | 事故 | 全局异常捕获；叠加层与控制窗解耦，控制窗崩了叠加层内容仍在 | M5 |
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
