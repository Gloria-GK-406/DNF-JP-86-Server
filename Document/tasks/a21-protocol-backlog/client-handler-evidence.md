# A21 已登记但立即返回的接收入口

## 直接证据

A21 归档内存镜像 ClientEvidence/dumps/dnf-dump-20260822-184128-028-16280/DNF-memory-rva.bin 在 RVA 0x01E31720 的首字节为 C3，x86 指令为 ret。镜像长 122,085,376 字节，SHA-256 为 9377c89c378751dd49d8b34de71281b0885e3bea6b2b00ba7859e30d9c2c0584，映像基址 0x00400000。对缺口中 997 个 S→C 身份按登记 RVA 读取入口窗口，得到 51 个 CMD 响应和 50 个 NOTI 推送映射到此入口。按 A21 静态登记读取，当前这些入口在进入处理函数后立即返回。

归档重建 PE ClientEvidence/binaries/DNF.unpacked-b1-full-e5a34bbf.exe（SHA-256 e5a34bbfcf5f7da99a71cbe6c4a7d10dd8ed700c68059f2c7ddafa3f141c0ee8）在 VA 0x02231720 的小窗口复核结果一致。ClientHack 也记录了这一共享地址。 [ClientHack 探查](https://github.com/Gloria-GK-406/DNF-JP-86-ClientHack/blob/6f40e149ec10452dac9b599803aafc90e2f587f3/Docs/a21-protocol/proto-0.md#L319) · [一个明确例子](../../../protocol/game/server-to-client/NOTI-0x0043.md)

## 判定边界

- 采用完整身份的接收方向判定，即 S→C、CMD 或 NOTI、type。在分批台账中标为「已退役」，含义限于 A21 当前静态登记入口没有消息正文消费或功能动作。同号请求的可达性和调用者单独取证。
- 同一入口共有 104 个索引身份。初筛外的 3 个已有出站注册，为 CMD 0x0028、0x032C、0x032D。逐条对账又证实初筛中的 CMD 0x0294 由攻坚的共用编码器实际发送。余下 100 个本轮候选用此证据判断接收端为空。当前发送帧的兼容边界需另用运行线路及版本证据验证。
- 共享地址本身只说明多个编号使用同一函数。其他共享入口如果有条件处理、转发或字段消费，继续按同一编号在当前玩法的作用判断。
