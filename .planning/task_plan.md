# 任务计划 —— DwgToPdf 工具（生产 v4.15，含 v4.16 修订）

## Goal
解决 `测试文件输入\打印问题文件` 下 8 张 PISCES 2A0 图纸"打印发浅"（文字可读、图形线条浅）。
**约束（用户 2026-10-07 明确）**：不得改变图纸内容 —— "隐藏/删除 OLE"类方案坚决不做。
验收：① 这批产物打印深浅被用户认可；② 全量回归无新增失败；③ 源图纸不被修改。

## Current Phase
phase5 —— **矢量化已达成但用户实测"还是很浅"**；剩余定位必须在 GUI 侧进行（已写进 handoff 第 5 节交 TRAE）

## Phase5 结果（2026-10-07 晚）
- 工具 accore 通道写**组码 76 = 0** → **6/8 变矢量**（内容流 4~22KB → 2.0~3.2MB；图块 119→9~27）
- ⚠ 更正两条错误结论：① **146 不是着色打印**（在AcDbLayout 段，是标高），真字段是 **76**；
  此前"accore 不认打印设置"的四组对照**全部作废**（变错了字段）
- ⚠ 更正第三条：**矢量化 ≠ 变深**（用户实测无变化）⇒ 光栅化不是"发浅"成因
- 已排除：打印线宽（LWDISPLAY 0/1、组码 70 的 0x80 位）—— **产出字节完全相同**
- 新线索（最强）：工具矢量产物**测不到任何线宽算子**（`line widths=none`），
  而用户参照的「按显示」矢量产物有 2/3/4/5/6/8pt 层次 ⇒ 疑似缺"线宽阶梯/pen table"
- 剩余 2 张（7633639、7643421）设 76=0 仍光栅，已排除"缺 76"猜测

## Next Step（单一动作）
由 TRAE 电脑操作助手在 AutoCAD GUI 上确认两件事（详见 handoff 第 5 节）：
① 「着色打印」界面选项 ⇄ 组码 76 取值的对照表；② 有无线宽/pen table 相关项及其状态。

## Phases
| phase | 内容 | 状态 |
|---|---|---|
| 1 | 研读手册第26章/速查表，摸清 v4.15 出图链路 | complete |
| 2 | 实测现状（产物页面/线宽/墨量） | complete |
| 3 | 探针普查（页面设置、系统变量、实体颜色/线宽/类型） | complete |
| 4 | 定位根因 + 修复（选纸容差 + PlotWithLineweights + 停用 HQ） | complete |
| 5 | 找矢量出图路径（COM+页面设置已否定 → PLOT 命令/GUI） | **in_progress** |

## Decisions（已敲定，含理由）
| # | 决策 | 理由 |
|---|---|---|
| D1 | 选纸加 3% 容差 `PageSpec.PaperFitTolerance` | 图框 1692x1199 只比 2A0 大 10mm，旧逻辑顶到 4A0。**这是正确性改进，不是"发浅"成因** |
| D2 | "发浅"根因 = 嵌入 OLE2FRAME 致整页光栅化 | 判别式实测：有 OLE→光栅（4→4、7→7 图块）、无 OLE→矢量（含同为 2A0 的 Y1567530） |
| D3 | **否决**"隐藏/删除 OLE2FRAME" | 用户：等于改图纸内容 |
| D4 | 停用 HQ pc3（`IsHighQuality` 恒 false + 删设置项） | 用户实测默认 pc3 效果速度都好；耗时 93.7s/450.9s/217.1s。**但已实测 HQ 不是光栅化元凶** |
| D5 | `PlotLineweights` → `PlotWithLineweights` | Layout 无 PlotLineweights 属性，旧代码每次抛异常被吞，该设置自 v4.1 从未生效 |
| D6 | accore 页面设置写组码 70 的 0x80 位 | 实测 COM 置 true 时 70 由 11828→11956(+0x80)，LWDISPLAY 保持 0 |
| D7 | 仓库只留 v4.15；v4.12/v4.14 由目录备份 | 用户 2026-10-07 决定 |
| D8 | 仓库根 = 工作区根（不是 `Git\` 子目录） | `Git\` 是便携版 Git 发行包（388MB/9611 文件） |
| D9 | 提交身份用 `git -c` 传入，不写任何配置文件 | 用户全局 user.name/email 为空且不允许改动 |
| D10 | push 走 SSH（22 端口通） | 实测 HTTPS(443) 被重置、SSH 通；本机无代理 |

## Error 表（同一失败动作不做第 2 次）
| # | 错误 | 处理 |
|---|---|---|
| E1 | PDF 墨量恒为 0.2~1.0 | **图块带 `/Predictor 15`(PNG预测器)需先解码**，已写 `pdfruns.py` |
| E2 | 追加函数到 `p_caps.lsp` 末尾致整个 load 失败 | AutoLISP 报错中断；新增探针一律新建独立 .lsp |
| E3 | `$doc.ActiveLayout` 返回 null | 用 `$doc.Layouts.Item("Model")` |
| E4 | `SetWindowToPlot` overload 错误 | 参数是**两个 2 元素点数组**，不是 4 个 double |
| E5 | 3 元素数组 → "安全数组中的元素数目不正确" | 点必须 2 元素 |
| E6 | COM 设页面设置时 `RPC_E_CALL_REJECTED` | AutoCAD 处于模态/忙碌（-PLOT 等待输入）。**需看屏幕才能诊断** |
| E7 | git push github.com 失败 ×3 | 网络层重置；改走 SSH（密钥已生成，待贴公钥） |
| E8 | `write_to_file` 报参数错误 ×2 | 改用"Set-Content 占位 + replace_in_file 写正文"种子法 |

## Next Step（单一动作）
用**电脑操作能力**打开 AutoCAD 可见窗口，对 PISCES 图纸走 **PLOT 界面/命令**路径，
看屏幕上真实提示序列、卡住时 ESC 退出，目标是拿到与用户 GUI 一致的**矢量**产物。
若矢量成立 → 工具新增"PLOT/GUI 通道"（只改打印设置，不动图纸内容）。
