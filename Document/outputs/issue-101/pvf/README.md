# 复现说明

本目录只针对 GH #101 的 A21 `Script.pvf` 做静态读取，不启动客户端，不读取或修改运行数据库。

在本目录执行：

```powershell
dotnet run --project .\PvfAdmissionProbe.csproj --configuration Release -- `
  E:\Project\DNF\DNFClient-A21-protocol\Script.pvf
```

预期输入 SHA-256：

```text
19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A
```

输出：

- `summary.md`：范围、声明分布、当前读取器消费核对和建议实机样本。
- `dungeons.tsv`：`dungeon/dungeon.lst` 的 1047 行逐项清单。

`ordinary` 是探针按当前服务端 Maze 选择规则给出的投影分类，不是 PVF 自带类型。PVF 静态声明不能证明原版服务端是否拒绝、何时扣疲劳或客户端 UI 是否允许选择。
