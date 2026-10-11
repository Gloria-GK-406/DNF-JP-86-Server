# 实施记录

基线 67ffa14f82e8e789e53e7a22fe1af1b85627d57e；工作分支 task/dungeon-admission-permits。

- 新增强类型 Admission 凭证、材料/次数费用、一次性收费收据与三阶段编排。
- 普通/循环经 Visit，主动深渊/镜像/异界经各玩法服务接入同一 Admission；目的地和布局仍由玩法选择。
- 普通材料与深渊候选票合并规划，保留账号材料及只持有要求。
- 次数在收费阶段通过 DirectAsync 更新；资格/消费复核不扣疲劳，出生房/新房间沿用一次扣除。
- 领域事务新增 rollback-only，确保已收费后返回正常拒绝也整批回滚；管线不发布该次成功效果。
- Document/DungeonAdmission.md 记载职责变化和边界。

自检：收费先于创建判据先 RED（旧实现创建时仍持有15，期望3），重构后 GREEN；首轮入场集84通过/12缺PVF跳过；凭证/事务边界集50通过/5缺PVF跳过；完整解决方案构建0警告0错误。正在以实际服务端PVF跑完整回归，结果在 artifacts/admission-verification/full。

未部署、未更新客户端/PVF/实际数据库、未实现新玩法；客户端行为兼容由既有协议判据守护，未声称本次已实机验收。

独立评审整改：恢复深渊票/不同ID普通材料错误类别；已有普通运行实例允许再挑战替换；镜像布局完成后再次核对精确会话。对应回归133通过/11缺PVF跳过，独立报告approved。最终真实PVF完整回归以verification/completion-evidence.md为准。
