# 阶段1：马战协议覆盖与共享依赖复核

2026-10-06。只读固定A21 dump与前轮证据，未修改产品、客户端、部署或数据库。**ClientStatic**；Confirmed仅表示此dump中的指令明确，High为有界调查支持，Unknown保留未知。本次不是WireCaptured或ClientVerified。

固定dump SHA256重新实算一致：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。image base=`00400000`；下表主要使用VA，并另列RVA，避免坐标混用。

## 覆盖结论

阶段1可以称为“**全部已定位的马战专属协议与相关依赖**”，不能称全程序已经证明没有其他消息。

- 局部manager注册函数VA `004D2870` / RVA `000D2870`完整覆盖7个S2C注册：CMD ACK04E6、04E7、04E8与NOTI04B8、04B9、04BA、04BB。
- 加上3个C2S request writer，目录应有10个方向条目，对应7个不同opcode。ACK与request应分别列布局、触发、UI作用和未知，不能合并成一条已知双向body。
- 通用NOTI006C为已确认功能前提；活动key093D属于共享EventManager数据，不是马战专属opcode。
- service45构造/reset、UI ensure/show、本地scene/tick、PVF配置、本地库存都是生命周期或客户端依赖，不能计作新增网络opcode。
- 库存更新与邮件交互应列为共享系统依赖；本轮没有把它们的完整schema重新逆向完成，不得把既有catalog中的上游含义或A12机器区升级为本次A21确认结果。

“7个注册完整”是局部函数事实。前轮501项形状扫描、13处派生RTTI代码引用、service45候选及006C闭环支持覆盖判断，但raw E8/E9、padding函数边界和固定注册pattern不是全程序CFG或指针别名证明。

## 两道入口门与生命周期

| 项目 | VA / RVA | Confirmed结果与边界 |
|---|---|---|
| 通用活动入口 | `025ABDC9` / `021ABDC9`；map检查`014172D0` / `010172D0` | EventManager+18有093D才允许打开结果UI1FC；缺项弹12BC0。是否配置完整不能仅由key存在推出 |
| 006C网络入口 | `01419E30` / `01019E30` | parser014196B0→条件tail01417650→新旧map同步01416BD0→事件应用0141A1E0 |
| 093D typed对象工厂 | `00C5E180` / `0085E180` | 支持0843..08AE；093D超界返回空。后置特殊分派至08EF，093D默认返回；这条确认链不为service45写非零state |
| service45创建 | `0140E039..0140E06F` / `0100E039..0100E06F` | 分配12B、调用004D29A0构造、以key45登记。构造未显式写manager+4，因此初值不是已证明的0 |
| service45 reset | `005B64C0` / `001B64C0` | vtable+8将manager+4清零，没有该函数内的UI/网络副作用 |
| reset调用链 | `014151A0→0140D030→0140CEC0→vt+8` | 按字符串service key操作；不能命名为登录/session reset。登录是否调用、何时调用仍Unknown |
| UI状态门 | `004F60C8/004F60CF` / `000F60C8/000F60CF` | 先发送04E6，再立即同步读state；0弹12BC0并拒绝。不会在这个函数中等待ACK后再检查 |
| ensure与show | `023060F0` / `01F060F0`；`02304C70` / `01F04C70` | 04E6成功ACK只ensure1FB；B9/BA也可ensure。ensure不能表述为已显示，ACK不能表述为赋state |

High，有界：已调查的马战特定路径只有04B8非零state赋值，未确认另一条原生静默同步state的消息。006C建立活动存在门，不能替代state通知；构造和reset也不提供已确认的合法轮次state初始化。

006C条件tail在新map同步之前查看旧manager event9。parser是否有其他间接修改未全排除，不能简单按新包是否带event9决定尾段，也不能把006C整体称作安全静默重放。活动移除093D会令入口gate拒绝打开，但已解析变化链没有直接重置马战state；已打开窗口如何关闭仍Unknown。

## 库存是实际数据依赖，不能从投注ACK推导扣除时序

本次重读VA `004F4300..004F4640`，例如VA `004F44A9/004F4532/004F45AC/004F4635`从global `03A5C9BC`调用共享helper `016EF580`，材料ID来自manager配置getter `028D71B0`。结合前轮UI初始化路径的`016EE6C0`，这是客户端读取现有库存与材料输入状态，而不是ACK自身携带库存扣除结果。

固定dump普通代码范围内对`016EF580`有359个、对`016EE6C0`有887个raw E8/E9候选引用，散布多个模块；候选数仅用于说明不能将这些helper当作马战独占，不是有效caller总数证明。

因此应将共享库存投影作为投注可见性依赖。静态证据不证明“04E7成功ACK到达前库存必须扣除”，也不证明“ACK后必定自动收到某个库存通知”。模拟时间线应明确库存通知是否已应用、ACK刷新读取的是哪个库存版本，另将04BA投注合计投影区分开。

已有`protocol/game/server-to-client/NOTI-0x000E.md`是共享库存协议记录；其机器区注明A12，人工A21段有其他取证。这里不据该机器区定位或字段含义宣称本轮完成A21 000E布局，也不将000E纳入马战专属7注册。

## 邮件是奖励交付的共享系统，不是已证赛果reader副作用

旧`mail-system-investigation/86-client.md`的邮件结论使用2026-09-22另一份dump，本轮只将其作为地址线索，并在本次固定dump重新读：

- VA `01189CC0` / RVA `00D89CC0`，新邮件提示消费入口；
- VA `01198E70` / RVA `00D98E70`，邮件列表消费入口的前部；
- VA `0111B340` / RVA `00D1B340`，附件领取ACK入口。

这些入口和马战局部manager注册、state setter、结果场景consumer不是同一组函数。本轮没有发现并确认“收到04BB或切scene12会原生创建奖励邮件”的链，也没有完成所有邮件入口/间接调用的否定证明。

邮件投递、领取、库存增加、保存与在线提示的业务时序应标为共享邮件领域方案或CurrentImplementation。若阶段2展示邮件领奖，可引用共享系统记录并显式标成模拟业务流程；不能把发奖规则、邮件数量、金币或物品变化从马战NOTI字段中推导出来。

## 首14B和模拟阶段必须保留的未知

前轮三条C2S writer本地传入长度为04E6/04E8各14B、04E7为23B；只有04E7尾部`knight u8 + slot i32 + amount i32`明确赋值。首14B在局部writer未见赋值，公共frame header另行生成，不可直接混称。

payload低层append进入VA `05790FE9`混淆转接，目标跳出本镜像。**最终wire首14B是否保留、改写或填充仍Unknown。** 模拟器可使用明确标记的占位前缀演示本地结构，不得声称零填充或任意字节是已验证有效原生请求，亦不能命名为token/随机垃圾/认证字段。

阶段2可演示已确认reader/scene规则，但包时间线与ACK/NOTI实际相对次序、state5原服时点、活动关闭、重连reset、库存刷新、奖励邮件和低层首14B应分别保留证据状态；模拟结果不是WireCaptured或ClientVerified。

## 可复核工件

- 本轮`coverage-check.py`：只读固定dump、重新hash、定点反汇编与共享库存helper raw候选引用。
- 本轮`coverage-raw.txt`：构造、完整局部注册、reset、打开state门、typed factory、投注库存读取、上述邮件入口的VA/RVA/字节。
- 上轮`../joust-client-reanalysis-20261006/initialization.md`及`initialization-*`：manager/service45、006C/093D闭环和全量派生RTTI候选范围。
- 上轮`../joust-client-reanalysis-20261006/protocol-set.md`及原始证据：request/ACK、get-or-create/show、库存路径、501项注册扫描和首14B低层边界。

本次只做阶段1覆盖评审，未修改协议代理的catalog，未开始模拟器或HTML实现。
