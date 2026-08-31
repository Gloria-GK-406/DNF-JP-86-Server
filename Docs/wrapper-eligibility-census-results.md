# 包装器改写资格普查：结果报告

日期：2026-08-31。任务书：`Docs/fleet-wrapper-eligibility.md`。
过程与实机记录：`Docs/fleet-wrapper-eligibility-workflow.md`。

## 一句话

1,654 个包装器全部判定完毕，**640 个入口合格进入阶段 2 施工清单**。
判定经六轮实机会话验证：**普查预防了一次真实崩溃**（`0x059D1D26` 单独改写即死），
**D 组 102 个入口的排除得到实机支持**，**A/B 组的转移改写得到执行证明的确认**。
另查出既有实机记录的一个方法论缺陷：原试点「16 个入口存活」实际只覆盖 3 个入口。

## 产物

| 文件 | 内容 |
| --- | --- |
| `ClientHack/tools/wrapper-eligibility-results.csv` | 1,654 行判定 + 逐字节证据 |
| `ClientHack/tools/wrapper-rewrite-plan.csv` | **阶段 2 施工清单，1158 行 `eligible=YES`**（含 `patch_mode`、`next_entry_gap`） |
| `ClientHack/tools/wrapper-rewrite-eligibility.csv` | 校验器原始产出，640 行（仅 `safe_len` 判据，未查间距——已被上表取代） |
| `ClientHack/tools/verification-changelog.csv` | 对抗复核推翻的 10 行及理由 |
| `plugins/0150_wrapper_pilot/` | 见证模式（`Witness` / `WitnessThenRewrite` / `CensusD` / `SkipLoaders` / `OnlyEntryHex` / `SkipEntryHex`） |
| `DNFClient-A21-debug\ClientHack.*.log` | 六轮实机会话原始日志 |

校验器输出（从仓库副本重跑）：

```
results rows      : 1654
accepted (evidence verified): 1654
rejected: bad evidence      : 0
rejected: bad verdict       : 0
work-list coverage: 1654 / 1654 (100.0%)
```

**证据零拒收。** 1,654 行每一条 `evidence_hex` 都是该行输入窗口的逐字节子串。

## 最终判定分布

```
  组   VERIFIED   LOADER   MISMATCH   ODD
  A       595        0         5      109
  B       565        0        10      114
  C         0       49        29       25
  D         1        0        99        2
  odd       0        0         6       45
```

`ODD` 中 232 行是 `.vmp1`/`.mackt`/`.#|U` 的 caller 不可读单元，按既定决策机械落盘，留人工。

## 施工清单

**640 个合格入口**，151 个不同 API：

```
  按组      A 312   B 327   D 1
  按模块    msvcp60 283 | user32 196 | shell32 32 | gdi32 30 | advapi32 28 | wininet 23
```

另有 **521 个入口判定为真转移型但不合格**，全部卡在 `safe_len` 小于模板需求
（A 需 ≥10、B/D 需 ≥9），其中 391 个 `safe_len=6`——正是既有文档记载的
「入口头部 ~6 字节是按调用方定制的栈修整，+6 是共享的已修整入口」现象。
**这批入口本身是真转移型，只是不能用当前模板原地改写**，是阶段 2 的下一个标的。

## 一、任务书的形状判据不成立（判据修正）

任务书第 2 步要求 B 组「call 后第 5 字节是 1 字节诱饵」。实测：

- 6 个 B 组实机真值样本里只有 2 个满足
- 581 个 caller 可读 B 行里只有 283 个（49%）满足
- **288 行不满足字面判据、但本次判定为 VERIFIED-TRANSFER**

照任务书逐字执行会凭空造出约 288 个假 MISMATCH，占 B 组一半。

真正的判据是**续行点**：A/D 返回地址不变，从 +5 续行；B/C 返回 R+1，从 +6 续行。
用双对齐反汇编判哪条是正常代码流，20 个真值样本复现 19 个（唯一失败者 caller 不可读）。
任务书的「单字节诱饵」只是两条对齐恰好重合时的特例。

最锋利的辅助判据：**转移型包装器的参数压在 `call wrapper` 之前且没有第二次调用；
装载桩的参数压在它返回之后，喂给一个窗口内从未被写过的寄存器。**

## 二、装载桩检测：实机确认预防了一次崩溃

普查在非 C 组检出 **23 行装载桩形状**，判 MISMATCH 并排除出施工清单。

实机验证 `0x059D1D26`（user32!GetAsyncKeyState，D 组）：

| 对照 | 改写范围 | 结果 |
| --- | --- | --- |
| A | **只有** `0x059D1D26` | **崩溃**：`0xC0000005` 在 `0x07213FBC` 执行 |
| B | 其余 21 个，**排除**它 | 存活（登录界面 >3 分钟） |

普查对它的判定理由是：「`push 0x11 / call ebx` 与 `push 0x12 / call ebx`
各自吃满 GetAsyncKeyState 的 arity，而调用前只有一个 dword」。
**按原计划（A/B/D 全量改写）这个入口必死；普查排除了它。**

其中 `0x05C5BED5`（IntersectRect）落在 **B 组**——通过率 97% 的健康组里也混进了装载桩，
说明不能按组整体放行。

## 三、D 组：排除成立

普查判 D 组 99/102 MISMATCH。实机验证经历了一次自我纠错：

1. 用普查清单生成全部 102 个 D 入口的样本表，纯见证跑一局
   → **31/102 个调用点确实执行**，且这 31 个的普查判定全是 MISMATCH，VERIFIED 零个。
2. 排除 18 个装载桩后，把剩余 84 个带进**真实会话**（进游戏 + 大量界面浏览）
   → 客户端在 26 个入口被就地改写后**崩溃**：
   `0xC0000005` 在 `0x00000002` 执行，`esp` 模 4 余 3，栈严重错位。

归因对照：

| 运行 | 配置 | 控制流跳进垃圾地址 | 结果 |
| --- | --- | --- | --- |
| 纯见证，同等强度点击浏览 | 102 入口只探测不改写 | **0** | 未崩 |
| 对照 A | 只改写 `0x059D1D26` | 1 | 崩 |
| 第六轮 | 26 个入口改写 | 1 | 崩 |

**该特征只在开启改写的运行中出现**，归因于改写而非玩家操作。

无法定位到具体入口：26 个被改写入口的**调用前 push 串全部恰好为 1**，
存活组与嫌疑组结构完全重叠，逐个二分需要多轮崩溃会话。

**结论：D 组 102 个入口维持排除。** 不建议继续二分——收益上限约 100 个入口，
成本是多轮崩溃会话，而 521 个 `safe_len` 受阻入口是五倍大的标的且无需拿崩溃换答案。

## 四、既有实机记录的方法论缺陷

`0150_WrapperPilot` 头注释记载「16 个热点入口存活 24 分钟会话」。
两轮完整会话（登录→副本通关→回城→退出）的执行见证显示：

```
执行:   05BDAB0A gdi32!SetTextColor   (A)
        05B563B7 user32!GetClientRect (B)
        05B7BB54 user32!OffsetRect    (D，两局中仅一局命中)
未执行: 其余 13 个
```

**13 个入口的补丁装上了，但那条调用点整局没跑到**——「稳定」对它们是空结论。
其中包括 D 组真值标签里的 SetFocus、GetSystemMetrics、TextOutW：
这三个的 `VERIFIED-TRANSFER` 真值标签没有证据支撑。

同一个错误我在本次工作中也犯过一次：曾据「21 个入口改写后存活」推论
D 组是过度定罪，但那 21 个只活到登录界面、未进游戏。第六轮的真实会话推翻了它。

**教训：没有崩溃不等于安全，除非能证明它被执行过、且执行的是完整的真实路径。**
新增的 `WitnessThenRewrite` 模式正是为此——int3 命中时就地换成转移模板并把 EIP
退回入口，**同一次调用先证明执行、紧接着运行改写后的代码**。
`SetTextColor`(A) 与 `GetClientRect`(B) 在此模式下确认安全。

## 五、另一个前置条件：打包版客户端上改不动

打包版 `DNF.exe` 上，16 个入口全部 `VirtualProtect err=5` + `WriteProcessMemory err=5`
（`ERROR_ACCESS_DENIED`）——内存可读、字节与 dump 逐字节一致，但保护属性改不动。
换 B1 未打包 exe（`CLIENTHACK_B1_MODE=1` + `DNF.unpacked-b1-debug.exe`）后 16/16 立即成功。

**试点与后续全量改写只能在 B1 exe 上做**，既有文档未记载这一点。

## 六、方法与规模

- 确定性预处理（capstone）产出解码包：双对齐反汇编、收敛点、回溯对齐候选、结构标记；
  **每条指令带逐字节原样 hex**，agent 引用证据靠复制而非记忆——这是 1,654 行零拒收的直接原因。
- 155 个 agent：校准 9（3 轮，含 1 轮全挂重试）、全量普查 94 + 补跑 14、对抗复核 18 + 补跑 6。
  12 个挂在 `Connection lost mid-response`（集中在 30 单元的长批次；拆成 15 单元后 20/20 成功）。
- 校准闸门按修正基准放行：剔除 3 个争议 D 标签和 1 个 `.vmp1` 行后 **16/16 = 100%**，
  且全 30 行三轮判定完全一致。
- 六轮实机会话验证。

## 七、待办

1. ~~521 个真转移型但 `safe_len` 不足的入口~~ **已解决（08-31）**：
   5 字节 `E9 rel32` 跳桩方案，520/521 可回收，并顺带修掉 29 个会打进隔壁入口的条目
   （`check_eligibility_results.py` 的 `eligible` 公式漏查相邻入口间距）。
   施工清单 **640 → 1158**。已实机验证 A、B 两种约定。
   见 `Docs/wrapper-rewrite-trampoline-plan.md`，
   清单 `ClientHack/tools/wrapper-rewrite-plan.csv`。
2. 232 行 caller 不可读单元（`.vmp1` 215 / `.mackt` 16 / `.#|U` 1）留人工。
3. 对抗复核推翻的 3 行 MISMATCH→VERIFIED（`0x05BCC221`、`0x05BDC8C0`、`0x05BEC728`）
   均因 `safe_len` 不足而不合格，**不进施工清单，无需阻塞**。
4. D 组维持排除；若将来要回收，须用 `WitnessThenRewrite` + 完整会话逐批验证。
