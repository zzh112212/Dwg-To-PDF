# 会话日志（追加式）

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
