# CLAUDE.md — Pulpit

教会直播用经文投屏工具。副屏透明叠加层显示经文，**不干扰正在放映的 PPT**。

开工前先读 `DEVELOPMENT_PLAN.md`（分级、里程碑、验收标准）和 `SCHEMA.md`（数据库结构）。

---

## 技术栈

.NET 8 · WPF · C# 12 · SQLite（`Microsoft.Data.Sqlite`，只读）· Windows x64
测试用 xUnit。除 SQLite 外**不引入第三方 NuGet 包**，未经确认不得添加 MVVM 框架。

---

## 这个项目最容易做错的五件事

### 1. 叠加层窗口绝对不能获取焦点

必须设置 `WS_EX_NOACTIVATE`，且 `ShowActivated=false`。
少了这条，叠加层一旦被激活，放映软件可能判定失焦而退出全屏——现场直接事故。

同时需要 `WS_EX_TRANSPARENT`（鼠标穿透）、`WS_EX_TOOLWINDOW`（不进 Alt+Tab）、`WS_EX_LAYERED`。
四个缺一不可。

### 2. 叠加层窗口从不 Close

"清屏"是把内容淡出、置空，窗口继续存在。
反复 Show/Hide/Close 会造成 Z 序抖动，且每次重建都要重新打样式和心跳。

### 3. 全局热键只能注册 F7 F8 F9 F10 F12

`RegisterHotKey` 是全局独占——注册了哪个键，那个键就**不再传给 WPS**。
所以：方向键、PgUp、PgDn、Space、Enter、Esc、B、W、F5 **一律不得注册**，它们是 PPT 的翻页键。
误注册方向键 = 操作员再也翻不了 PPT，这是最严重的回归。

### 4. Enter 不是送出键

中文输入法用 Enter 确认候选词。若 Enter 触发投放，操作员打「神爱世人」时会有半截内容上屏。
送出一律走 F9/F10 全局热键。输入框内的 Enter 不做任何事。

**破坏这条最容易的方式不是写键盘事件，是给按钮加一个属性。** `IsDefault="True"`
会让 Enter 自动触发那个按钮——等于把送出键绑回 Enter。同理还有 `KeyBinding`、
`PreviewKeyDown` 里手写的 `Key.Enter` 分支。

所以 `ControlWindow` 里**刻意一行都不写**：只有 `AcceptsReturn="False"`，
让 TextBox 自己吞掉 Enter。改控制窗口前先 grep 一遍
`IsDefault|IsCancel|KeyBinding|PreviewKeyDown|Key.Enter`，应该是零命中。

另一半防线是组合态跟踪：`AttachImeTracking` 的三个 `AddHandler` **必须带
`handledEventsToo: true`**——TextBox 的编辑器自己消费这些文本输入事件并标记已处理，
不带这个参数三个处理器一个都收不到，`IsComposing` 恒为 false，F9 在拼音组合中
到达时就会把半截未确认的文字送上副屏（真机首测确认过这个坑）。

### 5. 并节必须处理

和合本有 81 组经文把多节合并（民 1:20-21、诗 8:6-8 等），原始库中被合并的节号是空串。
交付的 `bible_cuv.db` 已让**组内每个节号都能查到完整文本**：

- `merge_head` / `merge_last` 给出真实范围，用于生成出处标签
- 范围查询后**必须按 `merge_head` 去重**，否则 `民1:20-21` 会返回两条一模一样的文本，分成两页

---

## 数据访问

数据库只读，连接串必须带 `Mode=ReadOnly`。不要写入、不要迁移、不要 `PRAGMA journal_mode=WAL`。

正文用 `text_display`（已清洗：去敬空、去译注括号、去制表符）。
`text_raw` 保留原貌，仅在配置 `text.useRawText=true` 时使用。

书卷别名查询前必须归一化：**NFKC 全角转半角 → 去 `空格 . - _ ·` → 转小写**。

引用解析的正则**不能把数字排除在书卷名之外**——存在 `1sa` `2co` `1john` 这类别名。

`约1:1` 必须解析为**约翰福音 1:1**，不是约翰壹书。别名表中刻意没有 `约1` `撒上1` 这类纯数字形式，不要"好心"补上。

---

## 代码约定

- 所有 `DllImport` 集中在 `Pulpit.App/Interop/NativeMethods.cs`，别处不得声明
- `Pulpit.Core` 不引用任何 WPF / WinForms 类型（`Screen` 除外，仅在 App 层用）
- 公开 API 用 `record` 表达不可变模型
- 异步只在真正需要处使用；SQLite 本地查询同步即可
- 注释与用户可见文案用中文，代码标识符用英文

---

## 绝不允许的行为

- 直播中弹出未处理异常对话框。全局异常一律捕获→写日志→继续运行
- 任何错误信息出现在副屏上。报错只在控制窗口
- 与 PowerPoint / WPS 做进程间通信、发送按键、读取其窗口
- 遥测；**自动**联网与**自动**更新。唯一允许的网络行为：操作员手动点击「⑨ 关于与更新」
  的「检查更新」，且只能经 `UpdateChecker` 这一个类发起（§9 的 2026-08-20 修订，
  DEVELOPMENT_PLAN §11 第 15 条）。启动时与后台联网仍然绝对禁止
- 修改锁定决策（`DEVELOPMENT_PLAN.md` 第 1 节）。冲突时停止并报告，不要自行变通

---

## 里程碑纪律

M0 是**硬性 go/no-go 门**：必须先交付透明叠加层尖刺，由人工在真实直播机上验收 9 项标准，通过后才能写业务代码。

每个里程碑结束时输出三段：完成了什么、验收自测结果、未决问题。
不要跨里程碑批量提交。

---

## 当前状态（2026-08-20，真机首测之后）

- ✅ 数据库已就绪：`bible_cuv.db`（和合本简体，31103 节，478 条书卷别名，81 组并节已解析）
- ✅ M0–M6 代码全部交付，并已在 Windows 上**首次编译（一次通过，0 错误）与真机运行**
- ✅ `Pulpit.Core` 测试 **282 条用例全绿**（跑在真库上）
- ✅ **双屏 + WPS 全屏放映共存已实测**：投放不夺焦（前台句柄前后一致）、WPS 不退全屏、
  F7/F8 全局翻页、跨 DPI（125%/100%）定位正确、106 字最长节无裁切、并节标签正确、
  单文件发布全新首启通过（嵌入库解出 + e_sqlite3 自解压）
- 🔴 **剩三项必须人工**：微软拼音组合态（验证 handledEventsToo 修复——打字到一半按 F9
  应被拒绝）、淡入帧率读数（M0 第 7 项，逃生口 `animation.fadeMs=0`）、投影线物理拔插
  （验证回退不再覆写目标屏名）
- 真机首测修复了 9 处（详见 2026-08-20 的三个提交）：启动期异常改为 fail-fast（此前会留下
  无窗口、握着单实例锁的僵尸）、`_loadingAppearance` 初值 true（XAML 解析期事件即开火）、
  IME 事件补 `handledEventsToo: true`、屏幕回退不覆写目标屏名、`OnDpiChanged` 延后定位、
  配置节 null 防护、书卷正则排除 `: , 、`
- ✅ P1-1 已解锁交付（2026-08-21）：F10 英文投放已实现（副屏正显示经文 →
  原地换语言保页位，否则投输入框引用，F9 换回中文）。**两版 NIV 都已入库**：
  NIV1984（trans_id=2，Zefania XML 源）、NIV2011（trans_id=3，MyBible 模块源，
  已验明确为 2011 版）——默认即 NIV2011（`text.englishCode`）。译本源文件与
  `tools/build_*_db.py` 转换脚本都已 gitignore（版权正文不入仓，只在本机）。
  英文相关改动已于 2026-08-21 在 Windows 编译通过并跑测（**297 条全绿**，较此前
  新增 15 条）；F10 界面行为（原地换语言保页位、F9 换回）仍待真机双屏人工复验
- ✅ v1.1.0（2026-08-21）：③ 操作 区新增**英文译本下拉**（NIV1984/NIV2011 切换，
  写 `text.englishCode`，副屏显英文时原地换版本保页位）与**中英对照**开关
  （`text.bilingual`，F9 每页英上中下；分页/标签/报错全按中文走，英文空档页只出中文，
  F10 仍投纯英文）。落点：`ContentComposer.ComposeBilingual` +
  `ContentBuilder.FromBilingualReferences`（英文按中文并节组**真实范围**查，
  按输入查会丢节——首测抓出的缺陷）。测试 **304 条全绿**；下拉/对照的界面行为
  与对照页真机观感仍待人工复验
- ✅ v1.2.0（2026-08-21，本机逐项试用定稿）：①带状区域抽成 **BandView** 共用控件
  （副屏 + ② 预览同一份渲染代码），② 预览升级为**输入即预览**（待投放层，与
  `_lastSentInput` 一致时回实况镜像）；②对照版式定稿：英文经文+英文出处、
  中文经文+中文出处各自成组，经文块居中（续行共享左缘、TextAlignment=Left），
  **出处贴带子绝对右缘**（Stretch+右对齐，不随经文宽度走——曾试过按组测宽对齐，
  操作员否掉）；③正文改「块居中、行左对齐」；④外观新增字体/背景颜色下拉
  （16/14 色预设 + 自定义色列出，`typography.foreground` / 新增 `band.background`，
  透明度仍归滑块）。测试 **305 条全绿**。另：本机 F12 注册失败确诊为 VS JIT
  调试器（AeDebug）致系统保留 F12，教会机无此问题，决定不处理

改代码前先看 `DEVELOPMENT_PLAN.md` §11「计划书修订记录」——2026-08-20 那轮校正了
5 处事实错误与实现偏差（M2 不用 Viewbox、M2 验收样本换成申30:9-10 等）。
