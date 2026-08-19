# Routing Decision

- TaskContract: `.rks-agent/tasks/inventory-container-refactor/attempts/spec-002-item-use-rules-and-effects/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果边界由根契约 revision 3 的 SC-6..SC-9 固定。三个设计问题
  已于 2026-08-18 由用户逐条定下：客户端确有提示能力（码表已探明并归档）；状态变更放在
  效果侧；不消耗表现为「不执行减数量那个效果」，而不是在减数量处加判断。
- Requirement stability: 判定的四类拒绝分支（冷却、等级、职业、场所）在归档中逐条有据，
  计数已实测；冷却键的定义（角色 ×（`[cooltime group]` 或模板 id））已固定；
  失败原因到客户端码的选择依据已归档为一张可逐条对照的表。
- Execution direction: 命令总线的形状不需要发明——同一代码库里已有七个 Game 切片走
  `ICommandBus` 并从类型化结果读失败原因（`DeleteCharacterCommand` →
  `CharacterDeletionResult`），本次是把仅剩的两个例外接进同一条路。
  容器接缝已由 spec-001 建成，效果的落点是容器的 `ConsumeAsync`。
- Remaining uncertainty: 效果集合的类型划分、判定服务的文件归属等局部选择，不改变成果语义。

## Marginal Control Value

- User precision signal: 用户要求「值得我们用sdd来跑。当然只是为了实现别漂移」，
  且在本成果上明确指定了三处设计（效果侧、不消耗即无该效果、命令承载失败语义），
  即此处要的是把这些指定钉住，而非流程完备。
- Direct failure mode: 本成果同时改判定、改效果、改冷却、改错误码，四者都落在同两个
  处理器上。Direct 最可信的失败是把它们揉回处理器里——判定与效果在一处、
  `unlimited` 写成减数量前的一个 `if`、失败码继续内联挑选。那样每一条验收都能测过，
  而用户明确指定的分离一条也没实现，且下一个道具协议接进来时又要重来一遍。
  第二个失败是错误码「看起来更好」：随手换成一个更常见的码而不去对照客户端码表，
  结果仍然是一句不相干的文案，且没有任何东西会因此变红。
- Useful control: Spec 把「判定只读且不暴露事实来源」「不消耗＝效果集合中没有减数量」
  「拒绝原因不得借用不符文案的码」写成可评审的验收条件，完成后由独立评审对照契约检查。
- Time-for-quality rationale: 这两个处理器是后续所有道具协议（`0x001E`、`0x0132`、
  `0x0198`、装备穿戴）要复用的形状。参考实现在同一位置是一条按 itemId 特判的链，
  六个分支各自改服务端状态，没有一处可复用。一次流程成本换掉的是这条已被验证过的路径。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯的接受边界——判定、效果、冷却、失败语义共同且只共同支撑
  「道具使用有了规则」这一个成果。任何一半单独交付都不成立：只有判定没有效果，
  `unlimited*` 仍被销毁；只有效果没有判定，冷却与场所无处安放；
  失败语义若不同时改，判定新增的拒绝分支只能继续复用那句「金库空间不足」。
- Why Direct is insufficient: 见上述两个 Direct 失败模式，二者都不会让测试变红。
- Why Multi is unnecessary: 四部分共享同一个回滚边界，且没有哪一部分单独交付后
  值得保留而其余回滚。
- Reroute conditions: 若实机证实客户端对 `unlimited*` 道具仍做本地自减，则 AC-2 的实机
  部分需要一条把真实数量送回客户端的推送路径——那属于多容器切换那一刀的投影工作，
  超出本子任务边界，应停在安全点并上报根控制器重新评估。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制有两个——把用户指定的三处分离与「诚实的拒绝」
  固定成耐久的验收面，以及在实施完成后用一次完整结果评审对照契约检查是否被揉回原处。
  Lite 提供这两者再加一次新鲜验证。中心执行方向已勘定（命令总线形状照搬既有七个切片、
  容器接缝已建成、码表已归档），实施中的局部选择可安全内联决定。
- Why lighter is insufficient: Direct 无法阻止「四件事揉回处理器」这一失败——它会让每条
  验收都测过，而用户明确指定的分离一条也没实现。这类偏离靠一次廉价的确定性检查发现不了。
- Why heavier is unnecessary: Standard 的耐久 Plan 在此不增加保护——判定的四类分支、
  冷却键的定义、效果的目标域划分、失败原因到码的对照，全部已在子契约与已归档的探针中固定，
  Plan 只会把同样的内容抄一遍。不存在需要提前排序的耦合决策，也不涉及 Governed 的任何
  硬性门槛（无安全、凭据、隐私、资金合规、不可逆外部变更或无恢复路径的持久数据丢失）。
- Verified by `single-spec-workflow`: 该节由根控制器预填，此处独立对照
  `skeleton-selection.md` 复核后维持不变。Standard 的判据是「若干耦合的实现决策，
  其中一个早期选错会在最终评审能廉价纠正之前扩散」。本成果确有一处早期耦合选择——
  移动的拒绝原因与使用的拒绝原因是同一套词汇还是两套（`0x0013` 不经可用性判定，
  移动不是使用）——但选错的代价是一次机械的类型拆分或合并，评审能看见也能廉价纠正，
  不属于会扩散的那类。Governed 的硬性门槛一条都不触及。故维持 Lite。
  Profile 取 `code-feature` 而非 `code-refactor`：本成果要求的正是行为变更。
