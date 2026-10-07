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
