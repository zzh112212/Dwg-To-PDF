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
