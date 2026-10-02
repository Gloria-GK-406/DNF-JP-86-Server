# Spec003 完整结果独立挑战集

2026-10-02。挑战作者独立于生产者、累计工作 drift reviewer 与后续完整结果 reviewer；按 `generating-adversarial-challenges/SKILL.md` 检查实际冻结工作树。本文件提供挑战输入，不给出审查 verdict 或完成声明。

## 目标与攻击边界

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`，分支 `feature/gm-web-admin`。
- HEAD 与接受基线：`416f26c9919383b58b0bd0e7179a99734fe53b48`；目标包含相对基线的完整 diff 与 untracked 新增产品/测试源码。
- 实测 27 个变更 `Server/` / `Web/` 路径的 SHA-256 集合指纹：`98016BD0667F676E724FD252C1F2E27DF42FA0F3937539199E9AA127E736A124`，与派发冻结目标一致。算法为路径排序去重，逐行 `SHA256  path`，LF 连接后对 UTF-8 字节取 SHA-256。
- 根与 child `TaskContract.md` 均为 revision 1；child `spec.md` SHA-256：`4D4349E0C10F6C90C4A1ACB13A082EEDB4896BDCC25BAA007B50891B74695169`。治理来源为适用 AGENTS、根合同、child 合同与规格；child 仅贡献根 SC-3 / SC-6。
- 攻击范围：五类 GET 的 QueryPlayers/白名单/只读边界，typed SQLite 全服目录、分页前筛选与原始 name_bytes，在线账号和精确入场会话、实时位置/频道/readiness，以及中文查询页的交互、错误与失权清除。没有邮件或点券写入的实现预期。

## 保留挑战

本次保留 **0 个 high-risk、0 个 low-risk**，不建立 CH 条目。没有找到需要增加独立决策的可信失败路径；不以推测、重复已有判据或补足数量制造挑战。

筛除候选的依据来自实际源码及判据形状，而非实施报告的结论：

- 在线过滤晚于分页、total 与 items 名单不一致、异常姓名按兼容文本匹配：`SqliteGmPlayerDirectoryPersistence.cs:15-55` 对同一筛选表达式先计数、后按 ID 分页，在线名单作为同次参数，name_bytes 在合法解码条件内参与字面匹配。查询命令使用现有事务边界；目录 HTTP 判据覆盖数据库后页在线对象、相邻页/重复页、异常姓名和组合条件。已能由普通源码审查和主控 fresh checks 廉价控制，不重复挑战。
- 旧会话位置冒充新会话、未发布会话被计在线：`GmPlayerRuntimeQuery.cs:27-40` 在读取位置前后检查 current selection、exact publication 与在线归属，`OnlineStateRegistry.cs:169-180` 不错误比较发布前后 generation。另追到实际生产选角准备路径：`SelectCharacterPhaseCommands.cs:49-109` 在 Select 新会话前完成旧会话清理；不是仅凭测试夹具人为先清理的顺序推断安全。位置读取交错与旧 exact 清理有对应判据。采样后发生断线属于规格明确允许的当次采样语义，不能升格为跨源全局原子快照要求。
- 查询失权后旧资料/旧 poll 恢复：`AdminApp.tsx:30-36` 的 403 分支撤去 QueryPlayers 并推进 generation；`useGmRead.ts:7-24` 按请求 key 与卸载状态弃除旧响应。`PlayerDirectory.test.tsx` 独立覆盖详情 403 清整页、权限撤回后的旧详情及 403 后旧身份轮询。没有保留与这些判据同义的挑战。

## 证据边界

已检查实际产品/测试变更及其依赖所有者，包括在线注册表、选中/发布目录、位置领域、生产选角生命周期、频道 policy、SQLite 事务与既有 GM 访问边界；`git diff --check` 无输出。实施报告与 browser-verification 仅用作已有检查/页面观察的证据指针，未拿 drift approved 当作产品正确性结论。

本轮没有运行 .NET、Host、浏览器或真实库探针；没有重跑自动判据，也不声称这些判据 fresh passing。主控完整 fresh 验证与独立完整结果审查仍须执行。本挑战集不表达其结论。仅新增本文件，未改产品、测试、cursor/root、现服或客户端，未提交。

本集合绑定上述冻结目标并保持不变；bounded 修正不重编号。只有任务边界、中心方案或 material-risk basis 改变时才需要新的挑战集。
