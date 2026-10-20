# 名称装饰卡购买与生效（2026-10-20）

关联：[#172 商城无法购买商品](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/172)。本次交付名称装饰卡，不扩大为商城所有商品已验收。

## 依据与规则

- **PVF**：`etc/cerashop.etc` 的 `[visual]` 共 42 个商品，7 天/300 点券、30 天/600 点券；商品数、价格、期限从内容源读取。只有装备文件明确声明 `[equipment type] [name tag]` 才允许走名称装饰卡分支，未知标志和内容诊断一律拒绝。
- 用户测试的商品 `102241` 对应模板 `100330501`，30 天/600 点券；装备路径 `equipment/character/common/chn_nametag/nametag27_100330501.equ`。
- **PVF 说明**：购买立即生效；同款延长，不同款覆盖原款及期限。同款按物品模板 ID 判断，所以 7 天与 30 天的同款商品可以相互续期。已到期则从本次购买时刻重新计时；换款从本次购买时刻计算新期限，不承接旧款剩余时间。
- **CurrentImplementation**：直接激活角色装饰，不发背包物品。点券、购买限制、装饰 ID/到期时间共用商城事务；余额不足、存储故障、条件更新失败、时间戳溢出都不能留下部分结算。没有增加无依据的请求去重规则。

## 持久化与刷新

复用 `characters.name_tag_item_id` / `name_tag_expire_time`，不新增表。名称装饰领域服务负责状态与条件更新，规则服务负责续期/覆盖/有效性，SQLite store 参与现有数据库事务。

进入角色、查看当前状态、换装等均经角色 entry 领域服务读取有效装饰；过期投影为 0/0。在线每分钟以受账号执行门保护的命令清理过期状态，提交后通知；重连/切人后旧会话不能通知新会话。

按用户要求统一完整 USERINFO：`CharacterUserStateProjection` 从同一份当前角色视图生成完整 NOTI0002 subtype0、subtype1。名称装饰卡购买/到期、普通换装、称号装备、装备过期、效果重投共用该组包器。自己的刷新为 subtype0→subtype1；可见玩家收到完整公开 subtype0，沿用协议的收件人权限，不发送购买响应、余额和私人扩展状态。

本次使用既有 USERINFO 的装饰 ID/到期字段，不新增名称装饰卡专用协议、部分外观字段包或属性补丁。渲染和物品模板属性的 A21 实机表现仍待用户验收。

## 自动验证

首次行为 RED 基线 `3e738703`：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNF-JP-86-Server/artifacts/scheduled-notice-pvf-20261019/Script.server.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --filter FullyQualifiedName~NameTagPurchaseTests --logger 'console;verbosity=minimal'
```

退出 1，预期 `None`，实际 `UnsupportedProduct`，确认购买能力缺失。

实现后的行为验证包含：真实商品价格/期限、同款续期、换款覆盖、到期重购、失败回滚、余额不足、不支持的选项/模板、时间戳溢出、在线清理一次、购买后完整状态刷新、附近玩家与私人购买通知隔离。内容测试核对当前 PVF 全部 42 个名称装饰商品。

协议回归覆盖普通换装、称号及重投；新增对比测试证明完整刷新与进入角色时的 USERINFO 载荷逐字节一致。整套报文发送前先完成编码校验。

最终无 PVF 整体回归：8 个测试程序集，7628 项，6736 通过、892 因资源条件跳过、0 失败。真实 PVF 相关回归：Application 156、Protocol 117、Infrastructure 33，全部通过。构建 0 警告、0 错误，Git diff 检查通过。日志和 TRX 保留在 `artifacts/name-tag-shop-20261020/`；部署身份与实机待验项见工作区 `Document/NameTagShop-Deployment-20261020.md` 和部署回执。

## 实机验收

1. 购买“我在恋爱”，成功扣除 600 点券，立即显示名称装饰；检查属性与说明。
2. 再买同款，到期延长；换另一款，立即替换并按新款期限计算。
3. 换装备、切图、切角色后重登，名称装饰与剩余期限一致。
4. 有第二客户端时，检查附近玩家也能看到变更，离开再进入仍一致。

部署继续使用常用测试库和现有 PVF；重启前保存在线和停服备份，运行版本、文件哈希与健康检查见部署回执。
