# DwgToPdf 代码质量改善增量 PRD

> 版本 v1.0 · 2026-10-16 · 产品经理 Alice 制定
> 上游输入：主理人质量扫描（2026-10-16）、《代码审查标准与流程 v1.0》、源码抽样核实（本 PRD 只读代码，未改任何文件）

## 1. 项目信息

| 项 | 值 |
|---|---|
| 项目名 | dwgtopdf_code_quality |
| 仓库 / 基线 | Dwg-To-PDF @ main 65d5fe4（工作区干净） |
| 原始需求复述 | 旧诉求「代码质量参差不齐」已由《代码审查标准与流程 v1.0》建立审查机制；本轮新诉求「**检查现有代码质量并改善**」，已选定**系统分批改善**路线 |
| 本轮定位 | 质量改善增量：**禁止新功能需求，禁止重写式重构**，优先可验证的低风险变更 |
| 技术栈 | C# / .NET Framework 4.8 WinForms（**非默认 Web 栈**）；AutoCAD 2021 accoreconsole.exe 无头导出 + COM 后期绑定双通道 |
| 代码规模 | 源码工程 38 个 .cs / 12,514 行；自测试工程 108 条断言 |
| 既有依据 | 《代码审查标准与流程 v1.0》（P0~P3 分级、空 catch 明断规则）、《DwgToPdf完整经验手册》、experiences/lessons.md（GEN-* / PRJ-* 系列） |

## 2. 产品定义

### 2.1 产品目标（3 个，正交）

- **G1 消除静默不一致**：每一处「放弃处理异常」都必须可自证——要么有「为什么可以失败」的注释，要么把真实错误送达日志/界面。对应 CQ-01 / CQ-02 / CQ-03 / CQ-05 / CQ-06。
- **G2 可维护性收敛**：注释覆盖、catch 规范化、巨型文件按既有纪律收敛（单文件 ≤ ~2,100 行、partial 拆分先例）。对应 CQ-04 / CQ-07 / CQ-08 / CQ-09。
- **G3 零行为回归**：任何变更以「编译 0 警告 0 错误 + SelfTest 108/108 + 全量回归 113 成功 / 2 失败（H77 既有）」为验收锚点，每批可单独 git revert。

### 2.2 用户故事

1. As a 工具维护者，I want 每处空 catch 要么注明「为什么可以失败」要么把真实错误记入日志，so that 出问题时能第一眼区分「设计上的放弃」与「事故」。
2. As a 工具维护者，I want 后台图框识别任务的异常不再被静默替换为空结果，so that 批量失败时日志报的是真实原因而非误导性的「图框问题」。
3. As a 排查工程师，I want 日志写入失败时在 BgLogger 留痕，so that 不会陷入「运行日志空白且无任何提示」的排查黑洞。
4. As a 新接手开发者，I want BgLogger 等公共 API 有 XML doc、巨型文件有拆分策略，so that 能快速定位职责边界、敢于修改。
5. As a 最终用户，I want 本轮改善不改变任何转换行为与产出，so that 回归基线与 v4.17 完全一致。

## 3. 核实后的质量发现清单

核实方法：多行正则 `catch\s*\{\s*\}` 全量扫描 + 逐文件上下文抽样精读；所有位置均已对源码复核。

| # | 扫描数据 | 核实结论 | 问题事实 / 影响 | 位置（抽样） |
|---|---|---|---|---|
| F1 | 空 catch 140 处 | 属实，分布吻合（ConverterEngine 30、CadConverter 16、ComStarter 14、PageSetupPreprocessor 12、FrameExtractor 10、MainForm 10、BgLogger 9、ConverterEngine.Plot 9、PlotSession 7、其余文件 24） | 按审查标准 §2.1 明断规则分三类：**A 合法 best-effort 约 90 处**（COM 幂等 SetVariable / ReleaseComObject / FinalReleaseComObject、File.Delete 与 Directory.CreateDirectory 幂等清理、env 与 FileInfo 探测、Process.Kill 取消路径、log.WriteLine 尽力写法——符合规则，保留）；**B 合法但缺「为什么可以失败」注释约 30 处**（BgLogger 8、ComStarter 8、RunHistory 1 等，多为多行式空 catch，违反 §2.1「一行式 + 注释」）；**C 疑似吞真实错误约 20 处**（逐处明细见需求池 CQ-01~CQ-06）。CadConverter「一行式 try/catch + 幂等注释」属实，判定合法 | CadConverter.cs:87/114/238/242；BgLogger.cs:50；ComStarter.cs:113/147 |
| F2 | `.Result` 阻塞 | 仅 1 处真实：ConverterEngine.cs:126（其余 4 处为 EnvironmentSelfCheck.Result 枚举成员，误报） | 核实为活代码：RecognizeChunkPipeline 内部不吞异常（OperationCanceledException / 超时 / 故障均会上抛），:126 的 catch 生效时把整个后台识别任务结果换成空字典 → 该 chunk 文件全部缺图框 → 以误导性「图框问题」失败。阻塞发生在引擎工作线程（非 UI 线程），无死锁风险，问题仅在异常吞噬 | ConverterEngine.cs:126-127 |
| F3 | Thread.Sleep 11 处 | 属实；10 处语义合理且有注释（worker 错开冷启动、重试间隔、marker 轮询、kill 冷却） | ConverterEngine.cs:565 的 15 秒经核实**不是「硬等任务」**，而是看门狗停滞检测的轮询间隔；stop 标志在 sleep 后检查、收尾 Join(3000) 不阻塞。仅剩整洁性改进空间 | ConverterEngine.cs:79/565；CadConverter.cs:273/353/407；Late.cs:88 |
| F4 | 巨型文件 | 属实：MainForm.cs 2086 行、ConverterEngine.cs 1437 行、自测试工程/Program.cs 1542 行 | MainForm 距审查标准 §3.1 上限（~2,100 行）仅余 14 行；已有 partial 先例 MainForm.PhaseLog.cs 可循 | 上述三文件 |
| F5 | XML doc 不均 | 属实：UiTheme 37/190、MainForm 31/2086、BgLogger 0/221（10 个 public 方法零文档）、ConverterEngine.Plot 4/656（无 public 成员） | BgLogger 是「新公共方法应有 XML doc」传统的最大亏空点 | BgLogger.cs:40-106 |
| F6 | async void / TODO / MessageBox | 属实且无问题：async void 0 处、TODO/FIXME 0 处、MessageBox 7 处均在 UI 层（MainForm 6 + SettingsForm 1） | 不进入需求池 | — |

## 4. 需求池（Requirements Pool）

口径：**P0 = 真隐患（阻塞、吞错误）；P1 = 一致性 / 可维护性必修；P2 = 择机改善**。⚠ = 改变行为（须重点回归）。
经核实本轮**无 P0 项**；最严重为 CQ-01（P1）。

### CQ-01 后台识别结果异常被静默换成空字典（P1，⚠）

- 问题：ConverterEngine.cs:126-127，`recogTask.Result` 抛出非取消异常 → catch 置空字典 → 整 chunk 以误导性原因失败、真实原因丢失（违反明断规则「conversion / 识别失败用户应知道」）。
- 方案：区分两类异常：OperationCanceledException 保持取消语义（置空字典 + 记日志）；其余异常记日志（含异常消息、堆栈、阶段上下文）后**不得再静默置空**——按 Open Question Q1 取向转为 chunk 级失败或逐文件失败（保留真实原因）。
- 验收：定向验证（方式见 Q5）；「识别任务抛非取消异常 ⇒ 日志含该异常且失败原因非『图框问题』误导」成立；编译 0 警告 0 错误；SelfTest 108/108；全量回归 113/115。

### CQ-02 日志写盘失败全程静默（P1，⚠轻微）

- 问题：LogWriter.cs:55-59 / 76-80 / 83-87，WriteLine / Flush / Close 吞掉所有写盘异常 → 运行日志为空且无任何提示（排查黑洞）。
- 方案：增加写入失败标志与 LastWriteError；失败发生时 mirror 一条 BgLogger.Error；引擎收尾（ConverterEngine.cs:1158 附近）追加一行「[警告] 日志写入失败：{原因}」。**成功路径零变化**。
- 验收：模拟日志路径不可写 ⇒ BgLogger 可检索到失败警告；正常路径日志文件与改善前逐字节一致；编译 0/0；SelfTest 108/108。

### CQ-03 空 catch 注释规范化（P1，不改行为）

- 问题：约 30 处合法 best-effort 缺「为什么可以失败」注释；多行式空 catch 不符合 §2.1「一行式 + 注释」要求（B1 实施时以脚本判定「catch 行或上方 1 行无注释」产出确定清单）。
- 方案：纯注释 / 排版批——逐处补一行注释（沿用既有措辞风格：幂等清理、best-effort 探测等），多行式空 catch 压成一行式；不动任何可执行代码。
- 验收：全仓空 catch 计数仍 = 140；脚本判定每处 catch 均有注释；git diff 仅含注释 / 空白行；编译 0/0；SelfTest 108/108。

### CQ-04 BgLogger 公共 API 补 XML doc（P1，不改行为）

- 问题：BgLogger.cs 0/221，10 个 public 方法零文档。
- 方案：为 Line / Info / Warn / Error / LogStartup / LogShutdown / LogRunStart / LogRunEnd / LogFile / DetectEnvironment 各补一句职责 summary。
- 验收：BgLogger.cs `/// <summary>` 计数 ≥ 10；编译 0/0。

### CQ-05 帧提取失败原因透传（P2）

- 问题：FrameExtractor.cs:425 / 562 把识别异常转成 box=null，下游只能报「图框识别失败」；:426 / 563 的 onFileDone UI 回调吞异常。
- 方案：catch 中把具体原因记入诊断日志（null 语义不变）；回调 catch 改 BgLogger.Warn 记一次。
- 验收：抽取异常时诊断日志（DWGTOPDF_LOG_DIR）出现具体异常消息；成功路径行为不变；SelfTest 108/108。

### CQ-06 UI 与杂项静默点补痕迹（P2）

- 问题：MainForm.cs:1928-1930 设置保存失败静默；MainForm.cs:1152-1154 粘贴整体吞异常；RunHistory.cs:104 / 111 历史读写静默；ConverterEngine.cs:404 / 437 / 580 / 1157 / 1188 各 envelope catch 无注释；EnvironmentSelfCheck.cs:58 修复失败吞异常。
- 方案：非 logger 自身失败改记 BgLogger.Warn / Error；BgLogger.cs:50（logger 自身写盘失败）加注释说明 chicken-egg 决策；其余补「为什么可以失败」注释。
- 验收：上述 catch 块内 / 周边可见 BgLogger 调用或注释；设置保存失败可在 BgLogger 检索到；SelfTest 108/108。

### CQ-07 看门狗形态整洁化（P2，⚠轻微）

- 问题：ConverterEngine.cs:557-582 轮询用 Thread.Sleep(15000)，循环体外层 catch { } 无注释。
- 方案：改 ManualResetEventSlim.Wait(15000, token)（行为等价、停止响应更快）；catch 补注释。
- 验收：停滞告警阈值（15 min）与 kill 冷却（3 min）逻辑不变（diff 审阅确认）；停止时看门狗退出耗时显著下降；SelfTest 108/108。

### CQ-08 巨型文件 partial 拆分（P2，⚠结构）

- 问题：MainForm.cs 2086 行（距红线余 14 行）；ConverterEngine.cs 1437 行；自测试工程/Program.cs 1542 行。
- 方案：仅按 MainForm.PhaseLog.cs 先例做**原样搬迁** partial（不改逻辑），拆分目标清单与粒度以 Open Question Q3 为准；自测试 Program.cs 是否纳入同批待定。
- 验收：搬迁后单文件 ≤ ~1,400 行；成员签名不变；git diff 显示纯搬迁；编译 0/0；SelfTest 108/108。

### CQ-09 注释密度提升（P2）

- 问题：ConverterEngine.Plot.cs 4/656、MainForm.cs 31/2086。
- 方案：ConverterEngine.Plot 无 public 成员 → 为关键私有方法补 summary；MainForm 补至 public/internal 成员 100% 覆盖。
- 验收：两文件 summary 计数各自提升 ≥ 50%；编译 0/0。

## 5. 分批建议

| 批次 | 内容 | 风险 | 独立验收标准 | 顺序 |
|---|---|---|---|---|
| B1 注释批 | CQ-03 + CQ-04 | 零（仅注释） | diff 仅注释 / 空白；空 catch 计数校验 = 140；编译 0/0；SelfTest 108/108 | ① |
| B2 异常可见化批（核心） | CQ-01 + CQ-02 + CQ-05 + CQ-06 | 中（⚠改行为） | 各 CQ 定向验证通过；BgLogger 可检索新痕迹；SelfTest 108/108；全量回归 113/115 | ② |
| B3 整洁项 | CQ-07 | 低 | 行为等价 diff 审阅 + 停止响应对比；SelfTest 108/108 | ③ |
| B4 结构批 | CQ-08（CQ-09 可并入 B1） | 中（⚠结构） | 纯搬迁 diff；编译 0/0；SelfTest 108/108 | ④（Q3 关闭后排期） |

执行纪律：每批一个 commit，message 口径 `v4.17 修订N：简述（范围）`，可单独 revert；批次间全量回归不达标不进下一批。

## 6. 不变量清单（红线，任何批次不得触碰）

1. **图纸铁律**：不改图纸内容、源图纸只读；探针先复制到 ASCII 临时目录。
2. **版本铁律**：Program.Version 维持 v4.17；升版须用户显式同意（PRJ-001）。
3. **测试底线**：SelfTest 断言不得低于 108 条；修复 bug 必须同批补断言。
4. **回归基线**：全量回归 113 成功 / 2 失败（2 张 H77 为既有问题），无新增失败。
5. **文档四件套（硬门）**：改代码必须同步《DwgToPdf完整经验手册.md》《说明.txt》《发布程序 v4.17/运行说明.txt》《版本变更记录.txt》。
6. **工程文件字节纪律**：Windows 工程文件字节级保留 CRLF+BOM；追加前探测行尾稳态（GEN-WIN-003 / 004）。
7. **Git 纪律**：只提交不推送；大批量编辑后立刻编译（GEN-TEST-002）。
8. **审查准入**：每批须过《代码审查标准与流程 v1.0》§4.3 自动门 + 双帽审查后才提交。

## 7. Open Questions（需用户 / 主理人拍板）

| # | 事项 | 影响 |
|---|---|---|
| Q1 | CQ-01 非取消异常的处置取向：整 chunk 级失败（简单、失败计数偏保守）还是逐文件失败并保留真实原因（精细、改动面大）？ | 决定 B2 改造半径与失败口径 |
| Q2 | CQ-02 日志写入失败的提示范围：仅收尾日志一行，还是同时 UI 提示（涉及改界面须 UI 取证）？ | 是否触发 UI 取证流程 |
| Q3 | CQ-08 是否接受 partial 搬迁式拆分？首批目标清单（建议：MainForm 的设置 / 持久化 + 拖放与文件收集两块）？自测试 Program.cs 是否同批？ | B4 排期前提 |
| Q4 | CQ-07 看门狗 15s 轮询改事件等待是否接受（行为等价、技术形态变化）？ | B3 范围 |
| Q5 | CQ-01 / CQ-02 的故障验证方式：受控人工评审 + 定向小工具，还是补注入型 SelfTest 断言（红线为 ≥108，允许新增）？ | B2 验收成本 |

---

**路由建议**：本 PRD 为实施依据。建议下一棒交给代码审查员 / 架构成员执行**批次 1（B1 注释批）**——零风险、当日可收官，为 B2 核心修复建立验证流程信任基线；B2 开工前请先关闭 Q1 / Q2 / Q5。


## 8. 用户拍板记录（2026-10-16，主理人转达）

| # | 决定 | 影响 |
|---|---|---|
| Q1 | CQ-01 取「**整 chunk 级失败 + 真实原因**」：保持现有失败文件范围不变，仅把误导性「图框问题」替换为真实异常记录 | B2 改造半径最小；CQ-01 方案按此取向实施 |
| Q2 | CQ-02 取「**日志 + UI 提示**」：BgLogger.Error mirror + 收尾日志一行 + UI 可见提示 | B2 涉及界面改动 → 触发 UI 取证流程（dump_controls + 截图） |
| Q5 | CQ-01/02 验证取「**补注入型 SelfTest 断言**」：新增注入型断言（总数允许 >108），符合审查标准「修 bug 必须同批补断言」 | B2 自测工程将新增断言；验收以断言 + 回归双轨 |
| Q3 | CQ-08 取「**接受 partial 原样搬迁式拆分**」：按 MainForm.PhaseLog.cs 先例仅做原样搬迁（不改逻辑）；**自测试工程/Program.cs 不同批**（单独安排） | B4 排期前提已满足，可随时启动 |
| Q4 | CQ-07 取「**接受事件等待改造**」：看门狗 15s 轮询改为事件等待（行为等价、技术形态变化）；**停滞告警阈值（15 min）与 kill 冷却（3 min）逻辑不变** | B3 范围以此为基线，设计已启动 |
