# Pulpit

教会直播用经文投屏工具。副屏以**透明叠加层**显示经文，**不干扰正在放映的 PPT**。

操作员在主屏输入 `约3:16`，按 F9，经文淡入到副屏 PPT 之上；按 F12 淡出。
全程不夺取焦点，不打断 WPS/PowerPoint 放映，现场观众与直播观众同时可见。

---

## 当前状态

| 里程碑 | 内容 | 状态 |
|---|---|---|
| — | 数据库 `bible_cuv.db`（和合本简体，31103 节，478 条书卷别名，81 组并节已解析） | ✅ 已就绪 |
| **M0** | 透明叠加层技术尖刺 | 🟡 **代码已交付，等待真实直播机人工验收** |
| **M1** | 数据层与引用解析（`Pulpit.Core` + 单测） | 🟡 **代码已交付，等待 Windows 上 `dotnet test`** |
| M2 | 叠加层渲染 | ⬜ |
| M3 | 控制窗口与输入（IME 安全） | ⬜ |
| M4 | 全局热键与分页 | ⬜ |
| M5 | 配置与健壮性 | ⬜ |
| M6 | 打包与实战彩排 | ⬜ |

**M0 是硬性 go/no-go 门。** 9 项验收标准见 [`docs/M0-验收清单.md`](docs/M0-验收清单.md)，
全部通过才能写业务代码。

---

## 构建

需要 **.NET 8 SDK**，**仅 Windows x64**。WPF 无法在 macOS/Linux 上构建或运行
（这是 L1 锁定决策的直接结果，不是疏漏；见 `DEVELOPMENT_PLAN.md` §9 明确的非目标）。

```powershell
dotnet build Pulpit.sln -c Debug
dotnet test tests\Pulpit.Core.Tests            # M1 验收：§6 全部用例
dotnet run --project src\Pulpit.App\Pulpit.App.csproj   # M0 尖刺
```

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
