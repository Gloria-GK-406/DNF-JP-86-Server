---
kind: rks-verification
decision: manual_required
---

# 求婚第一步验证边界

验证主张：本次原生求婚与答复服务端实现已通过对应自动化检查并部署；双客户端可见结果尚未验收。

| 条件 | 结果 | 证据 |
|---|---|---|
| 业务、并发和持久化回归 | Application3487通过，423跳过；Persistence468通过 | artifacts/marriage-proposal-20261016/tests/application.trx及本次Persistence完整测试输出 |
| A21协议编码、会话投递保护 | Protocol1474通过，60跳过 | artifacts/marriage-proposal-20261016/tests/protocol.trx |
| TCP连接回归 | Networking92通过 | artifacts/marriage-proposal-20261016/tests/networking.trx |
| 当前PVF最低等级 | 配置测试1通过 | 本次Infrastructure MarriageProposalContentTests输出；client-evidence.txt记录客户端证据 |
| 构建与隔离启动 | Release发布成功；副本迁移及HTTP正常 | artifacts/marriage-proposal-20261016/preflight2.stdout.log、preflight-after.json |
| 正式服务切换 | 进程/文件/版本一致，端口和HTTP正常 | artifacts/marriage-proposal-20261016/deployment.json、server.stdout.log |
| 部署数据保护 | 备份integrity ok；旧表内容哈希不变 | before-deployment-audit.json、after-deployment-audit.json |

## 未验证条件

- 双端原生求婚窗口、拒绝提示、接受成功，以及接受后可能发起的后续协议。用户已具备双客户端条件，已发送先拒绝后接受的操作步骤。
- 正常实帧与客户端UI共同证明的ClientVerified结论。
- 本次不包含婚礼、完整婚姻UI重登恢复、原生退婚和离婚。

资料同步脚本保留14项已存在的其他协议覆盖缺口；不能宣称全仓资料同步通过。当前结果保持未提交，待本步原生验证完成后形成一个本地结果提交。

## 11:35断线及修复包复验

原生03F0正文21B被旧7B decoder拒绝并断开，见proposal-disconnect.txt。发送链静态证据确认C2S前14B仍属于正文。codec回归先观察3失败，再修正为21/22B、从正文+14读字段；Protocol最终1476通过/60跳过，Networking92通过，TRX见旧artifact目录tests/raw-struct-protocol-green.trx及raw-struct-networking-green-nologprovider.trx。无日志提供器环境设置的第一次Networking跑数为90通过/2失败（WindowsEventLog权限引起旧UnknownInbound连接EOF），没有删除或修改该旧测试。

Release修复包已完成隔离副本启动并切换正式服务，身份、备份和31个TCP监听见artifacts/marriage-proposal-fix-20261016/deployment.json；after-deployment-audit.json确认84张既有表哈希均未变化且integrity ok。此时仍为manual_required，尚无正常双端接受/拒绝的可见结果或完整实帧；旧测试包部署记录不能代替该修复包身份。

## 11:48双端用户回报与运行核对

用户已完成拒绝和接受并反馈订婚成功；真实服务日志及SQLite的两份历史关系、两份有效名额与操作一致，连接后续心跳正常。详细证据见native-verification.md。本轮操作不再是“用户尚未尝试”；manual_required仅保留逐端UI细节、完整原始帧与重登界面等未验证边界，不用其否认已经观察到的求婚闭环。未继续婚礼或修改关系，尚未作最终本地结果提交。
