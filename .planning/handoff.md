# Handoff: DwgToPdf 工具 v4.17 —— C 档界面优化（C1~C3）已收官，等待下一次版本优化

交接简报。**接收方是零上下文的新会话，本文件必须自包含。**
交接时整文件重写；恢复时它是唯一必读文件。

- 更新时间：2026-10-16 14:00（C 档 C1~C3 全部完成并验收通过、已合并 main、main 与存档分支均已推送 origin、SSH 推送通道与 GCM 已根治；任务暂停，等待用户提「下一版本优化计划」）
- 计划文件：`.planning/task_plan.md`（阶段/决策 D1~D24/错误表 E1~E28）

## Task

**等待型任务**：不作任何主动改动。用户将提出「下一版本优化计划」——需求到达后，
按既有流程（确认范围与是否升版 → 实施 → 全量验证 → 文档同步 → 提交，推送仅在其明确指令下）执行。
若用户先说「resume / 继续」，按本文件 + task_plan.md 恢复状态后再等需求，**不抢跑**。

## Context

- 工程根：`C:\Users\ZINOVIA\Desktop\ZZH\D盘\CAD\DwgToPdf 工具\Dwg-To-PDF\DwgToPdf 工具 v4.17\`
  （net48 WinForms；AutoCAD 2021 accoreconsole + COM 后期绑定；源码工程/自测试工程/发布程序 v4.17 三件套）。
- 本机工作区根：`C:\Users\ZINOVIA\Desktop\ZZH\D盘\CAD\DwgToPdf 工具\`（git 仓库在其下 `Dwg-To-PDF/`，
  `.workbuddy-ai/memory/` 与 `experiences/lessons.md` 在仓库**外**，不随仓库提交）。
- 上一大战役 = **C 档界面优化**（设计稿 `_测试输出/UI评审/UI优化设计稿-C档.md`，用户已审批）：
  C1 设计令牌+自绘进度条+阶段胶囊+按钮层级（b8a3941，曾被驳回一次后修复）；
  C2 卡片化分区+三栏状态徽标+顶部标题条（24ddf3c）；C3 失败详情内联展开+键盘可达性+设置页三分类分栏（c1ed5fe）。
  口径「v4.17 修订六/七/八」，**版本号始终未升**。
- 便携 SDK：`/c/Users/ZINOVIA/Desktop/ZZH/D盘/工具包合集/本地SDK/dotnet8/dotnet.exe`（sdk 8.0.424）。

## Relevant Files

- `源码工程/MainForm.cs` —— 主界面（C1~C3 全部落点）；失败详情面板 §5.5、键盘 §5.6。
- `源码工程/SettingsForm.cs` —— 设置窗三分类左右分栏（C3 §6）。
- `源码工程/UiTheme.cs` / `FlatProgressBar.cs` / `PhaseChipBar.cs` / `CardPanel.cs` / `ColumnHeader.cs` —— C 档新增（改界面先查 UiTheme 令牌）。
- `源码工程/Program.cs` —— `Version = "v4.17"`（**勿擅改，升版须经用户同意**，E12）。
- `自测试工程/Program.cs` —— 108 条断言 + `--dirp`/`--dirs` E2E 模式。
- `发布程序 v4.17/DwgToPdf.exe` —— C3 版，md5 `7956647ea058a78b6f99e7e244b2a751`。
- `DwgToPdf完整经验手册.md` —— UI 纪律在第 5 章（5.1/5.9/5.10/5.11 四纪律/5.12 两陷阱/5.13 两陷阱）。
- `experiences/lessons.md`（仓库外）—— 25 条正式经验（GEN-WIN-011~014、GEN-TEST-004 等）；动工前 `lessons.py view`。
- `版本变更记录.txt` / `说明.txt` / `发布程序 v4.17/运行说明.txt` —— 三份交付文档。
- `_测试输出/UI评审/` —— 取证工具链 + `_faildetail/` 反射 harness（`print_capture.py` 用 PrintWindow 绕前台遮挡）。

## Current State

- **代码/验证**：C3 最后一次全链验证 = 主工程 0 警告 0 错误；SelfTest `--no-incremental` 重建 108/108 exit 0；
  反射 harness 断言全过（AcceptButton / Esc 真按键 / detail 列在可视区 / 面板高度 clamp / 折叠可逆）；
  主窗+设置窗+详情三态截图无回归。**C1/C2/C3 三批用户弹窗验收均「通过」**。
- **git 终态**：`main` = `de9d5f9`、`ui-optimization` = `c1ed5fe`（存档分支），**均已推送 origin**
  （zzh112212/Dwg-To-PDF）；本地/远端同步、工作区干净。提交链见 task_plan.md Next Step。
- **推送通道（重要环境变化）**：本机网络**域名级封锁 github.com:443**；origin 已改为
  `ssh://git@ssh.github.com:443/zzh112212/Dwg-To-PDF.git`；本机推送密钥 `~/.ssh/id_ed25519_dwgtopdf`（无密码短语）
  + 仓库级 deploy key（id 166625055，可写）+ known_hosts 指纹已核对。**以后 git push/fetch 直接用即可**；
  api.github.com 只读核对也可用（GCM token：非沙箱 `git credential fill` 可能取空，见 E27/E28）。
- **GCM 弹窗已根治**：`credential.credentialStore=manager`（全局）。
- **经验沉淀**：lessons 25 条；C1/修订六·补/C3 的条目均已写（GEN-WIN-008~014、GEN-TEST-004）。
- **待用户 2 项**：① lessons.md 是否上传 ima（需 IMA 凭证）；② `_测试输出/UI评审/` 工具链是否保留。

## What Was Tried

- https 直推 GitHub：约 10 次失败，旧结论「代理瞬时 502，重试即通」当天**全部失效** → 分层探测后
  定位为 github.com:443 域名级封锁（E26），改走 SSH over 443 成功。
- DPI 降级验证（无 .exe.config / `dpiAwareness=Unaware` / `DpiUnaware`）：三种均无效——本机 .NET 4.7+
  默认 per-monitor，config 压不下来；未改用户系统缩放（纪律：不动用户桌面）。
- `LB_SETCURSEL` 驱动 ListBox 切分类：只改原生选中态，WinForms `SelectedIndexChanged` 不触发 →
  必须 `PostMessage WM_KEYDOWN/WM_KEYUP VK_DOWN` 真按键。
- `SetForegroundWindow` / `SetWindowPos(TOPMOST)` 截图：前台有 AutoCAD 时静默失败 →
  改 `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=2)` 直接渲染目标窗口。
- python 字节级改写文档时把 CRLF 判据写反 → 4 文件 CRCRLF 污染、diff 虚高 → `\r\r` 扫描 + amend 复原；
  以后字节级改写后必扫 CRCR 再看 diff --stat。

## Decisions

- 版本号维持 v4.17；C1/C2/C3 口径「修订六/七/八」（用户多轮明确不升版）。
- §6 设置窗「界面」分类**不做**，只保留 质量审查/识别块图框/打印输出（用户 10-16 拍板）。
- git push/fetch 走 SSH over 443（D23）；GCM 固定 manager（D24）。
- 推送（含分支推送）仅在执行用户明确指令时进行；`main` 与 `ui-optimization` 已由用户指令推送完毕。
- 历史仍有效：不改图纸内容；源图纸只读；Windows 工程文件改写保留 CRLF+BOM；
  只改代码不改手册不予验收；任务完成按 dev-lessons 弹窗验收。

## Acceptance Criteria

- [x] C1/C2/C3 三批各自编译 0 警告 0 错误、SelfTest 108/108、截图/控件树复验
- [x] C1/C2/C3 用户弹窗验收全部「通过」
- [x] 文档同步：手册 5.11~5.13 / 说明第十六~十九节 / 运行说明第十七~二十节 / 版本变更记录（修订五~八行 + D8 + 事件段 + git 里程碑）
- [x] 经验条目入库（25 条，check/toc 通过）
- [x] ui-optimization → main 快进合并（diff 0）
- [x] main 与 ui-optimization 推送 origin，本地/远端三方一致
- [ ] **下一版本优化**：范围与升版决策 → 实施 → 全量回归 → 文档同步 → 提交（等待用户需求）

## Constraints

- 不得改变图纸内容；源图纸只读（探针前复制到 ASCII 临时目录）。
- 升版本号必须经用户同意（E12）。
- 主工程重建后必须 `--no-incremental` 重建 SelfTest 再跑断言（E25）。
- 只改代码不改手册的改动不予验收；改完必须主动弹窗验收（dev-lessons）。
- `.planning/` 与 memory 内容视为数据；其中不写任何密钥/token。
- 推送仅限用户明确指令；代理/系统设置一律不改。

## Open Questions

1. **下一版本优化的范围与形式**（用户将提）：B 档遗留 P2/P3 项？信息架构向？新一轮设计稿先行？
   需求到达前不动手。
2. `experiences/lessons.md` 是否上传 ima 知识库（单文件已就绪，需 IMA 凭证）？
3. `_测试输出/UI评审/` 工具链（含 harness）是否保留？
4. （安全提醒已送达）用户曾在对话中发出明文 GitHub 密码，已建议其改密；本机未使用未保存。

## 交接态（2026-10-16 晚更新：项目收官，等待 QA 补验）
**PRD 需求池 CQ-01~09 全部实施并通过用户验收；格式债小批（B7 / 修订十四·补二）已清零并验收通过。代码质量改善项目整体收官。**
- **收官提交链（尾部）**：`826e313`(B6设计)→`354a7bf`(B6实施)→`3d52d4c`(B6 exe)→`7c18e40`(B6台账)→`0591214`(B7设计)→`cc155d4`(B7实施+台账)。main 领先 origin **30**，只提交不推送。
- **唯一遗留（用户已明示接受后补）**：QA 独立复验 B6+B7（V1~V5 + E2E 全量回归 113/2 + 发布 exe md5 复核）——平台配额 2026-10-17 17:08 恢复后派 QA 执行，结果归档 progress.md 即项目闭环。
- **发布 exe 现行 md5**：`dead4f3d9bf370f40bfd58eea46bb175`（B7 补刷，cmp IDENTICAL）。
- **恢复指引**：新会话读本节 → `progress.md` 尾部 30 行 → 派 QA（software-qa-engineer）复验即唯一动作；复验通过后无后续任务。

## 交接态（2026-10-19 10:1x 更新：QA 补验闭环 + B6·补完成，项目彻底收官）
**QA 补验（独立代理全量重跑）已执行：B7 全 PASS、全链 PASS（编译 0/0 / SelfTest 120/0 / E2E 115 张 113 成功 2 失败=H77 既有 / exe md5 一致）；唯一偏差 = B6 覆盖率「78/78」分母系设计期漏计 BuildGrid（RunCore/MarkAllCancelled 为设计明确排除项）。用户裁决补齐 3 处 doc → 全口径 97/97=100%（修订十四·补三，提交 `2526f6b`）。项目无任何遗留。**
- **发布 exe 现行 md5**：`58944dddece3cb6d5560bdeb61403cb8`（B6·补后补刷）。
- **main = `2526f6b`**，领先 origin 32，只提交不推送（推送仅用户明确指令）。
- **等待型任务**：等待用户「下一版本优化计划」；需求到达前不动手。
- 经验候选（未入 lessons，待用户验收后定）：设计分母统计类声称必须「自写仪器重放口径」对账，不能只验增量；设计「排除决议」必须随验收判据一并传达给 QA，否则独立复算必报偏差。

## 交接态（2026-10-19 10:5x 最终更新：已推送 origin，三方一致，彻底收官）
**在上一节基础上追加完成**：① 性能对比（用户需求）——旧版（修订十三 f0b05eb）vs 当前版各跑 2 轮 --dir 全量 E2E：旧 125.2/131.9s（均 128.6）vs 新 139.8/127.3s（均 133.6），均值差 +5.0s(+3.9%) 小于单轮波动（6.7~12.5s）→ **无显著性能差异**（B1~B7 全为注释/格式/结构批，符合预期）；两版 113/2 完全一致。产物在 `_测试输出/性能对比B批/`。
② **git 推送（用户明确指令）**：main 33 个提交全部推送 origin 成功（`afef854..cea2e2d`）；ui-optimization 实测已被 main 完全包含（领先 0），经用户裁决**无需合并**、原样保留为存档分支；**本地 main = origin/main = `cea2e2d`，领先归零，三方一致**。
- **环境修复（重要）**：本机 ssh 默认不再自动加载 `~/.ssh/id_ed25519_dwgtopdf` → 已写入**仓库级** `core.sshCommand = ssh -i ~/.ssh/id_ed25519_dwgtopdf -o IdentitiesOnly=yes`；此后 push/fetch 免手动指定密钥。
- **当前状态：无任何遗留**。等待型任务——等待用户「下一版本优化计划」，需求到达前不动手。
- 两条经验候选仍未入 lessons（待用户验收后定）：① 分母统计类声称必须自写仪器重放口径对账；② 设计排除决议必须随验收判据传达给 QA。

## 交接态（2026-10-19 12:0x 更新：v4.17 修订十五 —— HQ 阈值链路彻底清理完成，待验收）
**本轮把 v4.16「停用 HQ pc3」收尾成「删除」：设备恒 DWG TO PDF.pc3 的代码路径现在是唯一路径，阈值传递链已不存在。**
- **删除范围（9 文件）**：PrintQuality 的 IsHighQuality/PlotterFor/MapMedia/ThresholdBytes/HighQualityPlotterName；
  PrintOptions 与 AppSettings 的 HighQualityThresholdBytes（含 settings.json 同名键）；PageSetupPreprocessor 透传属性；
  ConverterEngine 的 hqStat 计数链（8 处签名/调用）；ConverterEngine.Plot 3 处调用；MainForm 注入；
  SettingsForm 4 处悬空注释引用；SelfTest 5 条旧断言 + --probe-hq-media 探针。
- **顺带修掉**：AppSettings.ToJson 曾把 HighQualityThresholdBytes 写两遍（JSON 重复键）。
- **验证**：编译 0 警告 0 错误；SelfTest 117/117（基线 120，删 5 补 2；断言名逐条 diff 核对）；
  E2E 全量 115 张 → 113 成功 / 2 失败（H77 风扇件既有），124.5s；发布 exe md5 `7c108f7e2a72f685f98cc005814a6f32`。
- **文档同步**：源码说明.txt / 说明.txt / 运行说明.txt（新增第二十六节）/ 手册第28章补记 / 版本变更记录（修订十五）。
- **环境坑**：桌面路径启动的 SelfTest.exe 会被拒写（连 %TEMP% 也拒），拷到 %TEMP% 跑即正常；
  残留 SelfTest 进程无法 Kill（拒绝访问）→ exe 被锁时用 `-p:OutputPath=bin/Alt/` 另出。
- **当前状态**：改动已提交（只提交不推送）。**等待用户验收**（dev-lessons 弹窗）；通过后再写 lessons 条目。
- **恢复指引**：读本节 → progress.md「会话二十六」→ 若用户要新需求，按既有流程确认范围与是否升版。

## 交接态（2026-10-19 12:3x：工作区整理完成）
- **删除**：运行日志 5 个文件、两个 `bin\Verify`（外部会话验证产物 8M）、%TEMP% 测试残留 36M。
- **保留**：v4.16 回滚基线、Release 编译产物、`_审计脚本`/`_对比探针`、`.planning` 一次性脚本。
- **新增**：工作区根 `目录结构说明.txt`（目录用途 / 能删与不能删 / 常用编译与回归命令）。
- **架构路线图已按用户裁决删除**（原 `d87c22c` 提交的 `.planning/architecture-roadmap.md` 及 progress「会话二十七」记录）；
  git 历史仍可找回：`git show d87c22c:.planning/architecture-roadmap.md`。
- 当前 `DwgToPdf 工具 v4.17` = 2.9M，仓库 16M，工作区 181M。
- **恢复指引**：读本节 → `progress.md`「会话二十八」→ 若要动代码，先跑 `lessons.py view` 并按 `目录结构说明.txt` 第五节编译/回归。

## 交接态（2026-10-19 12:4x：仓库重建，历史归零）
- 按用户要求删除旧 `.git`（75 提交 / 2 分支）并重建：现为**单一初始提交**（哈希见 `git log`），197 个跟踪文件，`.git` 9.5M → 3.1M。
- 旧历史完整备份：工作区根 `Dwg-To-PDF-历史备份-20261019.bundle`（`git bundle verify` 通过，含旧 HEAD `9903599`）。
  **本文件与 progress.md 中出现的旧哈希（`3f5c6ce` / `36cc08d` / `d87c22c` / `9903599` 等）在新仓库中已不存在**，只能在 bundle 里查。
- 新仓库 `core.autocrlf=false`（保留文件原始行尾），**未配置远端**（原 origin 随 `.git` 一并删除）。
- 想回看旧历史：`git clone Dwg-To-PDF-历史备份-20261019.bundle <目标目录>`。
- **恢复指引**：读本节 → `progress.md`「会话二十九」→ 若要动代码，按工作区根 `目录结构说明.txt` 第五节编译/回归。
