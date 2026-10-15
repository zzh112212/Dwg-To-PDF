# Handoff: DwgToPdf 工具 v4.17 修订三 —— 交付、版本回退、提交推送

交接简报。**接收方是零上下文的新会话，本文件必须自包含。**

- 更新时间：2026-10-15 13:57
- 计划文件：`.planning/task_plan.md`（阶段/决策/错误表）

## Task

把用户 2026-10-15 的三项反馈落地到 DwgToPdf 批量转 PDF 工具，**保持版本号 v4.17 不升版**；全量回归无新增失败、文档同步、发布目录 exe 与源码一致，并完成 git 提交与推送。当前状态：已交付已入库，等待用户实测验收。

## Context

- 工程根：`C:\Users\ZINOVIA\Desktop\ZZH\D盘\CAD\DwgToPdf 工具\Dwg-To-PDF\DwgToPdf 工具 v4.17\`
  - `源码工程/` —— 主程序（net48 WinForms，AutoCAD 2021 accoreconsole + COM 后期绑定）
  - `自测试工程/` —— SelfTest，**直接编译源码工程的白名单 .cs**（`SelfTest.csproj` 显式 Compile Include，不含 MainForm.cs）
  - `发布程序 v4.17/` —— 交付目录：DwgToPdf.exe + .config + extframe*.lsp + 公司图框标准库.json + 运行说明.txt + 运行日志/
- **Git 仓库实况**：`.git` 在 `Dwg-To-PDF/`（10-14 从个人 GitHub `zzh112212/Dwg-To-PDF` 克隆，非个人机原始库）。
  本机 git 身份已配置且与历史作者一致（`zzh112212 <2959338582@qq.com>`）。
  已提交并推送 `8974e8f`（main 与 origin/main 同步）。**公司电脑 HTTPS 可直推 GitHub**（个人机须 SSH 的结论不适用于公司网络）。
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
- `发布程序 v4.17/DwgToPdf.exe` —— 10-15 13:15 刷新，UTF-16LE 检索确认含 `v4.17`、无 `v4.18`
- `.planning/` —— findings / handoff / progress / task_plan 四件套（已入库；trae_report.md 已于 13:32 被用户手动删除，见 What Was Tried）

## Current State

- 三项需求全部实现并通过验证：
  ① 跳过已成功图纸（`--dirs`：done=3/todo=1，旧 PDF `LastWriteTimeUtc` 零改动 = 没重打）；
  ② 时间戳文件夹（`--dirp` 固定时刻两轮 → `PDF-09：08：07` + `PDF-09：08：07-2`；默认模板 `PDF-HH:mm:ss` → 全角冒号）；
  ③ UCS 条件复位（世界 UCS 图纸零开销：2.29s vs 2.29s）。
- 编译 0 警告 0 错误（主工程 + 自测工程）；SelfTest **108/108 PASS、退出码 0**。
- 全量回归 115 张 → **113 成功 / 2 失败**（2 张 H77 风扇件为既有问题，与手册第30章基线一致）；113 张 PDF 墨迹测量：0 空页、最小边距 5.40mm、无 <3mm。
- 版本号回退完成（用户 13:09 明确「本轮的改动不升版本」）：93 处 v4.18 标注全部改为「v4.17 修订三」；exe 13:15 重新发布并核验。
- **git 已闭环**：提交 `8974e8f`（138 文件，+5989/-5440：v4.17 全量 82 文件入库，其中 48 个文件被 git 识别为 v4.15→v4.17 重命名、50 个纯删；.planning 四件套 + 手册）并推送，main 与 origin/main 同步。
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
  （11 条正式条目：GEN-WIN×3 / GEN-TEST×2 / GEN-GIT×2 / PRJ×4，`lessons.py check`/`toc` 通过；
  文件在工作区根、仓库 `Dwg-To-PDF/` 之外，不随仓库提交）

## Constraints

- 不得改变图纸内容；源图纸只读（探针前复制到 ASCII 临时目录）。
- 升版本号必须经用户同意（E12 教训）。
- 主工程重建后必须重建 SelfTest 再跑断言，否则引擎新鲜度守卫 FAIL（E13）。
- Windows 工程文件（.cs/.txt）改写必须保留原 CRLF 与 BOM。
- 只改代码不改手册的改动不予验收（用户规则）。
- 推送远端 = 用户个人 GitHub，仅在其明确指令下执行。

## Open Questions

1. （已关闭）v4.17 修订三实测验收：用户 10-15 确认「通过」，正式经验条目已写入
   `experiences/lessons.md`（11 条；dev-lessons 流程闭环）。
2. （已澄清）「工作日志」不存在独立文件，`运行日志/DwgToPdf_后台日志_*.txt` 由程序自动追加，无需手工同步。
3. **下一节点**：用户将提「下一版本优化计划」——需求到达前不动手（用户规则：95% 把握再执行）；
   新版本开工前先 `lessons.py view` 查经验库并过一遍 PRJ-001~004。
