# A21 普通技能学习样本归档

日期：2026-09-09。仅摘录 CMD 0x001D，排除同号 NOTI 及无关账户报文。

客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。

## 来源 `E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Debug/net10.0/Logs/server-20260909-110648.stdout.log`

归档时源 SHA-256：`CC918ECBC81E9807CBAFBC8E2F1B8A29A9AF7785DF56604C53680A773B1B8F68`；源长度 24234 字节。

原行 152：

```text
Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x001D seq=35 body(11B)=00010900000100CBB10216
```

请求 body：[样本 1](../../../game/client-to-server/samples/0x001D-20260909-1.hex)。

原行 156：

```text
Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x001D seq=41 body(19B)=000309000001070000010C00000700AB8A8960
```

请求 body：[样本 2](../../../game/client-to-server/samples/0x001D-20260909-2.hex)。

原行 158：

```text
Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x001D seq=44 body(11B)=0001090000080087DFD5B3
```

请求 body：[样本 3](../../../game/client-to-server/samples/0x001D-20260909-3.hex)。

原行 230：

```text
Unhandled Game frame Game:03-00-00-00: selector=0x01 type=0x001D seq=31 body(11B)=00010100000200BFD63B2A
```

请求 body：[样本 4](../../../game/client-to-server/samples/0x001D-20260909-4.hex)。

## 来源 `E:/Project/DNF/ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt`

归档时源 SHA-256：`8CB0EF7206D43A60A4C40B3CDE559660E22F5E85A417EE6E1407EDC634E6AE75`；源长度 3991434 字节。

原行 1062：

```text
RECV cmd=0x01 type=0x001D len=41 body=(27B) [00 05 07 00 00 01 4F 00 00 01 03 00 00 01 01 00 00 02 BE 00 00 01 00 99 28 43 7A]
```

请求 body：[样本 5](../../../game/client-to-server/samples/0x001D-20260909-5.hex)。

原行 1064：

```text
SEND cmd=0x01 type=0x001D len=47 body=(32B) [01 00 23 00 00 00 05 03 07 00 01 00 04 4F 00 01 00 05 03 00 01 00 01 01 00 03 00 C6 BE 00 01 00]
```

## 观察边界

当前 Host 的四份请求均未处理；用户最后一次明确将技能1从1级加到3级，报文增量为2。参照服日志给出五条目请求及对应32字节成功应答，但不是本仓实机结果。没有实际失败应答样本。
