# 角色职业与基础属性规则

日期：2026-09-09。基准提交：`8b19eaac9863e23da4703fa644bfd0cb882b6cd6`。

成长算法为参考代码推导与真实 PVF 数据核对（High），不是客户端实机证明。

## 输入与表示

- 本次档案：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。
- SHA-256：`19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。
- `CharacterCareer` 分别保存 Job、GrowType、Awakening；默认值是 job 0 未转职未觉醒。构造器拒绝 job 超出 0—255、转职或觉醒超出 0—15；属性只读。PackedGrowType 为低四位转职、高四位觉醒，与现存 PvfSkillPointDefinitionSource 的解释一致。
- 可编码不等于支持。属性服务只接受档案明确声明的职业组合。PVF 的 `[growtype N]` 对应 GrowType=N−1；觉醒段属于前面的 growtype。
- 属性支持 job 0—13；创建的既有校验仍仅接受 job 0—12。声明的占位转职有实际数值就保留，不按职业名称推测可创建或可转职状态。

## 成长与查询

原始 Int32/Single 属性在内容源中保留为 double，不提前量化成长值。未声明的属性为零；未声明的职业组合被拒绝。向 CharacterBaseStats 投影时统一乘十，AwayFromZero 四舍五入并检查溢出。

参考服务端 `ServerS4A21` 提交 `2db3a135ad2c51508426f3c1f5b2e030e0dce9d6` 的 `Server/DfoServer/Game/Characters/CharacterStatComputer.cs`，按 ComputeStat/GetGrowth 分段，等级 L 的原始属性为：

`初值 + min(L−1,14)×未转职成长 + clamp(L−15,0,35)×所选转职成长 + max(L−50,0)×所选择醒成长`

Awakening=0 时最后一项仍使用所选转职；Awakening=1/2 分别使用其声明，不累计两个觉醒。这里沿用参考分段，不沿用其 fallback 或逐项过早截断，也不引入参考实现的 Premium HP/MP 加值。装备、Buff、付费等额外贡献不属于本基础属性服务。

`ICharacterStatRuleService.InitializeAsync` 准备全部 1—86 级结果；并发初始化只发布一份完整只读表，取消不发布部分表。初始化后 `SupportedCareers` 给出稳定列表，`GetStats(career, level)` 只查内存，返回共享不可变 CharacterBaseStats；未初始化、未支持组合、越界等级分别明确失败。

创建在规则层初始化并查询未转职 1 级。Host 有档案时在网络 hosted service 启动前准备规则；缺档案时保留原启动行为，内容请求仍明确失败。

## 覆盖与成本

真实档案 token 独立投影为文本，用正则重新提取分段，再用上述闭式算式核对生产结果；未调用生产源取得 oracle 成长向量。174 组合 × 86 级 × 19 属性全部一致，每行重复查询返回同一对象。单独合成表覆盖不同一觉/二觉、分数成长与边界；所有 256 个 packed 值往返和越界组件均验证。

| Job | 声明组合数 |
|---|---:|
| 0 | 16 |
| 1 | 13 |
| 2 | 16 |
| 3 | 16 |
| 4 | 16 |
| 5 | 16 |
| 6 | 16 |
| 7 | 13 |
| 8 | 16 |
| 9 | 3 |
| 10 | 3 |
| 11 | 16 |
| 12 | 7 |
| 13 | 7 |

本次 Debug 测量（含首次打开档案）：初始化 357.947 ms，准备 14,964 行；100 万次查询 59.448 ms。查询校验和 20,186,173,950。耗时受机器负载、缓存和构建模式影响，只报告本次测量，不设性能阈值。

