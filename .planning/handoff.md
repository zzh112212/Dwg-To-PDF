# Handoff: DwgToPdf 工具 v4.17 修订八（C 档 C3：失败原因内联展开 + 键盘可达性 + 设置页三分类分栏）—— 待验收

交接简报。**接收方是零上下文的新会话，本文件必须自包含。**

- 更新时间：2026-10-16 11:0x（C3 已落地、已验收通过、已独立 commit【未 push】；C1/C2 经验已入库，C1 自身经验条目仍待用户确认后补写）
- 计划文件：`.planning/task_plan.md`（阶段/决策/错误表）

## Task

把用户 2026-10-15 的三项反馈落地到 DwgToPdf 批量转 PDF 工具，**保持版本号 v4.17 不升版**；全量回归无新增失败、文档同步、发布目录 exe 与源码一致，并完成 git 提交与推送。当前状态：**C1 已落地 → 用户验收驳回（进度条「不是流动状态」）→ 修订六·补已修好并验证，待重新验收**；用户已拍板「先修 `--dirp` 断言、修完继续 C2」（分支 `ui-optimization`，未推送）。
C 档 = 界面优化第二轮（设计稿 `_测试输出/UI评审/UI优化设计稿-C档.md`，用户已审批），分 C1/C2/C3 三批，每批一个 commit、可单独 revert。C1 = UiTheme 设计令牌 + FlatProgressBar 自绘进度条（带百分比）+ PhaseChipBar 阶段胶囊（五态）+ 按钮两级层级，**纯视觉、不升版本**。

## Context

- 工程根：`C:\Users\ZINOVIA\Desktop\ZZH\D盘\CAD\DwgToPdf 工具\Dwg-To-PDF\DwgToPdf 工具 v4.17\`
  - `源码工程/` —— 主程序（net48 WinForms，AutoCAD 2021 accoreconsole + COM 后期绑定）
  - `自测试工程/` —— SelfTest，**直接编译源码工程的白名单 .cs**（`SelfTest.csproj` 显式 Compile Include，不含 MainForm.cs）
  - `发布程序 v4.17/` —— 交付目录：DwgToPdf.exe + .config + extframe*.lsp + 公司图框标准库.json + 运行说明.txt + 运行日志/
- **Git 仓库实况**：`.git` 在 `Dwg-To-PDF/`（10-14 从个人 GitHub `zzh112212/Dwg-To-PDF` 克隆，非个人机原始库）。
  本机 git 身份已配置且与历史作者一致（`zzh112212 <2959338582@qq.com>`）。
  已提交并推送至 `f1c173a`（8974e8f → 3c6937c → ea50c40 → 76b9be4 → f1c173a，main 与 origin/main 同步）。**公司电脑 HTTPS 可直推 GitHub**（个人机须 SSH 的结论不适用于公司网络）；推送偶发失败（schannel 中断 / 代理 CONNECT 502 on github.com，同代理 api.github.com 仍通 200）属瞬时态，原样重试即通，勿急着改代理配置。
- 权威文档：`Dwg-To-PDF/DwgToPdf完整经验手册.md`（第31章 UCS 根因、第32章 修订三）；版本沿革看 `说明.txt` 第十三/十四节。
- 便携 SDK：`/c/Users/ZINOVIA/Desktop/ZZH/D盘/工具包合集/本地SDK/dotnet8/dotnet.exe`（sdk 8.0.424）。
- 用户项目规则：不明确先问（95% 把握再动手）；回复必须中文；只改代码不改手册不予验收；任务完成后需按 dev-lessons 流程弹窗验收。

## Relevant Files

- `源码工程/ItemStatus.cs` —— `RunSelection.FilterTodoFiles`（跳过已成功规则；放这里是因为 SelfTest 编译白名单不含 MainForm.cs）
- `源码工程/AppSettings.cs` —— `PdfFolderTemplate` 默认 `"PDF-HH:mm:ss"`；`ExpandPdfFolderTemplate`（半角冒号 → 全角「：」再过滤非法字符）
- `源码工程/ConverterEngine.cs` —— `ResolvePdfSubFolder`（纯名称解析；同名追加 -2/-3 至 999）
- `源码工程/PrintOptions.cs` —— `PdfFolderTemplate` / `RunStartedAt(?DateTime)` 字段
- `源码工程/MainForm.cs` —— `Start()` 委托 FilterTodoFiles；`OpenOutputFolder()` 开最新时间戳文件夹
- `源码工程/CadConverter.cs` / `PlotSession.cs` / `PageSetupPreprocessor.cs` —— UCS 条件复位（`WORLDUCS==1` 直接 return false）
- `源码工程/Program.cs` —— `Version = "v4.17"`（**勿擅改，升版须经用户同意**）
- `自测试工程/Program.cs` —— 108 条断言 + `--dirp`/`--dirs` E2E 模式 + `FindPdfFolder`
- `发布程序 v4.17/DwgToPdf.exe` —— 10-16 10:3x 随 C3 刷新（列宽 140/54/50 + 详情面板高度 clamp + 键盘可达性），md5 `7956647ea058a78b6f99e7e244b2a751`（C2 版为 `413e22a5e7728e433639a0ad978f73da`）
- `.planning/` —— findings / handoff / progress / task_plan 四件套（已入库；trae_report.md 已于 13:32 被用户手动删除，见 What Was Tried）

## Current State

### 2026-10-16 10:4x：修订八（C 档 C3）—— **最新，优先看这里**

- **C3 范围**（用户 10-16 拍板）：§5.5 失败原因内联展开 + §5.6 键盘可达性 + §6 设置页
  左右分栏；**§6「界面」分类不做**，只保留 质量审查 / 识别块图框 / 打印输出 三分类，
  不新增设置项、不改 AppSettings.cs。仍为纯交互 + 布局，不升版本号。
- **C3 内容**：
  · §5.5 note 列截断摘要（`Truncate(full, 32)`，全文存 `_noteByPath` 不受影响）；
    失败栏新增「详情 ▸」链接列 → Dock=Bottom 只读详情面板（完整原因 + 日志尾部 20 行），
    不弹窗；同行再点收起、行移走/清空自动收起；**详情面板高度按失败栏剩余空间 clamp
    （标称 132，最低保 44）**。
  · §5.6 `开始转换(&S)`（Alt+S）+ AcceptButton + KeyPreview/Esc→停止 + 显式 TabIndex。
  · §6 设置窗 480×350 → 560×400；左侧 132px 自绘 ListBox 三分类，右侧三页共存切 Visible。
- **两个实测坑**（已修，手册 5.13）：① 失败栏最窄 264 逻辑像素，三列最小合计
  170+70+62=302 溢出 → 横向滚动条把「详情 ▸」滚出可视区 → 改 140+54+50=244；
  ② 详情面板固定 132 高于失败栏约 126 → 表格压成 0 高、面板 Y=-18 溢出 →
  高度按容器剩余 clamp。② 与 GEN-WIN-010 同族不同维度（010 管窗口 MinimumSize，此条管容器高）。
- **验证**：主工程 0 警告 0 错误；**强制重建** SelfTest（`--no-incremental`，否则增量
  不重编、引擎新鲜度守卫假 FAIL）→ 108/108 exit 0；反射 harness（`_测试输出/UI评审/_faildetail/`）
  断言 AcceptButton / Esc 真实按键 / detailInView + hscroll=0 / clamp 全过；
  主窗 + 设置窗（三分类）+ 展开详情截图复验无回归；
  exe 重发布 md5 `7956647ea058a78b6f99e7e244b2a751`。
- **下一步**：① 正式经验条目已写（GEN-WIN-011~014 + GEN-TEST-004，25 条）；②
  （C1/修订六·补/C3 三批的条目都挂在这一次；C1 历史教训两件：`LB_SETCURSEL` 不驱动
  WinForms ListBox、96 DPI 本机无法降级——.NET 4.7+ 默认 per-monitor）；
  ③ C3 已独立 commit（未 push）；用户未明确指令前不 push。


### 2026-10-16 09:5x：修订七（C 档 C2）—— 已验收通过、已提交 `24ddf3c`（未推送）

- C2 = 卡片化分区（CardPanel）+ 三栏状态点/计数徽标（ColumnHeader）+ §5.7 设置移位
  （客户区顶部标题条 40px，操作条 5 列 → 4 列）；窗口默认 900×760 / 最小 840×730。
- 验证：编译 0/0；SelfTest 108/108；67 控件 0 越界 0 零尺寸；125% 与 96 DPI 双路径
  截图复验；exe md5 `413e22a5e7728e433639a0ad978f73da`。
- 经验沉淀：GEN-WIN-008（DisplayRectangle 复刻 GroupBox 语义）/ 009（自绘控件父链底色穿透）/
  010（Dock 加固定高度条后复核窗口最小尺寸），lessons 20 条。


### 2026-10-16：修订七（C 档 C2）—— **最新，优先看这里**

- **`--dirp` 断言修复**（用户 10-15 拍板）：`自测试工程/Program.cs` 三处非递归
  `Directory.GetFiles` 加 `SearchOption.AllDirectories`；SelfTest 108/108 PASS；commit `3008bd7`。
- **C2 范围**（用户 10-16 拍板）：按设计稿 §7 表 = 卡片化 + 三栏状态点/徽标 + §5.7 设置移位；
  §5.7 用「新建客户区顶部标题条」；§5.5/§5.6/§6 → C3。
- **C2 内容**：新增 `CardPanel.cs`（自绘卡片容器，重写 `DisplayRectangle` 复刻 GroupBox 语义，
  标题高 16 → 各卡片 Height 零改动）与 `ColumnHeader.cs`（栏标题：状态圆点 + 计数徽标）；
  3 个 GroupBox 卡片化；页面底色 `#F5F5F3` + 卡片间隙 10px；列表/日志降级 Subtle 卡片；
  DataGridView 只调色；§5.7 顶部标题条（⚙ 设置）+ 操作条 5 列 → 4 列；
  窗口默认 900×760 / 最小 840×730。
- **两个实测坑**（已修，手册 5.12）：① 透明自绘控件底色沿父链穿到 `SplitterPanel` 的系统灰
  → 三栏容器 `BackColor` 显式设卡片白；② 加 Top 条 + 间隙后原最小尺寸装不下 →
  `_splitLog` 溢出 58 逻辑像素、**日志卡片被挤出可视区**（控件树能 dump 到、截图里没有）
  → 同步放大窗口尺寸。
- **验证**：编译 0 警告 0 错误；SelfTest 108/108 PASS；控件树 dump 67 控件 0 越界 0 零尺寸；
  125% 与 96 DPI 双路径截图复验通过；exe 重发布 md5 `413e22a5e7728e433639a0ad978f73da`。
- **下一步**：① 请用户验收 C2；② 通过后进 C3（设置页左右分栏 + 失败详情面板 + 键盘可达性）；
  ③ 提交 C2 独立 commit；用户未明确指令前不 push。


### 2026-10-15 17:5x 起：修订四 / 修订五（**最新，优先看这里**）

- **修订四（界面优化 B 档）**：已交付、已验收通过、**已提交** `4e9d2be`（10 文件 +280/-57），**未推送**。
  内容：P0 1 处（「自动滚动」开关被压成 108×3px 静默失效 → 交换 logHeader 的 Add 顺序）
  + P1 6 处像素错位 + 信息层级重组（按钮行独立为 actionBar）+ 阶段色正名 + 一致性 5 项。
- **修订五（修订四回归修复）**：已实施并验证，**待用户验收**，**未提交**。
  · 回归：修订四 P1-5 把选项区两行拆成嵌套 TableLayoutPanel，子表 Dock=Fill 落在 AutoSize 行内
    被撑到 955×125（100 逻辑像素高）→ 行 2（显示线宽/质量核查/识别块图框）被顶到 y=327，
    而「选项」分组只到 y=279 → **整行不可见**（`IsWindowVisible` 仍为 True，看不出来）。
  · 修复：放弃嵌套 → 单个 6 列栅格 `tblOptions`；行 2 装进 `FlowLayoutPanel`
    （AutoSize + WrapContents=false + Anchor=Left|Top）+ `tblOptions.SetColumnSpan(optRow2, 6)`。
  · 效果：行 2 回组内（y=327 → 240）；保留「配色：贴合下拉框（100px → 3px）」与「两行左缘对齐（均 x=22）」。
  · 验证：编译 0 警告 0 错误；SelfTest 108/108 PASS；控件树复测 + 与修复前逐行 diff（除选项区外零差异）；
    发布 exe 与构建产物 md5 一致（`82b6967…`），版本串 v4.17 / 无 v4.18。
  · 文档：手册 **5.10**、运行说明 **第十七节**、说明 **第十六节**、版本变更记录（修订五行 + D6）、
    经验库 **GEN-WIN-007**（16 → 17 条，`lessons.py check`/`toc` 通过）。
- **下一步（两件事，均待用户）**：① 验收修订五；② **确定第二次 UI 优化的范围**（用户只说了「开始第二次UI优化」，
  未给方向）。候选：补完 B 档遗留（P2-3/P2-7/P3-3/P3-8）｜信息架构向（P3-2/P3-6/P3-7）｜C 档深度改版｜先出设计稿。
- **取证工具链**（`_测试输出/UI评审/`，纯 ctypes / 标准库）：`capture_win.py`（截图 + 局部放大 + PostMessage 点按钮）、
  `dump_controls.py`（控件树 dump，**UI 缺陷的显微镜**）、`crop_zoom.py`（从已有 PNG 裁剪放大）。
  ⚠ 截图左缘约 9px 是窗口边框（已用像素分析确认边框在 x=9 / x=990，内容未被裁切），看图时别误判为「被裁」。
- **UI 相关权威条目**：手册 5.1（Percent 行挤掉 AutoSize 行）、5.9（实机取证替代目测）、
  5.10（AutoSize 行内放 Dock=Fill 子容器 → 行被顶出容器）；经验库 GEN-WIN-005/006/007、GEN-TEST-003。

### 修订三（历史）


- 三项需求全部实现并通过验证：
  ① 跳过已成功图纸（`--dirs`：done=3/todo=1，旧 PDF `LastWriteTimeUtc` 零改动 = 没重打）；
  ② 时间戳文件夹（`--dirp` 固定时刻两轮 → `PDF-09：08：07` + `PDF-09：08：07-2`；默认模板 `PDF-HH:mm:ss` → 全角冒号）；
  ③ UCS 条件复位（世界 UCS 图纸零开销：2.29s vs 2.29s）。
- 编译 0 警告 0 错误（主工程 + 自测工程）；SelfTest **108/108 PASS、退出码 0**。
- 全量回归 115 张 → **113 成功 / 2 失败**（2 张 H77 风扇件为既有问题，与手册第30章基线一致）；113 张 PDF 墨迹测量：0 空页、最小边距 5.40mm、无 <3mm。
- 版本号回退完成（用户 13:09 明确「本轮的改动不升版本」）：93 处 v4.18 标注全部改为「v4.17 修订三」；exe 13:15 重新发布并核验。
- **git 已闭环**：提交 `8974e8f`（138 文件，+5989/-5440：v4.17 全量 82 文件入库，其中 48 个文件被 git 识别为 v4.15→v4.17 重命名、50 个纯删；.planning 四件套 + 手册）并推送；其后 `3c6937c`（.planning 验收通过）、`ea50c40`（手册合并记录）、`76b9be4`（版本变更记录.txt，规则七.2）、`f1c173a`（版本记录笔误订正）、`27ff693`（.planning 收尾刷新）均已推送，main 与 origin/main 同步；本次交接刷新（E16/F16 入册 + 条目 11→12 + 计数同步）将再产生一个 .planning 提交，见 progress.md「会话六·交接刷新」。
- 阶段进度：8.6/8.6 完成（见 task_plan.md Phases）。

## What Was Tried

- 半角冒号直接走非法字符过滤 → `09:08:07` 被滤成 `090807` → 先 `Replace(':', '：')` 再过滤。
- `LatestPdfSubFolder` 按 token 固定顺序截前缀 → 模板 `dd-MM` 被 `MM` 提前截成 `dd-` → 改为取最小下标。
- `FilterTodoFiles` 最初放 MainForm → SelfTest 编译白名单不含该文件（CS0234）→ 搬到 `ItemStatus.cs`。
- SelfTest 断言写死旧文件夹名 `"PDF文件"` → 产品改名后假失败 → 新增 `FindPdfFolder` 动态定位；教训：先读日志再信断言。
- 顺手把 `Program.Version` 升成 v4.18 → 用户否决 → 93 处全批回退（task_plan E12）。
- Edit 工具曾在 `try {` 后插入 6 行垃圾致 CS1513 → 与上一版本 diff 定位损坏范围后立刻编译验证（E9）。
- **git 提交时的发现**：仓库实际在 `Dwg-To-PDF/` 而非工作区根；v4.15 删除为 10-14 会话中所为（父目录 mtime 定格 17:59，无日志）；
  `.planning/trae_report.md`（TRAE 协作期历史文件）系**用户 13:32:15 手动删除**，提交如实记录，未主动删任何历史文件——如需恢复：`git checkout HEAD~1 -- .planning/trae_report.md`（27609B 已验证可读）。

## Decisions

- 版本号维持 v4.17，本轮三项需求称「v4.17 修订三」（用户 10-15 明确：本轮的改动不升版本）。
- 时间戳文件夹默认 `PDF-HH:mm:ss`，分隔符用全角冒号「：」，同名追加 -2/-3，设置页可自定义。
- 已成功图纸默认跳过，不在设置里加开关。
- UCS 先探测再复位（`WORLDUCS` 只读、微秒级；实测零开销）。
- git 提交范围（用户确认）：包含 v4.15 删除，一个提交涵盖全部；只提交、经用户明确指令后才推送。
- 旧仍有效决策（理由见 task_plan.md）：不改图纸内容；提交身份用本机已配置的 zzh112212；Windows 工程文件改写保留 CRLF + BOM。

## Acceptance Criteria

- [x] 三项需求实现且有 E2E 证据（`--dirp` / `--dirs`）
- [x] 编译 0 警告 0 错误（主工程 + 自测工程）
- [x] SelfTest 108/108 PASS、退出码 0
- [x] 全量回归无新增失败（113/115 与第30章基线一致）
- [x] 版本号 v4.17，全项目无 v4.18 残留（历史记忆日志/运行日志除外）
- [x] 发布目录 exe/config 刷新，版本串核验通过
- [x] 五份文档同步（运行说明 / 手册第32章 / 源码说明 / 说明.txt / 自测试工程说明）
- [x] git 提交 `8974e8f` 并推送，main 与 origin/main 同步
- [x] **用户实测验收通过**（10-15 用户确认「通过」）；经验文件 `experiences/lessons.md` 已创建
  （13 条正式条目：GEN-WIN×4 / GEN-TEST×2 / GEN-GIT×2 / GEN-DOC×1 / PRJ×4，`lessons.py check`/`toc` 通过；
  文件在工作区根、仓库 `Dwg-To-PDF/` 之外，不随仓库提交）；10-15 14:05 已把完整经验手册全文
  合并为文末附录（约 257KB / 3400+ 行，供后续 ima 单文件上传；手册原文仍在仓库内维护）
- [x] 规则七.2：专门版本变更记录 `Dwg-To-PDF/版本变更记录.txt`（版本总账 + 关键决策 D1~D4 + git 里程碑）已建库提交（76b9be4），笔误订正（f1c173a）
- [x] 规则二.4：`experiences/说明.txt` 已建（目录定位/结构与文件用途/lessons.py view·check·toc·stats 用法/新增经验纪律/ima 上传计划；experiences/ 在仓库外，不随 git 提交）
- [x] ima 上传就绪：`experiences/lessons.md` 253,721 字节 ≤10MB，check 11 条全过；流程 preflight→check_repeated_names→create_media→COS→add_knowledge（需 IMA 凭证），时机由用户定

## Constraints

- 不得改变图纸内容；源图纸只读（探针前复制到 ASCII 临时目录）。
- 升版本号必须经用户同意（E12 教训）。
- 主工程重建后必须重建 SelfTest 再跑断言，否则引擎新鲜度守卫 FAIL（E13）。
- Windows 工程文件（.cs/.txt）改写必须保留原 CRLF 与 BOM。
- 只改代码不改手册的改动不予验收（用户规则）。
- 推送远端 = 用户个人 GitHub，仅在其明确指令下执行。

## Open Questions

1. （已关闭）v4.17 修订三实测验收：用户 10-15 确认「通过」，正式经验条目已写入
   `experiences/lessons.md`（13 条；dev-lessons 流程闭环）。
2. （已澄清）「工作日志」不存在独立文件，`运行日志/DwgToPdf_后台日志_*.txt` 由程序自动追加，无需手工同步。
3. **下一节点**：用户将提「下一版本优化计划」——需求到达前不动手（用户规则：95% 把握再执行）；
   新版本开工前先 `lessons.py view` 查经验库并过一遍 PRJ-001~004。
   另：用户择机把 `lessons.md` 上传 ima 知识库（单文件已就绪，流程见 Acceptance Criteria）。
