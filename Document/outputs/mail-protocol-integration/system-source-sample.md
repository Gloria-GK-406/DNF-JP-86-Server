# 系统来源Builder与样本

2026-09-24：增加MailBuilder.FromSystem(senderName, recipient)和FromCharacter(sender, recipient)工厂，保留现有MailSource构造。35项邮箱测试通过，验证系统来源senderId=0、自定义名字保留、创建不扣玩家金币。

已备份原DB并部署mailbox-system-20260924/runtime，PID15836，监听7001/10010/10011/18080。通过系统Builder给000(id1)生成邮件16，标题“系统奖励测试-20260924”，发件人“系统奖励”，senderId=0，1000金币+3272材料×3，30天；SendReceipt无玩家扣除。UI隐藏玩家邮件标志待用户验收。公会来源没有混入本次实现。

本增量留在现有feat/mailbox-core邮箱集成工作树中，未单独提交未完结的整批协议改动。

## 2026-09-25 修订

旧样本数据库sender_name保留历史值；新投影对系统来源发送空名字。Luna已在新版确认列表和详情仅显示客户端内置“DNF管理员”，不再拼接“系统奖励”。FromSystem API不再接受自定义名称。
