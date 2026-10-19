﻿# 任务计划 —— DwgToPdf 工具（当前版本 v4.17，修订八）

## Goal
维持 DwgToPdf 批量转 PDF 工具稳定交付：用户反馈逐条落地、全量回归无新增失败、
文档（手册/说明/源码说明）同步更新、发布目录 exe 与源码一致。

**硬约束（历次用户决策，长期有效）**：
- 不得改变图纸内容（不隐藏/删除 OLE 等改图方案一律否决）
- 源图纸只读，绝不改写（探针前先复制到 ASCII 临时目录）
- **版本号 = 显式决策**：未经用户同意不得升版（2026-10-15 教训，见 E12）
- 不明确的需求先问清楚再动手（用户要求 95% 把握才执行）

## Current Phase
phase8.12 —— **C 档 C1~C3 全部完成、用户验收通过、已合并 main、已推送远端（main + 存档分支
ui-optimization）；推送通道（SSH over 443）与 GCM 弹窗已根治；任务告一段落，等待下一次版本优化**。
- 分支拓扑终态：`main` = `de9d5f9`（= 合并 `762b877` + 台账），`ui-optimization` = `c1ed5fe`（C3），
  **两个分支均已推送到 origin（zzh112212/Dwg-To-PDF）**，本地与 origin 完全同步、工作区干净。
- 推送真因与解法（10-16 13:5x）：本机网络**域名级封锁 github.com:443**（沙箱内代理 CONNECT 502 /
  沙箱外直连 SYN 超时），但 api.github.com 200、codeload 301、ssh.github.com:443、github.com:22 全通
  → 走 **SSH over 443**：本机 ed25519 密钥 `~/.ssh/id_ed25519_dwgtopdf` + 仓库级 deploy key
  （id 166625055，可写）+ GitHub 官方 ed25519 主机指纹核对（SHA256:+DiY3wvvV6…COqU）后信任。
  origin remote 已改为 `ssh://git@ssh.github.com:443/zzh112212/Dwg-To-PDF.git`（https 通道本机不可用，可逆）。
- GCM「CredentialHelperSelector」弹窗已根治：`git config --global credential.credentialStore manager`
  （根因 = credentialStore 从未设置 → GCM 不知把凭据持久化到哪 → 每次用凭据都弹选择器）。
- C3 验证基线（仍有效）：主工程 0 警告 0 错误；强制重建 SelfTest 108/108 exit 0；
  exe md5 `7956647ea058a78b6f99e7e244b2a751`；文档六件套同步（手册 5.13 / 说明第十九节 /
  运行说明第二十节 / 版本变更记录 / .planning / memory）。
- 用户待决剩 2 项：① 是否把 `experiences/lessons.md`（25 条 / 约 267KB）上传 ima 知识库（需 IMA 凭证）；
  ② 是否保留 `_测试输出/UI评审/` 取证工具链（含 harness 与 _faildetail/）。

## Phases
| phase | 内容 | 状态 |
|---|---|---|
| 6 | v4.16：选纸 3% 容差 / PlotWithLineweights / OLE 根因定位 | complete（10-07，用户验收「深浅可以」） |
| 7 | v4.17 修订一：通用版识别提速 (load) / 质量审查 5 维 / 输出目录清理 / 界面精简 | complete（10-14 交付） |
| 8 | v4.17 修订二：UCS 复位为世界坐标系（问题图纸 557 27MY-Y17369xx 错位/裁切根因） | complete（10-15） |
| 8.5 | v4.17 修订三：跳过已成功图纸 / 时间戳 PDF 文件夹（全角冒号）/ UCS 条件复位 | complete（10-15） |
| 8.6 | 版本号回退 v4.18 → v4.17（用户决策：本轮不升版），93 处标注批量回退 | complete（10-15 13:15 重新发布） |
| 8.7 | v4.17 修订四：界面优化 B 档（P0 自动滚动开关失效 + 6 处像素错位 + 信息层级重组 + 阶段色正名） | complete（10-15 用户验收通过） |
| 8.8 | v4.17 修订五：修订四回归修复（选项区行 2 被顶出分组 → 单栅格 + 行 2 跨 6 列流式容器） | complete（10-15 验收通过；分支 b717d69） |
| 8.9 | v4.17 修订六（C 档 C1）：UiTheme 设计令牌 + FlatProgressBar 自绘进度条（带百分比）+ PhaseChipBar 阶段胶囊（五态）+ 按钮两级层级 | implemented（10-15，待验收） |
| 8.10 | v4.17 修订七（C 档 C2）：CardPanel 卡片式分区 + ColumnHeader 三栏状态点/计数徽标 + 「设置…」移到顶部标题条 + 窗口默认 900×760/最小 840×730 | complete（10-16 用户验收通过；commit 24ddf3c） |
| 8.11 | v4.17 修订八（C 档 C3）：失败原因内联展开（§5.5，note 截断摘要 32 字 +「详情 ▸」链接列 + Dock=Bottom 只读详情面板，不弹窗）+ 键盘可达性（§5.6，&S/回车/Esc/显式 TabIndex）+ 设置页三分类左右分栏（§6，560×400，「界面」分类不做） | complete（10-16 用户验收通过） |
| 8.12 | 收尾：ui-optimization→main 快进合并、main 与存档分支推送 origin、SSH over 443 推送通道打通、GCM 弹窗根治、.planning/memory/变更记录台账、上下文交接 | complete（10-16 13:5x，用户指令「合并/推送」执行完毕；等待下一次版本优化） |

## Decisions（仍在有效期，含理由）
| # | 决策 | 理由 |
|---|---|---|
| D3 | 不隐藏/删除 OLE2FRAME | 用户：等于改图纸内容 |
| D7 | 仓库只留当前版本目录，旧版目录备份 | 用户 10-07 决定 |
| D8 | 仓库根 = 工作区根（不是 `Git\` 子目录） | `Git\` 是便携 Git 发行包 |
| D9 | 提交身份用 `git -c` 传入，不写配置文件 | 用户全局身份为空且不允许改动 |
| D10 | push 走 SSH（22 通） | 实测 HTTPS(443) 被重置｜**10-15 纠正注记（F15/F16）：公司电脑 HTTPS 经本地代理可直推，本条仅适用个人机网络** |
| D11 | 版本号维持 v4.17，本轮三项需求称「修订三」 | 用户 10-15：本轮的改动不升版本 |
| D12 | 时间戳文件夹分隔符用全角冒号「：」 | 半角冒号是 Windows 文件名非法字符，直接过滤会把 09:08:07 滤成 090807 |
| D13 | 已成功图纸默认跳过，不加设置项 | 用户：正常只转换"待转化"列表里的文件 |
| D14 | UCS 先探测再复位（WORLDUCS==1 直接 return false） | 实测零开销：2.29s vs 2.29s（噪声内） |
| D15 | Windows 工程文件改写保留 CRLF + BOM | 防编码破坏 |
| D16 | 界面优化按用户选定「B 档」执行（A 保守修复 + 一致性优化），不升版本号 | 用户 10-15："A/B。不升版本。"；纯 UI 改动，不动引擎与领域规则 |
| D17 | 修订四「选项区两行拆成嵌套 TableLayoutPanel」判定为**回归** → 修订五**回退嵌套**、改单 6 列栅格 + 行 2 跨 6 列 FlowLayoutPanel；口径仍记「v4.17 修订五」，不升版本号 | 实测子表被撑到 100 逻辑像素高、行 2 整行不可见；AutoSize 行内不得放 Dock=Fill 子容器（手册 5.10） |
| D18 | C2 范围**按设计稿 §7 表**执行：卡片化（§5.1）+ 三栏状态点/计数徽标（§5.4）+ 「设置…」移位（§5.7）；§5.5/§5.6/§6 留 C3 | 用户 10-16 明确「按设计稿 §7（推荐）」；设计稿 §7 表是用户已审批的权威批次划分，与口头计划冲突时以它为准 |
| D19 | §5.7 「设置…」采用**新建客户区顶部标题条**（40px，左应用名 / 右 ⚙ 设置），操作条 5 列 → 4 列 | 用户 10-16 选定「新建客户区顶部标题条（推荐）」；WinForms 原生标题栏放不了按钮 |
| D20 | C2 起窗口默认客户区 900×760、`MinimumSize` 840×730（原 760×680 / 800×660） | 新增标题条 40 + 卡片间隙 4×10 = 80 逻辑像素纵向占用；原最小尺寸下 `_splitLog`（Panel1MinSize 140 + Panel2MinSize 60 + 5）溢出 58 逻辑像素 → 日志卡片被挤出可视区（手册 5.12 陷阱②） |
| D21 | C3 范围 = §5.5 失败原因内联展开 + §5.6 键盘可达性 + §6 设置页左右分栏；**§6「界面」分类不做**，只保留三分类、不新增设置项、不改 AppSettings.cs | 用户 10-16 拍板；C3 为收尾批，仍纯交互 + 布局、不升版本号 |
| D22 | DataGridView 新增列必须复核「该栏最小宽度合计 ≤ 容器宽」；Dock 布局加固定高度条必须同时复核「容器自己的高」 | 实测：失败栏最窄 264 逻辑像素，170+70+62=302 溢出 → 横向滚动条把「详情 ▸」滚出可视区（改 140+54+50=244）；固定 132 详情面板高于失败栏约 126 → 表格压成 0 高、面板 Y=-18 溢出（改为按容器剩余 clamp，最低 44px）。后者与 E22/GEN-WIN-010 同族不同维度：010 管窗口 MinimumSize，此条管容器高（手册 5.13 陷阱①②） |
| D23 | git push/fetch 固定走 **SSH over 443**（origin URL = `ssh://git@ssh.github.com:443/...`），不再尝试 https 直推 | 本机网络域名级封锁 github.com:443（沙箱内 CONNECT 502 / 直连 SYN 超时，双重实测），但 ssh.github.com:443 与 github.com:22 畅通；api.github.com 亦可作只读核对通道。已备：本机 ed25519 推送密钥 + 仓库级 deploy key（可写）+ 主机指纹核对（SHA256:+DiY3wvvV6…COqU） |
| D24 | GCM 后端存储固定 `credential.credentialStore=manager`（全局），不再每次弹选择器 | 根因：credentialStore 从未设置 → GCM 不知把凭据持久化到哪（manager/wincred/none）→ 每次用凭据都弹 CredentialHelperSelector，"Always use this from now on" 未勾 = 不记住。该项同时解释非交互环境 `git credential fill` 取空 |

## Error 表（同一失败动作不做第 2 次）
| # | 错误 | 处理 |
|---|---|---|
| E9 | Edit 在 `try {` 后插入 6 行垃圾（3 个多余 `{` + 注释被 Tab 粘连）→ CS1513 | 改完立刻编译；错误行号会漂移（1162→1163） |
| E10 | `CadConverter.Convert` 遮蔽 `System.Convert` → CS0119 | 类内有同名静态方法时必须写 `System.Convert.ToInt32` |
| E11 | SelfTest 断言写死旧文件夹名 "PDF文件" → 产品改名后假失败（日志成功、断言 0 PDF） | 先读日志再信断言；PDF 定位用 FindPdfFolder 动态查找 |
| E12 | 顺手把 `Program.Version` 升成 v4.18 | 升版必须经用户同意；已整批回退（93 处） |
| E13 | 主工程重建后 SelfTest.exe 变旧 → 引擎新鲜度守卫 FAIL | 主工程重建后必须重建 SelfTest 再跑断言 |
| E14 | .NET 字符串在 exe 里是 UTF-16LE，ASCII grep 查不到版本号 | 用 `s.encode('utf-16-le') in exe_bytes` 验证 |
| E15 | SelfTest 控制台输出是 GBK，管道抓取后按 UTF-8 解码全是乱码 | 按 GBK 解码 |
| E16 | 追加 memory 日志时把行尾转换写死 CRLF，而 memory 日志是纯 LF 无 BOM → 追加段混入 CRLF、自校验 FAIL | 追加前先探测 BOM 与主导行尾再构造插入文本（已升华条目 GEN-WIN-004） |
| E17 | `logHeader` 先 Add(Dock=Right 复选框) 再 Add(Dock=Top 标题) → 复选框被压成 108×3px，v4.15 的"自动滚动"开关静默失效（无报错、肉眼几乎不可见） | **WinForms Dock 按 z-order 逆序布局（后 Add 的先占位）**：同容器内 "Top 标题 + Right 按钮" 必须**标题先 Add**；UI 缺陷一律用控件树 dump 复测尺寸，不能只靠看截图 |
| E18 | 修订四把选项区两行拆成嵌套 TableLayoutPanel（子表 Dock=Fill 落在 AutoSize 行内）→ 子表被撑到 955×125（100 逻辑像素高），行 2 三开关被顶到 y=327（分组只到 y=279）、整行不可见；子表 `IsWindowVisible` 仍为 True，看不出问题 | **AutoSize 行/列内禁止放 Dock=Fill 子容器**；要「某行不参与列宽计算」用 ColumnSpan 跨满 + FlowLayoutPanel。UI 验收须「尺寸比对 + 截图」双证据并**逐分区**核对，不能只核对本次动过的控件 |
| E19 | `SelfTest --dirp` 报 `-2 目录非空 :: pdfs=0`，但递归计数 -2 目录里其实有 40 个 PDF | 断言用**不递归**的 `Directory.GetFiles(dir,"*.pdf")`，而输出**保留输入的子目录结构** → 输入语料含子目录时必然假失败（既有缺陷，非 C1 引入）。判定"与本次改动无关"的省力证据 = 查 `自测试工程/SelfTest.csproj` 的 `Compile Include` 白名单：本次改的 5 个文件命中数全为 0，SelfTest.exe 编译源与基线一致 → 该结果不可能由本次改动引起（比"重跑基线"快，比"凭感觉说无关"可靠） |
| E20 | 临时新建的 net48 小工程读 RVA 常量数组读到垃圾值：`new[]{10,20,30}` → `{57599,20,30}`、`new double[]{297.0,420.0,594.0}` → `{302.06…,420,594}`；垃圾值跨进程稳定（57599=0xE0FF），元素 0/1 随机 | 加 `<PlatformTarget>x64</PlatformTarget>` 或改目标 net8.0 即正常；**主工程 exe 与 SelfTest.exe 实测全部正确**。已排除沙箱/LangVersion/ReferenceAssemblies 包/PE 节属性/文件内 blob 字节；机制指向 **AnyCPU(I386+ILONLY) 镜像作主模块被提升为 64 位时的 RVA 字段处理**，产品无恙故未深挖。**纪律：不要拿一次性小工程的读数质疑产品**——判定产品须在**产品进程内**验证（本次：临时在 `Program.Main` 首行挂 `BlobDiag.Dump()` 写 %TEMP%，跑完删文件重建，md5 复原一致=零残留） |
| E21 | 自绘/透明控件（`ColumnHeader`、`CheckBox`）底色沿父链上溯，穿到 `SplitterPanel`/`Panel` 默认的 `SystemColors.Control` 灰 → 圆点与文字浮在灰块上，与白卡片格格不入（`IsWindowVisible` 仍 True，dump 看不出，只有截图能发现） | 透明自绘控件的父链容器 `BackColor` 必须**显式**设为卡片白（`pendingPanel`/`successPanel`/`SplitContainer.Panel1/Panel2`）。与 E17 同族：凡是「看不见的父容器」参与配色就要显式指定（手册 5.12 陷阱①） |
| E22 | 给 Dock 布局加了一条 Top 标题条 + 4 个卡片间隙后，原 `MinimumSize=800×660` 装不下：客户区仅 622 逻辑高，而 `_splitLog` 硬需 ≥205（140+60+5）→ 溢出 58 逻辑像素，**日志卡片被整个挤出可视区**（控件树能 dump 到它在 y=786，截图里没有） | `SplitContainer` 各 Panel 的 `MinSize` 之和是**硬下限，不会自动让步**。给 Dock 布局「加 Top 条 / 加间隙」后必须**同步复核窗口最小尺寸**；本次改为默认 900×760 / 最小 840×730（手册 5.12 陷阱②） |
| E23 | C3 失败栏新增「详情」链接列后，最窄容器（264 逻辑像素）下三列最小合计 name 170 + note 70 + detail 62 = 302 > 264 → 出横向滚动条，「详情 ▸」被滚出可视区（反射 detailRect 与截图双重确认；控件树 dump 看不出链接列"看不见"） | Fill 列先被挤到 `MinimumWidth`、NotSet 列按 `Width` 分配，总宽超容器必出 HScrollBar。**新增列必须算最小合计**：本次改 name 170→140 / note Min 70→54 / detail 62/56→50/46（140+54+50=244 ≤ 264），复验 `GetColumnDisplayRectangle(detail).Right ≤ DisplayRectangle.Right` 且 `HorizontalScrollingOffset == 0`（手册 5.13 陷阱①） |
| E24 | 详情面板固定 `Height=132` 高于失败栏实测高（约 126，最小窗口下）→ Dock 布局不自动收缩固定条：失败表格被压成 `Height=0`、详情面板 `Y=-18` 整块溢出到栏外 | Dock 只做容器像素分配，**固定高度条必须按容器剩余空间 clamp**：`Math.Max(44, Math.Min(132, Panel2.H − 表头高 − 2))`；与 E22 同族不同维度（E22 管窗口 MinimumSize，此条管容器自身高度）。复验：展开态 `panel.Top ≥ 0` 且 `panel.Bottom ≤ 容器高`（手册 5.13 陷阱②） |
| E25 | 主工程重建后直接 `dotnet build SelfTest.csproj` 报「无需还原」、SelfTest.exe mtime 不刷新 → 引擎新鲜度守卫 FAIL（107/108，唯一 FAIL 是守卫自己） | 增量判断看不出"要强制重建"：SelfTest 只 Include 白名单 .cs，不依赖主工程输出。**必须 `--no-incremental`**（3.3s 重编，108/108 恢复） |
| E26 | push 连续失败约 10 次，一度归因为"本地代理 502 / schannel 瞬时中断"（旧经验：原样重试即通）——当天全部失效 | 真因是**域名级封锁 github.com:443**：沙箱内（WorkBuddy sandbox-cli 出口 127.0.0.1:62808）CONNECT 502、沙箱外直连 SYN 超时；而 api.github.com 200 / codeload 301 / ssh.github.com:443 / github.com:22 全通。**教训：网络类失败先分层探测（沙箱出口 / 直连 / 备用端口），别把"重试即通"当万能结论**；解法见 D23 |
| E27 | Git Credential Manager 每次用凭据都弹「CredentialHelperSelector」选择器（manager/wincred/no helper），非交互环境 `git credential fill` 取空 | 根因 = `credential.credentialStore` 从未设置，"Always use this from now on" 未勾不记忆。修复：`git config --global credential.credentialStore manager`（D24） |
| E28 | 非沙箱 bash 里 `git push` 报 `fatal: not a git repository`（沙箱内从没遇到） | 非沙箱 shell 的起始目录不在仓库（agents 工具的 cwd 不跨模式继承）→ 非沙箱命令必须显式 `cd` 到仓库根。另：Windows python 不识别 MSYS `/tmp` 路径 → curl 输出经 shell 重定向后要**stdin 管道**给 python，不要 `python /tmp/x.json` |

## Next Step（单一动作）
**等待用户提出「下一版本优化计划」——需求到达前不动手**（用户规则：95% 把握再执行）。
- 恢复时（新窗口说「resume」）：先读 `.planning/handoff.md`（自包含），再看本文件 Current Phase；
  动工前先跑 `lessons.py view` 过一遍 PRJ-001~004 与 UI 类 GEN-WIN-005~014 / GEN-TEST-003~004。
- 下一版本开工三件事：① 与用户确认范围与是否升版本号（E12 教训：升版必须显式决策）；
  ② 若又动 UI，复用 `_测试_output/UI评审/` 工具链 + UiTheme 令牌，遵守自绘控件四纪律（手册 5.11）；
  ③ 改完必同步文档（手册 + 说明 + 运行说明 + 版本变更记录），否则不予验收。
- 用户待决剩 2 项：① 是否把 `experiences/lessons.md`（25 条 / 约 267KB）上传 ima 知识库（需 IMA 凭证）；
  ② 是否保留 `_测试输出/UI评审/` 取证工具链（含反射 harness 与 _faildetail/）。
- 分支终态：`main` = `de9d5f9`、`ui-optimization` = `c1ed5fe`（批次存档，C1/C2/C3 可分别 revert），
  **均已推送 origin；本地/远端同步、工作区干净**。提交链：修订五 `b717d69` → `.planning` `012a4ec`
  → C1 `b8a3941` → 缓动 `1a4c5ea` → `--dirp` 断言 `3008bd7` → C2 `24ddf3c` → C3 `c1ed5fe`
  → 合并台账 + 推送台账（main）。
- 推送须知（环境变化，10-16 13:5x）：github.com:443 在本机不可达（域名级封锁），
  **git push/fetch 一律走 origin 的 SSH URL（ssh.github.com:443）即可**；如报错先查
  `ssh -T -p 443 git@ssh.github.com` 是否通，勿再怀疑代理（旧结论已作废，见 E26）。

## 历史归档
10-07「PISCES 发浅」协作期（含 CodeBuddy ⇄ TRAE 双向协议、F1~F9 实测）已完成闭环：
用户 10-07 验收「深浅可以」，结论以 `Dwg-To-PDF/DwgToPdf完整经验手册.md` 第 20~31 章为准，
原始 handoff 长文已由 10-14 的版本迭代取代，git 历史可查。
