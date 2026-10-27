---
kind: rks-review
target: spec.md
target_contract_revision: 1
round: 2
target_revision: B1
verdict: approved
---

# spec-002 规格独立评审（Round1，2026-10-24）

独立b_spec_review（非producer/非generator），reviewing-work Spec准则。目标首ready154行、Contractr1、baseline10a7218a；权威子契约/intakeapproved/rootr4/父RQ020～025/RQ070～072/A已接受结果与清单；匹配reviews/spec-challenges.md。只读，未改文件/跑测试/选方案。

approved，足以进入Plan，无实质阻断；不是Plan/实施/核验/根完成/Astra批准。

| 核对 | 依据 |
|---|---|
| 核心 | RQ001～003覆盖注册唯一映射、薄handler、内部来源/归并；不补未注册枚举、不靠改名/转发 |
| 显式上下文 | RQ004～006三协议状态+明确内部信任，失效/空帧/singleton改scoped不放行，AC002连续请求和singleton不串用 |
| 事务保护 | RQ006～008与职责表保主结算/重放/逐人围栏/真回滚/只读/提交点；204不误等204事务 |
| 注入分层 | RQ009禁发送/队列/执行器/接缝/service locator；当前CMD单元机制已具名披露，接口理由留Plan，不任意豁免 |
| 先保护 | F88/F93四项先测；F132条件；首单元F130/F127不扩所有测试进程或改失败结局 |
| 清零回归 | T1～6/B临时项有标准；常设能力按职责保留，A不变量/表征/线级/Golden/取证保持 |
| 测量交付 | 六SC映射、同工具/2秒/增减/文档/外审齐，合成不当实机、无推送/PR/收尾授权 |

保持WHAT/验收层，不要求Spec提前接口/204明细/远期Task。

## 挑战裁定

CH-1 confirmed，非阻断文字歧义。RQ008/F132行限定采用事务内组装先修，未采用记禁用/不可达不称已修；AC004覆盖RQ008按此条件解读，AC005不能独立扩成强制启用/修未采用机制。但这不豁免其他既有提交后业务失败的围栏/已提交写保护/不重结算。可在AC004/005组装部分补“按RQ008条件”，仅文字澄清，不改变批准边界，无需强制改事务机制才进Plan。

CH-2 confirmed，Plan控制的低风险非Spec缺陷。拆分排队会跨周期/退出/替换、影响NOTI响应次序；RQ002/F07/AC008/变化表已要求保结果，不授权只按人数拆。Plan决定入队/执行、周期/接收者输入、当前性与水位，并安排跨周期/替换区分验证；不能保次序须实施前修订列变化，不能最终汇报追认。Spec不锁算法/接口。

## 后续输入

Plan消费两裁定、具名登记/关闭/投影/单元机制接口与理由，遵守分层/不隐藏依赖。A不可重开。后续独立Plan/结果/核验门保持。

## 主控处置（2026-10-24）

采纳当前approved解释，委派原producer仅给AC004/005组装路径补RQ008条件，并明确其他既有提交后业务失败义务不豁免；154行、metadata/RQ/其他AC均未改。这是落实本轮已批准含义的非材料文字澄清，不扩大或缩小验收，不改契约；CH记录保留不改，Plan消费两项裁定。

## 主控受控修订处置B1（2026-10-24，Round2评审前）

Plan对CH2的细化确认：按唯一FIFO把整批拆成子CMD时，已有排队请求可能先于刷新NOTI；不在CMD等子CMD、不重做插队/预留，不能同时声称保持原整批不交错。主控选既有F07拆分目标，将有限次序变化明列VC-B3，周期/精确session/重新读取当前事实/登记水位保护保持。依据根允许Spec逐条列有意变化、当前Spec的受控修订条款及用户执行中裁定授权；不改根Goal/Scope/六SC或中心协议CMD路线。

委派当前Plan producer先暂停Plan（尚无文件），仅改F07责任/AC008/VC表及B1记录。旧Round1批准只适用于B1前目标，对当前修订暂不充当闸；需Round2独立approved后才能继续Plan。风险基础仍是既有CH2，其挑战集保持不可变，不为同一风险另造挑战。A不重开，产品未动。

## Spec Round2：B1有界复核（2026-10-24）

```yaml
kind: rks-review
target: spec.md
target_contract_revision: 1
target_revision: B1
round: 2
verdict: approved
```

独立b_spec_review，非producer/generator，reviewing-work Spec准则；目标163行B1、Contractr1、baseline10a7218a。检查F07/AC008/VCB3/B1及CH1澄清，未变沿R1；重读根r4/子契约/immutable CH，无修改/测试。

approved，B1可进Plan，未发现需先改TaskContract的结果边界变化。依据不是主控自称允许：根Goal允许经审Spec逐条有意变化，SC8B明确B Spec列明变化除外，子Goal沿用；F07本来就负责拆全会话长作业，没有引入业务能力/重做执行器。

根顺序三项仍成立：世界/写/通知均唯一执行器，不等网络、不入队等子CMD。B1不授权插队/预留/producer操作世界。只改变原整批刷新与已排队请求相对先后，VCB3明确可能请求先响应；不扩成单元内交错、单连接登记失序或任意跨连接变化。周期/精确session/当前事实/水位保持，旧周期/session不登记不推进，新周期后扫覆盖。符合根允许有限可见变化，不需升级根/子contract。

- CH1 resolved：AC004/005已按RQ008条件，其他既有提交后围栏/不重复结算不豁免；未采用不强制启用/修复，也不称已修。
- CH2 confirmed：可见次序已由VCB3处置，具体风险Plan管；不再同时承诺拆分与整批不交错。Plan需安排请求插入的FIFO、跨周期旧工作不登记/推进、退出替换不刷新替代者、后扫补新周期及防重复的区分验证。Spec不用提前定批量算法。

非阻断AC011仍只写VCB1/B2，须按RQ017/VC表/B1/SC11B理解包含B3；可补B3作文字对齐，不是新义务。本批准只释放B1 Spec闸，Plan/实现/核验/Astra尚未过，A保持封存。

主控接受Round2批准，委派Plan producer先顺手对齐AC011的B3文字再继续Plan；该对齐不改变当前批准含义，不另造审查循环。
