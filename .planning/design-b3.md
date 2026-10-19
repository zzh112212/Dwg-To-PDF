# DwgToPdf B3 整洁项批 增量设计文档

> 版本 v1.0 · 2026-10-16 17:1x · 主理人齐活林执笔
> **执笔背景**：本设计原派发架构师 Bob（architect-2）。其本次因**平台 429 频率限流**（配额 2026-10-17 12:16 才重置）未能落盘；此前该成员亦有三连平台故障（B2 设计）与一次「报完成但磁盘无产物」（B3 首轮）记录。**用户拍板「主理人亲自执笔」**（沿用 B2 时同一拍板）。
> **坐标勘误（重要）**：架构师口头报告称锁定范围 = `ConverterEngine.cs:768-1043 + 1189-1191`、`ManualResetEventSlim.Set()` **两处**。主理人实测**两者均不成立**——真实站点为 L531/L579/L586-589/L1226-1228，停止点仅 **1 处**。本文全部行号为主理人 2026-10-16 17:1x 在 main=1d11cfb 上亲自核对的结果（附录 A）。
> 上游：《.planning/prd-code-quality.md》§CQ-07（P2 ⚠轻微）+ §8 Q4 用户裁决
> 样板：《.planning/design-b2.md》（B2 已收官：15abeb2 + 6f5d22f + fabdcd6，经用户弹窗验收）

---

## Part A：系统设计

### 1. Implementation Approach

#### 1.1 本批的难点与对策

| # | 难点 | 对策 |
|---|---|---|
| ① | **看门狗是 `RunFiles` 内的局部匿名线程**（`bool[] watchdogStop` + lambda），无任何公开接缝，不可直接单测 | 不抽新类、不改方法签名（Q4 要求「技术形态变化、行为等价」，改造半径最小）；验证靠 ① 静态 diff 审阅常量与文案（V1）② 真机 UI 停止响应计时（V2，可观测的用户价值） |
| ② | **PRD 原文写 `Wait(15000, token)`，但引擎根本没有 CancellationToken**（实测 grep 计数 = 0） | 落地方案改为 `ManualResetEventSlim.Wait(15000)` + `Set()` 停止（无 token 形态）；此偏离显式记录（§5-D1），需 Engineer/QA 知悉 |
| ③ | **`ManualResetEventSlim` 是 IDisposable**，不在 finally 释放则句柄泄漏（Set 后内部会分配内核句柄） | finally 内固定顺序「**先 Set → 再 Join → 最后 Dispose**」；线程体内不判空（`Start()` 前必非 null） |
| ④ | **「行为等价」必须可证**：轮询周期、阈值、冷却、告警文案、KillAllOwned 语义全不能变 | §3.4 逐项对照表 + V1 对常量与文案做 grep 断言（改前改后逐字比对） |

#### 1.2 策略总览

```
原则：只换「等待原语」，不换「判定语义」；停止响应变快是唯一差异（且是改善方向）。

改前：
  bool[] watchdogStop = null;                       // 无内存屏障语义的共享标志
  while (!watchdogStop[0]) {
      Thread.Sleep(15000);
      if (watchdogStop[0]) break;                   // 睡满 15s 才检查停止信号
      ...停滞判定...
  }
  finally: watchdogStop[0] = true; watchdogThread.Join(3000);   // 停止后最坏等 15s；Join 3s 超时先返回

改后：
  ManualResetEventSlim stopEvent = null;            // 自带同步语义的等待对象
  while (!stopEvent.IsSet) {
      if (stopEvent.Wait(15000)) break;             // 停止信号直达，立即退出；超时=15s
      ...停滞判定（逐字不变）...
  }
  finally: stopEvent.Set(); watchdogThread.Join(3000); stopEvent.Dispose();   // 停止后毫秒级退出

不变项：判定体（idle >= 900000 且 距上次 kill >= 180000）、告警两行文案、
        ComStarter.KillAllOwned()、线程 IsBackground=true、catch 注释（B1 已补）。
```

### 2. File List

| 文件 | 改动 | 性质 |
|---|---|---|
| 源码工程/ConverterEngine.cs | L531 `bool[] watchdogStop` → `ManualResetEventSlim stopEvent`；L579 `new bool[1]` → `new ManualResetEventSlim(false)`；L586-589 循环改事件等待；L1226-1228 停止段改 Set→Join→Dispose | 行为（仅停止响应时延） |
| 自测试工程/* | **无需改动**（看门狗为局部线程，无白名单可测接缝；Q4 要求行为等价，不新增断言，见 §5-D3） | — |
| 文档四件套 | 手册第 5 章（新增小节）/ 说明.txt「修订十一」/ 运行说明.txt / 版本变更记录.txt | 文档 |

> **不升版本号**：按 PRJ-001，升版是显式决策，未经用户同意不得改 `Program.cs` 的 Version。文档口径标「v4.17 修订十一」（与 B1=修订九、B2=修订十 的既有命名一致）。

### 3. 关键设计细节

#### 3.1 声明与创建（L531 / L579）

改前：
```csharp
bool[] watchdogStop = null;
...
watchdogStop = new bool[1];
```
改后：
```csharp
ManualResetEventSlim stopEvent = null;
...
stopEvent = new ManualResetEventSlim(false);   // false = 未置位（运行中）
```
`using System.Threading;` 已存在（`Thread`/`Stopwatch` 在用），`ManualResetEventSlim` 同命名空间，**无需加 using**。

#### 3.2 线程体（L586-589）

改前：
```csharp
while (!watchdogStop[0])
{
    Thread.Sleep(15000);
    if (watchdogStop[0]) break;
```
改后：
```csharp
while (!stopEvent.IsSet)
{
    // v4.17 修订十一：由 Thread.Sleep 轮询改为事件等待——停止时立即唤醒，
    // 退出耗时从「最坏 15s」降到毫秒级；超时仍为 15s，判定语义不变。
    if (stopEvent.Wait(15000)) break;   // true = 已收到停止信号，立即退出
```
`Wait(15000)` 超时返回 false → 继续判定（与旧 `Sleep` 等价）；被 Set 时返回 true → 直接 break，省掉旧代码「睡满再检查」的 15s 尾延迟。

#### 3.3 停止段与释放顺序（L1226-1228，位于 `finally` 块内）

改前：
```csharp
if (watchdogStop != null) watchdogStop[0] = true;
// best-effort：看门狗线程可能阻塞，等待失败直接继续收尾
if (watchdogThread != null) { try { watchdogThread.Join(3000); } catch { } }
```
改后：
```csharp
// v4.17 修订十一：Set 唤醒看门狗（毫秒级退出），Join 后再释放句柄。
if (stopEvent != null) stopEvent.Set();
// best-effort：看门狗线程可能阻塞，等待失败直接继续收尾
if (watchdogThread != null) { try { watchdogThread.Join(3000); } catch { } }
// best-effort：释放等待句柄；Set 后内部已分配内核句柄，必须 Dispose
try { if (stopEvent != null) stopEvent.Dispose(); } catch { }
```

**顺序是硬约束**：
- `Set` 必须在 `Join` 前 —— 否则线程继续等到 `Wait` 超时，改造失效；
- `Dispose` 必须在 `Join` 后 —— 否则线程可能 `Wait` 一个已释放对象 → `ObjectDisposedException` 逃逸到 catch。

**边界情形**：
- `watchdogThread.Start()`（L610）本身抛异常 → 线程未起，finally 里 Set + Join（立即返回）+ Dispose 全部安全；
- 异常发生在 L579 之后、L580 之前 → `stopEvent` 非 null 但 `watchdogThread` 为 null → `if (watchdogThread != null)` 守住，Dispose 照常；
- 异常发生在 L579 之前 → 两者皆 null → 三个 `if` 全部跳过（与改前同构）。

> ⚠ **勿与 finally 内既有收尾混淆**：L1212 已有 `ComStarter.KillAllOwned()`（「收尾结束未正常退出的自启 AutoCAD 实例」），与看门狗的 KillAllOwned（解除 COM 阻塞）是**两个不同用途**，B3 不动 L1212。

#### 3.4 行为等价性对照表（V1 的审阅依据）

| 语义 | 改前 | 改后 | 等价？ |
|---|---|---|---|
| 轮询/判定周期 | `Thread.Sleep(15000)` | `stopEvent.Wait(15000)` 超时 | ✅ 同为 15s |
| 停滞阈值 | `const long stallMs = 900000L` | 不变 | ✅ |
| kill 冷却 | `const long killCooldownMs = 180000L` | 不变 | ✅ |
| 停滞判定 | `idle >= stallMs && (sw.ElapsedMilliseconds - lastKillAt) >= killCooldownMs` | 逐字不变 | ✅ |
| 告警文案 | 两行 `[警告] …` | 逐字不变 | ✅ |
| 解除阻塞动作 | `ComStarter.KillAllOwned()` | 不变 | ✅ |
| 线程性质 | `IsBackground = true` | 不变 | ✅ |
| **停止响应** | 置位后线程最坏睡满 15s；`Join(3000)` 超时先返回 | Set 立即唤醒，毫秒级退出 | ⚠ **有意改善**（CQ-07 的价值本身） |

### 4. Program Call Flow

```
RunFiles(files, outputFolder, print, progress)
 ├─ L527-539  声明 log / stopEvent / watchdogThread（try 外，finally 可见）
 ├─ L541 try
 │   ├─ L572   log = new LogWriter(text, onLine)
 │   ├─ L579   stopEvent = new ManualResetEventSlim(false)
 │   ├─ L580-608 watchdogThread = new Thread(() => {
 │   │            while (!stopEvent.IsSet) {
 │   │                if (stopEvent.Wait(15000)) break;      ← 停止信号直达
 │   │                ...idle 判定（逐字不变）...
 │   │            } })
 │   ├─ L610   watchdogThread.Start()
 │   ├─ ...转换主流程（识别 / 出图 / 校验）...
 ├─ L1195 finally
 │   ├─ L1201-1217 既有收尾（AI 残留 / 孤儿进程 / 临时清理）——不动
 │   ├─ L1226  if (stopEvent != null) stopEvent.Set()        ← 立即唤醒
 │   ├─ L1228  watchdogThread.Join(3000)                     ← 现在几乎立即返回
 │   ├─ L1229  log.Dispose()
 │   └─ L1233  }  ← finally 闭合
 └─ L1234  }  ← 方法闭合
```

### 5. Anything UNCLEAR / 偏离设计（需 Engineer/QA 知悉，不阻塞）

- **D1（偏离 PRD 原文）**：PRD §CQ-07 写 `ManualResetEventSlim.Wait(15000, token)`，但 `ConverterEngine.cs` 实测**无 CancellationToken**（grep 计数 0），引擎的取消是「MainForm 侧不再推进 + 看门狗自停」形态。故落地为**无 token 的 `Wait(15000)` + `Set()`**。语义与 PRD 意图一致（事件等待、行为等价、停止更快），仅去掉不存在的 token 形参。
- **D2（CQ-07 的注释项已在 B1 完成）**：PRD 记「循环体外层 `catch { }` 无注释」，但 B1 注释批（02f9233）已补 `// best-effort：看门狗异常退出不影响转换主流程`（现 L605-606）。故 B3 **不含注释改动**，避免重复劳动。
- **D3（不新增 SelfTest 断言）**：看门狗是 `RunFiles` 的局部匿名线程，无注入接缝；Q4 要求「行为等价」，为测试而改产品结构会违反 B2 已确立的「不为测试改产品语义」。故 B3 自测口径是 **120/120 保持**（红线以 120 起算），行为证据由 V2 真机计时承担。
- **D4（不升版本号）**：见 §2 注。文档口径「修订十一」，版本号维持 v4.17。

---

## Part B：任务分解

### 6. Required Packages

无新增依赖（`System.Threading.ManualResetEventSlim` 在 .NET Framework 4.8 基类库内）。

### 7. Task List（共 4 个任务，按依赖排序）

#### T1 事件对象声明与创建（P2）
- 位置：`ConverterEngine.cs` L531（声明）、L579（创建）
- 内容：`bool[] watchdogStop` → `ManualResetEventSlim stopEvent`；`new bool[1]` → `new ManualResetEventSlim(false)`
- 完成判据：编译 0 警告 0 错误

#### T2 线程体改事件等待（P2）
- 位置：L586-589
- 内容：`while (!watchdogStop[0]) { Thread.Sleep(15000); if (watchdogStop[0]) break;` → `while (!stopEvent.IsSet) { if (stopEvent.Wait(15000)) break;` + v4.17 修订十一注释
- 完成判据：**判定体 L590-607 逐字不变**

#### T3 停止段改 Set→Join→Dispose（P2）
- 位置：L1226-1228（finally 内）
- 内容：按 §3.3 三段式，顺序硬约束；补 best-effort 注释
- 完成判据：`Dispose` 在 `Join` 之后

#### T4 编译 + 断言 + 真机停止响应计时 + 文档四件套 + 提交（P2）
- 编译：主工程 + SelfTest 均 0 警告 0 错误
- SelfTest：`--no-incremental` 重建后 **120/120 PASS**（PRJ-002 引擎新鲜度守卫）
- 真机计时（V2）：启动一次转换（小语料）→ 点「停止」→ 记录引擎返回耗时（预期毫秒级；改前 ≈3000ms Join 超时）
- 文档四件套同步「修订十一」；提交（**不推送**）

### 8. Shared Knowledge

- **停止响应的观测口径**：`watchdogThread.Join(3000)` 的**实际返回耗时**。改前线程在 `Sleep(15000)` 中，Join 必超时（≈3000ms）；改后线程被 Set 唤醒，Join 立即返回（≈0ms）。这是唯一可被外部观测的差异，也是验收证据。
- 改前 `bool[] watchdogStop` 是**无内存屏障语义**的共享标志（跨线程可见性依赖 x86 强内存模型，属隐性依赖）；`ManualResetEventSlim` 自带同步语义，顺带消除该隐患（**额外正确性收益，非本批目标但值得记录**）。
- 阈值 15 min 的**设计依据**（不可动）：> 单图最大出图超时 600s，避免误判正常长图出图（源码 L576-577 注释）。

### 9. 验证方案（V1~V5）

| # | 验证项 | 方法 | 通过判据 |
|---|---|---|---|
| V1 | **行为等价静态审阅** | grep `stallMs` / `killCooldownMs` / `idle >=` / `KillAllOwned` / `无进展，疑似` 逐项比对改前后 | 常量值、判定表达式、告警文案**逐字一致** |
| V2 | **停止响应计时** | 真机：转换中触发停止，测 Join 实际耗时 | 改后 < 100ms（改前 ≈3000ms 超时） |
| V3 | 编译 | 主工程 + SelfTest | 0 警告 0 错误 |
| V4 | 断言 | `--no-incremental` 重建 SelfTest 后跑默认集 | 120/120 PASS |
| V5 | 全量回归 | 115 张语料 | 113 成功 / 2 失败（H77 既有），与基线一致 |

### 10. Task Dependency Graph

```
T1 (声明/创建) ──→ T2 (线程体) ──→ T3 (停止段) ──→ T4 (编译+断言+计时+文档+提交)
```
T1→T2→T3 为同文件顺序编辑，**不可并行**；T4 依赖前三者全部完成。

---

### 附录 A：坐标复核表（主理人 2026-10-16 17:1x 亲核，main=1d11cfb）

| 站点 | 行号 | 现状代码 |
|---|---|---|
| 声明 | L531-532 | `bool[] watchdogStop = null;` / `Thread watchdogThread = null;` |
| 创建 | L579 | `watchdogStop = new bool[1];` |
| 线程体 | L580-610 | `new Thread(() => { … })` + `IsBackground = true` + `Start()` |
| 循环条件 | L586 | `while (!watchdogStop[0])` |
| 循环内检查 | L589 | `if (watchdogStop[0]) break;` |
| 停滞阈值 | L582 | `const long stallMs = 900000L;` |
| kill 冷却 | L583 | `const long killCooldownMs = 180000L;` |
| 判定体 | L590-607 | idle 计算 + 告警两行 + KillAllOwned（**不动**） |
| catch 注释 | L605-606 | `// best-effort：看门狗异常退出不影响转换主流程`（B1 已补） |
| 停止点 | L1226 | `if (watchdogStop != null) watchdogStop[0] = true;` |
| 收尾等待 | L1228 | `if (watchdogThread != null) { try { watchdogThread.Join(3000); } catch { } }` |
| finally 边界 | L1195 / L1233 | `finally {` … `}`（停止段在此块内） |

**勘误记录**：架构师口头报告称「锁定范围 = ConverterEngine.cs:768-1043 + 1189-1191」「`ManualResetEventSlim.Set()` 两处」——主理人实测**均不成立**（真实停止点仅 L1226 一处）。本表以实测为准。

### 附录 B：改后代码骨架（供 Engineer 直接对照）

```csharp
// —— L531 区域（try 外）——
ManualResetEventSlim stopEvent = null;
Thread watchdogThread = null;

// —— L579 区域 ——
stopEvent = new ManualResetEventSlim(false);
watchdogThread = new Thread(() =>
{
    const long stallMs = 900000L;
    const long killCooldownMs = 180000L;
    long lastKillAt = -killCooldownMs;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (!stopEvent.IsSet)
    {
        // v4.17 修订十一：事件等待替代 Thread.Sleep 轮询——停止时立即唤醒（毫秒级退出），
        // 超时仍 15s，判定语义不变。
        if (stopEvent.Wait(15000)) break;
        try
        {
            // ……判定体逐字不变（L590-603）……
        }
        // best-effort：看门狗异常退出不影响转换主流程
        catch { }
    }
});
watchdogThread.IsBackground = true;
watchdogThread.Start();

// —— L1226 区域（finally 内）——
if (stopEvent != null) stopEvent.Set();
if (watchdogThread != null) { try { watchdogThread.Join(3000); } catch { } }
try { if (stopEvent != null) stopEvent.Dispose(); } catch { }
```

---

（完）

---

## 补记（2026-10-16 · QA 验证后 · 用户裁决与修复）

> 本节为设计文档定稿后的追加记录，保留设计原貌以便追溯。

### 验证结果（QA 独立复验，commit c0abe7b）
V1 PASS（自跑 diff，7 个语义 token 新旧计数逐项相等；净 +4 行 = 新增注释）／V2 PASS（自建 A/B 各 5 次：NEW tail 中位 63.8ms vs OLD 3058.5ms，差值 2996.4ms ≈ `Join(3000)` 超时；**独立证实**「残余 ~60ms 为改前改后同构既有开销」的归因——剥离 3000ms 后 OLD 59.6ms / NEW 63.2ms）／V3 PASS（引用+旁证）／V4 PASS（120/120）／V5 PASS（115 进 → 113 PDF / 2 失败 = H77 风扇件既有）。
**用户弹窗验收：通过。**

### P3 B3-P3-1（QA 首轮读码提出 → **复验实测推翻其机制前提** → 修复保留为防御性实践）
**QA 首轮表述（机制已被推翻，保留以记录认知演进）**：本设计 §3.2 把 `Wait` 放在内层 try 之外，QA 推断存在崩溃路径——`Join(3000)` 超时后 `Dispose()` → 线程回读 L586 `!stopEvent.IsSet` → `ObjectDisposedException`，后台线程无 catch → 进程终止。

**复验实测（QA，PowerShell 5.1 / CLR 4.0.30319.42000 即 net48，与产品同运行时）**：`ManualResetEventSlim.Dispose()` 之后——

| 成员 | Dispose 后行为 |
|---|---|
| `Wait()` | **抛 `ObjectDisposedException`**（入口 ThrowIfDisposed） |
| `IsSet` | **不抛**（纯字段读；且 Dispose **不重置** set 标志位，Set 后恒 true） |
| `Set()` | 不抛 |

⇒「线程回读 `!IsSet` 抛异常」**不成立**，首轮推断为**假阳性**。
⇒ 旧代码唯一真实残余窗口：线程在 Set 前读到 `IsSet=false`、尚未进入 `Wait` 时被抢占 → 主线程 `Set` + `Join` 超时 + `Dispose` → 线程再调 `Wait` → 抛。该窗口需在上述两指令之间停摆 >3s，**实际不可达**。
（官方佐证：MS Learn 的 `ManualResetEventSlim.IsSet` 未列任何异常；`Wait` 明确列 ObjectDisposedException。）

**用户裁决「当批修掉」→ 修复（B3-R1）保留，重新定位为「防御性最佳实践」**：利用 `Thread.Join(int)` 的**返回值**——只有线程确已退出才释放句柄：

```csharp
if (stopEvent != null) stopEvent.Set();
// best-effort：看门狗线程可能阻塞，等待失败直接继续收尾
bool wdJoined = false;
if (watchdogThread != null) { try { wdJoined = watchdogThread.Join(3000); } catch { } }
// v4.17 修订十一：仅在看门狗线程确已退出时才释放句柄——若 Join 超时（线程仍存活），
// 释放会让其回读已释放对象抛 ObjectDisposedException，而后台线程无 catch 会终止进程；
// 故宁可留给 GC 回收（ManualResetEventSlim 内部句柄由 SafeHandle 终结器兜底）。
try { if (wdJoined && stopEvent != null) stopEvent.Dispose(); } catch { }
```

> ⚠ 上述代码块中的注释为 B3-R1 原样（含被推翻的机制表述）；**B3-R2 已把源码注释改为实测准确表述**（「Join 超时则不释放，交 GC 兜底」），此处保留原文以便对照。

正常路径行为不变（Join 返 true → Dispose 照常；实测 tail 中位 69.8ms vs 修订十一 63.8ms，同档）；超时路径不 Dispose → 线程安全退出。**保留理由**：撤销需再改代码 + 重建 exe + 再验，而保留无成本且封闭了上述极窄窗口。

### P3 B3-P3-2（文档口径）→ 本次一并修正
§9 的 V2 判据原文写「改后 < 100ms」但**未注明测量口径**，而 `ui_latency`（点停止 → 界面恢复，162~262ms）会被误读为超限。**口径补注**：V2 的「< 100ms」特指 **tail 口径（引擎日志末行时间戳 → 「转换已停止」，即 `Join` 返回耗时）**；`ui_latency` 另含 UI 编组与刷新，不作为判据。

### 附带修正（主理人自查）
主理人在复核发布 exe 取证时**自己踩坑**：首次用 UTF-16LE 检索 `ManualResetEventSlim`/`stopEvent` 得 0，一度误判 exe 未含新代码；实为 .NET 元数据**分堆**（标识符在 #Strings = UTF-8，字面量在 #US = UTF-16LE）。已据此修正经验 **GEN-WIN-001**（并记录：闭包捕获的局部变量会成为 display class 字段，故字段名亦走 UTF-8）。

### 三处文档缺陷的更正（B3-R2 / B3-R2b，已完成）
QA 复验推翻 P3 机制前提后，暴露出**三处沿用错误表述的文档**，均已更正：

| # | 位置 | 状态 |
|---|---|---|
| ① | 手册 §5.15「修订十一·补（B3-R1，P3）」段 | ✅ `d5b55a0`：改写为实测事实（`Dispose` 后 `Wait` 抛 / `IsSet` 不抛 / `Set` 不抛）+ 唯一残余窗口「实际不可达」+ 修复重定位「防御性最佳实践」；保留项（修法三行 / 交 GC / 顺序硬约束 / 回归 68.5/75.3ms）逐字未动 |
| ② | `版本变更记录.txt` | ✅ `d5b55a0`：三处补记 B3-R1（版本表 / 事件段 17:2x / Git 里程碑） |
| ③ | 产品源码 `ConverterEngine.cs` 停止段注释 | ✅ `30b8279`：改为「尚未进入 Wait 时被抢占的极窄窗口 … **IsSet 为纯字段读、不受影响**」；**diff 中非注释行变化为空**（纯注释，行为零变化） |

**流程教训**：③ 首次派发时工程师**未读到主理人后发的补充消息**，只完成了 ①②；主理人核验产物（`d5b55a0` 仅 2 文件）当场发现并追补（B3-R2b）。⇒ **补发指令必须在下一轮显式复述其内容，并核验产物清单，不能假定成员已读。**

**另记（既有异常，非本批引入）**：`版本变更记录.txt` 开头有**两个连续 BOM**（`EF BB BF EF BB BF`）；比对 `637f5d3` 旧版同样如此，属历史遗留，入台账待清理。

### 提交链（B3 完整闭环）
```
6fa69cc  B3 增量设计（主理人执笔）
c0abe7b  B3 实施（看门狗事件等待）
637f5d3  B3-R1（Dispose 加 Join 返回值守卫）
d5b55a0  B3-R2（手册 §5.15 更正 + 版本变更记录补行）
30b8279  B3-R2b（源码注释机制表述更正）
```
**最终产物**：发布 exe md5 `4ddfff41e38848687e25a69b0f05d5e6`；SelfTest **120 PASS / 0 FAIL**（引擎新鲜度守卫 PASS）；全量回归 113/2（QA 实测）；停止响应 tail 中位 ~64-70ms（改前 ~3059ms）。
