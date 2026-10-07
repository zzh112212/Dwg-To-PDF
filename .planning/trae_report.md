# TRAE 电脑操作助手 —— 实测报告区

> 本文件由 **TRAE 助手独占写入/追加**（CodeBuddy 不写，避免冲突）。
> 协作协议见 `handoff.md` 第 0 节。写完告诉用户"报告已更新"，CodeBuddy 会读取并接手。
> 最近更新：2026-10-07 深夜（第 2 次更新）。

## 待办（抄自 handoff.md 第 4 节）
- [x] **T2** 线宽阶梯 / pen table 项 —— 已用实测回答；原文前提有两处反了，见 R1
- [~] **T1** 着色打印界面选项 ⇄ 组码 76 对照 —— 组码侧已实测（R0）；界面侧见 R5
- [~] **T3** A/B 打印验收（需用户参与）—— 候选对已产出且**在工具自己的图框算法下复现**，见 R4
- [x] **T4**（可选）7633639 / 7643421 仍光栅 —— 已定性，见 R6

> ⚠ **两条最重要的结论**：
> 1. `handoff.md` 里「工具产物没有线宽算子」这句**是错的**（测量方法本身有 bug，见 R1 与 R3）。
> 2. 真正的根因是 **`Layout.PlotWithLineweights`**，而且**这一次我用工具自己的图框识别算法重做了 A/B，结论不变**（见 R4）。渲染实测：深 **+34%**。

---

### [R0] 组码侧实测：PISCES_7643423 的 Model 布局 `g76 = 4`
- 操作：对**源图**跑探针（脚本内部先复制到 `C:\Temp\dwgtopdf_probe\`，**源图从未被打开**）：
  ```
  powershell -ExecutionPolicy Bypass -File .\_探针\run_plotcfg.ps1 -Dwg "<源图 PISCES_7643423-…>" -Lsp .\_探针\p_g76.lsp -Func p_g76 -KeepTemp
  ```
- 实测：`g76=4  g70=11828  g146=0.0  g4=ISO_A3_(297.00_x_420.00_MM)  g2=none_device  g48=0.0  g140=0.0`
- 结论：**确认** 76 确实 = **4**。另外这张图布局里存的是**出厂默认**页面设置（A3 / none_device / 无窗口）
  ⇒ 76=4 与纸张、比例无关，是**图纸自带**的值（不是工具写进去的）。
- 产出：`C:\Temp\dwgtopdf_probe\g76.txt`

### [R1] ⚠ T2 的前提有两处反了（测量方法问题）
1. **`pdfruns.py` 的测量 bug**：原 `_vector_widths()` 写 `if 0 < v < 20`，**把 `0 w` 整个过滤掉**。
   于是"所有描边都被写成 hairline `0 w`"被打印成 `vector line widths(pt)=none`，
   读起来像"文件里根本没有线宽设置"——**和事实正好相反**。
2. **2/3/4/5/6/8 pt 那组线宽不属于"用户参照物"**，属于**探针产物**
   `_探针\out\guiplot\PISCES_7758_g76_0_VECTOR.pdf`（COM 通道 + `PlotWithLineweights=true`）。
   用户的 `ISO FULL BLEED 2A0 按显示.pdf` 实测是**全 hairline**（`0 w` × 28,470）。
3. **新疑点（请 CodeBuddy 转问用户）**：用户参照物是 **28,470 条** hairline 描边，我们的产物约 **15,000 条**。
   两份**都是全 hairline**，所以"深浅不同"若真存在，来源就**不在线宽**，而在**描边数量 / 甚至不是同一张图**。
   ⇒ 请确认 `ISO FULL BLEED 2A0 按显示.pdf` 到底是**哪张 dwg**打出来的。

### [R2] 决定性 A/B（第一轮，旧的"整图窗口"版本）
同一张 PISCES_7807758、同一页面设置、76 都=0，**只改一个属性**：

| `Layout.PlotWithLineweights` | PDF | `w` 直方图（**算子计数**，有误导，见 R3） |
|---|---|---|
| false | 461,912 B | `0.000` × 11,353 |
| true | 456,370 B | `0.000`×154 ＋ 2/3/4/5/6/8pt × 131 |

- 组码：`true` → `g70` 11828→11956（**+0x80**）；`false` → 11828 不变。
  ⇒ **组码 70 的 bit 0x80 就是这个属性**（`PlotSession.cs:217` 写的位是对的）。

### [R3] ⭐ 测量方法修正：`w` 算子计数 ≠ 描边线宽
PDF 的线宽是**图形状态**：AutoCAD 可以只写一次 `6 w`，后面几千条 `S` 全部继承它。
数"`w` 算子出现次数"会把"有 9967 条 6pt 描边"误报成"只有 306 条有宽度"。
**改成"每条描边继承到的有效线宽"后，真相才出来**（`pdfruns.py` 已改）：

| PDF | 来源 | 描边数 | **每条描边的有效线宽 (pt)** |
|---|---|---|---|
| `…_g76_0_LWon.pdf` | 探针·COM·线宽开 | 13,290 | **6.000×9,967**、0×2,518、4×331、5×306、8×103、3×49 |
| `…_g76_0_LWoff.pdf` | 探针·COM·线宽关 | 15,062 | **0.000 × 15,062（100%）** |
| `PISCES_7807758-…pdf` | **工具当前产物** | 15,067 | **0.000 × 15,067（100%）** |
| `ISO FULL BLEED 2A0 按显示.pdf` | 用户参照物 | — | 全 hairline |

- 单位换算：内容流开头是 `0.12 0 0 0.12 0 0 cm`，所以 6.000pt × 0.12 = 0.72pt = **0.254mm**
  —— 正好是 **AutoCAD 默认线宽 0.25mm**。也就是说"线宽开"时描边拿到了**真实线宽**。
- 顺带修正我上一版的一个猜测：线宽开时 `S` 数量**没有**下降（13,285 vs 15,057），
  所以不是"粗线改成了填充"；差别**只在图形状态里的线宽**。

### [R4] ⭐⭐ 2026-10-07 复测：**改用工具自己的图框识别算法**，结论不变，且给出"深浅"的量化证据

**为什么要复测**：用户指出"你 plot 打印时要用工具的图框识别算法"。
我第一轮（R2）用的是 `EXTMIN/EXTMAX`（7807758 的窗口是 10805×6784，**整张图**），
而工具的识别框只有 **1682×1189mm** ⇒ 旧产物与工具产物**不可比**。已给探针加 `-FrameCache`，
并**逐行移植** `PageSpec.Resolve`（含 `PaperFitTolerance = 0.03`）。

**这次的做法**（与工具完全一致）：
`C:\Temp\v416vec\frame_cache.json` 取识别框 → `Resolve-PageSpec 1682×1189 M=10`
→ `media=ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)`、`rot=1`、**`s=0.966915`**（≈工具日志值）
→ 窗口 `(6476.2,1349.7)->(8178.2,2558.7)`、`PlotType=4`、`SetCustomScale(s,1)`、`CenterPlot=true`
→ 先 `(c:setgrp 76 0)`，再只改 `PlotWithLineweights`，`PlotToFile`，`Close(0)` 不保存。

命令：
```powershell
powershell -ExecutionPolicy Bypass -File .\run_shadeplot.ps1 -Dwg "<源图 7807758>" -Values "0" `
  -Lineweights 1 -Suffix "_frLWon"  -FrameCache "C:\Temp\v416vec\frame_cache.json"
powershell -ExecutionPolicy Bypass -File .\run_shadeplot.ps1 -Dwg "<源图 7807758>" -Values "0" `
  -Lineweights 0 -Suffix "_frLWoff" -FrameCache "C:\Temp\v416vec\frame_cache.json"
```
探针日志（两次都确认帧算法生效、76 都写成 0）：
```
[frame] 1,682.0x1,189.0mm -> ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)  rot=1  s=0.966915
[try  ] want 76=0  LW=1  readback=0  g70 11828->11940   -> 2,051,329 B
[try  ] want 76=0  LW=0  readback=0  g70 11828->11812   -> 2,057,789 B
```

**(a) PDF 内部（`pdfruns.py`）**

| PDF | 大小 | 页面 | 描边数 | 每条描边的有效线宽 (pt) |
|---|---|---|---|---|
| `…_g76_0_frLWon.pdf` | 2,051,329 B | 3370×4768pt (1189×1682mm) | 13,285 | **6.000×9,967**、0×2,518、4×331、5×301、8×103、3×49 |
| `…_g76_0_frLWoff.pdf` | 2,057,789 B | 同上 | 15,057 | **0.000 × 15,057（100%）** |
| `PISCES_7807758-…pdf`（**工具当前产物**） | 2,172,957 B | 同上 | 15,065 | **0.000 × 15,065（100%）** |

组码（本轮）：`LW=1` → `g70 11828→11940`（**+0x80**）；`LW=0` → `11828→11812`（**0x80 未置位**）。
两种情况的 `76` 回读**都是 0** ⇒ 自变量只有 0x80 这一个。

**(b) 渲染实测（`pdfink.py`，新写的：把 PDF 渲染成图，量整页墨量）**
这才是"深浅"的直接证据 —— 不看 PDF 算子，看**人眼看到的像素**：

| PDF | 墨点占比 `ink<128` | **平均墨量 /255** |
|---|---|---|
| `…_frLWon.pdf`（76=0 ＋ 线宽开） | **2.4092%** | **6.9533** |
| `PISCES_7807758-…pdf`（**工具当前产物**） | 1.6506% | 5.3516 |
| `…_frLWoff.pdf`（76=0，线宽关） | 1.5835% | 5.1811 |

⇒ **结论（在工具自己的图框算法下依然成立，并且量化了）**：
- 「发浅」= 描边被压成 hairline `0 w`；
- **工具当前产物 ≈ "线宽关"那一档**（5.35 vs 5.18，同一水平）；
- 打开 `PlotWithLineweights` 后整页**深约 34%**（墨点占比 +52%）。
- **T3 给用户打印的候选对**：
  `C:\Temp\dwgtopdf_gui\runs\PISCES_7807758-AC_LD004APIA3___4_g76_0_frLWon.pdf`（**新·应更深**）
  vs 现在的工具产物 `C:\Temp\v416vec\PDF文件\PISCES_7807758-….pdf`（**旧·发浅**）。
  两份**同一张图、同一识别框、同一纸张比例**，可以直接并排看。
- 产出：`C:\Temp\dwgtopdf_gui\runs\…_g76_0_frLWon.pdf` / `…_frLWoff.pdf`

> 补充：工具日志显示 **8 张图的识别框全都是 `1682×1189mm`**
> （`C:\Temp\v416vec\logs\DwgToPdf_20261007_184131.log` 第 21–27 行，各图"全图"尺寸千差万别但识别框相同）
> ⇒ 这一套 `2A0 / rot=1 / s=0.966915` 对**整批 8 张通用**，A/B 结论可直接外推。

### [R5] 为什么工具的产物还是全 hairline：**两条通道各自只做到一半**
| 通道 | 76=0（变矢量） | 组码 70 的 0x80（线宽） | 结果 |
|---|---|---|---|
| accore `-EXPORT`（工具主通道） | ✅ 认（6/8 变矢量） | ❌ **不认** | 矢量，但**全 hairline** = 浅 |
| COM `PlotToFile`（工具兜底通道） | ❌ 兜底路径**没写 76** | ✅ 认 | 整页**光栅** = 浅 |

源码佐证：
- `PrintOptions.ShowLineWeight` **默认 false**，由界面「显示线宽」勾选框驱动（`MainForm.cs:1191`）。
- COM 路径 `CadConverter.cs:149` 已正确设 `PlotWithLineweights`，但**没有**写 `76=0`。
- accore 路径 `PlotSession.cs:217` 写了 `4|128`，但 `-EXPORT` 不认 0x80。

- **给 CodeBuddy 的建议（探针已端到端验证）**：让出图走 **COM 通道**，
  在 `PlotToFile` **之前**用同一段 LISP `entmod` 写 `76=0`，并保证 `PlotWithLineweights=true`。
  `_探针\run_shadeplot.ps1` 就是这么做的，产物 = 矢量 + 真实 0.25mm 线宽 + 渲染深 34%。

### [R6] T4 定性：7633639 / 7643421 为什么仍光栅
- 工具日志 `C:\Temp\v416vec\logs\DwgToPdf_20261007_184131.log`：
  这两张是 **「Core Console 出图失败(PDF 内容为空)，回退 COM」** ⇒ 走了兜底 COM 通道 ⇒ 整页光栅。
  这正是 R5 里"COM 路径没写 76"的后果。**修好 R5 的建议后这两张应一并改善。**
- 另一个可疑点（`ConverterEngine.Plot.cs` 第 158/252/261 行）：
  `useAccoreSetup ? src : copyPath` —— **accore 模式下把源图 `src` 直接交给 accoreconsole**。
  这与「源图被改写」（见 R7）时间上吻合，建议 CodeBuddy 核对是否要改成 `copyPath`。

### [R7] ⚠⚠ 源图纸被改写 —— 违反硬约束，必须报告
用 `_探针\out\源图纸哈希_改前.txt` 做基线，重算 8 张源图 MD5：

| 图纸 | 基线 MD5 | 现在 MD5 | 判定 | mtime |
|---|---|---|---|---|
| PISCES_7633639-… | `BE65A200631C11F5F20EB221FB927D03` | `053C03E92DFDFB631606B34792037F38` | ❌ **变了** | 16:43:56 |
| PISCES_7633640-… | `EEBB2D50118D04EDA22086CE4A4834FC` | `DA08BB45C5351037A1ADE4181066706F` | ❌ **变了** | 16:42:41 |
| PISCES_7643421-… | `90927FD5B68B64347EC39E5EAB463134` | `90927FD5B68B64347EC39E5EAB463134` | ✅ 未变 | 16:42:45 |
| PISCES_7643423-… | `B9D8EEF9AE2A8F13FE3EF31B759D1D94` | `85BF5B3DBA3D7A45485D688F333E2342` | ❌ **变了** | 16:42:50 |
| PISCES_7807743-… | `AB27F28E8EC4E6D0A2C1292569588C3A` | 同 | ✅ 未变 | 16:42:58 |
| PISCES_7807753-… | `EC93C8635CA05F089423D837832F8C65` | 同 | ✅ 未变 | 16:43:02 |
| PISCES_7807757-… | `6E6BA5B188CE687415D8F728023B4371` | 同 | ✅ 未变 | 16:43:06 |
| PISCES_7807758-… | `EDC40D8DC3A488BC75B3AD1080F8F7A1` | 同 | ✅ 未变 | 16:43:10 |

- **3 张不一致**（7633639 / 7633640 / 7643423），全部 8 张的 mtime 挤在 **16:42:41–16:43:56** 这一分钟内；
  源目录还有 `plot.log`(16:46:20)、`C:\Temp\dwgtopdf_ole\…bak`(16:42:50)。
- 时间落在**上一轮 phase5（16:42–16:47）**，**不是本会话造成**：本会话所有探针都是
  "先 `Copy-Item` 到 `C:\Temp\` 再打开"，工具自己那一轮是 18:41，且 mtime 没跟着动。
- **但仍违反用户的硬约束「不得改变图纸内容」**，且**没有原始备份可还原**。
  建议：① 请用户从版本库/原件重新取一份这 3 张图；② 查 `ConverterEngine.Plot.cs` 的 `src` 直传（见 R6）。

---

## 安全纪律执行情况
- 全程**从未打开源图**：实测一律"先 `Copy-Item` 到 `C:\Temp\` ASCII 短路径再打开"，或由探针内部复制。
- 未 `Quit()` 用户的 AutoCAD 会话（只开关文档，关闭一律 `Close(0)` 不保存）。
- 源图 MD5 已复核，结果见 R7（**发现问题，如实上报**）。

## 本轮新增/修改的文件（都在 `DwgToPdf 工具 v4.15\_探针\`）
- **改** `pdfruns.py`：把"`w` 算子计数"换成**每条描边的有效线宽直方图**（原指标会误导，见 R3），
  并加了 paint 算子统计（`S/f/B/n/m/l/c`）。
- **改** `run_shadeplot.ps1`：新增 `-Lineweights 0|1`、`-Suffix`、`-FrameCache`（移植 `PageSpec.Resolve`、
  读 `frame_cache.json`）；日志加 `g70 前->后`。**修了一个致命 bug**：`Get-Content -Raw` 没带
  `-Encoding UTF8`，用 GBK 读 UTF-8 的 json 会把路径 `\\` 吃掉一个 → `ConvertFrom-Json` 报
  "Unrecognized escape sequence"。
- **新** `pdfink.py`：用 PyMuPDF 渲染 PDF 到灰度图，量 `ink<128` 与平均墨量 —— **"深浅"的量化证据**（R4b）。
- **新** `close_docs.ps1`：关闭可见 AutoCAD 里所有文档（**不保存、不退出**），带重试吃 `RPC_E_CALL_REJECTED`。
  注意：`$app.Documents.Item(1)` 在本机返回 `E_FAIL`，**必须用 `$app.ActiveDocument`** 逐个关。

## 环境坑（给 CodeBuddy 备查）
1. PowerShell 5.1 `Get-Content -Raw` 默认按**系统 ANSI(GBK)** 解码 → 读 UTF-8 的 json 必须显式 `-Encoding UTF8`。
2. `$app.Documents.Item(1)` → `E_FAIL`；`$app.ActiveDocument` 正常。`$app.Documents.Count` 正常。
3. `powershell -File` 把所有参数当字符串传 → `[bool]` 参数会拒收 `"1"/"0"`，用 `[int]`。
4. `RPC_E_CALL_REJECTED (0x80010001)` 在 AutoCAD 刚启动/忙时很常见，COM 调用要包重试循环。

## 待 CodeBuddy / 用户决定
1. **T3 打印验收**（需用户）：并排看 R4 给的"新 vs 旧"两份 PDF，确认是否更接近用户期望的深浅。
2. R1-3 的疑点：用户参照物 `ISO FULL BLEED 2A0 按显示.pdf` 到底是哪张图打出来的？
3. R7：源图被改写如何处理（能否重新取原件）。
4. R5 的两条通道修复方案是否采纳（COM + 写 76=0 + 线宽开）。
