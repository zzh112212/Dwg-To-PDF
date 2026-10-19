# B6 增量设计 —— CQ-09 注释密度提升（家族全口径 · 覆盖率 100%）

> 编写：主理人齐活林（沿用 B2~B5 同一决策：architect-2 曾两次失败——「假完成」+ 平台 429 限流）
> 编写日期：2026-10-16　基线提交：`f0b05eb`（main，未推送）
> 上位依据：`.planning/prd-code-quality.md` §CQ-09（P2）
> 用户裁决（2026-10-16）：范围 = **家族全口径**；验收 = **按覆盖率 100%**

---

## 1. 背景与目标

- **CQ-09（P2）**：注释密度提升。PRD 原问题描述——`ConverterEngine.Plot.cs` 4/656、`MainForm.cs` 31/2086。
- **PRD 内部口径矛盾（本批必须裁决）**：
  - §CQ-09 **方案**写「MainForm 补至 public/internal 成员 100% 覆盖」——实测只需 **+2 条**（构造函数 + `ProcessCmdKey`）；
  - §CQ-09 **验收**写「两文件 summary 计数各自提升 ≥50%」——按 MainForm.cs 基线需 **+14 条以上**。
  - 两者相差一个数量级，不可能同时满足。**用户裁决：以「覆盖率」为准，弃用「计数 ≥50%」这一行数相关的代理指标。**
- **PRD 写于 MainForm 尚未拆分时**：B4（修订十二）拆出 `Settings` / `FileCollection`，B5（修订十三）拆出 `BuildUi`。这三个新文件注释密度极低（`BuildUi` 0/1、`FileCollection` 1/8、`Settings` 2/4），且 PRD 视野内不存在。**用户裁决：取家族全口径，一并补齐。**

**本批目标**：把目标文件集中**所有方法/属性**的 XML `<summary>` 覆盖率提升到 **100%**。

---

## 2. 勘察实测底座（主理人亲测，基线 `f0b05eb`）

| 文件 | 行数 | 方法/属性 | 已有 doc | **待补** |
|---|---|---|---|---|
| `ConverterEngine.cs` | 1498 | 9（仅 public/internal 计入） | 3 | **6** |
| `ConverterEngine.Plot.cs` | 664 | 5 | 4 | **1** |
| `MainForm.cs` | 1197 | 41 | 25 | **16** |
| `MainForm.BuildUi.cs` | 679 | 1 | 0 | **1** |
| `MainForm.Settings.cs` | 100 | 4 | 2 | **2** |
| `MainForm.FileCollection.cs` | 164 | 8 | 1 | **7** |
| `MainForm.PhaseLog.cs` | 148 | 10 | 10 | **0** |
| **合计** | — | **78** | **45** | **33** |
| **（B6·补后记）终态实测** | — | **97** | **97** | **0**（分母 78 系设计期统计漏计 BuildGrid + 排除 RunCore/MarkAllCancelled 所致；补 3 处后全口径 97/97=100%，2026-10-19 QA 补验后用户裁决补齐） |

**待补总数 = 33 处**（精确坐标见附录 A）。

### 2.1 统计口径的两条硬规定（否则数字会虚高/虚低）

1. **字段不计入**。`MainForm.cs` 有 **70 个私有字段**（`private TextBox _txtInput;` 之类）。若把字段算进分母，覆盖率会被稀释成无意义的低值，且给私有字段写 XML doc 属**噪音**。→ 本批**只统计「方法 / 属性」**，且**不给字段加 doc**（见 D3）。
2. **「带初始化器的字段」不是方法**。首次统计曾把 `private readonly List<string> _pendingFiles = new List<string>();` 这类**因初始化器含 `(`** 的字段误判为方法，导致 MainForm.cs 虚高到 47 个成员、待补虚高到 39 处。**修正判据 = 只看第一个 `=` 之前的文本是否含 `(`**。→ 这是本批的**统计仪器陷阱**，QA 复算时必须用同一判据，否则会得出不一致的覆盖率。

### 2.2 其它实测事实

- **33 处待补成员上方均无特性行（`[Attribute]`）** → 本批不涉及「doc 与特性行的先后顺序」问题（规范仍写入 D9 备查）。
- **所有 partial 类声明均无 XML `<summary>`**（`ConverterEngine` 2 部件、`MainForm` 5 部件，逐文件核对前 3 行确认）→ 见 D4。
- 现有 doc 风格以**单行**为主，超长时折行且续行仍以 `///` 开头（如 `ApplyPrimaryButton` 的 2 行 summary）→ 本批沿用。

---

## 3. 范围（用户裁决：家族全口径）

**纳入**：
- `ConverterEngine.cs` —— 仅其 **6 个未覆盖的 public/internal 成员**（私有成员不在本批，PRD 未列）
- `ConverterEngine.Plot.cs` —— 全部未覆盖方法/属性（1 处）
- `MainForm` 家族 **5 个 partial 文件** —— 全部未覆盖方法/属性（16 + 1 + 2 + 7 + 0 = 26 处）

**不纳入**：
- `MainForm.cs` 的 70 个私有字段
- `ConverterEngine.cs` 的 2 个未覆盖**私有**方法（`RunCore` L512、`MarkAllCancelled` L1325）——PRD 只点了 public/internal；如后续需要可单独批（**B6·补后记**：QA 补验发现该排除口径导致「全口径 100%」不可复现，经用户裁决已于补批中补齐 doc——此排除决议作废，见 progress 会话二十五）
- 类级 / 命名空间级文档（见 D4）
- 任何**代码逻辑**——本批**只增注释**

---

## 4. 注释撰写规范（质量门槛，**硬要求**）

本批最大风险不是「漏写」，而是**写出低信息量注释**（把方法名换句话再说一遍），那样只是把文件变长。因此规定：

1. **每条 summary 必须回答「调用时机 / 前置条件 / 为什么这样做」中的至少一项**，而不是复述方法名。
   - ✗ 反例：`/// <summary>停止请求。</summary>`（`StopRequested`）——零信息
   - ✓ 正例（库内既有）：`/// <summary>v4.17 修订七（C 档·§5.6）：Esc → 停止当前转换（仅在转换进行中且尚未下达停止时生效）。</summary>`
2. **禁止臆造行为**。每条必须**读方法体后**再写；不确定的行为不要写，宁可写「调用时机 + 前置条件」这类可确证的内容。
3. **版本/来源标注**：若该方法由某次修订引入或改过，按库内既有习惯标注（如 `v4.17 修订七（C 档·§5.6）`）；无明确来源则省略，**不要编造版本号**。
4. **XML 转义（D6）**：summary 正文若出现 `<` `>` `&`（例如描述 `List<string>`、泛型、比较运算符），**必须**转义为 `&lt;` `&gt;` `&amp;`，否则触发 **CS1570** 警告，直接破坏「0 警告」基线。
5. **格式**：`/// <summary>…</summary>`，优先单行；过长可折行，续行仍以 `///` 开头并保持与首行同列缩进。
6. **缩进与位置**：doc 块紧贴成员声明**上一行**，缩进与成员声明一致（1 个 tab）；若成员上方有特性行，doc 必须放在**特性行之上**（D9）。

---

## 5. 行数测算

- 33 处 × 约 1~2 行 ≈ **+40 ~ +55 行**（纯新增，无删除）。
- `MainForm.cs` 1197 → 约 **1213~1219**；`BuildUi.cs` 679 → 约 680；`FileCollection.cs` 164 → 约 171；`Settings.cs` 100 → 约 102；`ConverterEngine.Plot.cs` 664 → 约 665；`ConverterEngine.cs` 1498 → 约 1506。
- **CQ-08 的 ≤1400 验收线不受影响**（`MainForm.cs` 仍在 1220 以内）。

---

## 6. 偏离与决策记录（D1~D9）

- **D1 验收口径（用户裁决）**：以「**方法/属性的 doc 覆盖率 = 100%**」为准，**弃用** PRD 的「summary 计数提升 ≥50%」。理由：后者是行数相关的代理指标，而 PRD 自身「方案」段（public/internal 100%）与其「验收」段（计数 ≥50%）相差一个数量级，无法同时满足。
- **D2 范围（用户裁决）**：**家族全口径** —— MainForm 家族 5 文件 + `ConverterEngine.Plot.cs` + `ConverterEngine.cs` 的 6 个未覆盖 public/internal。
- **D3 字段不加 doc**：`MainForm.cs` 的 70 个私有字段**一律不加** XML doc；覆盖率口径也**只算方法/属性**。理由：私有字段的 XML doc 是噪音，且会稀释覆盖率分母。
- **D4 不加类级 summary**：`ConverterEngine` 有 2 个 partial 部件、`MainForm` 有 5 个。在**多个部件**上同时写类级 `<summary>` 存在重复文档风险；只写其中一个部件又显得任意。→ **本批不涉及类级文档**，如确需可另立小批。
- **D5 注释质量门槛**：见 §4 第 1~3 条。**验收时 QA 必须抽查若干条，判定是否属于「复述方法名」的低信息量注释**。
- **D6 XML 转义**：见 §4 第 4 条；**编译 0 警告是硬指标**，CS1570 不允许出现。
- **D7 不升版本号**（**PRJ-001**）：版本仍为 **v4.17**；文档口径记为「**修订十四**」。
- **D8 发布 exe 补刷**（延续 B5 的 D6）：本批虽为**纯注释**（IL 不变），但源码已变，为保持「发布 exe ⇄ 源码」可追溯一致，**仍重建并补刷发布 exe**（记录 md5 + `cmp`）。
- **D9 特性行顺序**：doc 必须在特性行**之上**。本批实测 33 处上方**均无**特性行，故不实际涉及，仅写入规范备查。

---

## 7. 验证方案（QA 独立复验 V1~V5）

> 仪器**按内容锚定，严禁按行号**（**GEN-TEST-008**）。行号仅用于本文档描述。

- **V1 纯注释新增（本批核心判据，最强）**：`git diff --numstat <本批提交>^..<本批提交>` 对 6 个 `.cs` 文件应满足：
  - **删除行数 = 0**（每个文件第二列均为 `0`）；
  - **所有新增行（`+` 侧）`lstrip()` 后必须以 `///` 开头**——注释批**不可能**出现代码改动，任何一行非 `///` 的新增都是异常。
  - 建议实现：`git diff -U0` 取新增行做逐行前缀断言。
- **V2 编译**：`dotnet build 源码工程/DwgToPdf.csproj -c Release` → **0 警告 0 错误**（**特别核 CS1570「XML 注释格式错误」必须为 0**）；另做一次 `-t:Rebuild`。
- **V3 断言**：SelfTest **必须** `--no-incremental` 重建 → **120 PASS / 0 FAIL** + `[PASS] 引擎新鲜度`（**GEN-WIN-013 / PRJ-002**）；控制台 **GBK** 解码（**GEN-WIN-002**）。
- **V4 覆盖率复算 = 100%（本批硬指标）**：QA **自写脚本**独立复算，须遵守 §2.1 的两条口径（**字段不计入**；**只看第一个 `=` 之前的文本判断是否方法**）。判据：附录 A 的 33 处**全部**已有紧邻 doc，且 7 个目标文件的方法/属性覆盖率为 **100%**。**必须报告分母（方法/属性总数）**，便于与本文档的 78 / 45 / 33 对账。
- **V5 字节形态 + 全量回归**：6 个 `.cs` 均**无 BOM + CRLF**；E2E 全量回归 **113 成功 / 2 失败**（2 张 H77 风扇件为既有失败；基线 115 张）。
- **附加**：发布 exe md5 应等于 `源码工程/bin/Release/net48/DwgToPdf.exe` 的 md5（QA 强制重建后须 `cp -p` 还原）。

---

## 8. 任务分批（工程师 T1~T4）

- **T1 补注释**：按**附录 A** 的 33 处精确坐标逐条补 `<summary>`（读方法体后再写，遵 §4 全部规范）。
- **T2 自验**：编译（**0 警告 0 错误**）+ SelfTest `--no-incremental` 重建 + 120 断言 + **自跑 V1**（删除行数=0、新增行全为 `///`）+ **自跑 V4**（覆盖率复算，报分母）+ 报字节形态与各文件行数。
- **T3 文档 + 发布 exe**：文档四件套「**修订十四**」（手册新增 **§5.18**、`版本变更记录.txt`、`说明.txt`、`发布程序 v4.17/运行说明.txt`，**各自字节形态照旧**）+ **重建发布 exe 并补刷**（记 md5 + `cmp`）。
- **T4 提交**（**只提交、不推送**）：建议拆两个提交——实施（注释）与补刷（发布 exe）。

---

## 9. 风险与对策

| 风险 | 对策 |
|---|---|
| **低信息量注释**（复述方法名）——本批最大风险 | §4 硬要求 + D5；**QA 抽查判定** |
| XML 未转义 → CS1570 → 破坏 0 警告基线 | §4 第 4 条 + D6；V2 专门核 CS1570 |
| 顺手改了代码（注释批最不该发生） | **V1 双判据**：删除行=0 + 新增行全为 `///` |
| 覆盖率统计口径不一致导致数字打架 | §2.1 两条口径写死；V4 要求报分母对账 |
| 给私有字段加 doc 造成噪音 | D3 明确排除 |
| 行号漂移导致清单失效 | **GEN-TEST-008**：内容锚定；附录 A 的签名可作内容锚 |
| 发布 exe 未同步 | D8：写入 T3 |

---

## 附录 A 待补清单（33 处，基线 `f0b05eb` 实测）

> 行号为**基线快照**，仅供定位；仪器请按**签名内容**锚定。

### ConverterEngine.cs（6 处 —— 仅 public/internal）

| 行 | 类型 | 签名 |
|---|---|---|
| L476 | property | `public bool IsCancelled => _cancelRequested;` |
| L478 | method | `public void Cancel()` |
| L489 | method | `public void ResetCancel()` |
| L501 | method | `public void RunFiles(List<string> files, string outputFolder, IProgress<ProgressUpdate> progress, bool tight = …)` |
| L506 | method | `public void RunFiles(List<string> files, string outputFolder, IProgress<ProgressUpdate> progress, bool tight, …)` |
| L1452 | method | `internal static string UniquePdfPath(string outputFolder, string baseName)` |

### ConverterEngine.Plot.cs（1 处）

| 行 | 类型 | 签名 |
|---|---|---|
| L518 | method | `private void WorkerLoop(int wi, int[] next, List<string> files, int total, string outputFolder, IProgress<ProgressUpdate> …)` |

### MainForm.cs（16 处）

| 行 | 类型 | 签名 |
|---|---|---|
| L189 | method | `public MainForm()` |
| L415 | method | `private void StopRequested()` |
| L423 | method | `protected override bool ProcessCmdKey(ref Message msg, Keys keyData)` |
| L444 | method | `private void PushUndo(UndoEntry e)` |
| L453 | method | `private void UndoLast()` |
| L488 | method | `private void RefreshListUi()` |
| L513 | method | `private ItemStatus StatusFor(string path)` |
| L526 | method | `private void AddRow(string path, ItemStatus st)` |
| L585 | method | `private int FindRowInGrid(DataGridView grid, string file)` |
| L688 | method | `private void PickFolder(TextBox box, string desc)` |
| L764 | method | `private void Start()` |
| L831 | method | `private void RunCore(List<string> snapshot, bool isContinue)` |
| L995 | method | `private static string FormatElapsed(TimeSpan t)` |
| L1008 | method | `private void ApplyUpdate(ProgressUpdate u)` |
| L1056 | method | `private void OpenOutputFolder()` |
| L1125 | method | `private async System.Threading.Tasks.Task RunFirstUseSelfCheckAsync()` |

### MainForm.BuildUi.cs（1 处）

| 行 | 类型 | 签名 |
|---|---|---|
| L14 | method | `private void BuildUi()` |

### MainForm.Settings.cs（2 处）

| 行 | 类型 | 签名 |
|---|---|---|
| L13 | method | `private void LoadAppSettings()` |
| L89 | method | `private void OpenSettings(object sender, EventArgs e)` |

### MainForm.FileCollection.cs（7 处）

| 行 | 类型 | 签名 |
|---|---|---|
| L14 | method | `private void OnDragEnter(object sender, DragEventArgs e)` |
| L24 | method | `private void OnDragDrop(object sender, DragEventArgs e)` |
| L36 | method | `private void PasteFromClipboard()` |
| L68 | method | `private void AddFilesDialog()` |
| L104 | method | `private void AddFiles(IEnumerable<string> paths)` |
| L137 | method | `private void AddToBatch(List<string> batch, string path)` |
| L152 | method | `private void ClearList()` |

### MainForm.PhaseLog.cs —— **0 处**（10/10 已全覆盖）

---

## 附录 B 撰写示例（**示例仅供参考，工程师必须自己读方法体后撰写，不得照抄**）

> 编写本附录时，主理人第一版示例**凭印象写成、核对方法体后发现两处事实错误**（把 `UniquePdfPath` 的 `(2)` 半角括号写成了 `-2`；把 `RefreshListUi` 刷新的「三个」列表写成了「两个」）。这恰好说明 §4 第 2 条「禁止臆造行为」的重要性——**示例与正式注释都必须以方法体为准**。

```csharp
// ✗ 反例（禁止）：复述方法名，零信息
/// <summary>刷新列表界面。</summary>
private void RefreshListUi()

// ✓ 合格（依据 MainForm.cs L488 方法体撰写）：写清「做了什么 + 副作用 + 调用时机」
/// <summary>重建三个列表（待转换 / 成功 / 失败）的全部行：先清空三表并收起失败详情面板，
/// 再按 _pendingFiles 逐条 AddRow；同时刷新输入框提示文案（空清单 / 已添加 N 个）、
/// 两条进度条归零、两个「复制到文件夹」按钮的可用态，最后同步栏标题计数。
/// 由转换进度回调与撤销操作在 UI 线程调用。</summary>
private void RefreshListUi()

// ✓ 合格（依据 ConverterEngine.cs L1452 方法体撰写）：写清命名规则与易混淆点
/// <summary>为输出 PDF 取一个不冲突的完整路径。先确保 outputFolder 存在（v3.9.1 输出可能带
/// 子目录）；baseName 不含扩展名，返回路径已含 .pdf；同名时依次尝试
/// baseName(2).pdf、baseName(3).pdf …（半角括号、从 2 起）。
/// 注意与「PDF 子文件夹」的同名规则不同——后者用 "-N"（见 ResolvePdfSubFolder），两者勿混淆。</summary>
internal static string UniquePdfPath(string outputFolder, string baseName)
```

**库内既有范文（逐字引用，可直接对照质量标准）** —— `ConverterEngine.cs` L1469-1472 的
`ResolvePdfSubFolder`，它把「模板展开规则 / 缺省回落 / 去重策略 / 上限与兜底」四件事一次说清：

```csharp
/// <summary>v4.17 修订三：解析本轮 PDF 子文件夹名（模板展开 + 同名追加 -2/-3）。
/// startedAt 缺省时用当前时刻；模板为空/非法时回落 AppSettings 默认值/PDF文件。
/// 去重规则：目标名已存在（含空文件夹）就追加 "-N"，最多试到 999；
/// 999 仍占用则退回带纳秒的唯一名（避免覆盖任何已有批次）。</summary>
internal static string ResolvePdfSubFolder(string outputFolder, DateTime? startedAt, string template)
```

**关于 XML 转义**：若 summary 正文确需出现尖括号（例如说明泛型或比较规则），须写成
`&lt;` / `&gt;` / `&amp;`。**但更好的做法是避开**——用「半角括号」「尖括号」这类中文描述，
或用反引号包住（`List<string>`）而非写成裸 `<`，从源头消除 CS1570 风险。

---

## 补记（2026-10-16 实施后 · 过程异常与主理人接手）

### 过程异常：工程师 429 限流中断
工程师（寇豆码）**已写完全部 33 处注释**（T1 完成）后被平台 **429 频率限流**中断（配额
2026-10-17 17:08 重置），T2 自验 / T3 文档与发布 exe / T4 提交未能执行。主理人核验磁盘实况后
（沿用 architect 429 时「主理人接手」的既有裁决）**接手完成剩余步骤**。

### 主理人核验与接手结果（commit `354a7bf` + `3d52d4c`）
| 项 | 结果 |
|---|---|
| T1 完成度（中断后实测） | **33/33 全部完成**——7 文件方法/属性 doc 覆盖率 **78/78 = 100%** |
| V1 | `git diff --numstat` 六文件**删除行全为 0**；`-U0` 新增行**逐行以 `///` 开头**（非 `///` 新增行数 = 0） |
| D5 质量抽查 | 逐条通读 33 条；重点核 `PushUndo`（MaxUndo=50、RemoveAt(0) 先进先出）/ `UndoLast`（Added→RemoveAll、Cleared→按序恢复）/ `FormatElapsed`（h>0 → h:mm:ss，否则 mm:ss）/ `Start`（RunSelection.FilterTodoFiles + done）/ `ProcessCmdKey`（非转换中 Ctrl+V/Ctrl+Z、true 阻断、余交基类）——**五条与实现逐项吻合，无臆造** |
| V2 | 编译 **0 警告 0 错误**（**CS1570 = 0**） |
| V3 | SelfTest `--no-incremental` 重建 → **120 PASS / 0 FAIL / EXIT=0**，含 `[PASS] 引擎新鲜度`（GBK 解码） |
| V4 | 复算覆盖率 **78/78 = 100%**（分母与方法体口径同 §2.1；工程师中断前自算结果与主理人复算一致） |
| 字节形态 | 六 .cs 无 BOM + CRLF；手册无 BOM+CRLF；版本变更记录**双 BOM 保留**+CRLF；说明.txt / 运行说明.txt 有 BOM+CRLF |
| 文档四件套 | 手册新增 **§5.18**；版本变更记录（表行 + 关键事件 + git 里程碑三处）；说明.txt 第二十四节；运行说明.txt 第二十五节 |
| 发布 exe（D8） | 新 md5 **`b10441fccb42c5dcbe14190b361580c2`**（旧 = B5 的 `e0dff858…`）；`cmp` 逐字节 IDENTICAL |

### 备注
- **QA 独立复验暂缺**：平台配额 2026-10-17 17:08 才重置，QA 复验（V1~V5 + 全量回归 113/2）
  须待配额恢复后派发。**本批主理人已自跑 V1~V4 + 编译断言，唯「E2E 全量回归」未在本轮执行**
  （纯注释批，IL 不变，风险极低）——建议验收时明示此缺口，或等 QA 补跑后再收口。

---

（完）
