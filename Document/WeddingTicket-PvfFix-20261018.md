---
kind: rks-verification
decision: manual_required
---

# 爱相随守护券 PVF 修正与部署

用户要求直接修复错误脚本并部署，不允许通过解析器兼容错误写法。本轮只删除物品10002915「爱相随守护券」脚本中第二处完全相同的 `[stackable type] [waste] 0`，服务端解析器及业务程序集保持原样。

目标脚本：`stackable/event/chn_wedding_system_2014/chn_stk_couple_surprise_ticket.stk`。

## 修正方式

修复工具：[PvfWeddingTicketRepair](../Tool/PvfWeddingTicketRepair/Program.cs)。工具要求原档恰好包含两处已确认的相同声明；形状或值不符即失败，不原地覆盖，不处理有歧义的字段。生成的新档只重定向这一个脚本至追加压缩块。

服务端和两客户端分别从自身当前PVF生成修正版，保留已有资源差异。每份归档均验证：712010个文件中，712009个无关条目未变；所有原有压缩数据、名称/哈希池及尾部数据保持逐字节一致；目标只减少3个token。严格schema读取确认现在只有一个类型声明。

| 目标 | 原SHA-256 | 修正后SHA-256 |
|---|---|---|
| 服务端 | `91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4` | `BCF99E4E4CC691D43DD89373519EF67D632B83FB0BDAD560454E101415768D63` |
| 客户端1 | `573388731C394A71D5E75ED7DD215124B3D04383631D93116625ED353E06ACAB` | `FE01924C38E512782BE198038CE21E25503EB5ECCFD485B6FEC69F2613C30E30` |
| 客户端2 | `FDDBB938361D4CA817598AE479E2F951B47E1DB64E096943167D20014A97AF98` | `3FCCFE1066C7663E7A853D877D158817A5A134581097233E7A13ADCDE7E8BA04` |

## 检查结果

- 修复工具Release构建：0警告、0错误。
- 真实物品授予链拾取该券：成功持久入包；同一地面掉落再次拾取不重复获得。连同胶囊普通效果共2项通过，日志 `artifacts/wedding-ticket-pvf-fix-20261018/real-pickup-fixed`。
- 严格入包解析、夫妻副本64760/64761/64762所有独立掉落物元数据等18项通过，日志同目录 `strict-content`。
- 未放宽重复字段检查；未修改掉落概率、道具效果、其他PVF文件、客户端DLL或服务端业务实现。

## 已部署

部署时两客户端均已关闭。服务端改为读取 `artifacts/wedding-ticket-pvf-fix-20261018/Script.server.pvf`；客户端1/2各自的 `Script.pvf` 已原子替换为对应修正版。

运行时仍为 `0.2.5-marriage-capsule.20261018+4f328aeec1e32a30ec8ddf0939cef0cfd851c5f0`，PID42932。31个TCP、2个UDP端口保持一致，GM/登录引导均ready，GM页面HTTP200。部署后再次检查三端PVF哈希与实际服务配置，均匹配。

当前数据库停服备份为 `artifacts/wedding-ticket-pvf-fix-20261018/current-stopped.db`，旧PVF及配置备份在该目录的 `rollback/`。SQLite完整性与外键检查通过，35张角色、物品、任务、婚姻和邮件相关表的指纹保持一致；没有恢复旧游戏进度。

详细回执：同目录 `deployment-receipt.json`、`final-runtime-audit.json`、`final-database-audit.json`。

当前分支 `task/next-round-20261013`；修复工具、回归用例和本文作为单个本地结果提交保存。运行中的业务程序集提交仍为4f328aee。

## 用户验收

重新登录两个客户端，再次进入爱之摩伽陀并拾取Boss掉落，重点验证「爱相随守护券」入包时不再断线。此次自动化证明入包路径已可处理修正后的脚本；实际双端操作仍待用户验证。
