# TRAE 电脑操作助手 —— 实测报告区

> 本文件由 **TRAE 助手独占写入/追加**（CodeBuddy 不写，避免冲突）。
> 协作协议见 `handoff.md` 第 0 节。写完告诉用户"报告已更新"，CodeBuddy 会读取并接手。

## 记录格式（每条操作一段，便于 CodeBuddy 快速抓结论）

### [序号] 日期时间 —— 一句话标题
- 目的：
- 操作步骤：1) … 2) …
- 界面观察值：（**逐项抄写，不要概括**）
- 组码实测值：`g76=__ g70=__ g146=__ g4=__ g48=__ g140=__`
- 结论：**未确认 / 确认：____**（不确定就写"未确认"，不要猜）
- 产出文件路径：

---

## 待办（抄自 handoff.md 第 4 节，完成后在此勾选）
- [ ] **T1** 着色打印界面选项 ⇄ 组码 76 取值对照
- [ ] **T2** 线宽阶梯 / pen table 项是否存在及状态
- [ ] **T3** A/B 打印验收（需用户参与）
- [ ] T4（可选）7633639 / 7643421 仍光栅的原因

## T1 具体做法（最优先）
1. `Copy-Item "测试文件输入\打印问题文件\PISCES_7643423-AC_LB004APIA3_主驾4W腰托_临时图纸.dwg" C:\Temp\a.dwg`
2. 打开 AutoCAD → 打开 `C:\Temp\a.dwg` → `Ctrl+P` 打开 PLOT 对话框
3. 逐项抄下界面当前值：**着色打印 / 打印线宽 / 打印比例 / 图形方向 / 纸张 / 打印到文件 / 窗口范围**
4. **关掉对话框**（本步不出图），关文档时选「不保存」
5. 读组码实测：
   ```
   powershell -ExecutionPolicy Bypass -File .\_探针\run_plotcfg.ps1 -Dwg C:\Temp\a.dwg -Lsp .\_探针\p_g76.lsp -Func p_g76 -KeepTemp
   ```
   读 `C:\Temp\dwgtopdf_probe\g76.txt`
6. 交付：对照表「界面选项 ⇄ 组码值」，并指出**哪个值 = 按显示**

> 候选（TRAE 实测的产物形态，**但尚未与界面核对过**）：
> `0`→矢量1.9MB ｜`1`→与0逐字节相同 ｜`2`→0图块全矢量9.1MB ｜`3`→整页301dpi位图 ｜`4`（本批当前值）→整页高分辨率位图

## T2 具体做法
工具的矢量产物**测不到任何线宽算子**（`python .\_探针\pdfruns.py C:\Temp\v416vec\PISCES_7643423-….pdf`
→ `vector line widths(pt)=none`），而用户参照的「按显示」矢量产物**有 2/3/4/5/6/8 pt 层次**。
→ 在 PLOT 对话框里找与线宽/笔宽/pen table 相关的项（版本不同措辞不同，可能是「质量」「自定义」「注释」），
记录用户手动打印那次该项是什么状态。

## 安全纪律（务必遵守）
1. **绝不打开源图**：先 `Copy-Item` 到 `C:\Temp\` 的 ASCII 短路径再打开；关文档选「不保存」。
   源图 MD5 基线 = `_探针\out\源图纸哈希_改前.txt`，任务结束必须 `Get-FileHash` 复核。
2. 不要 `Quit()` 已运行的 AutoCAD（会关掉用户会话）；用完只关文档。
3. 复制副本若报"文件被占用"，先关掉所有已打开文档。
4. **键盘注入在 AutoCAD 里不可靠**（TRAE 实测）：命名键需先 `WScript.Shell.AppActivate(<pid>)`；
   `type_text` 与剪贴板 Ctrl+V 都送不进命令行控件。
   优先用 COM / LISP / SendCommand 驱动，Computer Use 只做**截图与读控件复核**。

---

## 已有资产（TRAE 提供，位于 `DwgToPdf 工具 v4.15\_探针\`）
`run_shadeplot.ps1`（复制→附加可见 AutoCAD→`(c:setgrp 76 v)`→按工具页面设置出图）、
`p_shade.lsp`（`c:shadeget` / `c:shadeget2` / `(c:setgrp 组码 值)`）、
`run_guiplot.ps1`（预置页面设置且**不关文档**留给 GUI 复核）、
`run_lispdiag.ps1`（Layout COM 成员清单 + SendCommand 是否真执行）、
`out\guiplot\PISCES_7758_g76_0_VECTOR.pdf` / `PISCES_7758_g76_3_RASTER.pdf`（矢量/光栅对照证据）
