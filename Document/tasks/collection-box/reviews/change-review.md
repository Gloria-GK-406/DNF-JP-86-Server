---
kind: rks-review
verdict: approved
target:
  baseline: bf283b868cef249ca731e5555b64d8d80202a556
  planning_checkpoint: 7ca2d8a7fb77248d954aed98e68df6a14fdaeec4
  implementation_commit: 024acb76ed46cf6b35687ac836721af4b64caef6
  branch: task/collection-box
governing:
  task_contract: ../TaskContract.md
  task_contract_revision: 2
  spec: ../spec.md
scope: 完整混合变更（生产代码、测试、协议记录与交付说明）
criteria:
  - reviewing-work/references/code.md
  - reviewing-work/references/deliverable.md
---

# 常驻收集箱完整变更独立审核

## 结论

固定实现提交 `024acb76ed46cf6b35687ac836721af4b64caef6` 相对任务基线 `bf283b868cef249ca731e5555b64d8d80202a556` 的完整差异，在本次审核范围内没有未解决的阻断缺陷。实现满足 TaskContract revision 2 和 `spec.md` 所定义的软件交付边界；生产代码、协议记录与交付说明相互一致，证据等级保持为 `CurrentImplementation + ClientStatic`，未把自动测试或静态逆向写成 `WireCaptured`／`ClientVerified`。

本结论批准该固定实现进入独立完成验证。它不代替完成验证，也不证明尚未执行的原生客户端验收。

## 决定性证据

| 契约边界 | 审核观察 |
|---|---|
| PVF动态系列与稳定槽位 | `CollectionBoxRuleService`统一读取并校验系列、坐标、重复物品、洞和容量；领域层以`boxId*256+slotIndex`访问角色专属241容器。合成系列、真实三系列、冲突定义与旧行场景均有针对性测试。 |
| 原物保管与事务 | 放入使用现有`TakeAsync`所得实例，取出以`Instance`交给`InventoryOnly`投递，未按请求itemId制造物品。选择表与物品存储加入同一命令事务；注入选择写失败的测试核对物品、位置和选择一并回滚。 |
| 永久单箱与角色隔离 | 延期在任何票券访问前拒绝；03EB编码`remaining=0/permanent=1`。选择按角色持久化，缺省最低有效箱，投影先填充全部箱再仅激活持久选择；持久层拒绝账号所有的241容器。 |
| 03E5–03E8、03EB与0381 | 解码器严格执行1/8/5/8字节及保留字节；ACK和拒绝由协议层编码；03EB为`8+4n`完整快照，0381仍为两条固定兼容帧。选角、当前状态与重放均接入CollectionBox投影。 |
| 普通库存隔离与旧数据 | 入场库存查询显式限制0/1/2/3/7，不暴露241。旧`character_collectbox_slots`仅产生诊断并阻止覆盖；定义删除、改义或异常实例会保留原物且不投影，不猜测或补造实例。 |
| 交付文档与证据边界 | `CollectionBox-Implementation-20261019.md`准确描述当前行为、同库回滚限制、实机步骤和错误码的不确定性；六份协议目录记录与实现一致，并明确A12历史证据不能升级为A21实帧。 |

审核还检查了 `7ca2d8a7..024acb76` 的44文件完整差异；`git diff --check` 无输出。归档日志显示：解决方案构建为0警告0错误，Application真实PVF 4154通过、Protocol真实PVF 1624通过、Persistence 475通过；无PVF路径分别为Application 3632通过/433跳过、Protocol 1559通过/60跳过，缺失PVF档案检查2通过，最终聚焦收集箱检查13通过/4跳过。历史红灯日志保留了缺陷发现过程，不作为当前通过证据；控制器仍需对固定实现执行并记录最终完成验证。

## 审核中发现并已复核的修正

1. **连接身份与帧顺序**：早期成功路径分开发送库存／箱状态和ACK，按可复用连接ID重新寻址会把ACK送到替换连接。当前成功路径把“库存绝对变化 → 03EB → ACK”绑定为同一物理连接的一次`SendBatchAsync`，拒绝也绑定原连接；替换连接的成功和拒绝测试均直接覆盖该风险。
2. **领域与线码边界**：领域不再返回原生错误字节，而是返回`CollectionBoxRejection`；03E5–03E8的错误码只在协议slice映射。源槽缺物／ID不符映射12，非系列物品映射15，交付说明和handler理论测试一致。
3. **选择重放最小投影**：选择请求只要求`CollectionBox`，且结果没有库存变化时不记录库存槽。丢失投递后的选择重放测试断言只恢复三条03EB，不夹带库存帧或旧ACK。
4. **03EB证据表述**：A12运行时记录已明确标为历史边界；A21当前布局基于ClientStatic，0381旧载荷迁移撤回只针对错误搬运，不否定原生03EB状态消息。

上述修正均已进入固定实现提交，相关代码、测试与文档的直接回归已复核。没有遗留需要实现者处理的审核发现。

## 非阻断残余风险

- 03E5–03E8拒绝提示、03EB严格恢复时序、单箱属性实际生效和常驻显示仍需按交付说明在原生A21客户端完成抓包与可见行为验收。
- 旧v0.2.6二进制不能直接读取含241容器的新库。交付说明已要求程序与数据库成套备份，并明确恢复旧库会丢失备份后的进度；本任务未部署或修改真实数据库。
- 新PVF系列只有在客户端具备匹配资源且稳定ID／槽语义未被破坏时才安全扩展；文档未宣称任意资源改动自动兼容。
