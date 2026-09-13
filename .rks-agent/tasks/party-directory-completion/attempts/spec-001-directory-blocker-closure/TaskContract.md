---
kind: rks-task-contract
revision: 1
state: ready
parent_controller: ../../multi-spec-map.md
---

# TaskContract: 固定 A21 公共目录阻塞证据闭合

## Context

### Source Request

根任务要求先解决公共目录全部已知阻塞，再实现或报告。现有综合静态报告已证明 `0x023C` parser、manager、
`0x0009` 联表和原生窗口消费者，但仍把 `lis_matching_info` writer、若干 required record 值域以及
`0x0183` 的必要性留作 frontier。用户已授权并行只读取证，禁止以 parser 可接受的零值猜测完成。

### Inputs

- `E:/Project/DNF/Document/A21-party-directory-and-quick-match-static-contract.md`
- 三份并行 follow-up：matching-info、wire source/value domain、server integration audit。
- 当前固定 `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe`、资源与既有 protocol 记录。
- 接受基线 `9a3a0b492da11f48525d1225d02513e0dd1cd458`。

## Goal

把所有会阻止服务端安全生成目录或导致原生列表必然不可见的未知项，裁决为“已证明可实施”“已证明非必要”
或“必须运行时取证且已有最小可执行 probe”；禁止把 unknown 字段改名为推测语义。产出足以决定是否进入
服务端实现的明确 verdict。

## Scope

### In

- `matching-info` 的 writer、输入、正常生命周期、窗口/地点适用范围与目录刷新关系。
- `0x023C` mode 0/1 required 字段的实际消费者、最小合法值域和可安全生成集合；mode 2 删除键。
- `0x0183` 是否是目录首帧/刷新必要请求，或能否由服务端主动 mode-0 快照完全排除。
- `0x0009` 与 `0x023C` 的 producer/consumer 联表顺序和 UI 拉取时间线。
- 固定当前 B1 的哈希、地址/字节锚点、反汇编调用链、资源哈希与可复现验证脚本。
- 若纯静态证据不足，写出 exact-session 最小 runtime probe、正负结果裁决与停止条件。

### Out

- 服务端生产代码、测试与协议权威 catalog 变更。
- 客户端补丁、逻辑绕过、二进制编辑或 UI 自动化。
- 快速组队、Story/Cycle、PVP 或地下城进入状态机实现。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | `matching-info` 的正常 writer/状态来源与空值语义被定位，并能判断其是否是公共目录本身的硬前置。 | 固定 B1 调用链、关键字段/分支和至少一个 producer 或返回值来源；冲突解释。 |
| SC-2 | 每个 mode-0/1 record 字段都有 wire shape、consumer、最低安全值裁决；业务语义未知时仍能证明某个值不会破坏过滤/联表/渲染。 | parser + manager + consumer 全路径；安全集合与禁止集合分开记录。 |
| SC-3 | `0x0183` 被证明必要、可选或无关；目录初始快照与刷新触发形成一个不依赖猜测的实施契约。 | sender/ack/窗口路径或覆盖所有目录动作的负证据；服务端 push 路径说明。 |
| SC-4 | 证据在当前客户端上可机械复现，且明确列出纯静态仍不能证明的运行时事实。 | hash-bound probe 全通过；runtime probe 包含动作、断点/日志、预期、正负裁决和停止条件。 |
| SC-5 | 独立审查确认 verdict 不把假设升级为事实，能够安全决定进入实现或终止。 | review verdict 无 P1/P2；fresh verification。 |

## Constraints

- 所有 VA/RVA 均绑定当前 B1 哈希与 `ImageBase=0x00400000`；旧构建地址必须逐字节复核。
- “全零可解析”本身不是合法业务值证据；只有 consumer 证明零被忽略/接受时才可进入安全集合。
- 不允许用客户端 patch 绕过 `matching-info` 门；正常协议/游戏状态必须可达。
- 不修改 `E:/Project/DNF/DNFClient-ClientHackLab-20260812`。
- 本 Spec 只能写自身任务/证据文件及指定 `Document` follow-up；不得触碰产品代码。

## Authorization

用户已明确授权继续、不能继续则派其他 agent 深入收集，并要求解决所有阻塞。允许只读反汇编、哈希、
已有日志/抓包分析、写证据文档与复现脚本；不包含运行客户端所需的用户 UI 动作或外部写入。

## Requires Confirmation

若完成静态取证后唯一剩余阻塞必须由当前客户端游戏内动作触发，向用户提出一次精确动作；等待用户时不把
纯等待作为 todo，也不伪造 completion。
