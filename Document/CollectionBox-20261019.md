# 收集箱：常驻规则与 A21 开发取证

日期：2026-10-19。基准提交：`4c695435411f28448f174c52ed45834b99b524bd`。
任务：[#164](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/164)；本轮顺序：[#170](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/170)。

## 已确定的产品边界

用户选择“常驻启用，取消历史到期限制”。当前服务端 PVF 的 SAO、生肖之灵、三国志三套收集箱均在本项范围内。历史日期保留为资源出处，不作为本服失效条件。永久有效后延期请求不得无效扣券；具体拒绝回包必须以 A21 客户端分支为依据。

当前阶段只完成开发取证，没有实现物品转移、修改玩家数据或部署。不能将本页的静态结论标为 WireCaptured 或 ClientVerified。

## 输入与证据

| 输入 | 身份与用途 |
|---|---|
| A21 映射镜像 | `DNFClient-A21-debug/dumps/dnf-dump-20260912-222635-726-46724/DNF-memory-rva.bin`，SHA-256 `52A9D002EB15AB6301069A954D84B77A122B9B6928F8886A1B0653054428AC72`，ImageBase `00400000` |
| 服务端 PVF | `artifacts/wedding-ticket-pvf-fix-20261018/Script.server.pvf`，SHA-256 `BCF99E4E4CC691D43DD89373519EF67D632B83FB0BDAD560454E101415768D63` |
| 收集箱定义 | PVF `localization/collectbox/collectboxinfo.etc`，索引1 SAO、2生肖之灵、3三国志；现行定义与旧记录“只有1、2”不同 |
| 复现脚本与反汇编 | [probe.py](outputs/collection-box-20261019/probe.py)、[输入身份](outputs/collection-box-20261019/inputs.json)、[有界反汇编](outputs/collection-box-20261019/a21-bounded.asm.txt) |

所有下述地址为 RVA；反汇编中跳转操作数为 VA，减 `00400000` 后对照。证据提取为有界线性反汇编，不把内嵌跳转表当作已执行指令。

## 可用于实现的已核对事实

| 结论 | 等级／置信度 | 直接依据 |
|---|---|---|
| A21 的选择、放入、取出、延期为 CMD03E5、03E6、03E7、03E8，收集箱推送为 NOTI03EB | ClientStatic / High | `protocol/patterns/data/probes/a21-identity-census.tsv`；客户端对应请求与处理函数 |
| 03EB 读取固定8字节前缀，再读取 count 个 u32；本处理路径的载荷长度为 `8 + 4×count` | ClientStatic / High | RVA8A7500–8A7538读取u8/u8/u32/u8/u8；8A7546按末尾u8控制循环，8A7550–8A7554每次读取u32，8A7633–8A763D比较循环计数 |
| 03EB的第一个u8进入箱子查找；u32列表用于清空后逐项重建箱内物品 | ClientStatic / High | RVA8A7643–8A765F传参至8A73E0；8A73E3/8A73FD使用首字段查箱；8A7466–8A746C调用8A70A0，后者8A70A8清空、8A70C5–8A70E3遍历重建 |
| 03EB偏移1的u8控制额外选中／角色刷新分支，不应无证据改名为“成功码” | ClientStatic / High | RVA8A7650取该字段；8A7664–8A7666决定是否进入刷新分支 |
| 03EB偏移2的u32与偏移6的u8写入箱子不同状态字段 | ClientStatic / High | RVA8A745B–8A7461、8A7471–8A7478分别调用899150和8991F0。永久标志语义还需结合读取方、属性与UI消费者闭合 |
| 放入请求顺序是箱子相关u8、常量0 u8、槽位u16、物品ID u32 | ClientStatic / High | RVA89E8AD–89E8E6，对应WriteU8、WriteU8、WriteU16、WriteU32；字段业务语义仍需沿调用者核对，不把两u8按u16混读 |
| 取出请求是箱子相关u8及u32参数 | ClientStatic / High | RVA89EA2D–89EA48；u32为物品ID的解释有上游参考，尚缺本服实帧 |
| 03E5成功分支在通用CMD结果之后额外读取一个u8 | ClientStatic / High | RVA8A6A33检查通用结果；8A6A42–8A6A5E读取并交给选择函数。上游“箱号重复两次”不能当作标准成功载荷 |

03EB完整布局和待核字段汇总到[协议人工区](../protocol/game/server-to-client/NOTI-0x03EB.md#2026-10-19-a21-有界复核)。旧 A12 的8字节空箱读取记录仍保留其版本边界。

## 当前实现的接入缺口

- `ICharacterCollectionBoxTable` 只有读取方法；`character_collectbox_slots` 已有表结构，但未形成具备事务原子性的放入／取出领域通路。
- 当前 `CharacterStateProjection.CompleteCollectionBoxProjection` 仍按历史 MaximumExpiresAt 产生过期状态。
- 当前 `CollectionBoxStateMessage` 发兼容 NOTI0381，选角只取前两个箱子。保留其既有参照帧语义；真实03EB需单独建消息与投影，不能原地改号。
- 上游 `CollectBoxRuntimeService` 可参考物品匹配、重复槽拒绝及取回流程；其旧协议、先改内存再提交、默认重建实例等做法不能直接作为本服事务设计或 A21 证据。

## 接下来的实现边界

1. 继续核对03E5–03E8的成功／拒绝体、永久标志及属性消费者，补足当前仍未知的语义；无实证不猜错误码和属性同步消息。
2. 由收集箱领域服务统筹规则与物品转移；规则服务独占PVF定义读取，持久层收口原子写入。普通背包与收集进度只能一起提交或一起回滚。
3. 成功后发送权威库存与箱子快照，重放读取当前事实，不重复转移；选角和恢复发送全部已定义箱子，不限前两套。
4. 覆盖错误物品／槽、锁定、重复放入、满包取出、失败回滚、重登、角色隔离、永久有效与延期不扣券。属性效果另行完成客户端验收。

本页不确定新的部署、发布或真实玩家数据操作；实施验收结果以任务issue及后续实际记录为准。
