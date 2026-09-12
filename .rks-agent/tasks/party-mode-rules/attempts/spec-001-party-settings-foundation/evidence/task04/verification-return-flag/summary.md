# 回归玩家队伍标志归零验证（2026-09-12）

## 变更结论

- 当前产品不实现回归玩家队伍玩法；客户端提交的 12 字节设置块 `[1]` 在领域写入前统一归一化为 `0`。
- `PartySettings` 值对象仍能 byte-exact 表达输入；业务归一化只在 `IPartyModeRuleService.Normalize` 定义，
  `PartyDomainService.CreateOrUpdateSettingsExact` 在校验和保存前消费它。
- 回归用例证明输入 `[1]=1` 时，成功结果快照与服务内当前快照均为 `[1]=0`，其余 11 字节保持不变。
- 此决策只覆盖队伍设置块，不修改角色持久化中的 `is_return_user` / `return_user_flag` 等其他字段。

## TDD 与聚焦回归

- RED：`ServerDisablesReturnUserPartyWithoutChangingTheOtherSubmittedSettings` 首次运行时，期望的 `[1]=0`
  与实际保存的 `1` 不同，1 项失败；随后才加入生产归一化。
- GREEN：`PartySettingsDomainTests` 10/10；Application Party 31/31；Protocol Party 72/72。
- capture helper 内置 self-test 通过；`unittest discover` 2/2 通过，覆盖 split/coalesced 重组、白名单及
  `0x0099 → 0x000B → 0x0009` 创建顺序。

## 完整适用测试

最终有效结果组合为 3082 Passed、0 Failed：

| Test project | Passed | Failed | Skipped | TRX |
|---|---:|---:|---:|---|
| Dfo.Launcher.Tests | 63 | 0 | 0 | `full-rerun/*13_09_41*.trx` |
| Dfo.Pvf.Tests | 60 | 0 | 0 | `full-rerun/*13_09_44*.trx` |
| Dfo.Server.Application.Tests | 1379 | 0 | 0 | `application-rerun/*.trx` |
| Dfo.Server.Host.Tests | 17 | 0 | 0 | `host-rerun/*.trx` |
| Dfo.Server.Infrastructure.Tests | 301 | 0 | 2 | `full-rerun/*13_10_36*.trx` |
| Dfo.Server.LauncherApi.Tests | 50 | 0 | 0 | `full-rerun/*13_10_46*.trx` |
| Dfo.Server.Networking.Tests | 62 | 0 | 0 | `full-rerun/*13_10_47*.trx` |
| Dfo.Server.Persistence.Tests | 353 | 0 | 0 | `full-rerun/*13_10_51*.trx` |
| Dfo.Server.Protocol.Tests | 797 | 0 | 0 | 审查修复后 fresh Release 全项目运行 |

有 PVF 时按条件跳过的两个“缺少档案应失败”用例，在移除 `PVF_ARCHIVE_PATH` 后单独 2/2 通过，见
`no-pvf/*.trx`。Release solution build 为 0 warning / 0 error；staged 与 unstaged `git diff --check`
均为 exit 0。

## 执行时例外

- 首轮并行完整测试中，Networking 的连接关闭偶发返回 Windows `IOException/ConnectionReset`，而测试
  只接受 `EndOfStreamException`；该用例随后单独连续 3/3 通过，第二轮 Networking 全项目 62/62 通过。
- 第二轮完整测试中，Application 的既有 50 ms 墙钟用例在负载下测得约 3.46 秒；同一项目脱离全量
  负载后 1379/1379 通过。两项均未涉及本次 party 变更，不修改产品代码或放宽断言。
- 首轮曾指定统一 `LogFileName`，多个测试项目覆盖同一 TRX；该目录不作为汇总证据。最终汇总只使用
  上表列出的独立 TRX 与隔离重跑。

## 独立完整结果审查修复

- Reviewer 发现全局 `HasFullState` 会在“更高 revision 设置更新先于接受/离队计划到达”时漏掉新成员
  完整名单，且 revision 高水位会连同旧计划中的离队 clear 或邀请接受回执一起丢弃。
- 首轮两个针对性用例先以 2/2 失败复现：接受路径只发设置通知；离队路径缺 clear。实现改为按
  `SelectedSessionIdentity` 记录已经收到完整状态的成员，迟到旧 revision 只允许补发 recipient-specific
  clear/acceptance，不重放旧 snapshot。
- 复审继续构造“重新入队 rev3 越过离队 rev2”和“离队 rev3 越过接受 rev2”两个反向场景，均先
  2/2 RED。实现追加同 generation 最新成员视图：迟到 clear 只发给不在最新成员视图的精确会话，迟到
  acceptance 仅在双方仍为最新成员时发送。四个乱序用例 4/4、Party 72/72、Protocol 797/797。
- 同轮修正 `protocol/game/index.md` 对 NOTI `0x0009` 的 type-1 `18/22` 长度遗漏，以及静态 trace 中
  服务端默认 `ModeTargetRaw` 应为 `0xFFFF` 的表述；客户端自动路径的 target `0` 静态观察仍独立保留。
