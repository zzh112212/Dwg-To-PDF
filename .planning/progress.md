﻿# 会话日志（追加式）

## 2026-10-07 会话一（v4.16 修复 + 根因定位 + 建仓）
- 研读手册第26章/速查表；实测 v4.15 产物页面 1682x2378(4A0) 而非参照的 2A0
- 实测四张对照图纸的组码70/LWDISPLAY/线宽直方图/图层线宽 → 推翻"只有这批关着线宽"的假设
- 修3 处：选纸容差、PlotWithLineweights、组码70 的 0x80；自测 34/34 + 78/78
- 8 张 PISCES 转换 8/8（104.4s），页面回到 2A0；公司版 40/40（88.8s）；源图 MD5 未变
- 建 Git 仓库（工作区根），入库 69 文件/1.05MB；排除 Git 本体、DWG 语料、v4.12/v4.14、PDF/日志
- 用户提供GUI 两份对照产物 → 量出「按显示」是矢量（3.2MB）、「着色」与工具同属光栅
- 扫 40 张产物统计矢量/光栅 → 发现判别式是 **OLE2FRAME 数量**
- 推翻我自己的"HQ 是元凶"假设（停用后仍光栅）；停用 HQ 仍保留（用户要求 + 快5 倍）
- 修 `pdfink.py` 测量 bug（PNG Predictor 15）→ 新增 `pdfruns.py`
- push 失败：github.com HTTPS 被重置（同一git 访问 baidu/gitee 正常）；SSH 22 通→已生成密钥
- 恢复 `.planning/` 四个文件（此前不存在），把结论全部落盘

## 2026-10-07 会话二（phase5：找矢量路径）
- 用户否决"隐藏 OLE"方案（等于改图纸内容）→ 改走 PLOT 界面/命令路线
- COM + 完整页面设置实测：仍光栅（28 图块@267dpi，1.56MB，比 accore 8.55MB 小但同性质）
- `Visible=true` 与 `false` 输出**字节数完全相同** ⇒ 界面模式不是因素
- `PlotType=acLayout` 在 Model 布局不可用
- 写 `run_plotcmd.ps1`：开日志 + SendCommand 发 `-PLOT` 抓真实提示序列
  → 卡在 `RPC_E_CALL_REJECTED`（AutoCAD 模态等待输入）
- **用户指出：这一步必须看屏幕，否则会一直卡住，需要 ESC 退出**；我确认自己看不到屏幕
- 用户建议改用 TRAE 的电脑操作技能；我改用本会话可用的 Computer Use 能力
- 按用户要求启用 context-continuity，把状态落盘到 `.planning\`

## 2026-10-14 会话（v4.17 修订一：交付版功能改进）
- 通用版识别提速：识别脚本改回 `(load)` 外部加载，单张识别约快 10%，整批阶段1 47.0s→31.9s（-32%）；
  识别结果与前一版逐张一致（0 张纸型差异）
- 质量审查由 4 维加严为 5 维：补"内容流为空 / 页尺寸短边不符 / PDF 被截断"三类漏判
- 输出目录自动清理：转换结束后删 frame_cache.json 与 *.log；日志改为仅实时面板显示（用户要求不落盘）
- 界面精简：设置页移除停用项、文案改短；主窗体移除"打开日志文件夹"按钮
- 已发布 exe 到 `发布程序 v4.17/`；版本号维持 4.17.0.0（未升 4.18：行为兼容修复不升版）
- 文档同步：说明.txt 第十三节、源码说明.txt、运行说明.txt、手册第30章

## 2026-10-15 会话一（v4.17 修订二：UCS 世界坐标系修复）
- 根因（控制变量实验确凿）：问题图纸 557 27MY-Y17369xx 三张保存了非世界 UCS；
  extframe.lsp 返回 WCS 图框，而 -EXPORT 窗口应答 / COM SetWindowToPlot 按当前 UCS 解释
  → 窗口整体平移 229.02,215.70 → 左裁 ~18%、底裁 ~25%、顶部留 318mm 空白
- 修复三处注入点：PlotSession.BuildScript（`_.UCS _World`）、CadConverter.TryResetUcsToWorld、
  PageSetupPreprocessor（精确模式设窗口前）
- 端到端 3/3 成功，墨迹 821.6×1161.6、边距 L9.7/R9.7/T13.6/B13.6；全量回归 113/115（2 张 H77 既有问题）
- 顺手修 MainForm 两个 tooltip 文案错误（出图配色实际只有黑白/彩色两项）
- 文档同步：手册第31章、说明.txt、源码说明、运行说明

## 2026-10-15 会话二（v4.17 修订三：三项需求 + 全角冒号 + 版本回退）
- 需求①跳过已成功图纸：规则落 `ItemStatus.cs` 的 RunSelection.FilterTodoFiles；MainForm.Start 委托
- 需求②时间戳 PDF 文件夹：默认模板 PDF-HH:mm:ss；ResolvePdfSubFolder 纯解析同名追加 -2/-3；
  「打开输出文件夹」改开最新时间戳文件夹；顺修 LatestPdfSubFolder 按 token 顺序截前缀的既有缺陷
- 需求③UCS 条件复位：WORLDUCS==1 直接跳过；实测零开销（2.29s vs 2.29s）
- 用户追加要求：时间分隔符圆点改冒号 → 半角冒号非法 → 映射全角「：」（PDF-09：08：07）
- 自测扩充：TestPdfFolderTemplate 16 条 + TestFilterTodoFiles 7 条 + --dirp/--dirs E2E；
  FindPdfFolder 替换全部写死 "PDF文件" 的断言；默认断言集 108/108 PASS
- **版本回退**：我顺手把 Version 升成 v4.18，用户否决（"本轮的改动不升版本，还是v4.17"）
  → Program.Version 改回 v4.17；93 处 v4.18 标注全部改为「v4.17 修订三」；手册"新增 24 条"计数纠错为 23 条
  → 重建主工程 + 自测工程（0 警告 0 错误），108/108 PASS，13:15 重新发布 exe（UTF-16 检索确认 v4.17 无 v4.18）
- 触发的两个机制认知：主工程重建后 SelfTest 变旧会触发引擎新鲜度守卫 FAIL（必须先重建自测再跑）；
  .NET 版本串在 exe 里是 UTF-16LE（ASCII grep 查不到）
- 交接：task_plan.md 重写为当前真实状态；handoff.md 整文件重写（10-07 PISCES 协作期内容已归档，
  结论以手册第 20~31 章为准）

## 2026-10-15 会话三（git 提交 + 推送）
- 仓库实况：Dwg-To-PDF/ 是 10-14 从个人 GitHub 克隆（HEAD 原为 8f6c7f2）；本机 git 身份已配置
  且与历史作者一致（zzh112212 <2959338582@qq.com>）
- 用户确认提交范围：包含 v4.15 删除，一个提交涵盖全部
- 新提交 8974e8f（138 文件，+5989/-5440）：v4.17 全量 82 文件入库（48 个文件被 git 识别为
  v4.15→v4.17 重命名、50 个纯删）+ .planning 四件套 + 手册；另记录 1 个删除：
  .planning/trae_report.md 系用户 13:32:15 手动删除，提交如实记录，可用
  `git checkout HEAD~1 -- .planning/trae_report.md` 恢复（27609B 已验活）
- 推送成功：8f6c7f2..8974e8f fast-forward，main 与 origin/main 同步；
  **公司电脑 HTTPS 直通**（个人机"HTTPS 被 Reset 走 SSH"的结论不适用于公司网络）

## 2026-10-15 会话四（验收通过 + 经验文件落盘 + 交接刷新）
- 用户确认 v4.17 修订三实测验收「通过」
- 按 dev-lessons 流程创建 `experiences/lessons.md`（项目首个经验文件：此前从未存在、git 历史也无记录，
  位置在工作区根 = 仓库 Dwg-To-PDF/ 之外，故不随仓库提交）：11 条正式条目
  （GEN-WIN×3 / GEN-TEST×2 / GEN-GIT×2 / PRJ×4），lessons.py check 通过、toc 目录重建
- .planning 四件套刷新（handoff 重写、task_plan/progress/findings 更新）并提交
- 下一节点：等待用户提下一版本优化计划（需求明确前不动手）

## 2026-10-15 会话五（手册合并进经验文件）
- 用户要求把 `Dwg-To-PDF/DwgToPdf完整经验手册.md`（v3.7~v4.17 全文，3259 行/243KB）合并进
  `experiences/lessons.md`，为后续上传 ima 做准备（ima 上传物 = 单文件）
- 合并方式：手册全文作「附录」追加在 lessons.md 文末；手册标题整体降两级
  （章 ##→####、节 ###→#####）——因为 `### ` 在 lessons.md 中专用于带 ID 的条目行，
  lessons.py check 会校验该格式；EOL 统一 CRLF（toc 在 Windows 文本模式写回的稳态）
- 结果：3384 行/253KB；check 通过（11 条正式条目无损）、toc 重建正常、附录占 96.7%
- 定位：条目库=速查索引（三要素），附录=完整证据链；原文仍在 git 仓库内维护，
  更新时以 lessons.md 为 ima 上传源
## 2026-10-15 会话六（收尾：规则台账补齐 + 版本记录入库 + 全量推送）
- 规则七.2：新建 `Dwg-To-PDF/版本变更记录.txt`（版本总账 v3.7~v4.17 修订三 + 关键决策 D1~D4 + 今日时间线
  + git 里程碑）→ 提交 76b9be4；自查发现 6 处笔误（根困×2 / 铉迟 / 落格 / 138→93 处标注 / 跳版本→逐版本；
  v4.16 行的「OLE光栅根困」实为后被推翻的假说，改为「OLE 光栅假说（后被组码 76 推翻）」）→ 订正提交 f1c173a
- 规则二.4：新建 `experiences/说明.txt`（目录定位/结构与文件用途/lessons.py 用法/新增经验纪律/ima 上传计划）
- 推送三连失败后成功：schannel 中断 → 代理 CONNECT 502（github.com 隧道不通，同代理 api.github.com 通 200）
  → 第三次原样重试即通；f1c173a 亦推，main 与 origin/main 完全同步
- lessons.py check/view 复核：11 条正式条目 + 手册附录完整（253,721 字节）
- 环境经验入 MEMORY.md 工程机制节：代理 502 是瞬时态，原样重试即可，勿急着改代理配置
- 用户规则台账：七.2 ✓ 二.4 ✓；遗留 = 用户择机 ima 上传 lessons.md + 等待下一版本优化计划
## 2026-10-15 会话六·交接刷新（context-continuity 技能触发，用户主动交接）
- task_plan：Current Phase 刷至 HEAD=27ff693 + 规则台账 + ima 节点；D10 补「公司电脑 HTTPS 可直推」纠正注记；
  错误表新增 E16（追加写死 CRLF 致 memory 日志混入 CRLF，已升华 GEN-WIN-004）；Next Step 补 ima 上传事项
- findings：F7 补纠正注记（SSH 结论仅限个人机）；新增 F16（推送重试与代理实况：出口 127.0.0.1:50486，
  github.com CONNECT 瞬 502 而 api.github.com 通 200 ⇒ 原样重试即通，勿改代理配置）
- lessons.md（仓库外）：新增 GEN-WIN-004（追加写入前先探测行尾稳态；本项目三类文件三种稳态：
  memory=LF 无 BOM / .planning=CRLF（progress 另带 BOM）/ 仓库 txt=BOM+CRLF）；条目 11→12，toc/check 通过
- experiences/说明.txt 与 handoff.md 计数同步（12 条、GEN-WIN×4、255KB/3394 行）
- 口径订正：字节数一律按十进制 KB（254,983B=255KB），勿按 KiB（会得出 249KB 的倒退值）
- 本提交即 .planning 交接刷新；推送后 HEAD 与 origin/main 同步，最终哈希见当日 memory 日志
## 2026-10-15 会话七（手册 6.5 真伪核查与更正）
- 用户问「手册 6.5 记录是真是假」→ 实测判定：**假 / 已过时**（当前宿主有桌面会话）
- 证据：explorer.exe / node.exe（智能体）/ bash.exe 全在 Console 会话 1；启动发布版 exe 5s 后
  ALIVE、Responding=True、MainWindowHandle=201130、Session=1、标题正常 ⇒ 窗口能弹出
  （用户选定「先实测再订正」后执行；窗口当前仍在用户屏幕上，PID 35816）
- 订正：手册 6.5 加【更正声明 · 2026-10-15】（附实测数据）；6.6 经验②、6.10 根因句加前提更正指引；
  速查表该行标注「已过时」；lessons.md 附录同步同文（仓库外，不随 git）
- findings 新增 F17；手册（仓库内）随本次提交入库
- 追加：新增 GEN-DOC-001（环境类结论须写明「适用环境 + 复核时间」，换环境先复核）——条目 12→13，
  toc/check 通过；lessons.md 约 257KB/3415 行（仓库外）；说明.txt 与 handoff 计数同步
## 2026-10-15 会话八（UI 优化 B 档：评审 → 实施 → 验证 → 文档同步）
- 评审：实机取证（8 张截图 + 控件树 dump）→ `_测试输出/UI评审/UI评审报告与优化方案.md`
  （P0×1 / P1×6 / P2×7 / P3×8）；新建可复用工具链 `capture_win.py` / `dump_controls.py`（纯 ctypes）
- 用户选定 **B 档（A 保守修复 + 一致性优化）+ 不升版本**
- 实施：31 条字节级规则补丁（MainForm.cs 28 / MainForm.PhaseLog.cs 2 / SettingsForm.cs 1）
  + 1 处 Edit（`ActiveControl = null`）；改动清单见 `说明.txt` 第十五节
- 验证：编译 0 警告 0 错误；SelfTest **108/108 PASS**；控件树复测（自动滚动 108×30、成功表头 50×26、
  两进度条均 894×28、五按钮均 150×38 物理、标签列 80）；截图复验（改版前后对照）
- 文档：运行说明.txt 第十六节（用户向）/ 说明.txt 第十五节（开发者向）/
  版本变更记录.txt（修订四行 + 决策 D5 + 今日事件）/ 手册第 5 章新增 5.9
- 旁支：`settings.json` 的 Win* 曾为异常值 543×355（疑似宿主清理进程时窗口非正常态），
  已恢复为用户值 577,84,815,711；实测正常关闭时 SaveUiState 原样写回 Bounds（逻辑无问题）
- 遗留：**未提交 git**（待用户验收）；未跑全量 115 张回归（纯 UI 改动，不触及引擎）
## 2026-10-15 会话八·验收（v4.17 修订四 —— 用户「通过」）
- 经验库写入 3 条正式条目（13 → 16，`lessons.py check` 通过、`toc` 重建）：
  · GEN-WIN-005 · WinForms 的 Dock 布局按 z-order 逆序处理（后 Add 的先占位）
  · GEN-WIN-006 · 宿主 GUI 取证三坑：截窗口用 GetDC(0)、开模态窗用 PostMessage、PowerShell Add-Type 被拦
  · GEN-TEST-003 · UI 缺陷要用"控件树尺寸"复验，截图只能"感觉不对"
- 同步：experiences/说明.txt 计数（13→16、257KB→261KB）、版本变更记录.txt 状态（修订四 → 已交付 + 验收通过）
- 遗留：**未提交 git**（等用户指令）；`_测试输出/UI评审/` 留有工具链与截图，待用户决定是否保留
## 2026-10-15 会话九（第二轮 UI 优化启动 → 复核发现修订四回归 → 修订五修复）
- 用户指令「先提交。开始第二次UI优化」→ 先提交修订四：commit `4e9d2be`（10 文件 +280/-57），工作区干净
- 启动第二轮前的复核（实机取证）发现**修订四 P1-5 引入的回归**：选项区行 2
  （显示线宽 / 质量核查 / 识别块图框）**整行不可见**
  · 证据：控件树 tblOptHost 955×80（物理），子表 tblOptRow1 被撑到 **955×125**；行 2 被排到 y=327，
    而「选项」分组只到 y=279（修复前 y=240 是在组内的）；放大截图确认行 2 完全消失
  · 根因：**子表 Dock=Fill 落在 AutoSize 行内时，行高不由子表内容决定**（手册 5.10）
  · 影响：三个开关在界面上不可见不可点，其中「质量核查」默认开启且参与「不达标判失败并回退重出」
- 修复（修订五）：放弃嵌套 → 单个 6 列栅格 `tblOptions` + 行 2 装进 FlowLayoutPanel
  （AutoSize + WrapContents=false + Anchor=Left|Top）+ `SetColumnSpan(optRow2, 6)`
  · 效果：行 2 回组内 y=240；「配色：」与下拉框间距 100px → 3px；行 1/行 2 左缘均 x=22
  · 与修复前逐行 diff：**除选项区外其余控件位置完全一致（零副作用）**
- 验证：编译 0 警告 0 错误；SelfTest **108/108 PASS**（退出码 0）；
  发布 exe 与构建产物 **md5 一致**（82b6967…），版本串 v4.17（无 v4.18）
- 文档同步：手册新增 **5.10**；运行说明.txt **第十七节**；说明.txt **第十六节**；
  版本变更记录.txt（修订五行 + 决策 D6 + 事件）；经验库新增 **GEN-WIN-007**（16 → **17 条**，check/toc 通过）
- 新增取证工具：`crop_zoom.py`（从已有 PNG 裁剪任意区域整数倍放大，纯标准库）——
  用它确认「截图左缘 9px 是窗口边框、内容未被裁切」（边框像素在 x=9 与 x=990，与控件树 x=10 / 宽 980 吻合）
- 遗留：**修订五未提交 git**（待验收）；**第二次 UI 优化的范围未定**（待用户给方向）

## 2026-10-15 会话九·验收（v4.17 修订五 —— 用户「通过」）
- 用户弹窗验收「通过」→ GEN-WIN-007 保持正式条目（经验库 17 条，check/toc 通过）
- 用户选定第二轮方向：**先出设计稿再定**（不是直接动手改代码）
- 产出 C 档设计稿：`_测试输出/UI评审/UI优化设计稿-C档.md`
  · 含：基线（已完成/未完成对照 P0~P3）/ 设计目标 / 色板（9 项语义色，全部 ≥4.5:1）/
    间距·圆角·字号规范 / 主窗口 8 项改版（卡片分区、自绘进度条+百分比、阶段胶囊、三栏状态点、
    失败详情内联、键盘可达、设置移位、按钮层级）/ 设置页左右分栏 /
    改动面约 800 行 / 风险与回退 / 验收标准 / **C1→C2→C3 三批实施建议**
  · 对话内附 2 张内联设计示意图（主窗口改版 / 色板与组件规范）
- 状态：**设计稿待用户审阅**；修订五**仍未提交 git**（等用户指令）

## 2026-10-15 会话九·分支（用户指令：git 开分支测试此次 UI 优化）
- 新建分支 `ui-optimization`（自 `main` @ `4e9d2be` 拉出，携带当时未提交的修订五改动）
- 在分支上提交修订五：`b717d69`（9 文件 +208/-43），工作区干净
- **分支分工**：`main` = 修订四（稳定基线，**不含修订五**）；`ui-optimization` = 修订五 + 后续 C 档改造（测试用）
- 未推送（push 需用户明确指令）；远端当前仅 `origin/main`
- 后续：C 档若开工，按 C1 → C2 → C3 三批**在同一分支**上提交，每批可单独 revert

## 2026-10-15 会话十 · C 档第一批 C1 落地（用户「审批通过」设计稿后开工）
- 分支：`ui-optimization`（未推送）。**纯 UI、不升版本（v4.17 修订六）**
- 批次微调（已告知用户）：设计稿 §10 的「进度条 + 阶段胶囊」由 C2 提到 **C1**（截图差异最大、不依赖卡片化）
- 新增 3 文件（`源码工程/`，主工程 SDK 通配自动纳入；SelfTest 白名单不含 UI，无需登记）：
  · `UiTheme.cs` —— 色板 / 间距 4·8·12·16·24 / 圆角 12·6 / 行高 / 字体 / `Dpi()` / `RoundedRect()` / `EffectiveBackground()`
  · `FlatProgressBar.cs` —— 自绘扁平圆角进度条；**Minimum/Maximum/Value 与 ProgressBar 同名同义** → 既有 13 处 `.Value=` 零改动；
    右侧 34px 百分比；总进度=主色 #185FA5、阶段进度=浅主色 #85B7EB
  · `PhaseChipBar.cs` —— 阶段条由 5 个 Label 改自绘胶囊 + 连接线；五态；`Apply(下标, 跳过标记)` 语义与旧 `ResetPhaseBar` 完全一致
- 改 `MainForm.cs`（字段类型 / 阶段条构造 / 双进度条构造 / 按钮层级 / 颜色与字体令牌）+
  `MainForm.PhaseLog.cs`（`SetPhaseBarIdle` / `ResetPhaseBar` 只做状态翻译）
- 验证链：编译 **0 警告 0 错误** → SelfTest **108/108 PASS**（exit 0）→ exe 发布 md5 一致（96fc4328…）、
  版本串 v4.17 在 / v4.18 不在 → **控件树逐行 diff：只差阶段条 5 个 Label 消失，其余完全一致（零副作用）** →
  125% 原生截图逐分区复验达标 → 96 DPI 路径（拷到无 .exe.config 目录运行）像素差 3.30%＝纯拉伸模糊 →
  `--dirp` 40 张两轮各 40/40、0 失败、`-2` 正确、无 `-3`
- 新增取证资产（`_测试输出/UI评审/`，不入 git）：`_c1预览/`（组件预览工程，反射注入状态，无需 AutoCAD）、
  `c1_main.png` + zoom1~3、`c1_states.png`、`c1_states_96dpi.png`、`c1_controls.txt`、`c1_btn_enabled/disabled.png`
- **新发现（非本次引入）**：`--dirp` 的「-2 目录非空」断言不递归 → 输入含子目录必然假失败（详见 task_plan E19）
- 文档同步：手册 **5.11** / 说明 **第十七节** / 运行说明 **第十八节** / 版本变更记录（修订六行 + D7 + 事件）
- 经验库：**本批暂未写正式条目**（dev-lessons 规则：先验收后记录）→ 已弹窗待验收

## 2026-10-15 会话十一 · C1 验收被驳回 → 进度条缓动修复（修订六·补）
- 用户驳回原话：**「进度条不是流动状态，而是突然变化。分支前的就是流动的。」**
  → 定性：**功能回退**，不是风格差异。系统 `msctls_progress32` 在 `PBM_SETPOS` 时自带位置过渡动画，
  换自绘控件后变成"瞬间跳到新值"；而本工程进度是几十秒才变一次的粗粒度更新，跳变因此格外刺眼。
- 修复（`源码工程/FlatProgressBar.cs`）：`Value` 仍是**目标值**语义（13 处调用点一行未改），
  另加**只用于绘制**的 `_displayValue`(double) + `System.Windows.Forms.Timer`(15ms ≈ 66fps)
  + **ease-out cubic**（`1-(1-p)^3`），时长 `Min(420, Max(140, 120+位移*2.5))` ms；
  **百分比数字仍显示目标值**（真实进度），只有填充长度在滑动。
  护栏：`StartAnimation()` 先判 `!IsHandleCreated || InvokeRequired` 直接落定；`Dispose(bool)` 停表并释放。
  调用链核实：`new Progress<ProgressUpdate>(ApplyUpdate)` 在 UI 线程构造 → 回调必回 UI 线程 → 动画不会被短路。
- 验证链：编译 **0 警告 0 错误** → SelfTest **108/108 PASS**（exit 0）→ exe 重发布 **md5 = 5752902419bd017d5a466fec885c79ec**
  → 组件预览工程内**逐帧像素采样**（每 15ms `DrawToBitmap` 量中间一行"填充色"像素宽度）：
  0→100% 用 **375ms 走完 25 帧**（0/42/84/121/…/362px，每帧 ≤15px）；100→25% 用 300ms 反向滑回；
  25→50 / 50→75 / 75→100 各约 170~190ms。**有中间帧 = 流动，非突变**。
- 新增工具：`_测试输出/UI评审/_c1预览/AnimProbe.csproj` + `AnimProbeForm.cs`（时序探针，输出 `anim_timeline.csv`）
- **排查插曲（结论：产品无影响）**：探针首步目标值读出 `57599` 而非 0 → 追查发现**临时新建的 net48 小工程**
  读 RVA 常量数组会读到垃圾值；`PlatformTarget=x64` 或 net8.0 正常。临时在 `Program.Main` 首行挂 `BlobDiag.Dump()`
  在**主程序进程内**实测 `PhaseOrder`/`PaperLongMm`/`RasterDpiChoices` + 现场构造的 int[]/double[] **全部正确**；
  删诊断重建后 exe **md5 复原一致**（零残留）。SelfTest 亦正确（其 108 条含 `PageSpec.Resolve` 断言，
  直接依赖 `PaperLongMm`）。→ 详见手册 5.11「环境观察」与 task_plan E20。
- 文档同步：手册 **5.11 纪律④ + 环境观察** / 说明 **第十七节「修订六·补」**（并把"不加动画"一条标注作废）/
  运行说明 **第十八节「修订六·补」** / 版本变更记录（修订六·补行 + 18:3x 事件）
- 用户同时拍板：`--dirp` 断言缺陷**修**（加 `SearchOption.AllDirectories`），**修完继续 C2**

## 2026-10-16 会话 · C2 落地（C 档第二批：卡片式分区 + 三栏状态点/徽标 + 顶部标题条）

- **先收尾 `--dirp` 断言修复**（用户 10-15 拍板「修（推荐）」）：`自测试工程/Program.cs` 三处
  非递归 `Directory.GetFiles(dir,"*.pdf")` 统一加 `System.IO.SearchOption.AllDirectories`
  （第 567 行 `--dirp`「-2 目录非空」主修 + 第 610/636 行 `--dirs` 快照/新增计数同族修复）；
  同步 `自测试工程/说明.txt`（顶部「修订六·补」段 + 经验要点新增一条）。
  重建 SelfTest → **108/108 PASS（exit 0）**。提交 `3008bd7`（独立小提交，可单独 revert）。
- **C2 范围先与用户对齐**（两处口径）：
  ① C2 按**设计稿 §7 表**（卡片化 + 三栏状态点/徽标 + §5.7 设置移位）；§5.5/§5.6/§6 留 C3；
  ② §5.7 采用**新建客户区顶部标题条**方案。
- **C2 实施**：
  · 新增 `源码工程/CardPanel.cs` —— 自绘卡片容器：白底 + 12px 圆角 + 0.5px `#D3D1C7` 描边；
    标题 12px 次要色画在卡内左上。**重写 `DisplayRectangle` 复刻 GroupBox 语义**
    （内容区 = 客户区 − Padding，再下移标题高度），标题高度取 **16**（= GroupBox 标题槽高）
    → 原 `tblXxx(Dock=Fill)` 原样搬入，各卡片 `Height` 无需改动、内容区高度不变。
    `Subtle=true`（圆角 8px）供列表/日志降级用。
  · 新增 `源码工程/ColumnHeader.cs` —— 栏标题 = 状态色圆点（待处理灰/成功绿/失败红）
    + 栏名 + 计数徽标（胶囊）。`UpdateHeaderCounts()` 在 `RefreshListUi`/`UpdateRow` 后同步。
  · `MainForm.cs`：3 个 GroupBox → CardPanel；窗体底色 `#F5F5F3`；卡片间插 10px 间隙 Panel；
    列表/日志区套 Subtle 卡片（标题"文件清单"/"日志（实时显示）"，原 `_lblLogTitle` 删除）；
    "自动滚动"复选框改手工定位到日志卡片标题条右端（高度 22→20，避免压到正文）；
    三栏 DataGridView 只调色（`EnableHeadersVisualStyles=false` + 表头扁平配色 + `GridColor`）；
    §5.7 顶部标题条（40px，左"DW 批量转 PDF" / 右"⚙ 设置"），操作条 5 列 → 4 列；
    窗口默认客户区 900×760、`MinimumSize` 840×730。
- **实测踩到两个坑（均已修，手册 5.12）**：
  ① **父链底色穿透**：`ColumnHeader`/`CheckBox` 沿父链取底 → 穿到 `SplitterPanel` 默认
     `SystemColors.Control` 灰 → 圆点浮灰块。修：三栏容器 `BackColor` 显式设卡片白。
  ② **加 Top 条后最小尺寸不够**：新增 40(标题条)+4×10(间隙)=80 逻辑像素，原 `MinimumSize`
     下 `_splitLog`（140+60+5=205 硬下限）溢出 58 → **日志卡片被整个挤出可视区**
     （控件树能 dump 到 y=786，截图里没有）。修：默认/最小尺寸同步放大。
- **验证链**：主工程编译 **0 警告 0 错误** → 重建 SelfTest → **108/108 PASS**（exit 0）
  → 控件树 dump 体检（67 控件：0 零尺寸 / 0 越界 / 无超出窗口）
  → 截图复验 125% 原生 + 96 DPI 路径（拷到无 `.exe.config` 目录运行）各一张：
  **顶部所有卡片逐像素一致**，Fill 区仅因窗口高度差 20px 不同，均无裁切/重叠
  → exe 重发布 **md5 `413e22a5e7728e433639a0ad978f73da`**。
- 文档同步：手册 **5.12**（容器类自绘控件：CardPanel 卡片化与三个实测陷阱）/
  说明 **第十八节 修订七** / 运行说明 **第十九节 修订七** / 版本变更记录（修订七行 + 10-16 事件段）。
- 取证产物（`_测试输出/UI评审/`）：`c2_main.png`、`c2b_main.png`、`c2c_main*.png`（125%）、
  `c2_96dpi.png`（96 DPI 路径）、`c2b_controls.txt` / `c2_nocfg_controls.txt`（控件树双路径对比）。

## 2026-10-16 会话 · C3 落地（C 档第三批·收尾：失败原因内联展开 + 键盘可达性 + 设置页三分类分栏）

- **C3 范围先与用户对齐**：§6 的「界面」分类**不做**（用户拍板），只保留 质量审查/识别块图框/
  打印输出 三分类；不新增设置项、不改 AppSettings.cs；仍纯交互 + 布局、不升版本号。
- **C3 实施**：
  · §5.5（MainForm.cs）：note 列截断摘要 `Truncate(full, 32)`（全文仍存 `_noteByPath`，
    分类统计不受影响）；失败栏新增「详情 ▸」链接列（`DataGridViewLinkColumn` 50px）；
    点链接 → Dock=Bottom 只读详情面板（完整原因 + `LogTail(20)` 日志尾部），**不弹窗**；
    同行再点收起「收起 ▾」；`RefreshListUi`/行移走时 `SyncFailDetail` 自动收起；
    Panel2 Add 顺序：`_gridFail` → `_failDetailPanel` → `_hdrFail`（Dock 逆序：Fill 先 Add）。
  · §5.6：`开始转换(&S)`、`AcceptButton = _btnStart`、`KeyPreview` + `OnMainFormKeyDown`
    （Esc → `StopRequested()`，仅转换中且停止按钮可用）、全控件显式 TabIndex。
  · §6（SettingsForm.cs）：`TabControl` → 左 132px 自绘 `ListBox`（OwnerDrawFixed，
    选中 PrimaryLight/PrimaryDark）+ 右内容卡片；窗口 480×350 → 560×400；
    三个内容页 Panel 常驻、只切 Visible；8 处 `ForeColor` 改 `UiTheme.TextMuted`。
- **实测踩到三个坑（均已修，手册 5.13 / task_plan E23~E25）**：
  ① **列宽最小合计溢出**：失败栏最窄 264 逻辑像素，170+70+62=302 → 横向滚动条把
     「详情 ▸」滚出可视区 → 三列改 140/54/50（合计 244 ≤ 264）；
     复验判据 `GetColumnDisplayRectangle(detail).Right ≤ DisplayRectangle.Right` 且
     `HorizontalScrollingOffset == 0`。
  ② **固定高度条高于容器**：详情面板 132 > 失败栏约 126（表头 24 + 表格 102）→
     表格压成 0 高、面板 Y=-18 溢出 → 高度按容器剩余 clamp（`Math.Max(44, Min(132, 剩余))`）。
  ③ **SelfTest 增量不重编**：主工程重建后普通 build 报「无需还原」、mtime 不刷新
     → 引擎新鲜度守卫假 FAIL（107/108）→ `--no-incremental` 真重编后 108/108。
- **反射 harness 实机验证**（`_测试输出/UI评审/_faildetail/`，本段会话新建）：
  `Assembly.LoadFrom` + `Activator.CreateInstance(nonPublic)` 实例化 `MainForm`，
  反射灌 3 行失败数据、调 `ToggleFailDetail`、`PostMessage` 真实按键 Esc；
  断言全过：AcceptButton 指向开始转换 / Esc → 停止按钮 `Enabled=False`+「正在停止…」/
  detail 列在可视区 + 无横向滚动 / 面板 Top=26≥0 不溢出 / 折叠-再折叠截图字节一致。
  实操坑：`_hdrFail` 是 `ColumnHeader` 不是 DataGridView；`Form.AcceptButton` 是
  `IButtonControl` 无 `.Text`；net48 dynamic 需 Microsoft.CSharp（未引 → 改反射取 Bounds）；
  `GetColumnDisplayRectangle` 只有 `(int index, bool)` 重载。
- **验证链**：主工程编译 **0 警告 0 错误** → 强制重建 SelfTest → **108/108 PASS（exit 0）**
  → 复抓截图：主窗（1050×913）+ 设置窗（720×549，三分类切换各一张）+ 失败详情三态
  → exe 重发布 **md5 `7956647ea058a78b6f99e7e244b2a751`**。
- 文档同步：手册 **5.13** / 说明 **第十九节 修订八** / 运行说明 **第二十节 修订八** /
  版本变更记录（修订八行 + D8 + 10-16 事件段）/ handoff + task_plan（phase8.11 + D21/D22 + E23~E25）。
- 用户 10-16 追加拍板留痕：C2 验收「通过」；C3 范围（界面分类不做）；`ui-optimization` 暂不推送。
- **已提交**（C3 独立 commit，10 文件 +622/−64，**未 push**）。
- 用户弹窗验收 **通过**；经验库新增 5 条正式条目（20 → 25：GEN-WIN-011/012/013/014 + GEN-TEST-004），`check`/`toc` 通过。
- **合并**：`ui-optimization` → `main` 快进合并（c1ed5fe；ff、diff 0、无冲突）；`ui-optimization` 保留作批次存档。
- **推送 main 未成（12:3x）**：代理对 github.com:443 CONNECT 502（约 10 次失败）；api/codeload 通、直连超时、无 SSH key。
  **本地 main = 762b877 领先 origin 9 提交**，待网络恢复后 `git push origin main`。
## 2026-10-16 会话十二·收尾与交接结束（13:5x~14:00）
- **推送成功（main）**：ssh://git@ssh.github.com:443 通道 → 3f2ddd7..de9d5f9，10 提交全上。
- **推送成功（存档分支）**：ui-optimization（c1ed5fe）上远端。
- 推送真因：github.com:443 域名级封锁（沙箱内 CONNECT 502 / 直连 SYN 超时双重实测）；
  api.github.com 200 / codeload 301 / ssh.github.com:443 / github.com:22 全通 → 走 SSH over 443。
- 通道建设：本机 ed25519 推送密钥 ~/.ssh/id_ed25519_dwgtopdf（无短语）；仓库级 deploy key（id 166625055，可写）；
  GitHub ed25519 主机指纹核对 SHA256:+DiY3wvvV6TuJJhbpZisF/zLDA0zPMSvHdkr4UvCOqU 后写 known_hosts；
  origin remote 改为 ssh://git@ssh.github.com:443/zzh112212/Dwg-To-PDF.git（https 本机不可用，可逆）。
- **GCM CredentialHelperSelector 弹窗根治**（用户截图提问）：credentialStore 从未设置 → 每次用凭据都弹；
  已 git config --global credential.credentialStore manager。
- 坑（已入 task_plan E26~E28）：非沙箱 bash cwd 不在仓库（必须显式 cd）；Windows python 不认 MSYS /tmp；
  沙箱内能取到 GCM token 而非沙箱取空（与弹窗同根因）。
- 验证：本地 main / origin/main / GitHub 侧 main 三方一致（de9d5f9）；工作区干净。
- **交接（context-continuity 状态三）**：task_plan phase8.12（Current Phase / Next Step=等待下一版本 / D23-D24 / E26-E28 / Phases 8.12 行）；
  handoff.md 整文件重写（自包含，等待型任务）；本段为会话结束日志；memory 日日志 + MEMORY.md 同步。
- **用户待决剩 2 项**：lessons.md 是否上传 ima（需凭证）；_测试输出/UI评审/ 工具链是否保留。
- 提醒：用户曾于对话中发出明文 GitHub 密码（未使用未保存；GitHub 已禁密码推送；已建议改密）。

---

## B1 注释批审查台账（v4.17 修订九 · CQ-03 + CQ-04，零行为变更）

**批次范围**：源码工程 18 个 .cs（103 处 catch 注释 + BgLogger 8 处多行式压行 + 10 个 public 方法 XML doc）。
**验证结论**：V1 变更面仅白名单；V2 token 等价校验 18/18 PASS（零可执行变更）；V3 TOTAL_EMPTY_CATCH=140 且 B1_TODO=0、EXCLUDED_B2=13；V4 编译 0 警告 0 错误；V5 SelfTest 108/108 exit 0。

| 日期 | 提交 | 审查方式 | 位置（文件:行） | 级别 | 问题描述 | 建议 | 状态 |
|---|---|---|---|---|---|---|---|
| 2026-10-16 | （本次，只提交未推送） | 自验 + 脚本复核 | 空catch清单.py EXCLUDE | P3 | EXCLUDE 按冻结行号排除 B2 站点；B1 在上方插入注释行使行号平移、EXCLUDE 失配（复扫一度 B1_TODO=15） | 按内容重新锚定 16 个站点行号（站点集合不变，EXCLUDED_B2 恢复 13） | 已修（附本次提交） |
| 2026-10-16 | （本次） | 自验 | 源码工程/*.cs（18 文件） | — | B1 落地 111 处注释 + 10 XML doc | 保持零可执行变更（V2 权威判据） | 已修 |
| 2026-10-16 | （本次） | 自验 | CadConverter.cs:115（原 114/115 成对） | P3 | 113 行一条注释覆盖 114/115 两行，机械规则仍标出 115 | 按设计补一条简短注释即可 | 已修 |

**遗留 P2/P3**：无（本批纯注释，无功能问题入账）。

**偏离设计之处（1 项）**：`空catch清单.py` 的 EXCLUDE 条目行号由冻结行号改为 B1 落地后的当前行号（内容一一对应、仅行号平移）。原因：仪器按行号匹配，注释插入必然使下方行号漂移，不重锚定则 V3 的 `B1_TODO=0` 永不可达；重锚定后站点集合与 T1 冻结版完全一致（EXCLUDED_B2 仍=13）。已在手册追加一行记录该教训。

## 2026-10-16 会话十六·B2 实施与独立验证进行中（16:0x~，主理人视角）
- 设计 b636286（主理人执笔，architect-2 平台三连败后接手）→ 工程师寇豆码实施 **15abeb2**（12 文件 +293/−17，只提交未推送）。
- 主理人核验工程师自述：LogWriter 三属性+三 catch 置标志+首次 mirror / Engine catch 分流（129 OCE、137-141 全异常）/ 纯助手 RecognitionFailureNote（1485）/ FrameExtractor 4 处 Warn（430/435/581/586）全部属实；
  断言口径厘清：两新方法实含 8 个断言语句、正常路径执行 7 条（N6 兜底语句仅在抛异常时执行）→ 108+7=**115/115**，基线无丢失。
- 偏离 2 项：① ValidateChunkPipeline 实为一处调用点（设计标注的第二处为 B1 前旧行号）；② missing 图纸真实原因附在块失败清单日志行而非逐文件 Reason（遗留 P2）。
- QA 严过关已派独立复验（V1~V6：变更面/字节纪律/注入断言/编译/smoke 坏图纸/E2E 113/115 + 不可写 logDir 场景 + 只读 settings.json 手工复现 + 成功路径 diff 抽核）。
- git：main=15abeb2，领先 origin 5（不推送纪律）。

## B2 异常可见化批审查台账（v4.17 修订十 · CQ-01/02/05/06，⚠改行为·成功路径零变化）

**批次范围**：源码 7 个 .cs（LogWriter/ConverterEngine/FrameExtractor/MainForm/RunHistory/EnvironmentSelfCheck/BgLogger）+ 自测 Program.cs（N1~N6）+ 文档四件套。

| 日期 | 提交 | 审查方式 | 位置（文件:行） | 级别 | 问题描述 | 建议 | 状态 |
|---|---|---|---|---|---|---|---|
| 2026-10-16 | 15abeb2 | 主理人核验 | 自测试工程/Program.cs:146-205 | P3 | 新增断言 8 语句/7 执行（N6 兜底不执行），工程师口径「七条」易误读 | 已在 QA 任务书明确要求独立计数并解释 115 构成 | 已释（QA 复跑中） |
| 2026-10-16 | 15abeb2 | 工程师自报 | ConverterEngine.cs ValidateChunkPipeline | P2 | missing 图纸真实原因仅块级日志行可见，无逐文件 Reason 登记 | 后续批次扩清单数据结构 | 遗留台账 |
| 2026-10-16 | 15abeb2 | 工程师自报 | MainForm.cs×2 | P2 | 不在 SelfTest 白名单，改动以编译+diff 审查佐证 | 维持既定口径，不入单测 | 遗留台账 |
| 2026-10-16 | 15abeb2 | 工程师自报 | 发布程序 v4.17/DwgToPdf.exe | P2 | 本批改运行日志行为，exe 未刷新 | QA 验收后统一刷新 | 遗留台账 |
| 2026-10-16 | 15abeb2 | 工程师自报 | BgLogger.Warn 诊断 | P3 | 诊断仅落后台日志，无界面查看路径引导 | 后续在说明文档补后台日志位置指引 | 遗留台账 |

## 2026-10-16 会话十八·架构师复审归档（16:3x，主理人视角）
- architect-2（重启后改派只读复审）结论：**设计落地符合，无 P0/P1**。逐项核对 §3 接口 vs 15abeb2 实现全部吻合；
  N6 null 安全为超出附录 C 的加固项；符合度唯一 gap = missing 图纸真实原因仅到块级汇总行（工程师自报偏离第 2 项实况）。
- P2-1（design-b2.md §3 旧表述与实现不符未注记）**已释**：补记 17 行提交 6f5d22f（降级理由 + B3 挂账入口 + 仅内存模式承诺边界 + P3 去向）；
  §3 旧表述作废，以补记为准。
- 承诺边界（重要）：仅内存模式（主程序默认）下 WriteFailed 恒 false、收尾警告行永不出现；Q2 的 UI 可见仅在落盘模式（DWGTOPDF_LOG_DIR）兑现。
- P3×2 挂账 B3：LogWriter WriteLine catch 缩进风格；ConverterEngine.cs:724/677 边缘收尾路径缺警告行。
- 架构师给 QA 的 3 个高风险点已转发（块级断言口径 / E2E 113/115 计数纪律 / 写盘失败仅落盘模式可达勿误报）。QA 复验进行中。
- git：main=6f5d22f 领先 origin 6，未推送。

## 2026-10-16 会话十九·QA 独立复验裁决 + B2-R1 修复轮派出（16:5x，主理人视角）
- QA（严过关）V1~V6 复验：V1 变更面 PASS（12 文件吻合、四件套纯追加、字节纪律保持）；V2/V3 **115/115 exit 0**（独立证实 8 语句 7 执行；
  mirror 实跑旁证：3 次跑各产生 1 条“运行日志写盘失败”降噪正确）；V4 编译 0/0；V5 机制 PASS 但运行佐证不可得（外部无法稳定注入通用 catch，P3 佐证局限）；
  V6-① --dirs 全量 **113/115 与基线完全一致**（2 张 H77 既有失败，B2 零引入）；V6-③ **B-4 FAIL→P1**；成功路径零变化 PASS（抽核 7 文件 + 自测 diff）。
- **P1（真问题，主理人亲验属实）**：AppSettings.Save()（L165-178）自带 catch{return false} 静默吞异常 → MainForm.cs:1937 后 1939-1943 的 catch 是死路径；
  SettingsForm.cs:540 拿到 false 仍无条件 OK+Close；MainForm.cs:1954 无条件“设置已保存”。CQ-06 B-4 验收不可达且对话框撒谎。
- **P2**：LogWriter 造期（L33-51 CreateDirectory/StreamWriter 无 try）→ 无效 DWGTOPDF_LOG_DIR 下转换开始即崩（exit 127）零痕迹；B2 前同样崩，非回归。
- 主理人拍板 P2 取向：日志 best-effort → 造期失败置标志 + BgLogger.Error + 降级仅内存模式，转换继续（收尾警告行自然触发）。
- **B2-R1 修复轮已派工程师**：F1 AppSettings.Save 记 BgLogger.Warn 后回 false（根因）；F2 MainForm 状态栏求真；F3 SettingsForm 保存失败弹错留窗不装 OK；
  F4 LogWriter 造期 try+降级；F5 断言 N7/N8（预期 117/118）；文档四件套“修订十·补”；重建发布 exe（行为已变，用户不能拿旧行为）。QA Round 2 复验候命。
- git：main=6f5d22f（领先 origin 6，未推送）。B1/B2 验收弹窗待用户。

## 2026-10-16 会话二十·B2-R1 修复落地 + Round 2 复验派出（17:0x，主理人视角）
- 工程师 B2-R1 完成：commit fabdcd6（10 文件 +192/−10，只提交未推送）。主理人亲验：F1 AppSettings.cs:178 记日志回 false / F2 MainForm.cs:1939-1940 状态栏求真 /
  F3 SettingsForm.cs:543-545 失败弹错留窗 / F4 LogWriter.cs:60-62 造期降级；新 exe md5 2e8ce7f18577f7673b778c90e9866213（旧 7956647e…）与回传一致；版本仍 v4.17。
- 工程师自验：编译 0/0；SelfTest 120/120（115+N7×3+N8+N8b）；B-4 真机复现通过（只读 settings.json→错误框留窗→BgLogger 命中→解锁后正常保存）；
  P2 真机复现通过（非法 DWGTOPDF_LOG_DIR→不崩→2/2 成功→界面收尾“[警告] 日志写入失败：构造期…”+BgLogger“初始化失败已降级”）。
- 偏离 2 项（已记录）：F2 原 catch 降级为字段段防御兜底；F3 错误框文案按规格不做细分。新 P3：降级模式下“日志文件:<意图路径>”行仍显示意图路径（警告行兜底纠正，入账）。
- 主理人裁定：qa_b2_r1 复现脚本与截图留在工作区 _测试输出/UI评审/（与既有 UI 工具链同口径，不入库）。
- QA Round 2 复验已派（R1 diff 审/R2 120 独立计数/R3 B-4 独立复现/R4 P2 独立复现/R5 --dirs 113/115+exe 字符串/R6 文档字节纪律）。
- B1 台账更新：P1（CQ-06 B-4 不可达）修复中 fabdcd6，待 Round 2 判定；P2（LogWriter 造期崩溃）修复中 fabdcd6，待 Round 2 判定。
- git：main=fabdcd6 领先 origin 7，未推送。B1 验收仍挂起；B2 待 Round 2 后提请。

## 2026-10-16 会话二十一·B2-R1 Round2 PASS + 提请验收（17:1x，主理人视角）
- QA Round 2 复验（commit fabdcd6）：**Verdict PASS，可验收**。R1 diff 审四处修复逐块对上（F1 记日志回 false 不重抛 / F2 状态栏求真 / F3 失败留窗不装 OK / F4 造期降级不抛且 Dispose/WriteLine 空态安全）；
  R2 编译 0/0 + SelfTest 120/120（独立数：115+5，N8 成功路径正向锚定）；R3 B-4 真机全流程复现通过（错误框 611×224 留窗 + BgLogger 命中 UnauthorizedAccessException + 解锁后正常重写）；
  R4 P2 复现通过（3/3 出图不崩 + BgLogger“初始化失败已降级”命中）；R5 全量 113/115 基线一致 + exe md5 2e8ce7f1… + UTF-16LE 4/4 字符串命中 + Version 未动；R6 文档四件套+字节纪律 9/9。
- P0/P1/P2 清零。P3×2 入账：① 降级模式“日志文件:<意图路径>”仍显示意图路径（[警告] 行在前兜底纠正，B3 整洁项）；② R4 UI 面板直拍不可得（harness 显示策略+capture 无输入能力，三重替代证据）。
- **记录项（非缺陷，取证诚信档·已核实纠正）**：QA Round 2 报告称工程师的 qa_b2_r1/“实测不存在”——**此话有误**：主理人 17:1x 亲测该目录在场，8 张截图（b4×4 + p2×4）+ qa_b2r1_gui.py，时间戳 16:46-16:47，与工程师自报逐项一致；QA 的 R4 结论不受影响（其 qa_b2_r2/ 10 张为独立复现，两套证据并存）。教训：队友互相矛盾的断言，主理人一律以磁盘实测裁半。
- git：main=fabdcd6 领先 origin 7，未推送。**B1（修订九）+ B2（修订十·补）均已具备验收条件，弹窗提请用户。**

## 2026-10-16 会话二十二·B1+B2 用户验收通过 + B3/B4 解锁（17:2x，主理人视角）
- **用户弹窗验收「通过」**：B1（修订九）+ B2（修订十·补）双双闭环。经验库 25→30：新增 GEN-TEST-005（动 catch 前先查上游吞点）/006（白名单外路径必须手工复现兜底）/007（矛盾断言以磁盘实测裁半）/008（仪器按内容锚定不按行号），check/toc 通过。
- **用户拍板 Q3/Q4**（拍板内容上回合已记入本台账，但 **PRD §8 落盘一度失败**：写入命令因 bytes 字面量含中文报 SyntaxError 而未执行，当时台账误记“已写回 PRD §8 并提交”——本回合按 str+encode 字节级补写落盘并单独提交，不实记载在此更正）：Q4=B3 接受事件等待改造（阈值/冷却不变）；Q3=B4 接受 partial 原样搬迁，首批仅 MainForm 两块（设置/持久化 + 拖放与文件收集），自测 Program.cs 不同批。
- architect-2 已派 B3（CQ-07）增量设计：注意 SelfTest 红线已变了 120（108+N1~N8 系），把“停止请求如何到达看门狗/句柄生命周期”列为核心设计点。
- git：main 领先 origin 9（B1×2 + 审查标准 + B2 设计+补记+实施×2 + PRD §8 补写），未推送。
- 下一步：B3 设计回传→核验→工程师实施→QA 复验→弹窗；随后 B4（Q3 已锁定范围）。

## 2026-10-16 会话二十三·PRD §8 补写 + architect-2 二次倒下 + B3 设计主理人执笔（17:0x，主理人视角）
- **PRD §8 补写落盘**：Q3/Q4 两行按用户裁决写入（str+encode 字节级，CRLF 151/151、无 BOM），提交 `1d11cfb`；progress/memory 中「已写回并提交」的不实记载同步更正。上回合失败根因 = 写入命令的 bytes 字面量含中文报 SyntaxError。
- **过程新坑 → GEN-WIN-015**：`Path.read_text()` 是文本模式，读入时把 CRLF 静默吃成 LF（write_bytes 不报错）→ git checkout 还原后按 read_bytes→decode 重做。
- **过程新坑 → GEN-WIN-016**：bash -c 内联 Python 含反引号被 shell 命令替换吞字（当天连踩两次）→ 复杂写入一律落盘成临时 .py 再跑。经验库 30→32，check/toc 过。
- **architect-2 二次倒下**：① B3 首轮报「完成」但磁盘无 design-b3.md（假完成，已按 GEN-TEST-007 实测裁半打回）；② 重派后因**平台 429 频率限流**失败（配额 2026-10-17 12:16 重置）。其口头报告的锁定范围（768-1043 + 1189-1191）与「Set() 两处」经实测**均不成立**。
- **用户拍板「主理人亲自执笔」**（沿用 B2 时同一拍板）→ 主理人亲核底座后执笔 `.planning/design-b3.md`（16,209 B、CRLF 284/284、无 BOM），提交 `6fa69cc`。
- **实测底座（main=1d11cfb）**：看门狗创建 L579-610（`bool[] watchdogStop` + `while(!watchdogStop[0]){Thread.Sleep(15000);…}`）；停止点 L1226 单处；收尾等待 L1228 `Join(3000)`；阈值 900000 / 冷却 180000 不变；循环外层 catch 注释 B1 已补（B3 无注释项）。
- **B3 已派工程师实施**（T1 声明/创建 → T2 线程体事件等待 → T3 Set→Join→Dispose → T4 编译 + 120 断言 + V1 grep 等价审阅 + V2 真机停止计时 + 文档四件套「修订十一」）。
- git：main 领先 origin 11（+PRD §8 补写 +B3 设计），未推送。
- 下一步：工程师回传 → 主理人核验 → QA 独立复验 → 弹窗验收；随后 B4（Q3 已锁定范围）。

### 会话二十三·续：B3 实施交付 + 主理人独立核验通过（17:1x）
- **工程师交付 commit `c0abe7b`**（6 文件 +81/−7）：ConverterEngine.cs 看门狗改事件等待（L530-531 声明、L579 创建、L586-589 循环、L1224-1231 停止段 Set→Join→Dispose）+ 发布 exe 刷新 + 文档四件套「修订十一」（手册新增 §5.15）。
- **主理人独立核验（不采信自述，逐项实测）**：`watchdogStop` 残留 = **0**；`stopEvent.Wait(15000)` = **1 处**；判定体 L582/583/596/600/601 逐字在；`Program.cs` Version 仍 `v4.17`（未升版）；停止段顺序 Set(L1227)→Join(L1229)→Dispose(L1231) 正确；发布 exe md5 = `8520f34112f70ab3d9b389a5cfb84146`（与自报一致）；文档四件套字节形态正确（手册无 BOM+CRLF、三 txt 有 BOM+CRLF）、§5.15 已建。**结论：diff 与设计逐项吻合，判定体未出现在 diff 中。**
- **V2 真机 A/B（工程师实测，待 QA 独立复核）**：改前 tail 3061.5ms（n=2）→ 改后中位 69.3ms（n=7，min 56.7 / max 349.3），差值 ≈2.9s = 被消除的 `Join(3000)` 超时；工程师诚实标注 7 次中 2 次落在 192/349ms（finally 既有同构开销）。
- **QA 已派独立复验**：V1（自跑 diff 审阅）/ V2（自建 A/B，先审阅工程师脚本口径）/ V3 编译 / V4 120 断言 / **V5 全量回归 113/2（工程师未做）** + 边界反例（Dispose 顺序、Start 异常路径、IsSet/Wait 竞态、发布 exe 真实性佐证）。

### 会话二十三·续二：QA 独立验证回传 + 主理人复核（17:2x）
- **QA 结论：B3 可验收**。V1 PASS（自跑 diff，7 个语义 token 新旧计数逐项相等；净 +4 行 = 新增注释）；V2 PASS（自建 A/B 各 5 次：NEW tail 中位 63.8ms vs OLD 3058.5ms，差值 2996.4ms ≈ Join(3000) 超时；**独立证实**工程师「残余 ~60ms 为改前改后同构既有开销」的归因成立——剥离 3000ms 后 OLD 残余 59.6ms / NEW 63.2ms）；V3 PASS（引用+旁证：沙箱将 MSBuild/csc 列为受限 LOLBin，无法重跑）；V4 PASS（120 PASS / 0 FAIL / exit 0）；V5 PASS（115 进 → 113 PDF / 2 失败 = H77 风扇件既有，136.3s）。
- **边界四项**：(a) Dispose 顺序正确（L1228 Set→L1230 Join→L1232 Dispose），但**发现 P3 理论风险**（见下）；(b) 异常路径 PASS（与设计 §3.3 三条完全吻合）；(c) IsSet/Wait 竞态 PASS（Set 落在窗口内时 Wait 立即返 true，无丢信号）；(d) 发布 exe 二进制取证 PASS。
- **P3 B3-P3-1（QA 发现，主理人复核属实）**：L586 `while (!stopEvent.IsSet)` 与 L590 `stopEvent.Wait(15000)` **位于内层 try(L591-607) 之外**；若 Join(3000) 超时（线程仍存活）后 Dispose，线程回 L586 求值 → `ObjectDisposedException` 于**无 catch 的后台线程** → 进程终止。触发需三条件同时成立（15min 停滞已触发 KillAllOwned + 该调用阻塞 >3s + 恰在窗口点停止），极罕见；**但属本批新引入的崩溃路径**（旧 `bool[]` 无需释放，无此风险）。
- **P3 B3-P3-2（文档口径）**：设计 V2 判据「改后 <100ms」未注明口径——ui_latency（162~262ms）会被误读超限；应注明「口径 = tail / Join 返回耗时」。
- **主理人复核**：① 读码确认 P3 属实（L586/L590 确在 try 外）；② 独立重跑 exe 取证**印证 QA**（NEW 含 `ManualResetEventSlim`=1 / `stopEvent`=1、`watchdogStop`=0；OLD 恰好相反）——**主理人首次用 UTF-16LE 检索得 0 是方法错**，已据此修正经验 **GEN-WIN-001**（标识符走 #Strings=UTF-8，仅字符串字面量走 #US=UTF-16LE；闭包捕获变量成为字段名故亦 UTF-8）。经验库仍 32 条，check/toc 过。
- **信息缺口（如实记录）**：V3 编译 QA 无法独立重跑（沙箱限制），仅引用工程师结果 + 旁证（exe 构建时间 17:14 > 源码 17:11、元数据含新符号、SelfTest 引擎新鲜度守卫在 V5 中 PASS）。
- 待用户裁决：B3 验收 + P3 B3-P3-1 处置（当批修 / 入台账 / 接受不修）。

### 会话二十三·续三：用户验收通过 + P3 当批修（B3-R1 交付 + 主理人核验）（17:3x）
- **用户弹窗裁决**：① B3 验收「**通过**」；② P3 B3-P3-1「**当批修掉**」。
- **B3-R1 交付 commit `637f5d3`**（3 文件 +21/−4：ConverterEngine.cs / 发布 exe / 手册 §5.15 补段）。修法 = 利用 `Thread.Join(int)` 返回值：`bool wdJoined = false;` → `wdJoined = watchdogThread.Join(3000)` → `try { if (wdJoined && stopEvent != null) stopEvent.Dispose(); } catch { }`；**L586/L590 结构未动**（用户红线）。
- **主理人独立核验**：diff 与方案逐字一致；实际代码顺序 `Set`(L1228) → `Join`(L1230 取返回值) → 条件 `Dispose`(L1234)；`wdJoined` 初值 false；提交清单仅 3 文件（未含 design-b3.md / Program.cs / 自测试工程）；新 exe md5 `22fad1523ea81bb7cdaa2bfa71450`（与自报一致）；exe 元数据取证 `ManualResetEventSlim`=1 / `stopEvent`=1 / `watchdogStop`=0（**`wdJoined`=0 属正常**——它未被 lambda 捕获，不进元数据，正好印证 GEN-WIN-001 的分堆规则）；Version 仍 `v4.17`；手册 §5.15「修订十一·补（B3-R1，P3）Join 超时保护」已建（L865）。
- **`design-b3.md` 补记已落盘**（主理人维护）：QA 验证结果 + P3 B3-P3-1 全文与修法 + **P3 B3-P3-2 口径修正**（V2 判据「<100ms」口径 = tail / Join 返回耗时；`ui_latency` 162~262ms 不作判据）+ 主理人自查踩坑记录（UTF-16LE 检索标识符得 0 的教训）。19,380 B、CRLF 319/319、无 BOM。
- **QA 小轮复验已派**（B3-R1 验证）：R1 静态核验（含 L586/L590 未动红线）/ **R2 修复有效性实证**（要求构造最小复现证明「Dispose 后 IsSet 抛 ObjectDisposedException」风险真实 + 修复消除，而非仅推理）/ R3 正常路径无退化（修复后 exe ≥5 次计时，与 63.8ms 同档）/ R4 产物与手册准确性 / R5 编译与 120 断言。并明确要求 QA「若认为修复方案本身有残余风险（GC 回收时机、Join 返回值误判），必须说出来，不得因用户已拍板而附和」。
- git：main 领先 origin 13（+B3 设计 +B3 实施 +B3-R1），未推送。

### 会话二十三·续四：B3-R1 复验（QA 推翻自身 P3 前提）+ B3-R2 文档更正（17:5x）
- **QA 小轮复验：修复可验收**（R1 静态 PASS / R2 修复有效性 PASS / R3 无退化 PASS / R4 产物与文档 PASS / R5 120 断言 PASS）。
- **QA 用实测推翻自身 P3 前提（高诚信自我更正）**：PowerShell 5.1（CLR 4.0.30319.42000 = net48）实测 `ManualResetEventSlim.Dispose()` 后——`Wait()` **抛** `ObjectDisposedException`；`IsSet` **不抛**（纯字段读；Dispose **不重置** set 标志位、Set 后恒 true）；`Set()` 不抛；看门狗循环 after Set+Dispose 正常退出（iterations=0）。⇒「线程回读 `!IsSet` 抛异常 → 进程终止」**不成立**，首轮 P3 为**假阳性**；旧代码唯一真实残余窗口（Set 前读 IsSet=false 且尚未进 Wait 时被抢占）需停摆 >3s，**实际不可达**。官方佐证：MS Learn `IsSet` 未列异常、`Wait` 明确列。
- **修复保留**（QA 建议 + 主理人认同）：防御性正确、无退化（修复后 tail 中位 69.8ms vs 修订十一 63.8ms，同档）、撤销成本更高 → 重新定位为「**防御性最佳实践**」。
- **QA 另抓两处文档缺陷（P3）**：① 手册 §5.15 沿用了被推翻的机制表述；② 版本变更记录.txt 未记 B3-R1。
- **主理人自查补一项**：产品源码 `ConverterEngine.cs` L1232-1233 的注释同样含被推翻的机制表述（会误导维护者）→ 一并纳入本轮更正。
- **B3-R2 已派（文档/注释更正轮）**：手册 §5.15 机制表述更正 + 版本变更记录补行 + **源码注释更正**（因改 .cs，需重编译 + 重建 exe + 120 断言）。
- **主理人已改 `design-b3.md` 补记**：P3 段改写为「首轮表述（保留以记录认知演进）+ 复验实测表 + 假阳性结论 + 修复保留理由 + 源码注释更正提示」。
- **经验库 32→33**：新增 **GEN-WIN-017**（.NET BCL「Dispose 后哪个成员抛异常」必须实测或引官方文档，勿靠读码推断；附 PowerShell 5.1 直调 BCL 的实测手段）。check/toc 过。
- **主理人笔误记录**：派 QA 任务书时把 exe md5 少抄 2 位（写成 30 位，实际 32 位），QA 核对时指出。
- git：main 领先 origin 14（+B3-R1），未推送。

### 会话二十三·续五：B3-R2 文档更正（含漏项追补）+ 双 BOM 既有异常（18:0x）
- **B3-R2 commit `d5b55a0`**（2 文件 +36/−12）：① 手册 §5.15 改写——错误机制 → 实测事实（`Dispose` 后 `Wait` 抛 / `IsSet` 不抛 / `Set` 不抛）+ 唯一残余窗口（需停摆 >3s，**实际不可达**）+ 修复重定位「防御性最佳实践」+ 教训改写；**保留项逐字未动**（修法三行 / 交 GC / 顺序硬约束 / 旧 bool[] 说明 / 回归 68.5/75.3ms）。② 版本变更记录.txt 三处补记（版本表 L29 / 事件段 17:2x 块 / Git 里程碑）。**主理人核过：改写准确、保留项完整。**
- **漏项（已追补）**：工程师未执行任务书后补发的第三项（源码注释更正）——`d5b55a0` 仅 2 文件，`ConverterEngine.cs` L1232-1233 仍是错误机制表述。已重发指令（**B3-R2b**）：改注释 + 重编译 + 重建 exe + 120 断言 + 提交。**教训：成员不保证读到后发消息，补发指令必须在下一轮显式复述其内容并核验产物。**
- **既有异常记录（P3，历史遗留）**：`版本变更记录.txt` 开头有**两个连续 BOM**（`EF BB BF EF BB BF`）——比对 637f5d3 旧版**同样如此**，**非本批引入**；工程师「BOM 数量守恒」说法成立。入台账待后续清理。
- git：main 领先 origin 15（+B3-R2），未推送。

### 会话二十三·续六：B3-R2b 补项闭环 + B3 收官（18:1x）
- **B3-R2b commit `30b8279`**（2 文件：ConverterEngine.cs +3/−2 纯注释、发布 exe 重建）：源码停止段注释改为实测准确表述（「尚未进入 Wait 时被抢占的极窄窗口 … **IsSet 为纯字段读、不受影响**」）。
- **主理人独立核验**：`git diff 637f5d3..30b8279` **非注释行变化为空**（纯注释、行为零变化）；exe md5 `4ddfff41e38848687e25a69b0f05d5e6`（与自报一致）；Version 仍 `v4.17`；ConverterEngine.cs 无 BOM、CRLF 1498/1498；**主理人自跑 SelfTest：120 PASS / 0 FAIL、引擎新鲜度守卫 PASS、exit 0**。
- **B3 完整闭环**：提交链 `6fa69cc`（设计）→ `c0abe7b`（实施）→ `637f5d3`（R1）→ `d5b55a0`（R2）→ `30b8279`（R2b）。最终 exe md5 `4ddfff41…`。
- **经验库 33 条**（本会话新增/修正：GEN-WIN-015/016/017 + GEN-WIN-001 修正）。
- **待办移交**：① 双 BOM 历史遗留（版本变更记录.txt）；② 设计文档 B3-P3-2 口径注已补；③ B4（CQ-08 partial 搬迁，Q3 已锁定范围）待用户启动。
- git：main 领先 origin 16（+B3-R2b），未推送。

### 会话二十四·B3 最终验收 + B4 启动（18:2x）
- **用户裁决**：① B3 全链（含 R1/R2/R2b）**最终验收通过**；② 启动 **B4**。
- **B4 勘察（主理人亲核，main=`0d6765a`）**：`MainForm.cs` = **2103 行**（超红线 2100）；已有 partial 先例 `MainForm.PhaseLog.cs`（148 行）。区块分布：BuildUi L222-888（**667 行**，最大）／列表与失败详情 L1305-1504（200）／转换执行 L1581-1811（231）／拖放与文件收集 L1110-1259（**151**）／设置持久化 L1873-1960（**88**）／文件夹复制 L1505-1580（76）／自检与输出 L1962-2103（142）。
- **⚠ 发现 Q3 范围与 CQ-08 验收标准矛盾**：Q3 两块合计仅 **~239 行**，搬完仍 ~1864 行，**达不到 CQ-08 的 ≤1,400 行**。→ 用户裁决：**首批按 Q3 两块做，验收口径改为「纯搬迁 diff + 编译 0/0 + 120 断言」，不要求 ≤1,400**（达标留待后续批次）。
- **用户另一裁决**：L1873-1874 既有缩进瑕疵（2 层 tab）**搬迁时顺手修正为 1 层**，明示以便 QA 区分「格式修正」与「逻辑改动」。
- **三项实测结论**：① `DwgToPdf.csproj` 是 **SDK-style**（无显式 Compile Include）→ 新增 .cs **无需改 csproj**；② `SelfTest.csproj` 白名单**不含 MainForm.cs** → 搬迁不影响自测编译（仍按 PRJ-002 重建）；③ 字节形态：全部 .cs = **无 BOM + CRLF**，而 `DwgToPdf.csproj` = **无 BOM + 纯 LF**（20 loneLF）。
- **`design-b4.md` 已落盘并提交 `3e62906`**（250 行、CRLF 250/250、无 BOM）：含搬迁清单（12 方法精确坐标）、不搬清单（字段/8 处调用点/相邻区块）、新文件 using 精确清单、行数测算、D1~D4 偏离记录、V1~V5 验证方案、附录 A 坐标表 + 附录 B 骨架。
- **B4 已派工程师实施**（T1 块 A 新文件 → T2 块 B 新文件 → T3 从 MainForm.cs 删除 → T4 编译+120 断言+V1 逐字比对+V5 调用点+文档四件套「修订十二」+提交）。
- git：main 领先 origin 17（+B4 设计），未推送。

### 会话二十四·续：B4 实施交付 + 主理人独立核验 + 设计勘误（18:3x）
- **B4 实施 commit `cefe588`**（7 文件 +339/−240）：`MainForm.cs` 2103→**1863**；新建 `MainForm.Settings.cs`（100）、`MainForm.FileCollection.cs`（164）；文档四件套「修订十二」（手册新增 §5.16）。
- **主理人独立核验（不采信工程师自检）**：① **V1 纯搬迁**——块 B **0 处差异**（150/150 逐字一致）、块 A **仅 1 处差异** = 设计 D2 声明的 `LoadAppSettings` 注释+签名缩进修正（与声明的例外**完全吻合**）；`git diff` **净增 0 行**（新 `MainForm.cs` 是旧文件的**纯子序列**，纯搬迁最强信号）。② 断言 **120 PASS / 0 FAIL** + 引擎新鲜度守卫 PASS（主理人自跑）。③ 三个 .cs 全部**无 BOM + CRLF**（1863/100/164）。
- **⚠ 设计勘误（主理人笔误，工程师补正）**：`design-b4.md` §5 的 using 清单漏 `System.IO`（`ApplyUiState` 用 `Directory.Exists`）与 `System.Linq`（`AddToBatch`/`ClearList` 的 `Contains(value, comparer)` 实为 `Enumerable.Contains` 扩展方法），且设计还**错写「不引 System.Linq」**——属方向性错误。工程师补两条后编译 0/0，并记入手册 §5.16。
- **经验库 33→34**：新增 **GEN-WIN-018**（partial 拆分配 using 须按「原文件 using − 新文件 using」逐一核对符号来源；扩展方法最隐蔽）。check/toc 过。已补记入 `design-b4.md` 补记节。
- **发布 exe 同步**：B4 为纯结构搬迁（功能零变化），工程师按设计未重建发布 exe（仍为 B3 的 `4ddfff41…`）；主理人核验后**要求补刷**——避免「发布 exe 与源码不对应」的可追溯性缺口（将来取证会看到旧结构），作为独立补提交处理。
- **发布 exe 补刷已完成（commit `c1f0739`）**：发布 exe 新 md5 = **`d15cee58a597f01bb21f29597d3ccb1b`**（旧 = B3 的 `4ddfff41…`）；与源码 `bin/Release/net48/DwgToPdf.exe` **md5 一致**（主理人双点核对）。「源码 ⇄ 发布 exe」对应关系恢复。
- 下一步：派 QA 独立复验（V1~V5 + 全量回归）→ 弹窗验收。
- git：main 领先 origin 18（+B4 实施 + exe 补刷），未推送。

### 会话二十四·再续：B4 QA 独立复验（18:4x）
- **QA（严过关）复验结论：V1~V5 全 PASS + 发布 exe 复核 PASS —— 可提交用户验收。** 未发现 P0/P1/P2，方案本身无问题。
- **V1 纯搬迁（difflib 内容锚定，遵 GEN-TEST-008）**：旧版取 `git show 3e62906:` 而非工作区；统一行尾后比对——旧→新 MainForm **inserted/replaced = 0**（`delete old[1109:1260]` 151 行 + `delete old[1872:1961]` 89 行）→ **纯子序列、净增 0 行**（与 `git --stat` 的「MainForm.cs 仅 240 −、0 插入」两法互证）。块 B **0 处差异**（旧 L1110–1259 与 `FileCollection.cs` L14–163 逐行一致；唯一被标记的 1 行是旧 L1260 的方法间空行，不属方法体）。块 A **仅 2 行不同** = D2 声明的缩进修正（与主理人先前判读的「1 处差异/2 行」口径一致）。12 方法（A 4 + B 8）集合完整、无重复无丢失。
- **V2**：主工程 + `-t:Rebuild` 强制重编 + SelfTest `--no-incremental` 均 **0 警告 0 错误**。
- **V3**：SelfTest（GBK 解码）**120 PASS / 0 FAIL / exit 0**，末行 `ALL TESTS PASSED.`，含 `[PASS] 引擎新鲜度`。
- **V4**：1863 / 100 / 164；三文件 + 既有 `PhaseLog` 全部**无 BOM + CRLF**（loneLF=0）；`partial class MainForm` 类声明一致（`: Form` 仅主文件，符既有先例与设计骨架）；using 逐条必要——**重点核 `FileCollection` 的两参 `Contains(full, StringComparer.OrdinalIgnoreCase)` 确为 `Enumerable.Contains` 扩展方法 → `System.Linq` 必要且已引**，设计勘误已正确落实。
- **V5**：调用点 **10 行逐字未改**（L204/207/208/246/248/492/778/819/820/1097；含 `FormClosing += delegate { SaveUiState(); };` 同行多语句与 DragEnter/DragDrop 两行）；E2E **115 进 → 113 成功 / 2 失败**（2 张 H77 风扇件既有失败）、124.0s，与基线一致。
- **发布 exe 复核**：QA 独立复现 `发布 == bin == d15cee58…`；QA 强制重建曾把 bin 改为 `40de5837…`（同 size/同元数据、仅 150 字节差，集中在 PE 头 timestamp/MVID 区）→ 复验后 `cp -p` 还原为 `d15cee58…`，工作区恢复 as-found。
- **QA 提出的两条观察（非缺陷）**：① 设计 §4/手册 §5.16 写「8 处调用点」，实际 grep 10 行（口径问题，设计补记已说明）；② **net48 构建跨次不可字节复现**（内嵌 PE 时间戳）→「发布 exe ⇄ 源码」对应**依赖人工补刷**，建议保留「每次改源码后同步补刷发布 exe 并记 md5」的既有流程。
- git：main 领先 origin 18，未推送（仅提交不推送）。

### 会话二十四·三续：B5（CQ-08 第二批）设计→实施→QA 全链（18:5x）
- **用户裁决**：B4 验收通过；**启动 CQ-08 第二批（BuildUi 拆分，编号 B5）**。
- **勘察实测（主理人亲测，基线 `91966d6`）**：`BuildUi()` = **L222–886 / 665 行**（`MainForm.cs` 中最大单一方法）；**调用点仅 1 处**（L205 构造函数内）；`MainForm.cs` 1863 行 / 无 BOM / CRLF。
- **⚠ 勘察关键发现（差点误判方案作废）**：`BuildUi` 在「按 `^\t` 抓顶层成员」的扫描中**不出现**，极易被误判为**构造函数内的局部函数**（若成立则捕获构造函数局部变量、**根本无法拆到别的文件**）。三条独立证据推翻：① 构造函数闭合大括号在 **L220**，`BuildUi` 声明在 L222（在其之外）；② 花括号配平显示 L222–886 恰为一个完整成员；③ 它未进清单纯粹因为**声明行丢了前导 tab**（全文件扫描确认这是唯一的此类瑕疵）。
- **`design-b5.md` 提交 `7187ffe`**（199 行 / CRLF 199/199 / 无 BOM）：含 D1~D7、using 符号级清单、V1~V5、T1~T4、附录 A 坐标表 + 附录 B 骨架。**using 定为 5 条**（System / Diagnostics / Drawing / IO / WinForms），**本块不需 `System.Linq`**（0 处 `Enumerable` 调用——与 B4 的 `FileCollection` 不同，**B4 教训不得机械套用**）。
- **B5 实施 `9921aa2`**（6 文件 +752/−666）：`MainForm.cs` **1863 → 1197**（**达成 CQ-08 ≤1400 验收线**）；新建 `MainForm.BuildUi.cs`（679 = 13 脚手架 + 665 方法 + 1 闭括号）；文档四件套「修订十三」（手册 §5.17）。
- **主理人独立核验（不采信工程师自检）**：V1(a) 旧删 L222–887 后 **0 差异**；V1(b) 方法体 664 行 **逐字 0 差异**；V1(c) `inserted=0/replaced=0/deleted=666`；D2 唯一例外确认；**V3 主理人自跑 120 PASS / 0 FAIL / EXIT=0** 含引擎新鲜度；两 .cs 无 BOM + CRLF；**D3 的 L187 四空格缩进原样未动**；调用点 L205 逐字未改；发布 exe md5 `e0dff858…` 与 bin `cmp` IDENTICAL。
- **B5 发布 exe 补刷 `cf1ff20`**（D6 按设计执行，**未事后追补**）：新 md5 **`e0dff8587a027c78bad4a52dd45b1106`**（旧 = B4 的 `d15cee58…`）。
- **QA（严过关）独立复验：V1~V5 全 PASS + 发布 exe 复核 PASS + 硬指标达标 —— 可提交用户验收**；无 P0/P1/P2。V4 附加：**using 必要性用「拷贝工程 → 删单条 → 编译」实测**（WinForms/Drawing/Diagnostics/IO 必要；**`System` 非必要**，符 D7 保守引入）；V5 全量回归 **115 进 → 113 成功 / 2 失败**（H77 既有）。
- **⚠ QA 抓出 P3（主理人文档笔误，当批更正）**：设计 §5 与手册 §5.17 把 **`Padding`×53** 列为 `System.Drawing` 的依据，**实际 `Padding` 属 `System.Windows.Forms`**。**主理人编译探针复现**：删 `using System.Drawing;` → **恰好 8 个 CS0103 全在 `ContentAlignment`、`Padding` 零错误** → 真正依据是 **`ContentAlignment`×8**。结论（System.Drawing 必要）不变，仅**依据符号归类错**。**QA 亦自我纠错**：它初判「System.Drawing 不必要」被探针推翻。
- **文档更正提交 `ec710d5`**（`design-b5.md` §5 + 手册 §5.17 + B5 设计补记与 QA 复验归档）。**泛化教训（待用户确认后并入 GEN-WIN-018）**：GEN-WIN-018 的「核对符号来源」有下一层陷阱——**符号的命名空间归属也必须实测，不能凭直觉**；可靠做法 = **编译探针（删单条 using → 看 CS0103 落点）**。
- git：main 领先 origin 23，**只提交不推送**。提交链：`7187ffe` → `9921aa2` → `cf1ff20` → `ec710d5`。
- **待用户弹窗裁决**：B5 全链验收 + P3 处置确认 + 是否记录经验 + 下一步（CQ-08 是否还有后续批次）。

### 会话二十四·五续：B5 收官 + B6（CQ-09 注释批）设计→主理人接手（19:2x）
- **用户裁决（三项）**：① B5 验收通过；② 经验并入 **GEN-WIN-018**（已扩写两层：符号来源核对 + 归属编译探针实测；34 条，check/toc 过）；③ 启动 **CQ-09**。
- **CQ-09 勘察两项发现**：① **PRD 内部口径矛盾**——方案段「public/internal 100% 覆盖」= +2 条 vs 验收段「计数 ≥50%」= +14 条以上，差一个数量级 → 用户裁决**改按覆盖率口径** + **家族全口径范围**；② **统计仪器陷阱**——`private readonly List<string> _x = new List<string>();` 这类带初始化器的字段因含 `(` 被误判成方法（首轮 MainForm.cs 虚高 47 成员/39 待补）→ 修正判据 = **只看第一个 `=` 之前的文本**。
- **又一自我纠错（主理人）**：设计附录 B 示例第一版凭印象写错两处（`UniquePdfPath` 的半角括号 `(2)` 写成 `-2`；`RefreshListUi` 的三个列表写成两个），核对方法体后修正并**留痕**——示例与正式注释都必须以方法体为准。
- **`design-b6.md` 提交 `826e313`**（254 行 / CRLF 254/254 / 无 BOM）：33 处精确清单（附录 A）；D1~D9（字段不加 doc、不加类级 summary、XML 转义防 CS1570）；**V1 最强判据 = 删除行全 0 且新增行逐行 `///`**。
- **⚠ 过程异常：工程师 429 限流中断**。工程师**已写完全部 33 处**（T1 完成）后被平台 429 打断（配额 2026-10-17 17:08 重置），T2~T4 未执行。主理人核验磁盘实况（33/33 全完成、V1 双判据过）后**接手剩余步骤**（沿用 architect 429 时「主理人接手」先例）。
- **主理人接手完成**：T2 自验（编译 0/0 含 CS1570=0、SelfTest `--no-incremental` 120/0 含引擎新鲜度、**D5 抽查五条**——`PushUndo`/`UndoLast`/`FormatElapsed`/`Start`/`ProcessCmdKey`——与实现逐项吻合、无臆造）+ T3（文档四件套「修订十四」：手册 §5.18 / 版本变更记录三处 / 说明.txt 第二十四节 / 运行说明.txt 第二十五节；发布 exe 补刷新 md5 **`b10441fccb42c5dcbe14190b361580c2`**，cmp IDENTICAL）+ T4 提交。
- **B6 提交**：`354a7bf`（实施，10 文件）+ `3d52d4c`（发布 exe 补刷）。工作区干净。
- **验收缺口（明示）**：QA 独立复验与 **E2E 全量回归 113/2** 因配额未跑——纯注释批（IL 不变）风险极低，但按流程应待配额恢复（2026-10-17 17:08 后）补 QA 复验。
- git：main 领先 origin 27，**只提交不推送**。提交链：`826e313` → `354a7bf` → `3d52d4c`。
- **待用户弹窗裁决**：B6 验收（含「QA 复验后补」缺口确认）+ 是否记录经验 + 下一步（PRD 需求池已清空；剩余 = 遗留台账：双 BOM、L187 缩进等格式债）。
### 会话二十四·六：格式债小批（修订十四·补二）主理人直接实施（19:5x）
- **前置状态**：B6 验收通过（QA 复验后补）；用户裁决下一步 = 格式债小批；工程师配额未恢复（2026-10-17 17:08）→ 主理人直接实施（PRJ-006 先例）。
- **设计 `design-b7.md` 提交 `0591214`**（73 行 / 无 BOM + CRLF）：双 BOM 起源考证（`76b9be4` 单 → `c1ed5fe` 引入双）+ L187 精确坐标；V1 双判据。
- **实施（字节级）**：① MainForm.cs L187 四空格 → tab（numstat 1/1，diff 仅缩进）；② 版本变更记录.txt 双 BOM → 单 BOM（-3 字节，逐字节保真断言 + CRLF 计数不变断言）。
- **验证**：编译 0/0；SelfTest `--no-incremental` 重建（引擎新鲜度 1792151148 ≥ 1792151142）**120/0 全过**；发布 exe 补刷 md5 **`dead4f3d9bf370f40bfd58eea46bb175`**（cmp IDENTICAL；首次 cp 报 busy 系瞬时锁，进程列表无 DwgToPdf/AutoCAD，重试成功）。
- **版本变更记录三处补记**（修订十四·补二）：版本表行 + 关键事件 19:5x 行 + git 里程碑行；写回显式单 BOM（防复发）。
- **遗留缺口**：E2E 全量回归与 QA 复验（B6 + 本批合并）待配额恢复后补。
- git：main 领先 origin 29，**只提交不推送**。
### 会话二十五：QA 补验 B6+B7（10-19 09:5x~10:1x）+ B6·补三处 doc（修订十四·补三）
- **QA 补验派发波折**：原 QA 队友（software-qa-engineer）两次投递均 0 秒失败（not available，代理不可用）→ 按 PRJ-006 改派全新独立验证代理（general-purpose-1，独立上下文保持复验独立性）。
- **补验结论**：B 批（格式债 7 项判据）PASS、C 段（编译 0/0 / SelfTest 120/0 / exe md5 dead4f3d 一致 / E2E 115 张 113 成功 2 失败=H77 既有，125.7s）PASS；A 段唯一 FAIL = A2 覆盖率声称「78/78」不可复现（实测 87 成员 84 带 doc，缺 RunCore/MarkAllCancelled/BuildGrid 三私有方法）。
- **主理人口径判读**：RunCore/MarkAllCancelled 是 design-b6 §「不纳入」明确排除项（PRD 只点 public/internal）——QA 不知此决议，此 2 处不成立；**BuildGrid 是设计分母静默漏计**（重放设计统计规则得 MainForm.cs 42/17，设计表写 41/16，差值恰为 BuildGrid）——设计文档统计错误。
- **用户裁决**：补齐 3 处 doc（推荐项）。主理人直接实施（量小 + 先例）：精读三方法体后按家族 doc 风格补齐（字节级锚定插入，无 BOM + CRLF 保持）。
- **终算**：7 文件方法/属性 doc 覆盖率全口径 **97/97 = 100%**（ConverterEngine 27/27、Plot 5/5、MainForm 42/42、BuildUi 1/1、Settings 4/4、FileCollection 8/8、PhaseLog 10/10）。
- **验证**：编译 0/0（含 CS1570=0）；SelfTest --no-incremental 重建（新鲜度 1792376515 ≥ 1792376504）**120 PASS / ALL TESTS PASSED**；发布 exe 补刷 md5 **`58944dddece3cb6d5560bdeb61403cb8`**（cmp IDENTICAL）。
- **文档同步**：design-b6.md 两处补记（排除决议作废 + 终算行）；版本变更记录三处补记（修订十四·补三）。
- **QA 代理其他有价值发现**：任务书字面口径「所有含=行」会误捕 212 行调用语句（QA 自行采用忠实口径处置正确）；E2E 应使用 --dir（3 参数语义），--dirp 是固定时刻两轮语义；SelfTest 自带 dir-e2e 断言期望 115 全成功与产品基线 113/2 不符属断言口径差异。
- git：只提交不推送。

### 会话二十六：HQ 阈值链路彻底清理（v4.17 修订十五，10-19 11:4x~12:0x）
- **用户需求原文**：`PrintQuality.IsHighQuality()` 恒 false、`PlotterFor()` 恒返回标准 pc3，但 `HighQualityThresholdBytes` 仍从 AppSettings / PrintOptions / PageSetupPreprocessor / ConverterEngine.Plot 多层传递 → 要求清理。
- **用户四项裁决（全按推荐项）**：① AppSettings 属性 + settings.json 同名键全删；② IsHighQuality / PlotterFor / MapMedia 三方法全删，调用点直引 `PrintQuality.StandardPlotterName`；③ SelfTest 删旧 API 断言 + 补 1 条（实施时实际删 5 补 2，理由见下）；④ 口径 v4.17 修订十五，**不升版本号**。
- **删除清单（9 文件）**：PrintQuality（IsHighQuality / PlotterFor / MapMedia / ThresholdBytes / HighQualityPlotterName，并重写类头注释）、PrintOptions、AppSettings（属性 + ToJson 两处 + Parse 一处）、PageSetupPreprocessor（属性 + 2 处调用）、ConverterEngine（`hqStat` 计数链 8 处 + prep 初始化器）、ConverterEngine.Plot（3 处调用）、MainForm（注入 1 处）、SettingsForm（4 处悬空注释引用）、SelfTest（5 条旧断言 + `--probe-hq-media` 探针与 CLI 分支）。
- **顺带修掉的真缺陷**：`AppSettings.ToJson` 曾把 `HighQualityThresholdBytes` **写两遍**（L198 写死 1048576 + L201 写属性值）→ JSON 重复键。
- **断言口径核对**：基线实测 119（该次运行缺主 exe → 新鲜度守卫跳过），补上新鲜度即 120；当前 117（含新鲜度）。断言名逐条 diff：仅基线有 5 条（旧 API）、仅当前有 2 条（JSON 无键 / 相邻 RasterImageDpi 未误删）+ 新鲜度 1 条 → 与「删 5 补 2」完全吻合。
- **验证**：编译 0/0；SelfTest 117/117 ALL PASSED；E2E 全量 115 张 → **113 成功 / 2 失败（H77 风扇件既有）**、124.5s，与基线一致；发布 exe 补刷 md5 **`7c108f7e2a72f685f98cc005814a6f32`**（cmp IDENTICAL）。
- **文档同步**：源码说明.txt（7 处）、说明.txt（v4.16 段补注）、手册（24.3 字段映射 + 第28章修订十五补记）、运行说明.txt（新增第二十六节 + 修「大图（高质量打印）」措辞）、版本变更记录.txt（版本表行 + 关键事件行）。
- **环境坑（本轮新增）**：① 从桌面路径启动的 `SelfTest.exe` 被系统拒绝 `Directory.CreateDirectory`（连 %TEMP% 也拒），同一 exe 拷到 %TEMP% 运行即正常；② 残留 SelfTest 进程无法 Kill（拒绝访问，Path 为空）→ 目标 exe 被锁时改用 `-p:OutputPath=bin/Alt/` 绕开。
- git：只提交不推送。

### 会话二十八：文件夹整理 + 目录结构说明（10-19 12:2x）
- **用户需求**：整理文件夹、删除不需要的文件、更新已有的文件。
- **四项裁决**：① 另一会话 12:15 提交的 `.planning/architecture-roadmap.md` 及 progress「会话二十七」记录——**保持删除**（git 历史 `d87c22c` 仍可找回）；② 删除范围圈定「运行日志 + TEMP 残留」，其余授权主理人自行判断；③ **保留 v4.16** 回滚基线；④ 「更新已有的文件」= 根级目录说明 + `.planning` 台账，两者都做。
- **实际删除**：%TEMP% 测试残留（e2e_hqclean 35M / st_cur 497K / stcopy_hq 377K）；`发布程序 v4.17\运行日志\` 5 个文件（161K，程序运行自动重建）；`自测试工程\bin\Verify` 与 `源码工程\bin\Verify`（8.0M，外部会话验证脚本产物）。
- **判断保留（附理由）**：`.planning` 一次性脚本与清单（50K，质量审查过程证据，git 有历史）；`_审计脚本` / `_对比探针`（313K，10-14 取证工具，各带说明）；v4.16 基线（2.7M，决策 D1/D2）；Release 编译产物（下次验证即用，且 SelfTest.exe 被不可杀残留进程锁住）。
- **整理结果**：`DwgToPdf 工具 v4.17` 11M → **2.9M**；仓库 24M → 16M；工作区 181M（其中测试语料 165M 为必需）。
- **新增**：工作区根 `目录结构说明.txt`（BOM+CRLF，97 行）：顶层结构 / 仓库结构 / 工程内部 / 能删与不能删 / 常用操作（含 SDK 路径与自测注意事项）。
- git：本会话只提交台账与删除，不推送。

### 会话二十九：仓库重建（10-19 12:4x）
- **用户需求**：要一个干净的 git，删掉之前的、重新建仓库。
- **四项裁决（全按推荐）**：① 删前先打包备份；② 新仓库含全部文件；③ 先不配远端；④ 单个初始提交。
- **执行链**：`git bundle create`（全部 refs，2.1M，verify 通过、含旧 HEAD `9903599`）→ 删 `.git`（9.5M）→ `git init -b main` → `core.autocrlf=false` → `git add -A`（197 文件，无 bin/obj/日志）→ 单个初始提交（哈希以 `git log` 为准，amend 会改写）。
- **关键处置**：首次 `git add` 出现 LF→CRLF 警告（全局 autocrlf=true），会改变 .py 等文件字节 → 改 `core.autocrlf=false` 后重新暂存，警告归零、索引内行尾与磁盘一致（.cs/.txt 仍 CRLF，部分 .py 仍 LF）。
- **结果**：提交数 1、跟踪文件 197、`.git` 3.1M、**无远端**（原 origin 随 .git 一并删除）、工作区干净。
- **旧历史去向**：仅存于工作区根 `Dwg-To-PDF-历史备份-20261019.bundle`（仓库外，2.1M）。本文与 handoff 中出现的旧哈希（`3f5c6ce` / `36cc08d` / `d87c22c` / `9903599` 等）在新仓库中已不存在。


## 会话三十（2026-10-19 12:5x）：远端仓库替换为当前干净仓库

- 用户要求「把 GitHub 上面那个删了，推现在的」。
- **探测**：`gh` 未安装；`GH_TOKEN`/`GITHUB_TOKEN` 空；`git credential fill` 返回空 username + `password=NO`
  → 本机无删除仓库的能力（需 `delete_repo` 权限）。远端原状 `main`=3f5c6ce、`ui-optimization`=c1ed5fe。
- **用户裁决**：强推覆盖（三选项中的推荐项），不删库重建、不传 token。
- **执行**：`git remote add origin ssh://git@ssh.github.com:443/zzh112212/Dwg-To-PDF.git`；
  `git config core.sshCommand "ssh -i ~/.ssh/id_ed25519_dwgtopdf -o IdentitiesOnly=yes"`；
  `git push --force origin main`（`+ 3f5c6ce...40cfff6 main -> main (forced update)`）；
  `git push origin --delete ui-optimization`（`- [deleted]`）；`git branch --set-upstream-to=origin/main main`。
- **终态**：远端仅 `refs/heads/main` = `40cfff6`，与本地一致；`git status -sb` = `## main...origin/main` 无差异；
  API `size` 2843 KB、`pushed_at` 2026-10-19T04:49:38Z、`default_branch` = main。
- **副作用**：远端旧历史不可达，唯一副本为工作区根 `Dwg-To-PDF-历史备份-20261019.bundle`。
