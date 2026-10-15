---
kind: rks-plan
contract: ./TaskContract.md
contract_revision: 1
mode: direct
state: ready
---
# Plan
1. 内容/规则/领域服务及入场路由，收敛活动2215默认常开到现有活动owner；真实PVF入场、失败与冻结等级回归。
2. 按规则补给周期，去掉整批早退而保留每物品持久幂等；牛头票声明6点，默认其他零点。登录读同一配置；统一调度器在线补票，通过命令事务和Event库存投影同步。
3. 经验书内容规则与原子领域用例，复用USE_STACKABLE及角色进度通知。使用测试观察旧行为失败，再覆盖固定/百分比、等级/过期与回滚。
4. 真实PVF独立掉落3本/86无掉落及新书期限，相关无PVF/真PVF回归，Release solution build。
5. 按reviewing-work委派新独立reviewer审查整个diff和部署条件，修复真实遗漏后verification-before-completion。
6. 独立发布产物并检查client进程；按用户条件部署或只保存待部署产物。完成文档/证据并正常本地结果提交，不推送。

## Implementation Notes
生产worker共享工作区写操作串行；允许并行只读取证。主代理保留整体契约、补给边界和条件部署判定。
