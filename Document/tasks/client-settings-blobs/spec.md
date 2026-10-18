---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification：设置原始字节保存与回放

## Why and Outcome

修改设置后正常重登可恢复，账号设置共享且不同profile／角色不串写。数据库保存成功与客户端实际恢复分别验收。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | C5保存150B主设置，170分别保存bank0／1的300B；C6保存最大64KiB的原始文本bytes，不解内部字段；C0保留516B完整包 |
| RQ-002 | 账号设置独立更新；键位按账号＋当前连接profile保存；C0按精确选中角色保存；旧连接／过期上下文不得串写 |
| RQ-003 | AD输出三个长度前缀blob；1C7输出u8类别＋u32长度＋原bytes；187输出完整516B，不重复加前缀 |
| RQ-004 | 登录使用普通profile，选角按普通／缔造者profile；发送profile与上传归属一致。没有保存记录使用客户端缺省入口，不合成旧二进制槽表；明确区分无记录和显式空 |
| RQ-005 | 保存使用现有命令事务和执行门，成功提交后记录归属／类别／长度；不新增ACK、即时NOTI或事件 |
| RQ-006 | 升级仅增量添加存储，保留旧列和旧表，不自动翻译二进制键位；非法长度／bank不修改历史设置 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001,RQ-002,RQ-006 | 用真实SQLite保存含零字节与奇数长度键位，重开后完全相等；两账号、两profile和两个bank独立；事务回滚恢复旧值 |
| AC-002 | RQ-003,RQ-004 | 编码长度严格相等，不含尾NUL、不截2B；无记录不会发送二进制默认槽表；角色包只带一个512长度 |
| AC-003 | RQ-001,RQ-002,RQ-005,RQ-006 | 接线可解析，所有写路径Transactional；错误长度、额外尾部、未知bank、过期连接／选角上下文被拒绝且无额外响应 |
| AC-004 | RQ-004 | 用户实测重登、选角、普通／缔造者切换；具体客户端恢复仍标待验收 |

## Success Evidence

专项测试、相关既有回归、独立审查、部署身份／备份／就绪检查记录于verification；用户列表另附。实施中确认空文本及profile切换缺省入口时保留ClientStatic证据，不冒充ClientVerified。
