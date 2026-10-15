# 研究发现 —— 全部实测，附证据

## F1 「发浅」= 嵌入 OLE2FRAME 致整页光栅化（当前唯一根因，判别式成立）
| 图纸 | 幅面 | 源大小 | OLE2FRAME | 产物 |
|---|---|---|---|---|
| Y1567530 | 2A0 | 1.5MB | **0** | **矢量**（矢量流 845,864B，0 图块） |
| Y1563456 | 2A0 | 2.0MB | 4 | 光栅 **4** 图块 |
| Y1563451 | 4A0 | 11.2MB | 7 | 光栅 **7** 图块 |
| **PISCES_7633640** | 2A0 | 3.6MB | **16** | **光栅 122 图块 = 整页 441840x1080px** |
| H56E_LB200 | A0 | 0.4MB | **0** | **矢量** |
- 探针：`_探针\p_types.lsp`（按类型普查）、`_探针\pdfruns.py`（矢量流/图块/线宽）
- 公司版 40 张：34 张矢量 + 6 张含位图（1~7 图块），覆盖 A2/A1/A0/2A0/4A0
  ⇒ **工具本身能出矢量**，不是"只能光栅"

## F2 用户 GUI 是矢量；工具与「着色」同属光栅
| | GUI「按显示」 | GUI「着色」 | 工具(accore) |
|---|---|---|---|
| 矢量内容流 | **3,237,320B** | 5,424B | 21,541B |
| 图块 | 27（@1px/mm） | 32（301dpi） | 120（393dpi） |
| 光栅线宽 | —（矢量） | 1~10px 混合 | 5px 单一（0.32mm） |
| 墨量 dark<128 | — | 3.92% | **0.88%** |
⇒ 光栅化抹平线宽层次 = "开了线宽才正常、不开则浅"的观感来源。

## F3 已被实测推翻（勿重复投入）
- **纸型跳档**：是正确性 bug（已修），但**不是发浅成因**（同幅面既有矢量也有光栅）。
- **HQ pc3**：停用后仍 119 图块；反例 1.5MB 的 Y1567530 本来就是矢量 ⇒ "凡>1MB 全光栅"是**假相关**。
- **线宽开关**：PISCES 87.2% 实体无组码370（ByLayer）、15950/16043 在图层0、图层0线宽 **-3(LWDEFAULT)**
  ⇒ 勾「显示线宽」对这 87% 实体不改变任何东西；四张图组码70 的 0x80 位**全部未置位**，LWDISPLAY 都=0。
- **accore -EXPORT 认打印设置**：不认。LWDISPLAY 0/1 × 0x80 开/关 × 组码146 0/1/2 四组对照内容流 md5 全同。

## F4 COM 通道实测（用户 2026-10-07 提议的路径 —— 已否定）
| 方式 | 产物 |
|---|---|
| accore -EXPORT | 光栅 119 图块 @393dpi，8.55MB |
| COM + 完整页面设置（窗口出图） | 光栅 28 图块 @267dpi，**1.56MB** |
| COM + `Visible=true` | **字节数完全相同**（1,563,179）⇒ 界面模式无影响 |
| COM + `PlotType=acLayout` | Model 布局上不可用（需纸空间布局+视口） |
⇒ **COM 对象模型走不通**，与 accore 一样光栅。唯一已知矢量路径 = **AutoCAD GUI 的 PLOT**。

## F5 PISCES 与其它图纸的实体/线宽差异
- 类型：PISCES **OLE2FRAME=16**、SOLID=200、POLYLINE=1554、ELLIPSE=2266；H56E OLE=0、SOLID=0
- 线宽：PISCES 16043 实体仅 2051(12.8%) 带组码 370；H56E 96.1%、L42P 98.4%、Y1563451 68.3%
- 颜色：PISCES color7=5023 / ByBlock=6923 / ByLayer=4083；图层0 颜色 7
- 幅面：A 图框 1692x1199(≈2A0) 含 OLE；C 图框 2612x1474(≈A0) 无 OLE

## F6 accoreconsole AutoLISP 限制（实测）
- **没有 `let`**：`(let ((q 41)) (+ q 1))` → `no function definition: Q`
- 无 `layoutlist` / `pairp`；`tblnext "LAYER"` 不可用，**但 `tblobjname "LAYER" "0"` 可用**
  —— 返回**图元名**，`(assoc 2 ename)` 报 `参数类型错误: listp <图元名>`，须先 `(entget ...)`
- `ssget` 无匹配返回 **nil**（`(sslength nil)` → `lselsetp nil`），须先判空
- `vl-catch-all-apply` 只吃符号不吃 lambda；`(fix 0.0)` 报 `fixnump`；`defun (/)` 局部表可用
- ⚠ **把新函数追加到已有 .lsp 末尾会破坏整个 load**（报错点之后函数全未定义）

## F7 探针与环境陷阱
- `.ps1` 含中文**必须带 BOM 的 UTF-8**（PS 5.1 按 ANSI 读无 BOM）；纯 ASCII 才安全
- accoreconsole **不支持中文路径**：源图复制到 ASCII 临时目录，**永不打开源文件**
- `git check-ignore`：**带中间斜杠的规则锚定到仓库根**，须写单段 `运行日志/`；
  改完必须 `git check-ignore -v --no-index -- <路径>` 验证
- GitHub：HTTPS(443) 重置、**SSH(22) 通** ⇒ 走 SSH 推送

## F8 语料与回归基线
- 语料 **174 张**（166 + 2026-10-07 新增 8 张 PISCES），指纹 `_探针\语料基线.txt`
- 回归口径：`SelfTest --unit` 34 断言；**PageSpec 断言在无参默认路径**（79 断言）不在 --unit 里
- 当前全绿：--unit 34/34、默认 79/79、公司版 40/40（矢量34/含位图6）、PISCES 8/8、源图 MD5 未变

## F9 Git 仓库（2026-10-07 建立）
- 仓库根 = 工作区根；`origin` = https://github.com/zzh112212/Dwg-To-PDF.git（HTTPS 不通，待改 SSH）
- 提交：`c711ec3` 初始化 → `b4a2aec` .gitignore 修正 → `f2e6537` 线宽实测 → `3a8b16e` HQ停用+OLE根因 → `6575031` 文档
- 用户 PATH 已加 `Git\cmd`；SSH 密钥 `~/.ssh/id_ed25519_github_dwg`（ed25519，空口令）待贴公钥

## F10~F14（2026-10-15 新增；详版证据见手册第31/32章，勿在此重复推导）

- **F10 UCS 根因**：`entget` 图框是 **WCS** 坐标；`-EXPORT` Window 应答与 COM `SetWindowToPlot`
  按**当前 UCS** 解释 → 非世界 UCS 图纸窗口整体平移。判别：`WORLDUCS`（只读，1=世界）、`UCSORG`。
  修复 = 出图前复位 UCS；**先探测再复位**对世界 UCS 图纸零开销（2.29s vs 2.29s）。
- **F11 Windows 文件名半角冒号非法**：直接过滤会把 `09:08:07` 滤成 `090807`（分隔符全丢）
  → 先 `Replace(':', '：')`（全角 U+FF1A）再过滤其余非法字符。
- **F12 三个工程机制**（本轮全部实测踩中）：
  ① .NET 常量串在 exe 里是 **UTF-16LE**，ASCII grep 查不到 → 用 `s.encode('utf-16-le') in bytes` 验证版本号；
  ② SelfTest 控制台输出是 **GBK**，管道抓取后须按 GBK 解码否则乱码；
  ③ **引擎新鲜度守卫**：SelfTest 每次运行先比对自身 mtime ≥ 主工程 exe mtime，否则 FAIL（防拿旧引擎跑出假绿）
  → 主工程重建后必须重建 SelfTest 再跑断言。
- **F13 自测编译白名单**：`SelfTest.csproj` 用 `EnableDefaultCompileItems=false` + 显式 Compile Include，
  **不含 WinForms 的 MainForm.cs / Program.cs** → 要回归的规则必须放在白名单文件里（如 ItemStatus.cs），
  放 MainForm 会 CS0234 编译不到。
- **F14 工程文件改写规范**：Windows 上 .cs/.txt 改写必须保留原 **CRLF + BOM**（防编码破坏）。

## F15 Git 仓库实况（2026-10-15 实测，纠正 10-07 旧结论）

- **仓库位置**：`.git` 在 `Dwg-To-PDF/`（10-14 从个人 GitHub `zzh112212/Dwg-To-PDF` 克隆），
  不是工作区根——工作区根的 `_测试输出/`、`测试文件夹输入/`、`.workbuddy-ai/` 在仓库之外。
- **公司电脑 HTTPS 可直推 GitHub**：`git push origin main` 成功（8f6c7f2..8974e8f）。
  10-07"个人机 HTTPS 被 Reset、须走 SSH"的结论**仅限个人机网络**，不适用于公司网络。
  凭证来自克隆时系统托管（credential.helper=helper-selector），全程无交互。
- **大批量变更的提交构成**：v4.17 首入库时 git 自动把 48 个 v4.15 文件识别为"搬入 v4.17 的
  重命名"（相似度 68%~100%），仅 50 个纯删 ⇒ 版本演进脉络以 rename 形式保留，勿手工拆成删+增。
- **提交前必查**：`git add -n` 预演文件数与类型（防 bin/obj/PDF/日志混入）；
  历史惯例 = 发布目录跟踪 exe+config+lsp+json+运行说明.txt（v4.15/v4.16 均如此）。
