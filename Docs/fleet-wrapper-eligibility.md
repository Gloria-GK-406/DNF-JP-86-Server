# 舰队任务规范：包装器改写资格普查（每单元协议）

日期：2026-08-31。输入：`ClientHack/tools/wrapper-eligibility-worklist.csv`（1,654 单元，
每行自带全部所需字节上下文，无需读 dump）。产出：`wrapper-eligibility-results.csv`，
由 `tools/check_eligibility_results.py` 机器校验后合并进阶段 2 施工清单。

## 背景（一段话）

`.-wI` 的 1,654 个命名包装器各有 symeval 测得的"约定"（CSV 的 convention/group 列）。
阶段 2 要把"转移型"包装器入口原地改写成两条指令跳真 API，但 C 组教训证明约定可能是
**测量伪影**（求值器跟 `ret` 走进调用方指令流，把"寄存器装载桩"误测成转移型——实测改写必崩）。
本任务用**调用方字节形状的双重绑定**逐个验证约定：约定若真，调用点必须长成特定样子；
形状对不上即为 MISMATCH（黄金产出——那是又一个待澄清的伪影）。

## 每单元三步

输入行的关键列：`entry_va, call_site_va, group, convention, source_section,
caller_before_hex`（call 前 16 字节）`, caller_after_hex`（call 起 32 字节，E8 在偏移 0）`,
wrapper_head_hex`（包装器入口 16 字节）。

**第 1 步：反汇编调用点窗口**。`caller_after_hex[0:5]` 应为 `e8 xx xx xx xx`（call
rel32）。从 `caller_before_hex`（反向找指令边界）与 `caller_after_hex[5:]` 解码上下文。

**第 2 步：按 group 核对预测形状**：

| group | 约定语义 | 调用点必须满足 | 判 VERIFIED-TRANSFER 的条件 |
| --- | --- | --- | --- |
| B (esp+0/R+1) | API 入口 esp 原位、返回地址+1 | `call` 后第 5 字节（caller_after 偏移 5）是 **1 字节诱饵**（常见 `c3`，也可能是 `90`/其他单字节指令），真正续行在 +6 | 诱饵字节存在，且 +6 起可解码为正常指令开头（push/mov/lea/cmp…） |
| A (esp+4/R) | API 入口 esp=+4、R 覆写被吞参数槽 | `call` **前**紧邻一条 push（50/51/52/53/55/56/57 或 6A xx 或 68 xx xx xx xx），压入的 dword 比该 API 首参多一个（被吞） | 前邻 push 存在，且语义上是"多余的首参"（结合 module!function 的已知签名判断） |
| C (esp+4/a1) | 伪影高危：寄存器装载桩 | `call` 后第 5 字节 1 字节诱饵，随后是**参数压栈串**，再后是 `ff dx` 类 `call <寄存器>`（间接调用） | 判 **REGISTER-LOADER**（不是 VERIFIED） |
| D (esp+8/a2) | API 入口 esp=+8、a2 当返回地址 | `call` 前紧邻**两条** push（两个被吞 dword） | 两条前邻 push 存在 |
| 空（odd） | 非四组标准约定 | 无预测 | 一律判 **ODD**（不猜；留给人工） |

**第 3 步：包装器头部签名（辅助判据，对 C 类特别重要）**。`wrapper_head_hex` 里若出现
"早读返回地址"模式——前 1-4 条 push（5x/9c）之后紧跟 `8b 44/45/4c… 24 XX`
（mov r32,[esp+N]，N=已 push 数×4）——这是寄存器装载桩的指纹（它要拿 R 去算 R+1）。
A/B/D 的真转移包装器头部也可能读栈，但通常读的是参数而非恰好在返回地址槽。
头部签名与调用点形状冲突时，**以调用点形状为准**并升级为 MISMATCH。

## 输出格式（CSV，每输入行一条）

```
entry_va,verdict,evidence_hex,evidence_note
```

- `verdict` ∈ {`VERIFIED-TRANSFER`, `REGISTER-LOADER`, `MISMATCH`, `ODD`}
- `evidence_hex`：**必须逐字节引用输入行里已有的十六进制串子串**（空格分隔、与输入
  完全一致，如 `e8 03 71 49 04 c3` 或 `50 51 55`）。校验器会拿它对输入行做子串匹配，
  引用不存在即整行作废——这是防幻觉机制，**不许凭记忆写字节**。
- `evidence_note`：一句话，说明引用的字节如何支撑判定（如 "+5 处 c3 诱饵，+6 起
  8b 55 fc 为正常续行" 或 "call esi 出现在压参后，装载桩确认"）。

## 范围规则

- `source_section` 不是 `.text`/`.tls` 的行（.vmp1/.mackt/.#|U/.bss，共约 240 行）：
  调用点窗口是字节码/桩表，**调用点形状检查不适用**——只做第 3 步头部签名，verdict
  只能写 `VERIFIED-TRANSFER`(头部无装载指纹) / `REGISTER-LOADER`(有) / `ODD`，
  note 里注明 "caller-unreadable"。
- 同一 API 名会出现多行（每调用点一个专属包装器副本），**逐行独立判定**。

## 校准批次（先跑这个）

`tools/wrapper-eligibility-calibration.csv` 含 30 个已知答案的单元：16 个实机验证过的
A/B/D 样本（真值 VERIFIED-TRANSFER）+ 4 个实测必崩的 C 样本（真值 REGISTER-LOADER）
+ 10 个盲样。校准批次得分 ≥ 90%（且四类均有覆盖）才放开全量。校准命令：

```
python ClientHack/tools/check_eligibility_results.py \
  --worklist ClientHack/tools/wrapper-eligibility-worklist.csv \
  --results <舰队输出.csv> --calibration
```

## 校验与合并

```
python ClientHack/tools/check_eligibility_results.py \
  --worklist ClientHack/tools/wrapper-eligibility-worklist.csv \
  --results <舰队输出.csv> [--calibration]
```

校验器做的事：verdict 合法性；`evidence_hex` 逐字节子串匹配；行覆盖完整率；
校准模式下的命中率与按组混淆矩阵。**只有 evidence 匹配的行会进最终
`wrapper-rewrite-eligibility.csv`（阶段 2 施工清单：entry, group, patch_len ≤
safe_len, eligible）。**

## 边界与纪律

- 只用输入行给的字节做判断；不确定就写 ODD/MISMATCH，不要猜。
- 不要改任何仓库文件；输出只写结果 CSV。
- 单元之间无依赖、无顺序，任意并行。
