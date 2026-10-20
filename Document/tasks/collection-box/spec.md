---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: 常驻收集箱服务端闭环

## Why and Outcome

交付可供用户原生客户端验收的软件实现，不修改实际运行库或部署。依据 ../../CollectionBox-Design-20261019.md 与契约。

## Requirements and Acceptance

| ID | Requirement | Acceptance |
|---|---|---|
| RQ-001 | PVF目录动态提供系列与槽位，稳定boxId+slot保管原物 | AC-001 合成新增系列与当前三套共用实现；-1洞、重复／超限定义拒绝；旧定义移除／变更不覆盖原物 |
| RQ-002 | 永久单箱生效、选择持久化 | AC-002 三箱remaining=0/permanent=1，全部填充后仅选中箱激活；缺省最低有效箱；跨角色无泄漏 |
| RQ-003 | 放入精确一件、取回原物；所有写入原子 | AC-003 拆堆／合堆往返字段一致；错箱、满包、锁、限时、重复格拒绝无写；故障回滚位置／选择／快照 |
| RQ-004 | 原生03E5–03E8与03EB正确，独立0381 | AC-004 选择1B、放入8B、取出5B、延期8B严格长度；ACK符合静态读取；常驻延期拒绝且不扣券；两个0381固定参照帧不读取真实进度 |
| RQ-005 | 当前状态与重放一致 | AC-005 CollectionBox专用replay类别，全箱快照+选中恢复；普通库存不读隐藏物；重复请求不重复转移 |
| RQ-006 | 安全处理旧数据与升级边界 | AC-006 旧itemId行不制造默认物品／不覆盖；新容器仅character owner；说明旧版不能直接读新库及恢复备份代价 |
| RQ-007 | 交付有审核与验证 | AC-007 构建与相关自动回归通过，完整独立审核approved，提供实机测试步骤，明确尚无ClientVerified |

## Success Evidence

领域真实SQLite测试观察资产数量及完整快照、查询和投影测试观察重登与权威帧。编解码向量、handler/effect重放测试观察ACK与拒绝不扣券。扩展现有结构判据名单而不绕过架构要求。真实PVF与无PVF路径分别运行适用检查；原始日志在工作区artifacts/collection-box/。最终审核与新鲜验证分别记录到配置位置。

## Boundaries

物品自身期限不被常驻规则消除，首版拒绝非永久／锁定物品。错误码先按客户端可处理分支映射并标明静态含义依据，不宣称抓包验证。属性仅由原生收集箱事实驱动，不修改角色基础数值。不发布、不部署、不改真实数据库或PVF；永久说明文案以交付说明记录资源更新建议。
