# A21 客户端逆向：方法与流水线

日期：2026-09-06。这份文档把 `.-wI` 包装层、`.vmp1` 虚拟机、CRT 层三条线的
**做法**串起来，供后来者复现。各线的**结论**在各自文档里，此处只讲怎么做出来的。

## 零、一条贯穿始终的教训

**对这个镜像做静态模式匹配，量到的是变形层的输出分布，不是程序结构。**

四次撞墙，每次都是这个原因：

| 尝试 | 结果 |
| --- | --- |
| `.vmp1` 按派发偏置扫描字节码找 handler | 1,199 个候选，只有 34% 真的停在派发上 |
| `.vmp1` 按指令形态聚类找克隆 | 1,199 个地址塌成 1,107 种形态——不是克隆 |
| `.vmp1` 回溯提取 VM 入口密钥 | 14,760 处引用里匹配到 15 个候选，1 个有效 |
| `.-wI` 递归下降区分包装器与 CRT | 「CRT 独有」= 0.0%，两条下降都灌进共享蹦床池 |

四次都靠**活体测量**绕开：在客户端里下探针、跑一局、读日志。
`0001_PageGuardTrace`（页首触）、`0150_WrapperPilot`（入口陷阱）、
`0160_VmEntryTrace`（VM 入口捕获）就是这三件工具。

推论：**遇到"静态分析给出漂亮但可疑的数字"时，先问它是不是在量变形器。**

## 一、防幻觉的证据纪律

两次舰队普查（1,654 个包装器 + 276 个 handler）合计约 1,930 行判定，
**证据拒收率 0**。做法只有一条：

> **确定性预处理把"从 hex 解码 x86"这件事从 agent 身上摘掉。**
> 解码包里每条指令都带**逐字节原样 hex**，agent 引用证据靠复制，不靠记忆；
> 校验器拿引用对输入行做子串匹配，引用不存在即整行作废。

`build_wrapper_decode_pack.py` 与 `build_vmp1_census_pack.py` 就是这一步。
判定协议在 `wrapper-census-rubric.md` / `vmp1-handler-census-rubric.md`。

配套的两条：

- **校准闸门**。用已知答案先跑一批，不达标就改判据重来，别拿错判据去烧几十个 agent。
  包装器那次校准直接推翻了任务书的形状判据（见下）。
- **对抗复核**。让第二批 agent 去**推翻**第一批的结论，而不是复述。
  包装器复核推翻 10 行，handler 复核推翻 3 行（全是同义词归并）。

## 二、`.-wI` 包装层流水线

```
wrapper-apis-a21.csv                        ← 既有：1,654 个命名包装器
  │
  ├─ build_eligibility_worklist.py          ← 既有：每行自带字节上下文
  ├─ build_wrapper_decode_pack.py           双对齐反汇编 + 收敛点 + 回溯对齐
  ├─ render_wrapper_batches.py              按 (组 × caller 可读性) 切同质批
  │     … 舰队判定，rubric = wrapper-census-rubric.md …
  ├─ merge_wrapper_results.py               JSON 分片 → 结果 CSV（含机械 ODD）
  ├─ check_eligibility_results.py           既有：逐字节证据校验
  ├─ apply_wrapper_verification.py          叠加对抗复核结论
  └─ build_rewrite_plan.py                  合并两个几何约束 → 施工清单
        └─ generate_wrapper_pilot_full.py   → wrapper_pilot_full.h
```

**判据修正**：任务书说 B 组「call 后第 5 字节是 1 字节诱饵」，实测只对 49%。
真判据是**续行点**——A/D 返回地址不变从 +5 续行，B/C 是 R+1 从 +6 续行，
双对齐反汇编看哪条是正常代码流。20 个真值样本复现 19 个。

**两个几何约束缺一不可**：`safe_len`（外部落点）**和**相邻命名入口间距。
校验器只查了前者，导致 29 个"合格"入口的补丁会打进隔壁入口。

**补丁只能用 5 字节 `E9` 跳桩**。9-10 字节原地模板实测进频道即崩——
间距检查只看得见**命名**入口，`.-wI` 里还有大量未命名包装器对它不可见。

## 三、`.vmp1` 虚拟机流水线

```
0160_VmEntryTrace 插件                      53 个 vm_entry 克隆下 0xCC 陷阱
  │                                         读 [esp+0x20] 的每函数密钥
  │                                         模拟被覆盖的 call $+5 → 陷阱保持武装
  ├─ 一局会话 → 1,028 个真密钥（93.5% 产出合法 VIP）
  │
  ├─ walk_vmp1.py                           VIP = rol(key,9)+0xBD81ACE6
  │                                         handler = [VIP]+bias，逐步走
  │                                         → vmp1-walk-a21.jsonl（961 函数）
  ├─ build_vmp1_census_pack.py              symeval 折叠混淆 + 符号效果
  │                                         → vmp1-handler-pack-a21.jsonl
  │     … 舰队判定，rubric = vmp1-handler-census-rubric.md …
  ├─ → vmp1-opcodes-a21.csv                 16 种操作，276 个变体
  └─ lift_vmp1.py                           字节码 → 伪代码（原型）
```

**关键：派发偏置是每 handler 自带的**，不是全局常量。用单一偏置扫描只能找到
经由那个偏置可达的 handler——这就是静态枚举失败的根因。走行器从 handler
自己的 trace 里取偏置，才能走对。

`discover_vmp1_handlers.py` 与 `cluster_vmp1_handlers.py` 是那两次失败的尝试，
保留下来是为了让后来者不必重犯。

## 四、活体验证的方法学

**「没崩」不等于安全，除非能证明它被执行过。** 这条教训在本轮出现三次：

1. 既有试点记载「16 个热点入口存活 24 分钟」——实测其中只有 3 个真的执行过；
2. 我据「21 个入口改写后存活」推论 D 组过度定罪——那 21 个只活到登录界面；
3. 全量可达性测量：一次典型会话只触及施工清单的 **17%**。

工具上的解法是 `WitnessThenRewrite` 模式：入口写 `0xCC`，命中时**换成改写并把
EIP 退回入口**，于是**同一次调用先证明执行、紧接着运行改写后的代码**。
`live_validated` 列就是这么来的，它和 `reached_ref_session` 一起标出
「实机验证」与「仅证据支持」的分界。

**归因要靠对照**，不能靠叙述：判断崩溃是不是改写引起的，比的是
「控制流跳进垃圾地址」这一特征在纯见证局（0 次）与改写局（1 次）之间的差异。

## 五、重定位表：跨 boot 消歧

B1 exe 把 dump 那一刻的模块基址烘死，入口桩按 `crossmodule-relocs-a21.csv`
在启动时重定位。**表由跨重启的 dump 对建成**，同一 boot 内 KnownDLL 不动，
所以同 boot 对看不见它们。

组内成员在某一对里共享基址差值时无法区分，工具记为 `same-delta` 猜测——
**那是潜伏的跨 boot 故障**。解法是攒够能把它们分开的 boot，用 `--refine` 追加。
2026-09-06 用三对新组合把 17 行降到 15 行，并**改正了 17 个站点的归属**
（其中 10 个实际指向 ntdll 却被记成 kernel.appcore）。

剩余歧义要判断是否有害：`d3dx9_43 | dbghelp` 在 5 个 boot 里恒隔 0x120000，
**基址差值恒等**，认成哪个都一样——与 KnownDLL 组的性质完全不同。

## 六、工具清单

| 文件 | 用途 |
| --- | --- |
| `build_wrapper_decode_pack.py` | 包装器解码包（双对齐 + 收敛点 + 回溯对齐） |
| `render_wrapper_batches.py` | 包装器批次渲染 |
| `merge_wrapper_results.py` | 分片合并 + 机械 ODD 落盘 |
| `apply_wrapper_verification.py` | 对抗复核结论叠加 |
| `build_rewrite_plan.py` | 施工清单（合并两个几何约束） |
| `generate_wrapper_pilot_full.py` | 全量样本表生成（纯转录） |
| `wrapper-census-rubric.md` | 包装器判定协议 |
| `discover_vmp1_handlers.py` | **失败尝试**：偏置闭包枚举 |
| `cluster_vmp1_handlers.py` | **失败尝试**：形态聚类 |
| `walk_vmp1.py` | VM 字节码走行器 |
| `build_vmp1_census_pack.py` | handler 解码包 |
| `vmp1-handler-census-rubric.md` | handler 判定协议 |
| `lift_vmp1.py` | 去虚拟化提升器原型 |
| `split_wi_reachability.py` | **失败尝试**：`.-wI` 可达性分割 |

数据产物：`wrapper-rewrite-plan.csv`、`vmp1-opcodes-a21.csv`、
`vmp1-functions-a21.csv`、`vmp1-handlers-a21.csv`、
`vmp1-walk-a21.jsonl`、`vmp1-handler-pack-a21.jsonl`。

插件：`0150_WrapperPilot`（见证/改写/跳桩/分批/DryRun）、
`0160_VmEntryTrace`（VM 密钥捕获）、`0001_PageGuardTrace`（页首触，既有）。

## 七、相关文档

- `wrapper-eligibility-census-results.md` —— 包装器普查结果
- `wrapper-rewrite-trampoline-plan.md` —— 跳桩方案与分批验证
- `fleet-wrapper-eligibility-workflow.md` —— 舰队工作流与实机记录
- `a21-vmp1-handler-enumeration.md` —— VM 枚举（含三次静态失败）
- `a21-vmp1-instruction-set.md` —— VM 指令集与翻译实验
- `a21-crt-layer.md` —— CRT 层（结论：A21 的 C 运行时未被变形）
- `differential-crossmodule-fixups.md` —— 重定位表与跨 boot 消歧
