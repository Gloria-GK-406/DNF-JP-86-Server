# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 边界由根控制器给定，并已对着代码收窄——原以为要「把容器私有
  化」，实测四个容器的字典本来就是私有字段，所以要做的是名字与类别，加上拆掉 spec-001 留下的
  那层透传壳。
- Requirement stability: 要求可逐条枚举：四个接缝、一个透传壳的三个成员、两个命令处理器、
  一个 `OnlineStateStore` 的改名，全部已定位。
- Execution direction: 已知。目标形状与 spec-001 相同，判据也已经立着——改完只要它仍然通过、
  且没有靠放宽规则通过。
- Remaining uncertainty: 只剩新名字的具体选词，不改变含义。

## Marginal Control Value

- User precision signal: 偏精度，且要求跑完再汇报——执行期间无人工检查点。
- Direct failure mode: 两个。其一，把「让判据通过」当成目的，于是再加一层壳或往豁免清单里塞
  东西——spec-001 已经因为这个留下一处负担，重犯一次就说明这套判据只是在被绕过。其二，改名
  误伤 `IExpertStoreRegistry` 的摆摊语义；这类错误改完仍然编译、仍然全绿，只是名字从此说谎。
- Useful control: 完整变更评审，以及一条专门的成功条件（C-3）要求判据不是靠放宽规则或扩充
  豁免而通过——它把「怎么通过的」也变成要核对的东西，而不只是「通过了没有」。
- Time-for-quality rationale: 值得，但只值一个 Spec 的量。收益是把 spec-001 留下的负担真正
  结掉而不是记着。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个验收口径——行为逐值不变，且这几个接缝的名字与类别和它们做的事一致。
  四个改名与那层壳的拆除共享同一个口径，且拆壳依赖改名（`ISelectedSessionDirectory` 得先是
  领域服务，命令处理器才能直接向它提问）。
- Why Direct is insufficient: Direct 给不出 C-3 那种「不只看通过、还看怎么通过」的核对。
- Reroute conditions: 若 `ISelectedSessionDirectory` 改成领域服务后发现它的动词无法在不改变
  调用方语义的前提下重述，说明它不是「名字不对」而是「形状不对」，那就属于下一个成果的聚合
  拆解范围，应退回根控制器重划而不是在本成果内扩张。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-refactor`
- Why this is sufficient: 没有需要在实现之前裁决的耦合决策。目标形状、判定规则、判据都已经由
  spec-001 立好，本成果只是把四个接缝搬到规则的正确一侧；实现局部的选择只剩新名字的选词。
  Lite 的 Spec 固定「什么必须为真」，完整变更评审加新鲜验证守住「行为没变、判据不是被绕过」。
- Why lighter is insufficient: Direct 不产出可核对的枚举，而本成果的失败模式（多加一层壳、
  改名误伤语义）都是判据看不见的。
- Why heavier is unnecessary: Standard 的 `plan.md` 是为「先定裁决再动手，否则返工」买单的。
  这里没有那种裁决——四个改名彼此独立，拆壳的顺序依赖只有一条且显而易见。为一条显而易见的
  次序写一份计划，买不到东西。
