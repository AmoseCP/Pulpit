# bible_cuv.db — 结构说明

由「简体中文和合本」旧库重组而成，供投屏应用直接使用。
约 24 MB，SQLite 3，UTF-8，无外部依赖。

## 表

### `verses` — 经文主表（93275 行 = CUV 31103 + NIV1984 31086 + NIV2011 31086）

| 列 | 说明 |
|---|---|
| `trans_id` | 译本 ID：1 = CUV（和合本简体），2 = NIV1984，3 = NIV2011（F10 默认）。应用按 `translations.code` 选英文译本（配置 `text.englishCode`，默认 NIV2011，缺席回退任一英文译本） |
| `book_id` | 1–66，标准书卷序 |
| `chapter` | 章 |
| `verse` | 节 |
| `text_raw` | **原始文本**，与原库逐字一致（含敬空、译注、制表符） |
| `text_display` | **清洗后文本**，供大屏显示 |
| `merge_head` | 并节组首节号；非并节时等于 `verse` |
| `merge_last` | 并节组末节号；非并节时等于 `verse` |

主键 `(trans_id, book_id, chapter, verse)`，`WITHOUT ROWID`——范围查询直接走覆盖索引。

**并节**：和合本有 81 组经文把两节以上合并成一段（如民 1:20-21、诗 8:6-8），
原库中被合并掉的 82 个节号是空串。本库让组内**每个节号都能查到完整文本**，
`merge_head`/`merge_last` 用于生成正确出处标签：

```
输入 民1:21 → 出处「民数记 1:20-21」，文本完整
输入 诗8:7  → 出处「诗篇 8:6-8」
输入 约3:16 → 出处「约翰福音 3:16」
```

范围查询时按 `merge_head` 去重，避免同一段文字连出三页。

### `books` — 书卷（66 行）

`id` / `osis` / `name_zh` / `short_zh` / `name_en` / `abbr_en` /
`testament`（0 旧约 1 新约）/ `category`（1–8 分类）/ `chapters`

### `book_aliases` — 输入别名（478 条）

`alias`（主键，已归一化）/ `book_id` / `kind`

`kind` 取值：`zh_full` `zh_short` `zh_var` `py` `en_full` `en_abbr` `en_var`

查询前先归一化输入：**NFKC 全角转半角 → 去空格与 `.` `-` `_` `·` → 转小写**。

约翰福音可用：`约` `约翰` `约翰福音` `yh` `jn` `jhn` `john`
约翰壹书可用：`约一` `约壹` `约翰一书` `约翰1书` `1jn` `1john` …

**刻意未收录 `约1` `撒上1` 这类纯数字形式**，否则 `约1:1` 会在
「约翰福音 1 章」与「约翰壹书」之间产生歧义。已验证 `约1:1` → 约翰福音 1:1。

注意书卷名可能以数字开头（`1sa` `2co`），引用解析的正则**不能**把数字排除在书名之外。

### `chapter_info` — 章节数（1189 行）

`book_id` / `chapter` / `verse_count`。用于输入校验，能给出友好报错：
`约3:99` → 「约翰福音 3 章只有 36 节」；`约99:1` → 「约翰福音只有 21 章」。

### `translations` / `meta`

译本信息与构建元数据（`schema_version`、`built_at`、`verse_count` 等）。

## `text_display` 的清洗规则

共改动 4578 节。全部规则可在 `build_bible_db.py` 中调整后重跑。

| 规则 | 影响 | 例 |
|---|---|---|
| 剥除敬空 U+3000 | 3573 节 | `起初，　神创造天地` → `起初，神创造天地` |
| 制表符/回车归一为空格 | 7 节 | 诗 105:17、启 11:15 等诗歌体分行 |
| 剥除译注括号 | ~1075 节 | `得永生（或译：…）` → `得永生`；`（细拉）`、`（有古卷加…）`、`（原文是…）` 同理 |
| 剥除雅歌说话人标注 | 6 节 | `〔新郎〕我的佳偶` → `我的佳偶` |
| 去除跨节悬空括号 | 74 节 | 出 16:35 结尾孤零零的 `（` |
| 标点收尾 | — | 剥除后残留的重复逗号、句首标点 |

**保留了 190 节的括号**——这些是经文本身的插入语而非译注，例如
创 48:14「按在以法莲的头上（以法莲乃是次子）」。这批我抽样复核过，
如果现场觉得仍嫌啰嗦，改 `NOTE_PATTERNS` 重跑即可。

`text_raw` 始终保留原貌，所以应用里做一个「显示原文/清洗版」开关是零成本的。

## 常用查询

单节：

```sql
SELECT v.text_display, b.name_zh, v.merge_head, v.merge_last
FROM verses v JOIN books b ON b.id = v.book_id
WHERE v.trans_id=1 AND v.book_id=? AND v.chapter=? AND v.verse=?;
```

范围（按 `merge_head` 去重，一节一页）：

```sql
SELECT MIN(v.verse), v.merge_head, v.merge_last, v.text_display
FROM verses v
WHERE v.trans_id=1 AND v.book_id=? AND v.chapter=? AND v.verse BETWEEN ? AND ?
GROUP BY v.merge_head
ORDER BY v.merge_head;
```

书卷解析：

```sql
SELECT book_id FROM book_aliases WHERE alias = ?;   -- 传入已归一化的字符串
```

## 英文译本（NIV1984 = trans_id 2，NIV2011 = trans_id 3）

由 `tools/build_niv_db.py` 写入（幂等可重跑）。**源文件与转换脚本都已 gitignore
（版权正文不入仓），只存在于构建机本地**——脚本丢了按本节 + DEVELOPMENT_PLAN §11
第 16 条的规则重写即可。两个源：

* NIV 1984：Zefania XML（`New International Version (1984) (US).xml`）
* NIV 2011：MyBible SQLite 模块（`NIV_en.SQLite3`，book_number 带间隙 10–730，
  按升序映射标准 1–66 并与 `books.name_en` 逐卷核对）

共同性质：

* 书卷、别名、章节表与中文共用；NIV 无并节，`merge_head`/`merge_last` 恒等于 `verse`
* NIV 归入脚注的 16 节（太 17:21、可 7:16、约 5:4、徒 8:37 等）**无行**，
  查不到时按下面的回退约定提示（两版同位）
* 与 `chapter_info`（按和合本建）有 3 处节数出入：约 7 章（NIV 有 53 节）、
  约叁（NIV 14 节 vs CUV 15）、启 12 章（NIV 17 节 vs CUV 18）——允许，回退提示兜底
* `meta` 每版三键：`niv1984_*` / `niv2011_*`（`built_at` / `verse_count` / `source`）

清洗规则（Zefania，1984）：`text_raw` 保留 XML 原貌；`text_display` 做空白归一 +
`--` → `—`；`[补词]` 方括号是 NIV 正文标记，保留。

清洗规则（MyBible，2011）：正文带传输标记，`text_raw` 也须剥标记——
`<f>[n]</f>` 脚注号整体删除；`<t>` `<J>` `<i>` `<e>` 及 `<pb/>` 去壳留字；
`<n>` 内容整体为方括号者是抄本编注（全库仅 2 处：可 16:8、约 7:52），
raw 保留、display 删除（对应 CUV 的译注处理），**其余 `<n>` 包的是真正文**
（约 8:12-59 整段被模块包进 `<n>`，已核对），拆包保留。

## 后续

* 英文节号与中文偶有出入，取不到时回退提示即可（见上）。
* 需要关键词搜索（「神爱世人」反查出处）再补 FTS5 外部内容表，
  目前按引用精确寻址不需要。
