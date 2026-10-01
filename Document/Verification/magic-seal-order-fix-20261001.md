# 魔法封印 S02：转换成功刷新顺序修复

```yaml
kind: rks-verification
decision: manual_required
```

## 范围与验收状态

承接既有effect任务中的验收缺陷，不重新划分任务契约。用户已确认C01、C02、S01通过；C01观察到剩余23小时22分钟。S02仍未通过，新增S02a／S02b复验当场一致和重新登录一致。

## 现场证据

- `CurrentImplementation`：运行4c810b6f，服务日志位于 `E:/Project/DNF/DNF-JP-86-Server/artifacts/cooldown-seal-fixes-20261001/stdout.log`。目标角色2（1123）、主背包槽42、UID1205、装备100210143。最后成功包原顺序是CMD01B6、装备000E、金币000E。
- 真实A21 PVF与同规则只读复算：23:01:55 seed1439661421产生`(80,18,16)`；23:01:57 seed1039251123产生`(71,27,28)`，SQLite保存后者。用户转换窗口最后“鹰眼B”，重登“智慧B”，与前后两次结果相符。
- 用户补充截图：窗口“武力B”与同件装备tooltip“活力C”同时存在。23:08:48 seed1359490601产生`(70,10,21)`；23:08:54 seed557305530产生`(72,6,2)`。此证据将不一致定位到重登前。后来23:10:20再次转换，seed758166160产生`(70,18,23)`，SQLite同步保存，不能把新一次状态误当作截图当时状态。
- 复算不读取或改写角色数据；现场SQLite通过mode=ro读取。规则ID与品质字节来自当前实现/PVF；中文名称和档位由用户截图／反馈提供。

## 客户端静态证据与根因

A21 `E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe` SHA256为 `d7618a102251f1db724f2514c74a4cc855df26e6a4bc308f8ee9f2397db0a99a`，基址00400000。相关指令：

```text
VA010450B1  mov ecx,eax
VA010450B3  call 014DBCB0       ; 窗口对象+1AC
VA010450B8  mov ecx,[03A2B21C]  ; 当前选中装备
VA010450BE  push ecx
VA010450BF  mov ecx,eax
VA010450C1  call 01044EB0
VA01044FF0  call 010448B0       ; 选项重建
VA01044917  mov ecx,[03A2B21C]
VA01044922  call 016708A0       ; 装备数据
VA01044927  mov ecx,eax
VA01044929  call 011BA6D0       ; 后续读取词条集合
```

`ClientStatic`：成功ACK同步重建窗口，使用现有装备，没有随ACK解码新词条。`CurrentImplementation`：原ACK先发、装备后发，导致窗口停留前次结果，背包及数据库是新结果。字段持久化链条未发现丢失；没有改存储、品质规则、协议body或PVF。

`70Reference`旁证：`df_game_r`的Dispatcher_ChangeRandomOption::process，VA081DC9BA，size2087；成功路径VA081DD0EE调用CUser::SendUpdateItemList，VA081DD18F发送中间装备包，VA081DD1A2才调用SendCmdOkPacket。本条只支持历史通知先于成功ACK的顺序，不作为A21消息布局或原服实帧证据。

## 修复

MagicSealCompletedHandler先构造装备更新，再构造金币更新，最后构造成功ACK。初次解封仍只有装备和金币通知；失败ACK无资产变化。所有通知仍在原事务提交后向当前session发送。

## 自动检查

聚焦回归覆盖成功序列、encoder输出的词条type/value字节、收费一次；清除角色容器并重新选择后，用生产ReplayStateProjectionDelivery读取SQLite及构建快照，比较全101字节装备条目，持久化字节与金币保持。该路径验证快照序列化，不宣称跑过实际客户端登录。

聚焦RED在4c810b6f业务代码下因首包为ACK而失败（编译所需using补齐后才作为行为RED）；修复后的MagicSeal相关18项通过。测试编写过程中纠正了wire字段offset和跨session的旧请求重放方式，最终改用明确的当前状态快照投递，不放宽会话隔离。

Release整解决方案构建：0 warning、0 error。设置PVF_ARCHIVE_PATH为真实A21档案后，Application2663、Protocol1231、Networking86项全部通过（合计3980，0失败，0跳过）；聚焦MagicSeal18项通过。TRX在 `E:/Project/DNF/DNF-JP-86-Server/artifacts/magic-seal-order-fix-20261001/tests/`，汇总tests-final.json。

部署包已经发布到同artifact的runtime；与当前运行包比较，只有Dfo.Server.Protocol.dll哈希改变，配置与其他服务DLL一致。独立审查 `/root/seal_order_review` 判定approved（reviewing-work Code／Deliverable，基线4c810b6f上的6文件范围）。审查独立读取TRX及同SHA PE，未发现提交后NOTI读旧装备的问题。

审查补充 `ClientStatic`：NOTI handler VA0117D720，经0117E064–E072将当前101字节entry交给01088A50；该解析器读取entry+3B的type及+3E/+41的两个品质字节。已有槽对象路径0117EB19–EB21→01089000→01087FB0在本帧解析vector非空时清除该装备+3F4缓存中的词条vector，再经0170C2F0写入本帧新词条。由此支持当前selected装备指针／缓存会随NOTI更新。共同收尾重复调用时vector通常已清空，不将收尾写成无条件重建。运行切换以外部deployment.json为准，不由文档准备状态推断已经部署。

## 尚未验证

S02a当场窗口／背包一致以及S02b关闭重开／真实重登一致仍需用户实机复验。自动快照一致、静态调用链与70参考都不代替GUI验收。未改角色当前词条、UID、金币、PVF或数据库结构。
