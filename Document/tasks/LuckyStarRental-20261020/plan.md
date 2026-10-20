---
kind: rks-plan
spec: ./spec.md
state: ready
mode: direct
---

# Plan: 幸运星租赁闭环

一个direct Plan维护全部依赖与验证；分配的有界技术工作由主代理集成，写同一checkout的工作顺序执行，不另建Task单位或子分支。

## T-001 内容、余额与租赁领域

覆盖RQ-001–005、RQ-007–008。先以行为测试验证未实现的购星/租赁/去重/回滚结果，排除编译失败作为RED；使用现有SQLite LearningFixture/真实投递夹具或小型固定内容夹具，不让无PVF测试隐式加载实际档案。

新增Abstractions/Rental领域契约：账号星币及角色租赁服务、两个规则服务、一个内容源与两个存储。内容源在Infrastructure解析当前PVF，不把旧chn_rental文件全纳入；Persistence沿现有accounts余额CAS、真实物品UID/期限关联租赁记录，补业务去重记录。Application业务服务经规则获取价格/资格，复用ICharacter金币、IItemDeliveryDomainService InventoryOnly及现有容器/生命周期。失败RequireRollback。规则固定种子/时间便于真实状态断言。

余额接口提供Read、Purchase、Grant、Spend、GrantForClear和幸运星道具使用接入；租赁接口提供Rent、ReadPanel与到期关系整理；保持租赁→余额单向依赖。受支持客户端请求重放沿现有命令执行/会话序列及当前状态投影适配，不以新Guid每次假装幂等。

## T-002 当前A21协议与通关/消耗品接线

覆盖RQ-006–008。独立静态核实03CF/03D0构包与响应、03C1非空列表；给出字段证据和拒绝行为后再实现decoder/codec/handler与ProtocolModule注册。复用选角的完整租赁列表构包；通知从当前状态读取余额/租赁，登录、购星、租赁与消耗品共享其编码。正常通关接入现有GrantClearExperienceCommand与实际run.ClearOpen；冻结参与者与适正资格，不能按每次房间清空发放。幸运星道具沿现有ItemUseEffectDomainService接线，消费与加币同事务。登录/频道加载复用通用到期清理，新增在线即时扫描不在本次初版规则内。

## T-003 集成验证与文档

覆盖全部AC。运行新领域/协议测试及影响回归，再全解决方案无PVF测试及实际部署PVF专项（目录、真实商品、持久化与期限）。新增行为包括满包/无金币/超额/非零前缀/重复/回滚。完整实现后提交implementation checkpoint，独立reviewing-work审查，修复问题并重新验证受影响范围。protocol人工区、catalog与Document/LuckyStarRental.md记录CurrentImplementation/ClientStatic/待实机边界。

## T-004 部署

覆盖RQ-009。先核对当前Host进程路径/版本、运行receipt、DB/PVF、客户端运行状态和监听，不假定历史PID。publish独立artifacts/lucky-star-rental-20261020/runtime，保留旧runtime，在线及停服备份并检查完整性。沿现有部署脚本hidden启动新Host，复核程序集commit/哈希、GM与launcher健康以及TCP/UDP监听、数据库兼容迁移与原关键数据。若存在客户端活动影响替换，使用question工具请求必要退出，不跟踪纯等待todo。不擅改角色资产或时钟。写verification与实机清单，最后seal；不合并或推送任务分支。
