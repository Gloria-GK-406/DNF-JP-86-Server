# 官方邮件 Spec 独立挑战记录

- Target：`../spec.md`，ready，关联子合同 revision 1。
- Target SHA-256：`4F3763FDE5FEC7B507D18131FCF541AB53F29152A7FC840EB4D5871DBEF5D91F`。
- 代码基线：`573be46b075d8363f0882c2f40a5a0135d1ada0c`。
- Governing sources：根 `../../../TaskContract.md`（SC-4/SC-6）、子 `../TaskContract.md`（全部约束及 SC-1..3）、任务工作区 `AGENTS.md`、`generating-adversarial-challenges/SKILL.md`。
- Attack scope：错误收件人或账号执行范围、附件生成与领取生命周期、批量回滚、提交不确定与防重、在线提醒、管理权限及个人资料边界。只检查 Spec 的可观察要求是否遗漏必要控制，不替实现阶段确定局部设计，不要求形式化证明。

## 结果

保留 **0 项挑战**（high-risk 0，low-risk 0）。在上述范围内，未找到由本版 Spec 缺陷通向错误发放、资料泄漏或无法实现接受结果的具体路径。此记录不是 reviewer verdict，也不证明尚未实施的功能正确。

## 核对依据

以下均为当前代码事实与 Spec 要求的对应核对；没有执行服务、真实玩家数据库或客户端操作，也没有重新运行测试。

- **批量部分提交与孤立装备**：`MailboxDomainService.cs:147-176` 会将异常回滚到整个 ambient transaction，不能捕获单人失败后继续提交；`UnitOfWorkBehavior.cs:36-49` 在命令返回后提交。Spec RQ-006、AC-006 及 `spec.md:102` 已明确全批邮件、装备、收据和成功审计同事务，并要求第二收件人、装备、附件、收据及审计失败注入。
- **装备复用与生命周期丢失**：`MailboxDomainService.cs:39-54` 检查邮箱持有状态及 detached 实例；`EquipmentTransferDomainService.cs:50-101` 读取并将原 detached UID 转移到领取容器；`SqliteWorldItemTable`（`InventoryItemTables.cs:506-551`）由数据库分配有界 UID。RQ-005、AC-005 要求新建独立 UID、正常初始 core、领取前无背包槽和原 UID 领取。
- **不支持的永久物品、期限或权益退化**：`PackageOpeningRuleService.cs:58-80` 与 `PvfPackageOpeningDefinitionSource.cs:151-204` 提供 supported、placement、期限及权益事实；`MailStackTransfers.cs:16-21,72-89` 依托分类和领取容器，账号材料走既有领域。`spec.md:54-58` 明确类别、数量、初始实例支持及期限/权益为零，禁止未知内容退化；局部生成方案留待实现，不据此构造挑战。
- **错锁账号与排队旧权限**：`AccountExecutionBehavior.cs:23-48` 对声明账号排序取租约，事务行为位于其内；`GmAccessDomainService.IdentifyAsync` 从当前会话及管理员存储读取身份。RQ-004、`spec.md:37` 和 AC-001/004 已要求服务器解析账号、事务中复核目标/归属及身份权限，并验证预检后变化与排队撤权。
- **截断、协议不可表示附件和重复发放**：`StateSynchronizationPayloadCodec.Entry.cs:614-675` 的摘要个数为 u8、文本合并方式及 messageId 范围具有实际约束，`MailboxIds.cs:5-9` 限制附件 ID；RQ-003/006/007 与 `spec.md:52-66` 已约束 511 UTF-8 字节、最多 10 个展开物品及 1 个金币行、ID 范围、持久唯一防重、同编号输入冲突和未知提交状态。AC-003/006/007 对应验证；不存在/已删除目标不妨碍已提交收据重放也已明确。
- **通知失败变成再次发放与跨管理员资料**：RQ-008、`spec.md:68`、AC-008 规定提交后只读当前邮箱投影及提醒失败隔离；RQ-001/009、`spec.md:37,43,74`、AC-001/009 覆盖 SendMail 专用最小收件人投影、个人对象、旧响应与浏览器保存状态的身份隔离。`GmAccessBoundary.cs` 已有独立端口、传输、权限与防伪边界，可在本模块沿用。

## 写入边界

已核对工作区绝对根、根合同及子合同身份、目标哈希和 Git HEAD。输出绝对路径位于本子任务 `reviews/spec-challenges.md`；工作区及现存目录祖先无 reparse point，输出文件事前不存在，仅新建该记录。其他任务的既有变更保留；未修改目标 Spec、合同、产品代码或 Git 状态。
