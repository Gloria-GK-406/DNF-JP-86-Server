# 当前 A21 Story 定向实机捕获计划

## 目标与停止条件

目标是同一 exact session 中取得原生 Story create、edit 和“尝试委任队长”的可归因时间线，闭合
`0x000C` 的 target/default 与关联 opaque 字节，并证明 Story UI 阻止 `0x0079`。

出现下列任一条件立即停止，不把样本写成事实：

- debug EXE SHA-256 不再是 `DC322D8...06E9`，或插件/Host 不属于记录的 build/commit；
- UI 没有可识别的 Story 创建入口，或入口实际只启动 `0x01BE` 快速匹配；
- 帧缺少同一连接/序列/时间，无法把动作与 `0x000C/0x0009` 对齐；
- target 被用于尚未闭合的危险索引，或客户端出现异常读取/崩溃；
- 需要捕获登录凭据或无关协议才能继续。

## 捕获配置

使用 `client-debug-lab` commit `c8f7497e...af7` 的 NetworkHook；只在一次临时 debug 部署副本中设置：

```ini
[FrameCapture]
Enabled=1
Types=000C,0009,0099,000B,0079
Stack=000C,0079
MaxBodyBytes=4096
WriteTrace=1
LogFile=clienthack-story-frame-capture.log
```

`Types` 会同时匹配双向同号消息，因此判定时必须连同 `direction/selector/header_length`，不可只看 type。
Channel traffic 不落盘；若改用接受的透明代理，必须保留同等 allowlist，并显式加入 C2S `0x0079`，否则
“没有发送委任帧”不可证。

Host 以 successor worktree 的同一候选 build 启动，运行前记录：Git HEAD、DLL/EXE/INI SHA-256、Host PID、
监听端口、数据库副本和日志起始 offset。端口转发使用 7001→17001、10010→20010、10011→20011 时，
先证明前端/后端均由本轮进程持有。

## 操作序列

1. 原生 UI 无队状态，记录当前剧情任务/场景名称与可见 Story 入口；创建 Story 队伍，不修改标题或容量。
2. 等待 UI 稳定，记录第一份 C2S `0x000C`、Host create 结果、S2C `0x0009 type=0`、`0x0099` 和 UI 状态。
3. 打开队伍设置，修改一个低风险字段（优先容量 4→3 或标题），保存；记录第二份 C2S `0x000C`、Host
   update 结果、S2C `0x0009 type=1` 和 UI 状态。
4. 让第二名真实会话加入，队长在 Story 队伍内点击委任队长；记录 dstr 48613 对应提示，确认捕获中没有
   C2S `0x0079`。随后切换到已知可委任的非 Story 对照队伍，执行一次委任，要求捕获到
   `0x0079 body=<slot u8>`，以证明探针没有漏帧。
5. 正常退出/停止，先归档日志再恢复临时 INI；不把临时配置部署到 play 目录。

## 必须提取的判定表

| 动作 | C2S | Host | S2C | UI |
|---|---|---|---|---|
| Story create | exact 12B 或 `16+n` body；逐字节列 `[0..11]` 与 title | accepted/rejected、partyId、generation/revision | type 0 canonical block + audience | Story 类别、目标文本、容量/标题 |
| Story edit | exact body，标出变化字节 | 原子 update 或拒绝且状态未变 | type 1 canonical block + audience | 编辑值是否保持 |
| Story 委任 | 期望无 `0x0079` | 期望无对应 command | 无委任投影 | dstr 48613，队长未变 |
| 非 Story 委任对照 | `0x0079` + 1-byte slot | 当前服务端若未实现可为 Unhandled，但必须记录 | 仅记录直接相关输出 | 操作触发已被 capture 看到 |

## 放行条件

只有 create 与 edit 都能在同一固定客户端身份下归因，且至少一个样本明确给出 category 0、target 及
`[3..7]`，才可重新进入 Story T-002。若两次值不同，必须先说明是任务状态变化还是 UI 编辑造成；若 Host
未实现 `0x0079`，这不妨碍证明客户端 Story 阻断，但不得宣称服务端已支持通用委任。
