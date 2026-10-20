# A21 幸运星与租赁武器 ClientStatic 证据

日期：2026-10-20。只读反汇编，不是 WireCaptured 或 ClientVerified。

输入为 `E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，SHA-256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`。manifest 标明 S4A21、image base `0x00400000`、122085376 字节、无读取空洞。下文地址全部为 **RVA**，不是 VA；工作产物目录 `artifacts/lucky-star-rental-20261020/probe-clientstatic.py` 重现指令提取，结果为 `clientstatic-disassembly.txt`。

## 请求 22B 是 body，整帧为 36B

共同调用链为 MakeHeader `0x0234D1E0` → WriteBytes `0x0234CED0` → Finalize。

- MakeHeader 在 `0x0234D2A1` 压入 `0x0E`，`0x0234D2A3` 指向其自己的 header 缓冲区，`0x0234D2AF` 将其追加到网络发送缓冲区；`0x0234D2B9` 将 body 计数置零。
- WriteBytes 在 `0x0234CEF2` 读取长度、`0x0234CEF5` 增加 body 计数，再于 `0x0234CEFD` 追加整个指针指定区域；没有跳过 14B 的分支。
- 因此两个请求各自额外传入的 `0x16` 是 body 长度，不能把其中前 14B 当外层 Game header 再剥掉。其前 14B 在本构包函数内未初始化，应当忽略，不能要求为零或从中读取权限/价格。
- 对照已实现 USE_STACKABLE：`0x00BB6E91..0x00BB6E9B` 同样先 MakeHeader，随后 `0x00BB6EA8` WriteU16、`0x00BB6F21` WriteU8；header 不参与后续字段计数。
- 服务端 `GameClientFrameRequestInfo` 只剥真实外层 14B，decoder 收到本请求的完整 22B body。

## CMD 0x03D0 CHARGE_RENTPOINT

`0x00B281D0` 函数分配局部栈，构包前只写两个字段：

| body offset | 宽度 | 证据 | 语义 |
|---:|---:|---|---|
| 0 | 14 | 从 `[ebp-0x1C]` 传入 WriteBytes，前缀没有本函数写入 | 未初始化冗余区，忽略 |
| 14 | 4 | `0x00B281F7 mov [ebp-0xE],1` | 购买模式 1 |
| 18 | 4 | `0x00B281E9 mov eax,[esi+0x24]`；`0x00B281FE mov [ebp-0xA],eax` | UI 购买数量 |

`0x00B281F2` 指定 CMD 0x03D0，`0x00B2820D..0x00B2821A` 从 `[ebp-0x1C]` 发送 22B。

响应 handler `0x00B29410` 先在 `0x00B2943F` 检查公共 ok。成功在 `0x00B2944E..0x00B29463` 一次读 12B，映射为三个 u32：mode、quantity/result、total。`0x00B2948E` 检查第二值是否为 1，`0x00B29499` 检查第一值是否为 2；该组合进入提示字符串 `0x11D2C` 分支。`0x00B2955C` 的 mode=1 分支使用字符串 `0x11BAF`。最后 `0x00B29585..0x00B2958E` 将第三值写入余额；setter `0x00DF85B0` 写对象+0x7C，getter `0x015216F0` 读取同字段。

因此成功 body 为 `01 + u32 mode + u32 resultOrQuantity + u32 total`（13B）。模式1购买可回原请求数量；**模式2/第二值1是达到余额上限的提示，不能用作正常获得1星的通知**。正常非购买获得可用模式2/第二值0/新总数。

## CMD 0x03CF RENT_EQUIPMENT_ITEM

请求函数起于 `0x00B2C8B0`，`0x00B2C9A3`/`0x00B2C9AD` 只初始化 `[ebp-0x11A]`/`[ebp-0x116]`，前14B没有构包写入。

| body offset | 宽度 | 证据 | 语义 |
|---:|---:|---|---|
| 0 | 14 | `[ebp-0x128]` 的未写前缀 | 忽略 |
| 14 | 4 | `0x00B2CAF3 mov edx,[ebx+edi*8]`；`0x00B2CAFB mov [ebp-0x11A],edx` | 选中目录的装备模板ID |
| 18 | 4 | `0x00B2CAED mov [ebp-0x116],ecx` | 客户端角色上下文，非价格 |

上下文源于 `0x00B2CA09 call 0x02507060`；该函数将两个输入的低16位打包为 u32，不能当花费/租赁天数使用。模板来自目录中8字节对的第一项；同一对第二项在 `0x00B2CB3E` 等位置参与UI费用累计。`0x00B2CE0A` CMD 0x03CF，`0x00B2CE1B..0x00B2CE2B` 发送22B body。

响应 handler `0x00B29350`：公共 ok=0 直接返回；ok非零则 `0x00B2935C..0x00B29366` 读4B结果值。结果0为成功分支（`0x00B29374 → 0x00B293BF`）；1使用字符串 `0x11BA7`；2使用 `0x11BB6`；其他值不进入这三个分支。成功 body 为 `01 00 00 00 00`（5B）。结果1/2是成功信封内的业务错误枚举，不能把其位置放到公共ok字节。

## 本地化提示与费用调整分支

工作产物目录 `artifacts/lucky-star-rental-20261020/ui-strings.txt` 来自只读解码 `DNFClient-A21-protocol/dstr.dat` 的 FILE/102/1042 资源，文件 SHA-256 `DA151F228267746221F13F598A388479266A8ABF21A0AB542E62753EB5DF43D7`。

| 字符串ID | 实际文本 | 触发条件 |
|---|---|---|
| 0x11BA7 | 同时租赁的物品不能超过10件。 | 03CF result=1 |
| 0x11BB6 | 您的物品栏已满， 请清理后再继续。 | 03CF result=2 |
| 0x11D2C | 所持幸运星已达上限999个， 无法再继续获得。 | 03D0 mode=2 且 resultOrQuantity=1 |
| 0x11BAF | 已超出所持幸运星总量上限， 无法继续累积。 | 03D0 mode=1 且新总数未增加 |
| 0x11BAE | 已获得%d颗幸运星 | 03D0 新总数大于当前总数，格式化差值 |

999是本地化旧文案的常量，不是协议上限；10是客户端已有业务错误提示，服务端仍需决定并实施相应持有规则。

请求费用UI存在调整分支：`0x00B2CB01..0x00B2CB0F` 先检查 `0x83C` 对应的客户端功能开关；再于 `0x00B2CB1D` 调 `0x0086E690` 判断租赁管理对象 `+0x5C > 0`。满足时将目录原价传给 `0x0086E680`，后者转调 `0x02737290`，计算截断后的 `原价 × (100 - 调整百分比) × 0.01`。该百分比恰好来自同一对象+0x5C（函数收到对象+0x0C后读取+0x50）。不满足则 `0x00B2CB3E` 直接累计目录价。倍率常量 VA `0x03076030` 在镜像中解为 double 0.01。

仅据此不能断言当前客户端调整开关已启用，也不能断言折扣字段由哪个当前PVF标签设置；服务器不能信任请求中的上下文当价格。实际规则仍须从内容侧核定。body@18最终由两个16位角色/界面值打包，未携带折后报价。

## 两个 CMD 的公共失败信封

A21 接收层 `0x0123BF30` 调 ReadU8 取 ok；`0x0123BF63` 比较0，`0x0123BF6D` 非零则跳过第二个读取；只有 ok=0 时 `0x0123BF73` 再取 reason。`0x0123C069..0x0123C078` 把 reason、ok 和header传给CMD分发器。

失败应至少为 `00 + u8 reason`，不能照搬参照服的裸 `00`。具体 reason 的业务含义沿用已证公共拒绝码；不从本调查编造错误枚举。

## NOTI 0x03C1 EQUIPMENT_RENTAL_LIST

handler `0x00B29FD0` 在 `0x00B2A012`、`0x00B2A01B` 连续调用 ReadU32，依次为余额与条数。余额于 `0x00B2A04C` 调同一 setter `0x00DF85B0`。条数控制 `0x00B2A066..0x00B2A0C5` 循环；每次 `0x00B2A073..0x00B2A080` 读取8B，分别作为两个u32传给 `0x0086EAB0`。

`0x0086EAB9` 读取客户端当前时间，`0x0086EABE..0x0086EAC3` 比较第二个参数必须大于当前时间，随后把第一值与第二值作为一对登记。因此列表项为 `(u32装备模板ID,u32绝对到期秒)`，不是剩余秒数。现有完整列表encoder与此一致。

## 证据边界

以上足以约束本仓 codec 的长度和读取位置；没有本轮购买/租赁实机流量，不证明UI操作、错误提示、非空列表清理与物品下发顺序已经通过实机。价格、限额、期限及产星适用副本属于PVF/服务端规则，不能从这些协议字段推导。
