# 普通黑钻售货机实现与部署

2026-09-29，分支task/black-diamond。用户授权“全部由你来处理…完成后再和我说”。范围：已捕获普通机1/group1的扣币、抽奖、发奖、响应闭环，不扩张到未采样的高级/活动售货机。

## 证据补全

两帧C2S 00D7 body=01000000010000008200。请求布局10B与70一致，opcode不同。A21响应逐字段及物品列表证据见a21-response.asm.txt；70导入器见tw70-import.asm.txt：VA0890B546..B552累加原始权重并写行阈值，抽取函数首阈值>ticket命中。四列末项==1另收集公告候选（0890B4D9..B527）；公告推送不属于本轮核心交付，原始标志保留。

## 架构和事务

IVendingMachineDefinitionSource为普通机PVF全量启动缓存；IVendingMachineDomainService负责会话、城镇、黑钻资格、材料、抽取及事务。复用IPackageOpeningRuleService准备奖励及IOpeningInventoryDomainService扣料发奖。消费的是已有材料，不把硬币当礼盒额外消耗。未知机器/组抛明确拒绝；非法PVF启动失败，不静默空奖。奖励不重抽来绕过背包不足，失败不扣币。

Protocol仅解10B输入、执行命令及投影。成功ACK包含剩余硬币、奖励及实际物品记录；原生ACK后跟本人绝对背包投影。未增加金币扣费或未采样的全服公告。序号去重仅同会话一分钟重放窗口，跨会话清理，不声称任意时间重放保护。

## 验证

- 全解决方案构建0 warning/0 error，Release publish成功。
- Application售货机及相邻开罐/礼盒回归63通过，0跳过：真实SQLite扣币发奖、最后一币、缺币、重复及并发、存储失败回滚重试、容量不足、无资格、离开城镇、旧会话、权重边界；全普通奖池有效奖励内容准备检查。
- Protocol相关回归31通过，0跳过；捕获请求、严格长度、失败响应；Application亦验证完整成功ACK偏移/长度、先原生后背包顺序。
- Infrastructure实际PVF缓存与畸形表2通过，0跳过，关闭档案后仍可查询缓存。
- 首次满包测试只占消耗品栏，遗漏6格快捷栏。现有入包规则允许放快捷栏，所以修正测试占满两者后拒绝正常；未改生产入包规则。

## 部署

artifacts/black-diamond-vending-20260929/runtime。确认无10010/10011在线连接后停止旧进程，SQLite backup到before-deploy.db，quick_check=ok；复用绝对主库路径及现有配置，不覆盖主库。1123仍101枚7454。进程26640启动，缓存1组，HTTP bootstrap ready，无stderr。

## 待用户验收

普通机点击一次：减1币、动画完成、奖励图标/数量与背包一致；连续几次后重登仍保持。高级机、全服稀有奖公告未交付，不能按普通机验收推定通过。
