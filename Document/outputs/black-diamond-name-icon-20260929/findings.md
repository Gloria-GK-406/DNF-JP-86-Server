# 黑钻姓名图标调查

日期：2026-09-29。只读调查；未修改生产代码、数据库或运行服务。

## 结论

已找到现有 NOTI 0002 / subtype0 的姓名菱形图标字段，不需要先猜测一个新 NOTI。
宠物三元组（u32 id、dstr name、u8 alive）之后的第二个字节，写入客户端用户信息的受保护数值 +0x2E8。
姓名显示代码在该值等于 1 时选用菱形图标。
当前服务端 SubtypeZeroFixedTail[1] 固定为 0，是静态可定位的缺口。
字段传递和图标资源选择为 ClientStatic；修改后的自身/他人显示尚未 ClientVerified。

## A21 证据（本节地址均为 VA，ImageBase=0x400000）

取证镜像：DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin。
本次使用本地既有 RVA 镜像，不以原壳 EXE 的哈希代替该镜像身份。复现用同目录 sources.json 中 SHA256。

1. NOTI0002 handler 0x013E91E0 的 subtype0 循环在 0x013E9293 调用 0x013E7340。
2. 0x013E75BF 调用 0x013E1940：读取宠物 u32 / 字符串 / u8。
3. 0x013E75CB 调用 0x013E2CA0：再读一字节并写入用户信息 +0x2E0。函数自己的日志字符串明确名为 CNRDUserInfoPacketHandler::readPremiumPcRoom（字符串 VA03294A70）。
4. 0x013E75D7 调用 0x013E2DB0：再读一字节，零扩展后经受保护数值写入器写 +0x2E8（0x013E2DCA）。这不是紧邻的 PCRoom 布尔位。
5. 姓名显示辅助函数 0x017888A0 从角色取得用户信息；+0x2E8==1 选择资源帧3，否则 +0x2E0 非零选择帧2。帧3装载于 0x017889D2，比较于 0x01788B94；帧2装载于 0x01788931，比较于 0x01788C0F。
6. 第二条姓名显示路径 0x0178A0F0 同样于 0x0178A865 读取 +0x2E8，0x0178A8DB 比较1，命中读取预载的帧3（0x0178A1DF）；否则转查 +0x2E0。
7. 资源路径为 interface/newResult/pcroompremiumreward.img，取自 sprite_interface_newresult.NPK。直接解码其帧2/3：帧2为黄色 PC 字样，帧3为 11×9 菱形图标。见同目录 PNG（放大12倍，最近邻）。因此不是仅凭变量名或上游注释猜测。

## 服务端映射

StateSynchronizationPayloadCodec.Entry.cs 的 EncodeCurrentUserState 在 WriteCreatureTriple 后直接写 BuildSubtypeZeroTail。
该64字节尾块开头为 00 00；当前只动态填写专家职业、经验及一个既有频道字段。
因此最小变更候选为 tail[1] = 黑钻有效 ? 1 : 0；tail[0] 保持原来的 PCRoom 状态，不跟随黑钻开启。
应从既有权益服务查询取得黑钻资格后放入角色公开投影，不让 codec 查询数据库或直接读取配置。
所有复用这一公开外观包的登录/相遇/换装/入队同步都应一致，不能只补自己的登录包。

## 70Reference

df_game_r / CUser::make_basic_info 在 0x0865A845 查询 premiumType12，0x0865A85A 写一字节，随后 0x0865A865 获取 PCRoomNo 并写 u32。
这是旧版公共信息中确实存在权益字段的旁证，不能据此套用 A21 偏移，也不能把70的type12直接当A21的type56。
A21 实际有两个相邻字段，当前已由自身接收代码与图片资源分别确认。

## 边界和最小验收

- 未穷尽 +0x2E8 的全部业务读取点，不宣称它只影响视觉；此次已闭合的范围为上述姓名图标路径。
- 不改 PCRoom 字段：+0x2E0 也参与退出虚弱警告条件，不适合用来替代黑钻显示。
- NOTI0042 可更新契约状态/期限，但本轮没有发现它直接写入这个公共用户信息字段；不将重复发0042作为修复依据。
- 实现后需验证：自己姓名旁出现菱形；另一账号观察也有；切图/回城/换装后仍在；关闭黑钻资格后公开投影写0并移除图标。
- 本轮未修改协议知识库的自动生成字段区；其旧版推断不能替代这里的A21证据。

## 后续实现（2026-09-29）

用户授权后已实施：权益查询进入角色公开状态，统一编码 tail[1]；tail[0]保持不变。未增加协议或数据库字段。

验证：CharacterEntryStateTests 3项通过；BlackDiamondNameIconTests、TownAreaProjectionDeliveryTests、BothScreensDrawTheSameCharacterTests 合计9项通过；Release publish成功。实机姓名图标仍待用户验收，不升级为ClientVerified。

部署至 `artifacts/black-diamond-icon-20260929/runtime`，沿用上一运行版配置及绝对数据库路径；部署前以SQLite backup保存数据库至同级 `before-deploy.db`。未改动角色数据。

## 实机验收（2026-09-29）

用户反馈：“单人和多人验收通过，全部ok。”黑钻姓名图标的单人、多人数据信息投影与显示标记为 **ClientVerified**。此结论不扩展到客户端该字段的其他未调查消费者；关闭权益的逆向测试仍仅有自动化覆盖。
