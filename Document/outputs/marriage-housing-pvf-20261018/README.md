# 婚房 PVF 调查机械输出

本目录由 `HousingPvfScanner` 从以下当前 PVF 只读导出：

- `artifacts/sao-ticket-20261014/Script.server.pvf`
- SHA-256：`91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`
- 仓库 HEAD：`61beac4036eaad20a92f7c74483bf097764eb543`

扫描结果：

- 家具：商店正数 token 75 个、唯一商品 75 件、位置配置 75 条，集合差为空；地板 2、壁纸 3、窗户 11、装饰物 59。先前“约 76”是粗略计数误差，没有被排除项。
- 目标 ID 引用：454 条。
- 关键词命中：515 条。关键词导出包含大量无关的普通“传送/扩张”文本，必须结合路径和上下文筛选，不能按命中数推断功能。

文件：

- `furniture.tsv`：75 件家具的 ID、名称、页签、心意点费用、位置与 action 路径。
- `key-pvf-dumps.md`：城镇、地图、NPC、商店、婚房配置、树、戒指和目标物品的原始 token 展开。
- `target-references.tsv`：家具及夫妻系统目标 ID 在整个 PVF 中的整数引用上下文。
- `text-hits.tsv`：婚房相关中文关键词的全 PVF 文本命中。
- `scanner/`：可重复运行的临时扫描器源码；`bin/`、`obj/` 和二进制不纳入 Git 归档。
- `sha256.txt`：上述文本工件的 SHA-256。

扫描器通过 `HintPath` 读取预先构建的 `Tool/PvfProbe/bin/Release/net10.0/Dfo.Pvf.dll`。若该文件不存在，先构建 `Tool/PvfProbe/PvfProbe.csproj`，再复跑扫描器：

```powershell
dotnet build Tool/PvfProbe/PvfProbe.csproj -c Release
dotnet run --project Document/outputs/marriage-housing-pvf-20261018/scanner/HousingPvfScanner.csproj -c Release -- artifacts/sao-ticket-20261014/Script.server.pvf Document/outputs/marriage-housing-pvf-20261018
```

该扫描器只输出静态内容证据。它不证明商店、房间、摆放、树、戒指或传送在 A21 客户端上可达。

Git 归档文本统一使用 LF，并去除展示文本的行尾空格与文件尾空行；`sha256.txt` 对应归档后的文件。原始 token 的布局、ID、数字与 TSV 字段边界保持不变。
