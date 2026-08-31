# 包装器资格普查：动态工作流方案

日期：2026-08-31。对应任务书：`Docs/fleet-wrapper-eligibility.md`。
状态：**已批准，执行中**。定档：判据由本方案自行判定（不照抄任务书表格）；
232 行 caller 不可读单元一律 ODD；规模最高精度档（~107 agent）。

## 一、执行前的实测发现（会改变方案本身）

我先用 capstone（本机 5.0.7 可用）把 30 个校准单元和全量 1654 行跑了一遍统计，
四条结论直接影响工作流设计：

### 1. 文档第 2 步的 B 组判据在真值上不成立

任务书说 B 组「call 后第 5 字节是 1 字节诱饵（常见 `c3`）」。实测 6 个 B 组真值样本：

| entry | +5 字节 | 是否 1 字节指令 | 真值 |
| --- | --- | --- | --- |
| 0x059AB000 | `b3` (`mov bl,0x80`) | 否 | VERIFIED-TRANSFER |
| 0x059C9419 | `da` | 否 | VERIFIED-TRANSFER |
| 0x05B18AB8 | `c3` | 是 | VERIFIED-TRANSFER |
| 0x05B563B7 | `1e` (`push ds`) | 是 | VERIFIED-TRANSFER |
| 0x05BF65DD | `01` | 否 | VERIFIED-TRANSFER |
| 0x05C740E6 | `c7` | 否 | VERIFIED-TRANSFER |

只有 2/6 满足字面判据。全量 581 个 caller 可读的 B 行里只有 102 个 `+5 == c3`。
**照字面判，B 组会产出几百个假 MISMATCH。**

### 2. 真正的判据是「续行点」，不是「诱饵字节形状」

约定的语义本来就是续行点：A(`esp+4/R`) / D(`esp+8/a2`) 返回地址不变 → 从 **+5** 续行；
B(`esp+0/R+1`) / C(`esp+4/a1`) 返回 R+1 → 跳过 1 字节，从 **+6** 续行。
所以正确做法是**双对齐反汇编**：分别从 +5 和 +6 解码 `caller_after`，看哪一条是正常代码流。

在 20 个真值单元上复算，19 个可复现（唯一失败的 `0x05BEDE28` 在 `.vmp1`，caller 本就不可读）：

```
A 组 6/6  +5 正常、+6 乱码      D 组 4/4  +5 正常
B 组 6/6  +6 正常、+5 乱码      C 组 3/3(可读)  +6 正常且是「压参串 + call <寄存器>」
```

例：`0x059AB000` 从 +5 解出 `mov bl,0x80 ; jg ; add [eax+ebx-0x75],dh`（垃圾），
从 +6 解出 `cmp byte [edi+8],0 ; je ; mov ecx,[edi+4]`（干净代码）→ B 组成立。

文档的「1 字节诱饵」其实是这条判据的**特例**（+5 恰好是单字节指令时两种对齐重合）。
**方案采用续行点判据，并让校准批次来裁决它是否取代文档表格。**

### 3. D 组按文档字面判会直接卡死校准门

文档要求 D 组「call 前紧邻两条 push」。4 个 D 真值样本里，只有 `0x05C7E5FE`
能看到相邻 push；另外 3 个的 `caller_before` 窗口起点落在指令中间，线性反汇编全是垃圾。
照字面判 = 3 个假 MISMATCH → 校准得分 16/20 = **80% < 90% 门槛**，全量放不开。

修法：`caller_before` 必须做**回溯对齐**——16 个起点全试，只保留「指令流恰好停在窗口末尾」
的对齐，取各对齐的共识末指令。实测 `0x05B161A0` 有 15 个合法对齐、共识末指令 `push eax`；
`0x05C03DB9` 14 个对齐、共识 `push esi | push esi`（两条 push，符合 D 预测）。

### 4. 第 3 步「头部签名」在 16 字节窗口里基本失效

文档说 C 组头部有「前 1-4 条 push 后紧跟 `mov r32,[esp+N]`」指纹。我在全量 1654 行上扫：
**C 组 103 行命中 0 条**，B 组反而命中 6 条。原因是混淆链把这条 `mov` 推到了 16 字节之外
（文档自己举的 PeekMessageW 例子 `0x05BD62EF`，指纹在第 5 条指令、偏移 ≥ 14 字节）。

后果：**232 行 caller 不可读的单元（`.vmp1` 215 / `.mackt` 16 / `.#|U` 1）几乎没有可用证据**，
按任务书规则只能大批量写 ODD。这是决策点 ②。

### 5. 规模与可机械化程度（全量实测）

- 1654 行，caller 可读（`.text`/`.tls`）**1422** 行，不可读 232 行。
- 去重后只有 **338 个不同 `module!function`** → A 组「吞掉一个首参」的签名判断可**按函数缓存**，
  不必做 709 次（A 组只有 159 个不同函数）。
- 用一版粗糙打分器跑全量续行点判据：**470 行机械可决**（A 198 判 +5 / B 180 判 +6，
  方向与预测高度一致；D 22 判 +5、9 判 +6，噪声最大——与「D 组需逐个复核」的既有结论吻合），
  **953 行需要真判断**。打分器在校准阶段调优后，可决比例还会上升。

> 附带产出：D 组是本次最可能出黄金 MISMATCH 的地方；C 组 103 行的 `call <寄存器>` 只有
> 17/99 落在 32 字节窗口内，其余需靠「+6 续行 + 压参串」间接判定。

## 二、工作流设计

总体形态：**确定性预处理 + 舰队判断 + 机器校验 + 对抗复核**。
预处理只做「把字节解码成指令」（纯粹派生自输入行的字节，不引入外部信息，
不违反任务书「只用输入行给的字节」纪律），**判定权仍在 agent**。

### Phase 0 —— 解码包（本地脚本，0 agent，~2 分钟）

`scratchpad/decode_pack.py` 读 worklist，为每行追加派生列（不改仓库文件）：

- `dis_at5` / `dis_at6`：从 +5 / +6 双对齐反汇编（各 8 条指令）
- `align_score5` / `align_score6`：消耗字节数 − 垃圾指令罚分
- `back_aligned`：`caller_before` 16 起点回溯对齐的共识末 2 条指令 + 合法对齐数
- `head_dis`：`wrapper_head_hex` 反汇编
- `callreg_off`：窗口内 `ff d0-d7` 的偏移（无则空）

输出 `scratchpad/worklist-decoded.jsonl`。**这一步把「手工从 hex 解码 x86」这个最容易
幻觉的子任务从 agent 身上摘掉**，agent 只做判断并引用原始 hex 子串。

### Phase 1 —— 校准闭环（动态，1-4 轮，每轮 1 agent）

这是整个工作流「动态」的核心：**判据本身要先被真值证伪一遍**。

```
轮 N：  rubric vN + 30 个校准单元 → 1 个 agent 出 30 行结果
       → check_eligibility_results.py --calibration
       → 命中率 ≥ 90% 且四组全覆盖？ 是 → 放行 Phase 2
                                    否 → 我读混淆矩阵，改 rubric → 轮 N+1
```

- rubric v1 = 续行点判据（发现 2）+ 回溯对齐（发现 3）+ C 组 `call <reg>` 补充判据，
  文档表格作为**辅助**而非主判据。
- 硬性熔断：**4 轮仍不过 90% 就停下来找你**，不放全量（避免用错判据烧 20+ agent）。
- 每轮只用 1 个 agent，成本极低；这是全流程唯一必须串行的部分。

### Phase 2 —— 全量扇出（约 24 个 agent，可并行）

按 `(group, source_section 可读性)` 切成同质批次，每个 agent 只面对**一条判定规则**：

| 批次 | 行数 | agent 数（60-70 行/个） |
| --- | --- | --- |
| A · caller 可读 | 606 | 9 |
| B · caller 可读 | 581 | 9 |
| C · caller 可读 | 99 | 2 |
| D · caller 可读 | 102 | 2（**降到 35 行/个，D 是高危组**） |
| odd · caller 可读 | 34 | 1 |
| caller 不可读（全组） | 232 | 3（按决策点 ② 的口径） |

- A 组批次额外附带**函数签名速查表**（159 个函数的形参个数，由 A 组第一个 agent 产出后共享），
  避免每个 agent 重复推断同一个 API 的签名。
- 每个 agent 把自己那批写成 `scratchpad/shards/<batch>.csv`（4 列），
  workflow 脚本只回收行数与路径，避免把 1654 行塞进返回值。
- 输出用 schema 约束：`{entry_va, verdict, evidence_hex, evidence_note}`，
  `evidence_hex` 必须是输入行 hex 的原样子串。

### Phase 3 —— 对抗复核（约 8 个 agent，pipeline 紧跟 Phase 2 各批次）

不复核全部，只复核**高风险类**（用 `pipeline`，A 组还在跑时 B 组的复核已经开始）：

1. 全部 `MISMATCH` 行（黄金产出，必须逐条经得起推敲）
2. D 组全部 102 行（C 组的教训 + 实测噪声最大）
3. C 组被判 `VERIFIED-TRANSFER` 的行（与既有实机结论冲突 → 必须能自圆其说）
4. caller 不可读批次里非 ODD 的行
5. 其余 `VERIFIED-TRANSFER` 抽 5% 随机样

复核 agent 拿到「原始 hex + 原判定 + 证据」，只回 `{agree: bool, reason}`，
不一致的行降级为 MISMATCH 并进人工清单。

### Phase 4 —— 合并与报告（0 agent）

```
合并 shards → wrapper-eligibility-results.csv
python check_eligibility_results.py --worklist ... --results ...
→ wrapper-rewrite-eligibility.csv（阶段 2 施工清单）
```

报告含：四类判定分布、按组混淆、evidence 拒收率（**应为 0，非 0 说明有幻觉**）、
覆盖率、MISMATCH 清单（附一句话理由）、以及「续行点判据 vs 文档表格」的差异统计
——后者是回写 `themida-api-wrapper-recovery.md` 的素材。

### 断点续跑

Workflow 支持 `resumeFromRunId`：Phase 1 改 rubric 后重跑，已完成且入参未变的 agent
直接命中缓存；只有改过的批次会重跑。校准阶段迭代基本零额外成本。

## 三、规模与成本

| 档位 | agent 数 | 批大小 | 说明 |
| --- | --- | --- | --- |
| 保守 | ~16 | 110-120 行/个 | 贴近默认 15 上限，尾部质量会下滑 |
| **标准（建议）** | **~34** | 60-70 行/个（D 组 35） | Phase0 0 + Phase1 1-4 + Phase2 24 + Phase3 8 |
| 高精 | ~60 | 30 行/个 + 全量复核 | 只在标准档校验拒收率偏高时再上 |

标准档粗估：Phase 2 每 agent 输入 ~25KB / 输出 ~7KB，Phase 3 更轻。

## 四、已定决策

1. **判据口径**：采用「续行点双对齐」为主判据，任务书表格降为辅助。
   任务书由另一模型总结，B/D 两组的字面判据与真值冲突，不予采信。
   差异统计将回写 `themida-api-wrapper-recovery.md`。
2. **232 行 caller 不可读单元**：一律 `ODD`，交人工。由 `merge_results.py --odd-for`
   机械落盘（evidence 取 head 前 4 字节，必为 RAW head 子串），不占 agent 名额。
3. **规模**：最高精度档，~107 agent。

## 五、Phase 0 实测结果（已完成）

`scratchpad/fleet/decode_pack.py`（capstone 5.0.7）已对 1654 个单元产出解码包，
每条指令都带**逐字节原样 hex**，agent 引用证据靠复制而非记忆。派生信号：

- 双对齐反汇编（+5 / +6）各 10 条指令 + 合成打分
- **收敛点**：两条流重新同步的偏移。收敛后指令完全相同，
  决策面只剩前缀——1422 个可读单元里 425 个在 +6 收敛（即 +5 处是单字节指令，
  线性反汇编原理上无法区分，必须靠「这条指令作为真代码执行是否说得通」来判）
- `caller_before` 16 起点回溯对齐 + 共识末指令 + 紧邻 push 串
- 窗口内 `call <寄存器>` 位置、resume 流的 push 串、结构标记（ret / int3 padding / 序言 / 垃圾指令）

**机械提示在真值上的表现**（19 个 caller 可读真值单元）：

```
判对 8   弃权(AMBIGUOUS) 11   判反 0
```

唯一判反的 `0x05BEDE28` 在 `.vmp1`，按决策 ② 本就走 ODD。
高精度、中召回，正是先验该有的性质。全量 1422 可读单元：
`resume@+5` 423 / `resume@+6` 303 / 模糊 696。

批次（`render_units.py`）：94 个，同质分组，模糊行 12/批（D 组 6/批）、
机械可决行 30/批（D 组 15/批），单元平均 1430 字符。

## 六、交付物清单

| 文件 | 作用 |
| --- | --- |
| `scratchpad/fleet/decode_pack.py` | Phase 0 解码包生成 |
| `scratchpad/fleet/render_units.py` | 批次渲染 |
| `scratchpad/fleet/rubric.md` | 判定协议（agent 读的那份） |
| `scratchpad/fleet/merge_results.py` | JSON 分片 → 结果 CSV（含机械 ODD 落盘） |
| `wrapper-eligibility-results.csv` | 舰队产出，喂给 `check_eligibility_results.py` |
| `wrapper-rewrite-eligibility.csv` | 校验器产出的阶段 2 施工清单 |

agent 产出走 **JSON 而非 CSV**：evidence_note 里的逗号/分号不可能损坏字段，
CSV 只在 `merge_results.py` 这一处生成。

## 七、校准闸门第 1 轮（3 个独立 agent，已完成）

三轮全部 **16/20 = 80%**，未过 90% 门槛。但三轮 **20/20 逐单元完全一致**，
且 **90 行证据零拒收**——判据稳定、字节零幻觉。失分集中在两处：

```
confusion (truth -> got)   三轮完全相同
  VERIFIED-TRANSFER -> {VERIFIED-TRANSFER: 13, MISMATCH: 3}   ← 3 个全是 D 组
  REGISTER-LOADER   -> {REGISTER-LOADER: 3, ODD: 1}           ← 1 个是 .vmp1，按决策 ② 走 ODD
```

把这 4 个争议行剔除，**非争议标签 16/16 = 100%**（A 6/6、B 6/6、C 可读 3/3、D-SetFocus 1/1）。

### 7.1 D 组：真值文件本身很可能是又一个测量伪影

三轮独立给出的 D 组理由高度一致且可核：

| D 单元 | 调用点形状 | 判读 |
| --- | --- | --- |
| `0x05B161A0` TextOutW | 包装器调用**之后**压 5 个 dword → `call ebx`（ebx 全窗口未装载）；TextOutW 正好 5 参 | 装载桩 |
| `0x05C03DB9` GetSystemMetrics | `esi` 在 32 字节窗口内被调用**两次**，每次前一个 `push 0`；esi 未装载 | 装载桩 |
| `0x05B7BB54` OffsetRect | `push ebp` → call wrapper → `push eax/lea/push eax` → `call edi`，edi 未装载，合计 3 参 | 装载桩 |
| `0x05C7E5FE` SetFocus | 无 `call <reg>`；调用前 3 个 push 对 1 参 = argc+2 | **真转移型**，三轮判对 |

判据的最锋利形式（已写入 rubric）：**转移型包装器的参数压在 `call wrapper` 之前
且没有第二次调用；装载桩的参数压在它返回之后，喂给一个窗口内从未被写过的寄存器。**

全量统计给出裁决：

```
装载桩形状检出率（各组在自己预测的续行点上）
  A    1 / 606 =  0.2%
  B    1 / 581 =  0.2%
  C   13 /  99 = 13.1%   ← 已被实机证伪的组
  D   12 / 102 = 11.8%   ← 与 C 同量级，是 A/B 的约 60 倍
```

C 组已知 100% 是装载桩，却只有 13% 让 `call <reg>` 落进 32 字节窗口——所以 13% 是
**检出率**而非真实比例。D 组检出率与 C 组无法区分，而与 A/B 相差约 60 倍。

**结论：D 组（102 个入口）很可能是第二个 C 组。**`themida-api-wrapper-recovery.md`
里「D 组 4 个样本实机改写后稳定」的记录与调用点字节形状冲突；同一文档本就写了
「鉴于 C 组的教训，全量改写前建议对 D 组逐个抽样复核路径」。这条建议现在有了字节层证据。

**对阶段 2 的影响**：D 组 102 个入口在人工复核前不应进入施工清单。
本工作流的判据会把这类行判为 `MISMATCH`（保守方向：排除出施工清单、转人工），
无论最终谁对，安全侧都成立。

### 7.2 修复的一处真实缺陷

`0x05AC934F`（GetWindowTextLengthW，A 组）：`caller_before` 末尾 `... 8b d8 53 57`
正确读法是 `mov ebx,eax; push ebx; push edi`（2 个 push = argc+1），但 15 个合法对齐里
11 个共识在垃圾 FPU 指令 `fcom dword ptr [ebx+0x57]` 上——**多数投票选中了垃圾**，
报告"零 push"，一轮据此误判 MISMATCH。

修法：回溯对齐不再只报共识，改报按「非垃圾优先、push 串更长优先」排序的前 3 个候选读法，
连同各自的 push 串一起交给 agent 权衡；rubric 增加「只有当没有任何对齐产生 push 串时
才能断言零 push」。第 2 轮校准据此重跑。

## 八、D 组实机仲裁实验（待你执行）

### 8.1 为什么字节判不出来

试点插件头注释记录：16 个 A/B/D 入口在 24 分钟会话（含完整副本通关与退出 flush）中
存活，first-chance 日志与零补丁对照组逐条一致。样本表 VA 与工作清单只差
`0x400000` 映像基址（`0x057161A0` ↔ `0x05B161A0`），**确认打的就是那 4 个 D 入口**，
不是标签错位。

于是两边证据都很硬：

| 支持 D = 真转移型 | 支持 D = 装载桩 |
| --- | --- |
| 24 分钟实机稳定，对照组逐条一致 | 3/4 调用点的参数压在包装器调用**之后**，喂给窗口内从未装载的寄存器 |
| | 装载桩形状检出率 D 11.8% ≈ C 13.1%，而 A/B 仅 0.2%（60 倍差） |

**字节层无法仲裁。** 唯一的裂缝在于：插件只在 init 时打补丁并记录打补丁成败，
**全程没有执行计数**。所以「稳定」有两种解释——真的是转移型，或者那 4 个调用点
在那次会话里根本没跑到（每个包装器只服务一个调用点，`0x0249A9BC` 这样的具体
TextOutW 调用点完全可能是冷路径）。

### 8.2 实验：D 组执行见证

比已经跑过的实验**更小**，只回答一个问题：那 4 个调用点执行过没有。

1. 样本表只保留 4 个 D 条目（`SetFocus 0x0587E5FE`、`OffsetRect 0x0577BB54`、
   `GetSystemMetrics 0x05803DB9`、`TextOutW 0x057161A0`，均为插件表内 VA）。
2. 加一个 ini 旋钮（如 `[WrapperPilot] Witness=1`）：写入的不是转移模板，而是
   入口首字节改 `0xCC`（int3）。**必须做成一次性**——命中后处理器记录 VA、
   还原原字节、EIP 不动继续执行，这样一次会话能见证全部 4 个，且不影响游戏进程。
3. `Enabled=1`，按与原实验相同的协议跑（24 分钟 + 完整副本通关 + 退出 flush）。
4. 看 `0007_fault_recorder` 是否为这 4 个 VA 各记到一次断点。

### 8.3 结果如何解读

| 见证结果 | 结论 | 对阶段 2 的处置 |
| --- | --- | --- |
| 4 个全部命中 | 原实机测试确实压到了这些路径，「稳定」成立 → **D 是真转移型**，本次普查对 D 的字节判读需要重新解释 | D 组可进施工清单；我重跑 D 组那 15 个批次 |
| 部分命中 | 只有命中的那几个入口的「稳定」成立 | 逐个入口处置：命中且稳定的可入清单，未命中的留人工 |
| 全部未命中 | 「D 组存活」是空结论——补丁装上了但从未执行 | **D 组 102 个入口全部排除出施工清单**，与 C 组同等对待 |

一次性还原的实现细节（异常处理器里改 EIP 还是改字节、fault_recorder 的钩子层级）
我可以直接写，你说一声就行。普查不等这个实验——D 组行本轮判 `MISMATCH`（排除、转人工），
无论仲裁结果如何，安全侧都成立；仲裁若翻案，只需重跑 D 组 15 个批次。

## 九、全量普查结果（第一轮，1230/1422 落盘）

94 个 agent，85 个返回、87 个成功落盘（2 个写完文件才断线），9 个挂在
`Connection lost mid-response`。失败集中在 30 单元的大批次（9 个里 7 个），
补跑时拆成 15 单元/批。

**证据纪律：1230 行全部通过校验器，`bad evidence: 0`、`bad verdict: 0`、重复 0。**
解码包让每条指令都带原样 hex，agent 引用靠复制——1230 行零字节幻觉。

```
verdicts by group（1230 行）
  A    VERIFIED 476   MISMATCH  5   ODD  5
  B    VERIFIED 494   MISMATCH 10   ODD  5
  C    LOADER    49   MISMATCH 29   ODD 21     VERIFIED 0
  D    VERIFIED   5   MISMATCH 94   ODD  3
  odd  ODD       28   MISMATCH  6
```

### 9.1 D 组的第三条独立证据：栈算术硬矛盾

94 个 D 组 MISMATCH 全部引用「push 数量不够」。由于 rubric 的 D 段写了「要严格」，
需要排除措辞诱导，因此**独立用 capstone 重算**（不经 agent）每个调用点在 `call` 前
实际压入的 dword 数，并区分「缺口是窗口截断」还是「缺口被窗口内真实指令界定」：

```
                        满足要求   真矛盾(有界)   截断(不可判)
  A（约定需 ≥1）        486 (100%)        0            0
  B（约定需 ≥0）        509 (100%)        0            0
  D（约定需 ≥2）          6 (  6%)       89 (87%)      7 (7%)
```

**A 组的约定预测「调用前一个 push」，486 次全中；D 组预测「两个 push」，
102 次只中 6 次，且 87% 的缺口由窗口内可见的非 push 指令界定，不是截断。**

至此 D 组有三条互相独立的证据：

1. 装载桩形状检出率 D 11.8% ≈ C 13.1%，而 A/B 仅 0.2%（60 倍差）
2. 94/102 被 agent 独立判定 MISMATCH，三轮校准同样结论
3. 栈算术 87% 硬矛盾（本节，机械计算，不经 agent）

**D 组（102 个入口）在实机见证实验（第八节）出结果之前，不得进入阶段 2 施工清单。**

### 9.2 A/B 组站得住

A 组 476/486 = 98% VERIFIED，B 组 494/509 = 97% VERIFIED，且两组的栈算术
100% 自洽。任务书表格对 A/B 的语义判断是对的，只有「+5 单字节诱饵」这条
**形状判据**是错的（真判据是续行点，见第一节）。

## 十、见证模式已实现（`0150_WrapperPilot`）

`ClientHack/plugins/0150_wrapper_pilot/wrapper_pilot.c` 新增 `Witness` 旋钮，
纯增量、默认关闭，`Witness=0` 时行为与改动前逐字节相同。
`clang -fsyntax-only -m32 --target=i686-pc-windows-msvc` 通过。

**机制**：`Witness=1` 时不写转移模板，改在入口写 1 字节 `0xCC`。首次执行触发
`EXCEPTION_BREAKPOINT`，VEH 还原原字节、把 EIP 退回该字节、
`EXCEPTION_CONTINUE_EXECUTION` 继续——包装器随后原样运行，会话不受影响。
**一次性、每入口一条日志**。

原字节取自样本表已有的 `expected[0]`；`patch_apply` 的 `memcmp(target, expected, length)`
按实际长度比对，所以 1 字节补丁能过宿主校验。

**VEH 顺序**：`AddVectoredExceptionHandler(1, ...)` 是头插，插件 0150 加载晚于
0001/0005/0006/0007，因此本处理器排在链首，其余处理器不会看到这个断点。

**安全性**：无论 D 组是转移型还是装载桩，见证模式都只是「陷一次、还原、继续」，
不改变栈形，不会崩——这正是它比重跑转移型试点更适合做仲裁的原因。

### 运行步骤

```ini
[WrapperPilot]
Enabled=1
Conventions=D      ; 只武装 4 个 D 组样本
Witness=1
```

重新编译 `0150_WrapperPilot`，按与原实验相同的协议跑（24 分钟 + 完整副本通关 +
退出 flush），然后看 exe 旁的 `ClientHack.log`。

**启动应看到**：

```
WrapperPilot  witness mode: entries trap once on first execution, then run unmodified
WrapperPilot  witness armed user32.dll!SetFocus entry XXXXXXXX (caller YYYYYYYY)
WrapperPilot  witness armed user32.dll!OffsetRect entry ... （共 4 条）
WrapperPilot  pilot armed: 4/4 entries trapped for witness
```

**会话中每命中一个入口一条**：

```
WrapperPilot  WITNESS HIT gdi32.dll!TextOutW entry XXXXXXXX thread NNNN -- call site DID execute
```

`grep WITNESS ClientHack.log` 即可。若 `armed` 不是 4/4，说明有入口的实机字节与
dump 不符（`patch_apply` 返回 `INCOMPATIBLE_MEMORY`），那本身也是一个发现。

### 结果解读

见第八节 8.3 的三分支表。命中数即结论：4 个全中 → D 组翻案，我重跑 D 组批次；
部分命中 → 逐入口处置；零命中 → 原「D 组存活」结论为空，D 组 102 个入口
按 C 组同等排除。

## 十一、实机仲裁结果（2026-08-31，已执行）

五轮实机会话，**结论：普查的装载桩检测正确并预防了一次真实崩溃；但基于 push
计数的 D 组全组判死是过度定罪。**

### 11.1 先决发现：打包版客户端根本改不动

`本地游戏S4.bat` 跑的打包版 `DNF.exe` 上，16 个入口全部
`VirtualProtect err=5` + `WriteProcessMemory err=5`（`ERROR_ACCESS_DENIED`）——
内存可读、字节与 dump 逐字节一致，但保护属性改不动，壳拦住了
`NtProtectVirtualMemory`/`NtWriteVirtualMemory`。换 B1 未打包 exe
（`DNF.unpacked-b1-debug.exe` + `CLIENTHACK_B1_MODE=1`）后 16/16 立即成功。

**所以试点实验只能在 B1 exe 上做**，既有文档未记载这一点。

### 11.2 原实验「16 个热点入口存活」的证据只覆盖 3 个

两轮完整会话（登录→副本通关→回城→退出），16 个入口累计只有 3 个执行过：

```
执行:   05BDAB0A gdi32!SetTextColor   (A)
        05B563B7 user32!GetClientRect (B)
        05B7BB54 user32!OffsetRect    (D，且仅在两局中的一局命中)
未执行: 其余 13 个
```

**其余 13 个入口补丁装上了但那条调用点整局没跑到**，「稳定」对它们是空结论。
其中包括 D 组真值标签里的 SetFocus、GetSystemMetrics、TextOutW ——
这三个的 `VERIFIED-TRANSFER` 真值标签没有证据支撑。

### 11.3 见证＋改写同调用：A/B 组得到执行证明的确认

新增 `WitnessThenRewrite`：int3 命中时不还原原字节，而是写入转移模板并把 EIP
退回入口，**同一次调用先证明执行、紧接着运行改写后的代码**。

`SetTextColor`(A)、`GetClientRect`(B) 均在此模式下改写并存活。
这是原实验拿不到的证据——它无法区分「安全」与「没执行」。

### 11.4 D 组全量可达性：31/102

用普查工作清单生成全部 102 个 D 组入口的样本表（`wrapper_pilot_census_d.h`，
`expected` 取 `wrapper_head_hex` 前 10 字节），纯见证跑一局尽量多浏览界面：

**31/102 个 D 组调用点确实执行**，且**这 31 个的普查判定全部是 MISMATCH
（30 MISMATCH + 1 ODD），VERIFIED 零个。**

### 11.5 决定性对照：一个入口致命，21 个无害

对全部 102 个 D 入口开启见证＋改写，客户端在第 5 次改写后死亡，
崩溃为 `0xC0000005` 执行访问违例、**EIP=0x07213FBC**（控制流跳进垃圾地址，
正是转移模板搞错栈后应有的死法）。随即做单变量对照：

| 对照 | 改写范围 | 结果 |
| --- | --- | --- |
| A | **只有** `0x059D1D26` (user32!GetAsyncKeyState) | **崩溃** |
| B | 其余 21 个，**排除** `0x059D1D26` | **存活**（>3 分钟） |

### 11.6 阶段性结论（**已被 11.9 推翻，见下**）

```
致命的 1 个   普查理由：明确的装载桩形状
              「push 0x11 / call ebx 与 push 0x12 / call ebx 各自吃满
                GetAsyncKeyState 的 arity，而调用前只有一个 dword」
存活的 21 个  普查理由：21/21 全部只是「push 数量不够 / 续行点」
              装载桩标记：0 个
```

**装载桩检测（第九节的 23 行崩溃预防清单）是真的**——点名的入口经实机确认会崩，
这次普查实实在在预防了一次崩溃。

**但 D 组基于 push 计数的全组判死是过度定罪**——21 个仅因 push 数不足被判
MISMATCH 的入口，改写后全部存活。第九节 9.1 的三条证据里，
「栈算术 87% 硬矛盾」这一条**不能推出「不可改写」**；它至多说明
symeval 测出的 `esp+8/a2` 语义有问题，而非入口不可原地改写。

### 11.7 对阶段 2 施工清单的修正

- **必须排除**：23 行装载桩形状（第九节清单），其中 `0x059D1D26` 已实机确认致命。
  另两个同族 `0x059C8919`、`0x05BA4F07`（同为 GetAsyncKeyState 装载桩）
  在可达性图谱里确认会执行，**未单独实机验证，按同族处理，一并排除**。
- **可以回收**：D 组中仅因 push 计数被判 MISMATCH 的入口，
  已有 21 个实机改写存活。回收前建议按本节方法逐批见证＋改写验证。
- 存活证据目前只到登录界面（>3 分钟），未经完整副本会话。
  完整会话验证是下一步。

### 11.8 复现方式

```ini
[WrapperPilot]
Enabled=1
Conventions=ABD      ; 或 D
Witness=1            ; int3 一次性探针
WitnessThenRewrite=1 ; 命中即改写，同一次调用运行改写后代码
CensusD=1            ; 用全部 102 个普查 D 入口替代 20 个手挑样本
OnlyEntryHex=059D1D26  ; 单入口隔离
SkipEntryHex=059D1D26  ; 单入口排除
```

必须用 B1 exe：`CLIENTHACK_B1_MODE=1` + `DNF.unpacked-b1-debug.exe`。
日志：`ClientHack.log`（`WITNESS HIT`）、`clienthack-faults.log`（崩溃现场）。
本次五轮的原始日志保存在 `DNFClient-A21-debug\ClientHack.*.log`。

## 十二、修正：D 组的排除是站得住的（11.6 结论撤回）

11.6 依据「21 个仅因 push 计数被判 MISMATCH 的入口改写后存活」推论
「D 组全组判死是过度定罪」。**该推论下得过早，现予撤回。**

那 21 个只活到登录界面（约 3 分钟，未进游戏）。第六轮把 `SkipLoaders=1`
（排除 18 个装载桩）后剩下的 84 个 D 入口带进**真实会话**，玩家进游戏并浏览大量界面，
客户端在 26 个入口被就地改写后崩溃：

```
0xC0000005 在 0x00000002 执行     ← 控制流跳进垃圾地址
esp = 0x001AE98F                  ← 模 4 余 3，栈严重错位
崩溃栈为 user32 窗口过程派发路径
```

### 12.1 归因：是改写，不是玩家操作

三次运行的故障特征对比：

| 运行 | 配置 | 异常数 | 控制流跳进垃圾地址 | 结果 |
| --- | --- | --- | --- | --- |
| 第四轮 | 102 入口**纯见证**，玩家大量点击浏览 | 7 | **0** | 未崩 |
| 对照 A | 只改写 `0x059D1D26` | 1 | 1 | 崩 |
| 第六轮 | 26 个入口改写（已排除装载桩） | 5 | 1 | 崩 |

**「控制流跳进垃圾地址」只在开启改写的运行中出现**；同等强度的界面浏览在纯见证下零次。
崩溃归因于改写。

### 12.2 无法归因到具体入口，且结构上分不开

26 个被改写的入口中没有 user32 函数（装载桩清单已把 user32 的 D 入口全部排除），
崩溃却发生在 user32 派发路径，最后一条 `WITNESS HIT` 是 fmodex。
曾怀疑 imm32 那 5 个（user32 输入法派发路径、调用点位于
`0x02C91Dxx` 单 push 调用链区域），但结构对比不支持：

```
                        调用前 push 串   前 5 条续行指令中 call 数 >= 3
  存活的 21 个           全部 = 1         5/21
  imm32 的 5 个          全部 = 1         3/5
```

**26 个入口的调用前 push 串全部恰好为 1**，存活组与嫌疑组在这个维度上完全重叠，
无法分离。逐个二分需要多轮崩溃会话。

### 12.3 定案

- **D 组 102 个入口维持排除**，不进阶段 2 施工清单。普查的判定得到实机支持。
- **装载桩检测另有独立价值**：`0x059D1D26` 单独改写即致命（对照 A），
  这次普查确实预防了一次真实崩溃。
- 11.5 的对照 A/B 仍然成立（单入口致命 / 排除它则登录界面存活），
  但**「登录界面存活」不足以支撑回收**——这正是本次实机测量最重要的方法论教训，
  与 11.2「原实验 16 个入口里 13 个从未执行」是同一个错误的两种形态：
  **没有崩不等于安全，除非能证明它被执行过、且执行的是完整的真实路径。**
- 不建议继续对 D 组做崩溃二分：收益上限约 100 个入口，成本是多轮崩溃会话；
  阶段 2 的更大标的是 521 个真转移型但 `safe_len` 不足的入口。
