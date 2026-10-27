# Spec B3／Plan P4 主控消费与修订检查点

日期：2026-10-25。产品父检查点：`dba3ede68e15f892afd35534db9fed7ed00d04a8`（TASK-19 r2／R1）。同worktree／branch，根／子Contract与A封存不变；本记录只负责workflow消费，不是产品完成或Spec封存。

## 精确门与身份

| 制品（相对本子B目录） | SHA-256 |
|---|---|
| `spec.md` B3 | `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05` |
| `verification/spec-b3-amendment.md` | `8cdcbb991a4ccbe202a965bcac8433e6ee816804a8ba9a3b4519c007c2cb89b2` |
| `reviews/spec-b3-challenges.md` | `d61c61fa5f8aad20a3518b45619878b38927027a9e280d3da881f5750cf80fac` |
| `reviews/spec-b3-review.md` | `42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52` |
| `plan.md` P4 | `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7` |
| `verification/plan-p4-amendment.md` | `a3683f7449fc4bf9414c06f30d2447c4731bcaa64aea4a7c8830f199d70101bf` |
| `reviews/plan-p4-challenges.md` | `7b23d10a1cd1eee526449351fb89e281645793b752b0de4423874d8a1c98644d` |
| `reviews/plan-p4-review.md` | `82611a76c0d4ee0dd264357dbd4c8d2c164050b919bc14e593cdac8ee01bbbbb` |
| `verification/evidence/spec-b3-amendment/spec-b2-before.md` | `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228` |
| `verification/evidence/plan-p4-amendment/plan-p3-before.md` | `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` |

主控完整读取两个当前目标及actual diff、两amendment、两个fresh挑战记录、两份fresh独立审核。挑战均0high／0low，不把数量当approval；Spec B3与Plan P4各由另一独立reviewer作approved，无未决blocking finding或remediation。原B2/P3快照逐byte等产品父检查点，前版历史未被改写。技术白名单、普通Task详细实现及新运行证据仍由门后单元承担。

## 所接受与未完成的边界

只保CollectionBox原Main完成后的初次成功一次exact physical batch之实际encoder故障，须注册许可＋真实来源＋request/job/phase/call/target单次绑定，全部drain／unit／scope cleanup结束后仅同一原cause无typed或独立额外错，才raw／Committed／无新增fence／原sourceclose／current replay。拒绝、业务Full／clock、current／lookup／guard／Register、其它协议、一般IOException仍默认保护。没有新增VC、ambient／DIcurrent／jobslot／凭据框架或A重新设计。

P4最小方法选择通用SettledInitialBatch、穷尽阶段判定、既有opaque原对象透传、batch仅initial／replay与required单独固定矩阵。CollectionBox原Main／mandatory footprint、四namedcatch、ordinary SQL外抛、逐条clock／Full及首帧序与minimum保持；旧两个许可、P3commit-only与来源guard／RequireRollback保因／sticky、Compose禁用及F-132边界不变。

已接受独立review的有意后置：foreign attempt目前仅标owner时，新caller catch后能否继续为未演示风险，必须在同provider真实新request先probe actualwrite／return，再由实际结果保区分证据或做最小显式修正；不允许带缺口交付或借此加暗道。成功Withdraw现有动态注错缺口仍披露，任何新动态覆盖结论须实跑。原Select／Put旧tests绕decoder、只有类型与后续replay间证、Discard不真关socket；下一单元先真正旧decoder fixedcause的直接entry／Ordinal／nofence／未登记／sourceclose／current oracle，再新whole对应实跑，不能补造前时点或提前记pass。新增来源行为mutation、共享core后完整八full／DFO74＋七control／Golden15／Char10与数量身份闭合仍未执行。

当前已接受库存8828＝8808pass＋2准确原Dimfail＋18skip；最新3full7028、另1800仅Task15库存，不是本次文档修订新八full。TASK-01～19、根／子Contract、A及旧证据不变。

## 主控动作与下一阶段

主控已核10个目标／快照／gate制品SHA和精确物理集合，Server／Tool／AGENTS无本轮差异，index空、父HEAD精确；更新routing仅当前四gate路径及本次消费说明，其余授权与auto gate不变。amendment作者manual_required自查标签仅交付核对，不是用户HITL。将这10制品＋routing＋本记录共12路径作amendment-only本地检查点，带Co-Authored-By；不push／PR／merge／tag／版本／远端issue／部署／cleanup。

检查点完成后才可指定TASK-20并作详细规划／readiness：下一Useful Unit为一个logical66533／四decoder的CollectionBox whole消费者与最小shared phase／adapter，而非孤立enum空壳。Spec实施其余未完成122message／175fine／72source／5保护／8过渡及最终T-012／Astra／封存仍继续，不以本修订宣告完成。
