# DwgToPdf B4 结构批 增量设计文档（CQ-08 首批：MainForm partial 原样搬迁）

> 版本 v1.0 · 2026-10-16 18:2x · 主理人齐活林执笔
> **执笔说明**：本设计原拟派架构师（architect-2 因平台 429 频率限流不可用，配额 2026-10-17 12:16 重置；该成员本阶段已有两次未交付记录）。**用户拍板「按 Q3 两块做」**，主理人亲核全部坐标后执笔。
> 上游：《.planning/prd-code-quality.md》§CQ-08（P2 ⚠结构）+ §8 Q3 用户裁决
> 样板：《.planning/design-b3.md》、《MainForm.PhaseLog.cs`（既有 partial 先例，v4.6 拆分）
> **坐标基准**：main = `0d6765a`，`MainForm.cs` = **2103 行**（超红线 2100）

---

## Part A：系统设计

### 1. Implementation Approach

#### 1.1 本批的四个关键判断（含一处必须澄清的口径调整）

| # | 判断 | 依据与结论 |
|---|---|---|
| ① | **Q3 两块不够达到 CQ-08 的 ≤1,400 行** | 实测：两块合计仅 **~239 行**，搬完 `MainForm.cs` 仍 ~1,864 行。**用户已裁决**：首批按 Q3 两块做，**验收口径改为「纯搬迁 diff + 编译 0/0 + SelfTest 120/120」，不要求 ≤1,400**；达标留待后续批次（BuildUi 667 行 / 列表与失败详情 200 行 / 转换执行 231 行） |
| ② | **字段不搬**（`_appSettings` L49、`_btnSettings` L48 等） | 字段被多块交叉引用（如 `_appSettings` 出现在 L1689/L1876/L1884/L1920/L1921/L1952/L1956），搬字段会扩大 diff 且无结构收益。**本批只搬方法**（Q3 说「原样搬迁」，方法搬迁是主体） |
| ③ | **两个工程文件都不用改** | 实测 `DwgToPdf.csproj` 是 **SDK-style**（`<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">`，**无显式 `Compile Include`**）→ 新增 `.cs` 走默认通配符，**无需改 csproj**；`SelfTest.csproj` 白名单**本就不含 MainForm.cs** → 搬迁不影响自测编译（仍按 PRJ-002 重建 SelfTest） |
| ④ | **既有缩进瑕疵顺手修正** | `MainForm.cs` L1873-1874（注释 + `LoadAppSettings` 签名）被误缩进为 **2 层 tab**，而同方法体 `{`（L1875）是 1 层——恰在本次搬迁范围内。**用户裁决：搬迁时顺手修正为 1 层**，并在设计/提交中明示，便于 QA 区分「格式修正」与「逻辑改动」 |

#### 1.2 策略总览

```
原则：只移动代码位置，不改任何一行逻辑；方法体逐字相同（含注释）。
块 A（设置/持久化，~88 行）→ 新文件 MainForm.Settings.cs
    LoadAppSettings / ApplyUiState / SaveUiState / OpenSettings
块 B（拖放与文件收集，~151 行）→ 新文件 MainForm.FileCollection.cs
    OnDragEnter / OnDragDrop / PasteFromClipboard / AddFilesDialog /
    AddFolderDialog / AddFiles / AddToBatch / ClearList
不动：字段、调用点（8 处）、其他区块、两个工程文件
```

### 2. File List

| 文件 | 改动 | 性质 |
|---|---|---|
| 源码工程/MainForm.Settings.cs | **新建**：搬入块 A 的 4 个方法（含注释与 XML doc）；头部 using/namespace/partial 声明 | 结构（纯搬迁） |
| 源码工程/MainForm.FileCollection.cs | **新建**：搬入块 B 的 8 个方法 | 结构（纯搬迁） |
| 源码工程/MainForm.cs | **删除**已搬的 12 个方法（及方法间空行），2103 → ~1864 行 | 结构（纯搬迁） |
| 源码工程/DwgToPdf.csproj | **无需改动**（SDK-style 通配符） | — |
| 自测试工程/SelfTest.csproj | **无需改动**（白名单不含 MainForm） | — |
| 文档四件套 | 手册第 5 章新增小节 / 说明.txt「修订十二」/ 运行说明.txt / 版本变更记录.txt | 文档 |

> **不升版本号**：按 PRJ-001，未经用户同意不改 `Program.cs` 的 Version。文档口径标「**v4.17 修订十二**」（承 B1=修订九 / B2=修订十 / B3=修订十一）。

### 3. 搬迁清单（精确坐标，基准 = main `0d6765a`）

#### 块 A：设置/持久化 → `MainForm.Settings.cs`

| # | 成员 | 原行区间 | 行数 | 备注 |
|---|---|---|---|---|
| A1 | `LoadAppSettings()` | L1873-1878 | 6 | L1873 是注释行（**2 层缩进，本次修正为 1 层**） |
| A2 | `ApplyUiState()` | L1880-1914 | 35 | 含 XML doc（L1880-1881） |
| A3 | `SaveUiState()` | L1916-1947 | 32 | 含 XML doc（L1916-1917） |
| A4 | `OpenSettings(object, EventArgs)` | L1949-1960 | 12 | L1949 是注释行 |
| | **合计** | | **85** | + 方法间空行 ≈ 88 |

#### 块 B：拖放与文件收集 → `MainForm.FileCollection.cs`

| # | 成员 | 原行区间 | 行数 | 备注 |
|---|---|---|---|---|
| B1 | `OnDragEnter(object, DragEventArgs)` | L1110-1118 | 9 | |
| B2 | `OnDragDrop(object, DragEventArgs)` | L1120-1130 | 11 | |
| B3 | `PasteFromClipboard()` | L1132-1162 | 31 | 含 CQ-06 的 `BgLogger.Warn` 分支 |
| B4 | `AddFilesDialog()` | L1164-1182 | 19 | |
| B5 | `AddFolderDialog()` | L1183-1198 | 16 | |
| B6 | `AddFiles(IEnumerable<string>)` | L1200-1231 | 32 | |
| B7 | `AddToBatch(List<string>, string)` | L1233-1246 | 14 | |
| B8 | `ClearList()` | L1248-1259 | 12 | |
| | **合计** | | **144** | + 方法间空行 ≈ 151 |

> **B8 `ClearList` 的归属说明**：它是「文件收集」的**逆操作**（清空已收集清单），与 `AddFiles`/`AddToBatch` 同属「清单内容维护」，且共享 `_pendingFiles` / `RefreshListUi` 依赖。若审阅认为它应归「列表操作」块，可移出（影响 12 行，不影响其余结论）。

### 4. 明确不搬（边界）

| 不搬项 | 位置 | 理由 |
|---|---|---|
| 全部字段 | `_appSettings`(L49)、`_btnSettings`(L48)、`_pendingFiles`、`_statusByPath`、`_noteByPath`、`_undoStack`、`MaxUndo`、`_converting` 等 | 交叉引用广，搬字段扩大 diff 无收益（见 §1.1 ②） |
| 全部调用点 | L204 `LoadAppSettings()`、L207 `ApplyUiState()`、L208 `FormClosing += … SaveUiState()`、L246 `AddFilesDialog()`、L248 `AddFolderDialog()`、L492 `ClearList()`、L778 `_btnSettings.Click += OpenSettings`、L819-820 `DragEnter/DragDrop +=`、L1097 `PasteFromClipboard()` | partial 类内互相调用，**搬迁后零改动** |
| `PushUndo` / `UndoLast` | L1261-1304 | 属「撤销」块，不在 Q3 范围 |
| `RefreshListUi` / `AddRow` / `UpdateRow` 等 | L1305+ | 属「列表与失败详情」块（后续批次） |
| `PickFolder` | L1505-1519 | 属「输出文件夹选择」，不属文件收集 |
| `OpenOutputFolder` / `LatestPdfSubFolder` | L1962-2030 | 属「输出功能」，**不属**「设置/持久化」 |
| `RunFirstUseSelfCheckAsync` | L2031-2103 | 属「首次自检」块 |
| `BuildUi`（667 行） | L222-888 | 最大块，后续批次 |

### 5. 新文件格式（照 `MainForm.PhaseLog.cs` 先例）

```csharp
using System;                        // ← 块 A：另加 System.Drawing
using System.Collections.Generic;    // ← 仅块 B
using System.Drawing;                // ← 仅块 A
using System.IO;                     // ← 仅块 B
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十二：从 MainForm.cs 拆分出「设置/持久化」方法（纯移动，不改逻辑）：
// LoadAppSettings / ApplyUiState / SaveUiState / OpenSettings
internal sealed partial class MainForm
{
	（搬来的方法，保持原缩进层级）
}
```

**using 精确清单（只引必要项）**：
- `MainForm.Settings.cs`：`System`（`Exception`）、`System.Drawing`（`Rectangle`、`SystemInformation`）、`System.Windows.Forms`（`DialogResult`、`FormWindowState`、`FormStartPosition`）
- `MainForm.FileCollection.cs`：`System`（`StringComparer`、`StringComparison`）、`System.Collections.Generic`（`List`、`IEnumerable`）、`System.IO`（`Directory`、`File`、`Path`）、`System.Windows.Forms`（`DragEventArgs`、`DataFormats`、`DragDropEffects`、`Clipboard`、`OpenFileDialog`、`FolderBrowserDialog`）
- **不引** `System.Linq` / `System.Diagnostics` / `System.Threading` / `System.Threading.Tasks`（用不到）

### 6. 行数测算

| 文件 | 前 | 后 |
|---|---|---|
| `MainForm.cs` | 2103 | **~1864** |
| `MainForm.Settings.cs` | — | ~100（15 头 + 85 体） |
| `MainForm.FileCollection.cs` | — | ~159（15 头 + 144 体） |

### 7. Anything UNCLEAR / 偏离设计（需 Engineer/QA 知悉）

- **D1（验收口径调整）**：CQ-08 原验收「单文件 ≤ ~1,400 行」在本批**不适用**（Q3 首批两块不够），已获用户裁决。本批判据 = **纯搬迁 + 编译 0/0 + 120/120**。≤1,400 留待后续批次。
- **D2（缩进修正）**：L1873-1874 的 2 层 → 1 层修正**属本批唯一的非纯搬迁内容**，QA 校验「纯搬迁」时须把这 2 行作为**已知例外**。
- **D3（`ClearList` 归属）**：见 §3 注。若审阅认为应移出，影响 12 行。
- **D4（不升版本号）**：文档口径「修订十二」，`Program.cs` Version 维持 v4.17。

---

## Part B：任务分解

### 8. Required Packages

无新增依赖。

### 9. Task List（共 4 个任务）

#### T1 新建 `MainForm.Settings.cs`（块 A）
- 内容：using（System / System.Drawing / System.Windows.Forms）+ `namespace DwgToPdf.Batch;` + 拆分说明注释 + `internal sealed partial class MainForm { … }`
- 搬入 A1~A4 四个方法（**方法体逐字相同**，含注释与 XML doc）
- **顺手修正 L1873-1874 缩进**（2 层 → 1 层）
- 字节纪律：**无 BOM + CRLF**（本工程 .cs 统一形态）

#### T2 新建 `MainForm.FileCollection.cs`（块 B）
- 内容：using（System / System.Collections.Generic / System.IO / System.Windows.Forms）+ 同款头部
- 搬入 B1~B8 八个方法（**方法体逐字相同**）
- 字节纪律：无 BOM + CRLF

#### T3 从 `MainForm.cs` 删除已搬方法
- 删除 A1~A4、B1~B8 共 12 个方法及其方法间空行
- **注意行号平移**：删除块 B（L1110-1259，靠前）会使块 A（L1873-1960）的行号前移 ~151 行——**一律用内容锚定定位，勿用行号**（GEN-TEST-008）
- 保持文件整体格式整洁（不留连续多行空行），但**不得引入无关格式改动**

#### T4 编译 + 断言 + 文档四件套 + 提交
1. 编译主工程 Release → 0 警告 0 错误
2. `--no-incremental` 重建 SelfTest → 0 警告 0 错误（PRJ-002）
3. 跑默认断言集 → **120/120 PASS**
4. 文档四件套同步「修订十二」（口径：本次为结构搬迁，无行为变化）
5. 提交（只提交不推送）：`v4.17 修订十二：B4 结构批（CQ-08 首批）——MainForm 设置/持久化 + 拖放与文件收集两块 partial 原样搬迁，2103→~1864 行`

### 10. Shared Knowledge

- **纯搬迁的判据**：方法体（含注释、空行、缩进）逐字相同；`git diff` 应显示「新文件整段新增 + 原文件整段删除」，且**新增内容 == 删除内容**（除 D2 的 2 行缩进）。
- **partial 类内互相调用无需改动**：新文件与主文件同属 `internal sealed partial class MainForm`，私有成员互通。
- **`MainForm.cs` 是 CRLF + 无 BOM**（实测 2103 CRLF / 0 loneLF）；`DwgToPdf.csproj` 是 **LF + 无 BOM**（实测 0 CRLF / 20 loneLF）——若需改 csproj（本批不需要），注意其形态与 .cs 不同。

### 11. 验证方案（V1~V5）

| # | 验证项 | 方法 | 通过判据 |
|---|---|---|---|
| V1 | **纯搬迁校验（核心）** | 对 12 个方法做「搬迁前 vs 搬迁后」逐字比对（token/字节级，仪器按**内容锚定**） | 12/12 逐字相同；**唯一例外 = L1873-1874 缩进修正（D2）** |
| V2 | 编译 | 主工程 + SelfTest（`--no-incremental`） | 0 警告 0 错误 |
| V3 | 断言 | SelfTest 默认集 | 120/120 PASS（含引擎新鲜度守卫） |
| V4 | 行数与结构 | `wc -l` + 新文件 using 必要性检查 | `MainForm.cs` ≈ 1864；两个新文件行数符合 §6；无多余 using |
| V5 | **调用点零改动** | grep 8 处调用点与搬迁前比对 | 逐行一致（partial 内调用无需改动） |

### 12. Task Dependency Graph

```
T1 (块 A 新文件) ─┐
                  ├─→ T3 (从 MainForm.cs 删除) ─→ T4 (编译+断言+文档+提交)
T2 (块 B 新文件) ─┘
```
T1/T2 可并行（不同新文件）；T3 必须在 T1+T2 之后（否则代码短暂消失）；T4 依赖 T3。

---

### 附录 A：搬迁前坐标复核表（主理人 2026-10-16 18:2x 亲核，main=`0d6765a`）

| 成员 | 行区间 | 行数 |
|---|---|---|
| `OnDragEnter` | L1110-1118 | 9 |
| `OnDragDrop` | L1120-1130 | 11 |
| `PasteFromClipboard` | L1132-1162 | 31 |
| `AddFilesDialog` | L1164-1182 | 19 |
| `AddFolderDialog` | L1183-1198 | 16 |
| `AddFiles` | L1200-1231 | 32 |
| `AddToBatch` | L1233-1246 | 14 |
| `ClearList` | L1248-1259 | 12 |
| `LoadAppSettings` | L1873-1878 | 6 |
| `ApplyUiState` | L1880-1914 | 35 |
| `SaveUiState` | L1916-1947 | 32 |
| `OpenSettings` | L1949-1960 | 12 |

**同类坐标（不搬，供后续批次参考）**：`BuildUi` L222-888（667）；`UpdateListBounds` L973-1080（108）；`PushUndo`/`UndoLast` L1261-1304（44）；`RefreshListUi`~`InitFailDetailCell` L1305-1504（200）；`PickFolder`~`CopyGridToFolder` L1505-1580（76）；`Start`~`RunCore` L1581-1811（231）；`ApplyUpdate` L1825-1881（57）；`OpenOutputFolder`/`LatestPdfSubFolder` L1962-2030（69）；`RunFirstUseSelfCheckAsync` L2031-2103（73）。

### 附录 B：新文件完整骨架（供 Engineer 直接对照）

```csharp
// ===== MainForm.Settings.cs =====
using System;
using System.Drawing;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十二：从 MainForm.cs 拆分出「设置/持久化」方法（纯移动，不改逻辑）：
// LoadAppSettings / ApplyUiState / SaveUiState / OpenSettings
internal sealed partial class MainForm
{
	// v4.3：从 %APPDATA%\DwgToPdf\settings.json 加载用户设置；首次启动自动生成默认
	private void LoadAppSettings()
	{
		…（原样，注意缩进已修正为 1 层）
	}

	…（ApplyUiState / SaveUiState / OpenSettings 原样）
}

// ===== MainForm.FileCollection.cs =====
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace DwgToPdf.Batch;

// v4.17 修订十二：从 MainForm.cs 拆分出「拖放与文件收集」方法（纯移动，不改逻辑）：
// OnDragEnter / OnDragDrop / PasteFromClipboard / AddFilesDialog /
// AddFolderDialog / AddFiles / AddToBatch / ClearList
internal sealed partial class MainForm
{
	…（8 个方法原样）
}
```

---

（完）

---

## 补记（2026-10-16 实施后 · 主理人核验与设计勘误）

### 实施结果（commit `cefe588`，7 文件 +339/−240）
| 项 | 设计测算 | 实际 |
|---|---|---|
| `MainForm.cs` | ~1864 | **1863** |
| `MainForm.Settings.cs` | ~100 | **100** |
| `MainForm.FileCollection.cs` | ~159 | **164** |
| `git diff` 净增行 | — | **0**（新 `MainForm.cs` 是旧文件的**纯子序列**，纯搬迁最强信号） |

### 主理人独立核验（不采信工程师自检）
- **V1 纯搬迁**：块 B **0 处差异**（150/150 逐字一致）；块 A **仅 1 处差异** = 设计 D2 声明的 `LoadAppSettings` 注释+签名缩进修正（`\t\t` → `\t`）——**与声明的例外完全吻合**。
- **V3 断言**：SelfTest **120 PASS / 0 FAIL**、引擎新鲜度守卫 PASS（主理人自跑）。
- **字节形态**：三个 .cs 全部无 BOM + CRLF（1863 / 100 / 164）。
- **V5 调用点**：工程师报告 10 行一致（设计 §4 写「8 处」是按调用语句数，实际 grep 到 10 行——含 `FormClosing += delegate { SaveUiState(); };` 这类同行多语句，无实质差异）。

### ⚠ 设计勘误：§5 的 using 清单漏列两条（主理人笔误，工程师已补正）
| 文件 | 漏列 | 原因 |
|---|---|---|
| `MainForm.Settings.cs` | `using System.IO;` | `ApplyUiState` 用到 `Directory.Exists` |
| `MainForm.FileCollection.cs` | `using System.Linq;` | `AddToBatch`/`ClearList` 的 `Contains(value, comparer)` 是 **`Enumerable.Contains` 扩展方法**（非 `List<T>.Contains`），必须引 System.Linq——而设计 §5 还明确写了「不引 System.Linq」，属**方向性错误** |

**教训（已记入手册 §5.16）**：partial 拆分给新文件配 using 时，**不能凭「这个文件看起来用不到」裁剪**，必须用「**原文件 using − 新文件 using**」逐一核对每个符号的来源（扩展方法尤其隐蔽：`List<T>` 的两参 `Contains` 其实来自 `Enumerable`）。

### 发布 exe 同步
B4 为纯结构搬迁（功能零变化），工程师按设计未重建发布 exe；主理人核验后**要求补刷**（避免「发布 exe 与源码不对应」的可追溯性缺口），作为独立补提交处理。

**已完成（commit `c1f0739`）**：`DwgToPdf 工具 v4.17/DwgToPdf.exe` 新 md5 = **`d15cee58a597f01bb21f29597d3ccb1b`**（旧 = B3 的 `4ddfff41e38848687e25a69b0f05d5e6`）；与 `源码工程/bin/Release/net48/DwgToPdf.exe` **md5 完全一致**（主理人 `md5sum` 双点核对）。至此「源码 ⇄ 发布 exe」对应关系恢复。

---

## QA 独立复验（2026-10-16 · 严过关）

**结论：V1~V5 全 PASS + 发布 exe 复核 PASS —— 可提交用户验收。** 无 P0/P1/P2；方案本身无问题，未发现残余风险。

| 项 | 结果 | 关键原始数据 |
|---|---|---|
| V1 纯搬迁 | **PASS** | 旧→新 MainForm `inserted/replaced = 0`（delete 151 + 89 行）→ 纯子序列、净增 0；块 B **0 差异**（150 行逐行一致）；块 A **仅 2 行** = D2 缩进；12 方法集合完整 |
| V2 编译 | **PASS** | 主工程 + `-t:Rebuild` + SelfTest `--no-incremental` 均 0/0 |
| V3 断言 | **PASS** | 120 PASS / 0 FAIL / exit 0，含 `[PASS] 引擎新鲜度` |
| V4 行数结构 | **PASS** | 1863 / 100 / 164；无 BOM + CRLF；类声明一致；using 逐条必要（**两参 `Contains` = `Enumerable.Contains` → System.Linq 必要**，勘误已落实） |
| V5 调用点回归 | **PASS** | 10 行调用点逐字未改；E2E 113 成功 / 2 失败 = 基线 |
| 发布 exe | **PASS** | 独立复现 发布 == bin == `d15cee58…` |

QA 独立复核要点（不复用主理人结论）：旧版取 `git show 3e62906:` 而非工作区；比对前统一行尾（git blob = LF vs 磁盘 = CRLF）；V2 强制重建曾把 bin exe 改写为 `40de5837…`（同 size / 同元数据、仅 150 字节差集中在 PE 头 timestamp/MVID 区）→ 复验后 `cp -p` 还原，工作区恢复 as-found。

**两条观察（非缺陷，供后续批次参考）**：
1. 本设计 §4 与手册 §5.16 写「8 处调用点」，实际 grep 到 10 行——差异来自「调用语句数 vs 调用行数」口径（`FormClosing += delegate { SaveUiState(); };` 同行多语句，及 DragEnter/DragDrop 各占一行）。**泛化教训：涉及「N 处」这类计数口径时，设计与验证必须写明「按语句还是按行」。**
2. **net48 构建跨次不可字节复现**（PE 头内嵌 timestamp/MVID）→「发布 exe ⇄ 源码」的一致性**不是构建保证、而是人工同步保证**。故「改源码后同步补刷发布 exe 并记录 md5」必须固化为流程步骤（本批 `c1f0739` 即为该步骤的产物）。
