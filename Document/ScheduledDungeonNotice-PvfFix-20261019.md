# 牛头与南部右上角常驻提示关闭

2026-10-19。用户授权按已调查的字段修改并同步 PVF，随后自行实机检查。仅修改 `etc/chn_schedule.etc` 的 `[dungeon]` 两条记录，各将第六个整数（包括地下城编号计数）由 1 改为 0：

```text
100  1 1 1 1 1 … → 100  1 1 1 1 0 …
3700 1 1 1 1 1 … → 3700 1 1 1 1 0 …
```

记录与其他整数全部保留，包括入口关联、历史文本索引和 `[dungeon minimap]`。服务端 033F、共享活动69、各活动开关、门票和奖励逻辑均未修改。

## 字段证据和修改工具

ClientStatic：只读 A21 RVA dump SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，首选 ImageBase 00400000，以下为 VA：

- `011C4FA7..011C514C`：地下城记录为17个整数；第六整数保存为显示模式，并以大于0生成配置 `+0F` 的显示标志。
- `011C34AD`：标志为0时跳过该记录的常驻文本绘制；`011C35A0..011C37EC` 为模式1的右上角文本绘制路径。
- `011C2190` 接受开放状态1和2；两者都会进入提示更新，不能将2当成已证的静默开放状态。
- 入口的调度记录存在性与开放状态查询保持原样，不删除记录来隐藏UI。

专用工具 `Tool/PvfScheduledNoticePatch` 将字段含义写入源码注释。它要求明确的源归档SHA-256、17列整数形状、唯一目标编号和原值1；拒绝原地修改与覆盖输出。沿用现有归档补丁方式，追加一个数据块，仅重定向一个子文件。

三份产物回读均通过：712010个子文件中712009个索引未变，字符串池、哈希表、原有数据块及尾部保持原字节，目标脚本仅2个字节由01变00，其余token完全一致。工具Release构建0警告、0错误。这些检查不代替原生客户端显示验收。

## 同步和运行身份

客户端正常退出后替换以下两份PVF，逐份从各自原文件生成，保留各自既有差异：

| 用途 | 路径 | 修改后SHA-256 |
|---|---|---|
| 客户端1 | `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` | `3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B` |
| 客户端2 | `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf` | `D45CF58B1A2B3D16D6B35ABE0D4E302E052F0B6BC43E9D3E98D815FED54AEF84` |
| 服务端 | `artifacts/scheduled-notice-pvf-20261019/Script.server.pvf` | `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06` |

服务端运行目录继续为 `artifacts/southern-dale-deploy-20261019/runtime`，实际程序集构建 `0.2.7+5297d9b00b5add4bd5c4dc9b1812951c6bfe2779`，只更新Pvf.ArchivePath并重启，未发布新程序集。重启后PID40784，Launcher bootstrap及GM health均ready，31个TCP和2个UDP监听与切换前一致。原108张表内容摘要不变，SQLite完整性及外键检查通过。

证据及回退输入在 `artifacts/scheduled-notice-pvf-20261019/`：三份PVF及各自JSON校验、sync-manifest.json、sync-receipt.json、原客户端PVF及appsettings备份、stopped.db和post-start-comparison.json。旧服务端PVF仍保留。若需回退，使用本次备份恢复两份客户端PVF与运行配置并重启；不覆盖玩家数据库。

## 实机待验收

重启客户端，检查右上角两项常驻提示消失，同时山麓牛头、南部入口仍显示并可正常入场。当前是静态依据、归档回读与部署就绪，尚未得到本次UI修改的ClientVerified反馈。没有推送或修改issue状态。
