# B5 增量设计 —— CQ-08 第二批：MainForm「界面构建」partial 原样搬迁

> 编写：主理人齐活林（沿用 B2/B3/B4 同一决策：architect-2 曾两次失败——「假完成」+ 平台 429 限流）
> 编写日期：2026-10-16　基线提交：`91966d6`（main，未推送）
> 上位依据：`.planning/prd-code-quality.md` §8 Q3「CQ-08 取『接受 partial 原样搬迁式拆分』」
> 用户裁决（2026-10-16）：B4 验收通过；**启动 CQ-08 第二批：BuildUi 拆分**

---

## 1. 背景与目标

- **CQ-08** 的验收标准是 `MainForm.cs ≤ 1400 行`。
- **B4（首批）** 只搬了「设置/持久化」（88 行）+「拖放与文件收集」（151 行），2103 → 1863 行，**未达标**；用户已裁定该批豁免该口径（改为「纯搬迁 + 编译 + 断言」）。
- 本批（第二批）搬迁 **`BuildUi()`（665 行，MainForm.cs 中最大的单一方法）**，预期 `MainForm.cs` **1863 → 1197 行**，**本批即达成 ≤1400 验收线**。

---

## 2. 勘察实测底座（主理人亲测，基线 `91966d6`）

| 项 | 实测值 |
|---|---|
| `MainForm.cs` | **1863 行**；无 BOM；CRLF 1863 / loneLF 0 |
| `BuildUi()` 区间 | **L222–886**（665 行，花括号配平实测；L223 `\t{` 起、L886 `\t}` 闭） |
| `BuildUi()` 声明行 | **`private void BuildUi()` —— 顶格，缺 1 个前导 tab** |
| 全文件 0 缩进行扫描 | 非 `using`/`namespace`/`class` 的 0 缩进行**仅 L222 这一行** → 确认为**格式瑕疵** |
| `BuildUi()` 调用点 | **仅 1 处**：`MainForm.cs` L205（构造函数体内） |
| 相邻行 | L221 空行；L887 空行；L888 起为 `OnMainFormKeyDown` 的 XML 注释 |
| `MainForm.cs` using（9 条） | System / Collections.Generic / Diagnostics / Drawing / IO / Linq / Threading / Threading.Tasks / Windows.Forms |
| `DwgToPdf.csproj` | SDK-style（`Microsoft.NET.Sdk.WindowsDesktop`）、net48、LangVersion latest、**无 `ImplicitUsings`** → **每条 using 必须显式** |
| 既有 partial 先例 | `MainForm.PhaseLog.cs`（148 行 / 5 usings）、`MainForm.Settings.cs`（100 / 4）、`MainForm.FileCollection.cs`（164 / 5）——**三者均含 `using System;`** |

### 2.1 关键澄清：推翻一个极易犯的误判

`BuildUi` 在「按 `^\t(private|…)` 抓顶层成员」的扫描中**不会出现**，很容易被误判为「**构造函数内部的局部函数**」——若成立，则它捕获构造函数局部变量、**根本无法拆到别的文件**，本批就得推翻。

**实测结论：它是常规私有实例方法。** 两条独立证据：
1. 构造函数 `MainForm()` 的闭合大括号在 **L220**（`\t}`），`BuildUi` 声明在 **L222**，在构造函数**之外**；
2. 花括号配平：从 L222 起累计 `{`−`}` 首次归零于 L886，说明 L222–886 恰好构成**一个完整的成员**；
3. 它之所以「不在清单里」，纯粹因为**声明行丢了前导 tab**（见上表 0 缩进行扫描）。

→ **可原样搬迁**，与 B4 的 `MainForm.Settings.cs` / `MainForm.FileCollection.cs` 同构。

---

## 3. 搬迁清单（块 C）

| 项 | 内容 |
|---|---|
| 块名 | 块 C「界面构建」 |
| 源区间 | `源码工程/MainForm.cs` **L222–886**（665 行，含声明行与闭合大括号） |
| 目标 | 新建 `源码工程/MainForm.BuildUi.cs` |
| 方法 | `private void BuildUi()` —— **1 个** |
| 性质 | **原样搬迁**（逐字）；唯一内容差异见 **D2** |

块内锚点（供验证者对照，均为内容锚定）：
- L224 起始注释：`// ================= v3.4：Dock 布局（顶部三组 + 中部列表区 + 底部状态栏） =================`
- L228 `_cardInput = new CardPanel { Title = "输入输出", … }`
- L600 `Process.Start("explorer.exe", "/select,\"" + p + "\"")`（位于 lambda 内）
- L671 / L687 / L747 三个 `new SplitContainer`
- L884–885 收尾：`// 标题条` / `_btnSettings.TabIndex = 0;`

---

## 4. 不搬清单（明确边界，防多搬/少搬）

| 项 | 处理 | 理由 |
|---|---|---|
| 字段区 L15–187 | **留** | partial 共享字段，搬走会破坏语义 |
| 构造函数 `MainForm()` L189–220 | **留** | 含 **L205 `BuildUi();` 调用点**，必须逐字未改 |
| `UpdateListBounds()` L973–1080 | **留** | 它内部才创建 `DataGridView`（L999 / L1026 / L1056），与块 C 无交集 |
| `MakeCardGap()` L945 / `PlaceLogAutoScroll()` L956 / `UpdateHeaderCounts()` L965 | **留** | 被 `BuildUi` 调用，但属独立方法；partial 跨文件调用无碍 |
| L888–1863 其余全部成员 | **留** | 不在本批范围 |

---

## 5. 新文件 `MainForm.BuildUi.cs` 的 using 精确清单

依据 **GEN-WIN-018**：按「**原文件 using − 新文件 using**」**逐一核对符号来源**（对块内 L222–886 实测扫描，非凭感觉裁剪）。

| using | 是否引入 | 实测依据（块内命中） |
|---|---|---|
| `System` | **是** | 块内**未直接命中** System 类型（`Math.`/`Convert.`/`Environment.`/`EventArgs`/`Exception`/`Array.`/`Action`/`Func<`/`IDisposable`/`Guid`/`DateTime`/`StringComparer`/`StringComparison`/`TimeSpan` 全部 0 命中）；**引入依据 = 与三个既有 partial 先例保持一致**（PhaseLog/Settings/FileCollection 均含），且未使用 using 不产生编译警告、后续往本文件追加代码不必再补 → 见 **D7** |
| `System.Collections.Generic` | **否** | `List<` / `Dictionary<` / `IEnumerable<` / `IList<` / `KeyValuePair<` / `HashSet<` 全 0 命中 |
| `System.Diagnostics` | **是** | L600 `Process.Start(...)` |
| `System.Drawing` | **是** | **`ContentAlignment` × 8**（编译探针实证：删本 using → 恰好 8 处 CS0103，全在 `ContentAlignment`）。注：本块 53 处 `Padding` 实为 **`System.Windows.Forms.Padding`**，**不是**本 using 的依据——初稿曾误记，见文末补记；另 8 处 `Font = UiTheme.FontXxx` 是**属性赋值**，不构成 `Font` 类型引用，故 `Font` 本身也不是依据 |
| `System.IO` | **是** | L598 `File.Exists(p)` |
| `System.Linq` | **否** | `.Select(` / `.Where(` / `.ToList(` / `.Any(` / `.All(` / `.FirstOrDefault(` / `.OrderBy(` / `.Cast<` / `.Count(` / `.Sum(` / `.Distinct(` / `.ToDictionary(` / `.GroupBy(` / `.OfType<` / **两参 `Contains(x, comparer)`** 全部 0 命中 —— **注意：与 B4 的 `FileCollection` 不同，本块不存在 `Enumerable` 扩展方法调用；B4 的教训不得机械套用** |
| `System.Threading` | **否** | `Thread` / `Interlocked` / `Monitor` / `ManualResetEvent*` / `CancellationToken` / `Mutex` / `Volatile` 全 0 命中 |
| `System.Threading.Tasks` | **否** | `Task` / `Task<` 0 命中 |
| `System.Windows.Forms` | **是** | 大量：`Button`×11 / `Label`×11 / `Panel`×11 / `TableLayoutPanel`×6 / `SplitContainer`×4 / `CheckBox`×4 / `TextBox`×3 / `GroupBox`×3 / `Control`×2 / `RadioButton`×2 / `ComboBox`×2 / `StatusStrip`×2 / `DockStyle`×44 / `AnchorStyles`×2 / `ScrollBars`×2 / `ToolTip`×2 / `FlatStyle`×2 / `FlowLayoutPanel`×1 |

**最终 5 条**：`System`、`System.Diagnostics`、`System.Drawing`、`System.IO`、`System.Windows.Forms`

**同命名空间免 using 核对**：`CardPanel` / `UiTheme` / `PhaseChipBar` / `ColumnHeader` / `FlatProgressBar` / `ItemStatus` / `ConverterEngine` 等，实测**全部位于 `namespace DwgToPdf.Batch`**（逐文件核对 `^namespace` 行），与 `MainForm` 同命名空间 → **不需要任何 using**。

---

## 6. 行数测算

| 项 | 前 | 后 |
|---|---|---|
| `MainForm.cs` | 1863 | **1197**（删除 L222–887 = 665 行方法 + 1 行尾随空行） |
| `MainForm.BuildUi.cs` | — | **≈683**（脚手架 ~18 行 + 665 行方法） |
| `MainForm.Settings.cs` / `FileCollection.cs` / `PhaseLog.cs` | 100 / 164 / 148 | 不变 |
| **CQ-08 验收线** | 1863 > 1400 **✗** | **1197 ≤ 1400 ✓（本批达标）** |

---

## 7. 偏离与决策记录（D1~D7）

- **D1 验收口径**：本批**要求**达成 `MainForm.cs ≤ 1400`（与 B4 不同——B4 因范围过小，已由用户裁定豁免该口径）。验收 = V1 纯搬迁 + V2 编译 0/0 + V3 120 断言 + V4 行数/结构 + V5 调用点与全量回归。
- **D2 缩进修正（本批唯一声明的内容差异）**：`MainForm.cs` L222 `private void BuildUi()` → 新文件内为 `\tprivate void BuildUi()`（补回丢失的 1 个前导 tab）。
  沿用 B4 D2 先例（用户已拍板「搬迁时顺手修正（推荐）」）。**QA 须将此 1 行识别为「已声明的格式修正」，而非逻辑改动。**
- **D3 L187 缩进瑕疵 —— 不在本批范围**：`MainForm.cs` L187 `    private bool _initialListSplitterSet;` 用 **4 个空格**，而相邻 L186 用 tab。它与本批搬迁块**无交集**，为避免扩大 diff、保持 V1 判据纯净，**本批不修**；记入台账，留待后续专门的格式清理小批。
- **D4 `MainForm.cs` 的 using 一条不动**：实测搬迁后 `MainForm.cs` 对其 9 条 using **仍全部有依赖**（例：`Process.Start` 未限定名仍出现 2 处 → `System.Diagnostics` 仍必需；`Size`×7 / `Color`×8 → `System.Drawing`；`Path.`×11 → `System.IO`；`List<`×14 → `Collections.Generic`；`.Select(`/`.Where(`/`.ToList(` → `System.Linq`）。故**无需任何增删**，diff 保持为**纯删除**。
- **D5 不升版本号**（**PRJ-001**）：版本号仍为 **v4.17**（未经用户同意不得改 `Program.cs` 的 Version）；文档口径记为「**修订十三**」。
- **D6 发布 exe 必须补刷（写入本批流程，不再事后追补）**：B4 的教训是——「发布 exe ⇄ 源码」的一致性**靠人工同步、而非构建保证**（net48 的 PE 头内嵌 timestamp/MVID，跨次构建**不可字节复现**；B4 时 QA 强制重建后 bin exe 即变为另一 md5，仅 150 字节差且集中在 PE 头）。本批 **T4 内必须包含**「重建发布 exe + 记录 md5 + `cmp` 逐字节一致」。
- **D7 `using System;` 保守引入**：见 §5 表。依据 = 与三个既有 partial 先例一致 + 零风险（未使用 using 不产生编译警告，当前基线 0 警告即证）。

---

## 8. 验证方案（QA 独立复验 V1~V5）

> **仪器一律按内容锚定，严禁按行号**（**GEN-TEST-008**）。行号仅用于本文档描述，验证脚本必须用内容/序列匹配定位。

- **V1 纯搬迁（核心，最高优先级）**：取 `git show <本批提交>^:DwgToPdf 工具 v4.17/源码工程/MainForm.cs`（**1863 行**）为旧版（**不得依赖工作区残留**），统一行尾后（git blob = LF、磁盘 = CRLF）比对：
  - **(a)** 旧文件删去 **L222–887** → 应与本批提交后的 `MainForm.cs`（**1197 行**）**逐字一致（0 差异）**；
  - **(b)** 旧 **L222–886** → 应与新 `MainForm.BuildUi.cs` 的方法体**逐字一致**，**唯一差异 = 声明行缩进（D2）**；
  - **(c)** 新 `MainForm.cs` 必须是旧文件的**纯子序列**（`difflib` 的 `inserted/replaced = 0`；`git --stat` 对该文件只应有 `−`）；
  - 建议用 Python `difflib.SequenceMatcher` 自动定位，勿人工切分。
- **V2 编译**：`dotnet build 源码工程/DwgToPdf.csproj -c Release` → **0 警告 0 错误**；另做一次 `-t:Rebuild`。
- **V3 断言**：SelfTest **必须** `dotnet build 自测试工程/SelfTest.csproj -c Release --no-incremental` 重建（普通 build 会判「无需还原」→ 引擎新鲜度守卫假 FAIL，**GEN-WIN-013 / PRJ-002**），再跑出 **120 PASS / 0 FAIL** 且含 `[PASS] 引擎新鲜度`；控制台输出为 **GBK**，抓取后须按 GBK 解码（**GEN-WIN-002**）。
- **V4 行数与结构**：`MainForm.cs` = **1197**；`MainForm.BuildUi.cs` ≈ **683**；两文件均**无 BOM + CRLF**；`partial class MainForm` 的类声明（修饰符/类名）与其余三个 partial 文件一致；**using 逐条必要性复核**（重点：**本块不需要 `System.Linq`** —— 不得因 B4 有而机械添加；同时核对 5 条 using 各自被哪个符号用到）。
- **V5 调用点 + 全量回归**：`BuildUi()` 调用点（`MainForm.cs` 构造函数内，搬迁后行号前移）**逐字未改**；E2E 全量回归 **113 成功 / 2 失败**（2 张 H77 风扇件为**既有失败**，非本批引入；基线 115 张）。
- **附加**：发布 exe 的 md5 应等于 `源码工程/bin/Release/net48/DwgToPdf.exe` 的 md5。

---

## 9. 任务分批（工程师 T1~T4）

- **T1 新建 `源码工程/MainForm.BuildUi.cs`**：脚手架（5 条 using + 空行 + `namespace DwgToPdf.Batch;` + 空行 + 头注释 + `internal sealed partial class MainForm` + `{`）+ 旧 **L224–885 逐字**搬入（**声明行按 D2 补 1 个前导 tab**）+ 末尾 `}`。字节形态：**无 BOM + CRLF**。
- **T2 从 `MainForm.cs` 删除 L222–887**（665 行方法 + 其后 1 行空行）；**其余一行不动**（含 L205 调用点、L221 空行、L888 起全部内容）。
- **T3 自验**：编译（0 警告 0 错误）+ SelfTest `--no-incremental` 重建 + 120 断言 + **自跑 V1 逐字比对** + 自报两文件行数与字节形态。
- **T4 收尾**：文档四件套「**修订十三**」（手册新增 **§5.17**、`版本变更记录.txt` 等，按既有口径）+ **重建发布 exe 并补刷**（记录 md5 + `cmp` 逐字节一致）+ 提交（**只提交、不推送**；git 推送需用户明确指令）。

---

## 10. 风险与对策

| 风险 | 对策 |
|---|---|
| **新文件漏 using**（B4 已发生过一次，导致 3 个编译错误） | §5 表已给出符号级实测的 5 条；T3 编译即兜底；**QA 再逐条复核必要性** |
| 多搬/少搬一行 → 破坏「纯子序列」 | V1(a) 「删除后逐字一致」+ V1(c) 「纯子序列」**双判据互锁** |
| 误把 `BuildUi` 当构造函数局部函数而放弃拆分 | §2.1 已澄清（构造函数 L220 闭合 + 花括号配平实测） |
| 发布 exe 未同步（B4 教训） | **D6**：写入 T4，不再事后追补 |
| 行号漂移导致验证仪器失效 | **GEN-TEST-008**：一律内容锚定 |
| 把 B4 的 using 结论机械套到本块（如误加 `System.Linq`） | §5 表已显式标注本块**无 `Enumerable` 调用** |

---

## 附录 A 坐标表（基线 `91966d6` 实测）

| 名称 | 行区间 | 行数 | 归属 |
|---|---|---|---|
| using 区 | L1–9 | 9 | 留 |
| 字段区 | L15–187 | 173 | 留 |
| 构造函数 `MainForm()` | L189–220 | 32 | **留**（含 L205 调用点） |
| 空行 | L221 | 1 | 留 |
| **`BuildUi()`** | **L222–886** | **665** | **搬 → `MainForm.BuildUi.cs`** |
| 空行 | L887 | 1 | **随块删除** |
| `OnMainFormKeyDown` 起 | L888–1863 | 976 | 留 |
| **合计** | 1–1863 | **1863** | 删 666 行 → **1197** |

## 附录 B 新文件骨架（`MainForm.BuildUi.cs`）

```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十三：从 MainForm.cs 拆分出「界面构建」方法（纯移动，不改逻辑）：
// BuildUi（665 行，MainForm.cs 中最大的单一方法）。
// 搬迁依据：CQ-08（MainForm.cs ≤1400 行）；本批后 MainForm.cs 1863 → 1197，达成验收线。
internal sealed partial class MainForm
{
	private void BuildUi()
	{
		// …（旧 L224–885 逐字搬入）
	}
}
```

---

## 补记（2026-10-16 实施后 · 主理人核验 + QA 独立复验 + 文档勘误）

### 实施结果（commit `9921aa2`，6 文件 +752/−666）
| 项 | 设计测算 | 实际 |
|---|---|---|
| `MainForm.cs` | 1197 | **1197** ✓ |
| `MainForm.BuildUi.cs` | ≈683 | **679**（13 行脚手架 + 665 行方法 + 1 行闭括号） |
| `git diff` 对 `MainForm.cs` | 纯删除 | **`0+/666−`**（纯子序列） |
| CQ-08 验收线 | ≤1400 | **1197 ≤ 1400 ✓ 达标** |

### 主理人独立核验（不采信工程师自检）
- **V1(a)** 旧删 L222–887 → 与新 `MainForm.cs` **差异 0**；**V1(b)** 方法体 664 行 **逐字差异 0**；**V1(c)** `inserted=0 / replaced=0 / deleted=666`。
- **D2 唯一例外**：旧 `'private void BuildUi()'` → 新 `'\tprivate void BuildUi()'`，确认唯一。
- **V3 断言**（主理人自跑）：**120 PASS / 0 FAIL / EXIT=0**，含 `[PASS] 引擎新鲜度`。
- 字节形态：两 .cs 均**无 BOM + CRLF**；`D3` 的 L187 四空格缩进**原样未动**；调用点 L205 逐字未改。
- 发布 exe md5 `e0dff8587a027c78bad4a52dd45b1106`，与 `bin` exe **cmp 逐字节 IDENTICAL**。

### QA 独立复验（严过关，报告 `_测试输出/UI评审/qa_b5/b5_qa_report.md`）
**V1~V5 全 PASS + 发布 exe 复核 PASS + 硬指标达标**。无 P0/P1/P2。
- V4 附加实证：**using 必要性用「拷贝工程 → 删单条 → 编译」实测**——WinForms / Drawing / Diagnostics / IO **必要**；**`System` 非必要**（删除后仍 0/0，符 D7 保守引入）。
- V5：E2E **115 进 → 113 成功 / 2 失败**（H77 靠背/座垫风扇件既有失败）；独立复核 `find _e2e_out -iname "*.pdf" | wc -l` = **113**。

### ⚠ 文档勘误：`System.Drawing` 的依据符号归错（主理人笔误，QA 抓出）
初稿 §5 表与手册 §5.17 均把 **`Padding`×53** 列为 `System.Drawing` 的依据。**实际 `Padding` 是 `System.Windows.Forms.Padding`**，与 `System.Drawing` 无关。

**主理人编译探针复现（拷贝工程 → 删 `using System.Drawing;` → 编译）**：产生 **恰好 8 个 CS0103，全部落在 `ContentAlignment`**，**`Padding` 零错误** → 本块 `System.Drawing` 的**唯一依据是 `ContentAlignment`×8**。

**结论未变**（`System.Drawing` 仍是必要 using），但**依据符号归类错了**，已当批更正 `design-b5.md` §5 与手册 §5.17。

**泛化教训（建议并入 GEN-WIN-018）**：GEN-WIN-018 要求「逐一核对每个符号的来源」——本次暴露该步骤的**下一层陷阱**：**符号的命名空间归属也必须实测，不能凭直觉**（`Padding` 直觉像 `System.Drawing`，实为 `System.Windows.Forms`；`ContentAlignment` 才是 `System.Drawing`）。**可靠做法 = 编译探针（删单条 using → 看 CS0103 落点）**，比查文档/凭记忆都可靠。

### 发布 exe 同步（D6 按设计执行，未事后追补）
`cf1ff20`：发布 exe 新 md5 = **`e0dff8587a027c78bad4a52dd45b1106`**（旧 = B4 的 `d15cee58…`），与 `bin` exe `cmp` 逐字节一致。QA 强制重建后已 `cp -p` 还原。

---

（完）
