# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已授权先实现商城实体商品购买，明确排除契约等权益服务。当前实帧、PVF商品映射和现有实例/事务模型足以定义成功购买、完整回滚及客户端同步的可观察边界；购买后的自动使用仍为独立观察，不影响本次实现。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户按实机逐步推进；这里若直接沿用NPC购买或参照服的部分成功处理，会在多个物品容器与账号余额间产生扣款成功却发货不完整的结果。一个Spec覆盖整次实体购买及明确的权益拒绝边界，完整结果审查与真实SQLite故障验证可防止漏掉此要求，且比事后修复角色资产更便宜。商品类别共享一个购买验收和回滚边界，不拆成独立Spec。
- Reroute conditions: 若PVF与客户端证据否定实体商品发货边界，或用户要求实现权益/赠送等独立结果，重新对齐范围；普通字段和发货差异在本Spec内解决。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `sdd-standard/assets/profiles/code-feature.yaml`
- Rationale: 显式Plan先统一目录/实体授予结果、支付计划、容器细节写入及ACK/NOTI时序，避免不同商品先行实现后才发现共用契约不兼容；这项早期依赖决策是Lite完成末尾审查不能便宜替代的控制。使用隔离测试数据和可恢复工作树，不涉及真实充值、外部金融或不可逆资产迁移；Standard审查与新鲜验证足够，不需要Governed。
