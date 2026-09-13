---
kind: rks-postcheck
contract:
  path: ./TaskContract.md
  revision: 1
state: passed
decision: retain
---

# Postcheck: 固定 A21 原生公共队伍目录运行时闭环

## Actual postconditions

- 用户通过游戏内正常退出结束两个指定客户端；exact-path DNF 进程均不存在。
- task-owned Host PID `22076`、proxy PID `59496`、probe PID `61272` 按收据停止；增强 probe PID `62620`
  通过其独立 PTY 会话停止。
- `7001/10010/10011/17001/18080/20010/20011` 均无 listener。
- 主数据库前后 SHA-256 均为
  `E06DFFBA863FB5FFFF89DB62D73584F7F4DB3A34F6C22387E39C07A5CF2F5BA7`；Host 仅写任务临时副本。
- 本轮没有修改两份 ClientHack.ini、launcher、插件或客户端二进制，不存在配置恢复动作。
- raw runtime evidence 保留在唯一任务临时目录；仓库只保存无凭据摘要、typed snapshot 与协议样本。

## Recovered execution exception

首次停止尝试对三个收据均返回 `identity_mismatch_not_stopped`，保护门未结束任何进程。回溯确认路径完全匹配、
实际启动 UTC 也与 JSON 文本一致；根因是 PowerShell 已把 JSON ISO 时间转换为 UTC `DateTime`，脚本再经字符串
解析时丢失 `Z` 并按本地时区额外减 8 小时。新增 tick-level 回归后改为直接规范化 typed value，再次执行时
三个收据均为 `stopped`。该异常只影响回收工具，不影响业务运行证据。

## Decision

选择 `retain`：保留 Spec 的探针、测试、sanitized evidence、协议文档和样本，保留仓库外 raw runtime 供 hash
复核；无需恢复产品状态。受控运行已结束，可进入 complete-result review。

## Independent-review remediation postcheck

- 用户再次通过游戏内正常退出结束 pro1 PID `39576` 与 pro2 PID `62412`；两个 exact-path DNF 进程均不存在。
- receipt 校验后停止 probe `40440`、proxy `48740`、Host `41996`，三者均返回 `stopped`；hold shell 随后按预期
  报告 owned Host 已结束。
- 七个任务端口再次确认无 listener，receipt PID 均不存在。
- 主数据库 SHA-256 仍为 `E06D...F5BA7`；第二 runtime 只写隔离副本。
- 因复审补证仅新增只读对象字段与证据，没有客户端/产品配置需要恢复，decision 仍为 `retain`。
