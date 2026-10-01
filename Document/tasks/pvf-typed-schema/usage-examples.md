# PVF 读取典型写法

字段声明只保存名称、类型和校验规则，不保存任何档案的字符串偏移。游戏字段放在 Infrastructure 的主题解析器中；Dfo.Pvf 只提供通用机制。

## 普通可选标量

以下是当前 PvfEquipmentDurability 的读取方式，缺失耐久仍按该调用方的旧行为返回0：

```csharp
private static class Fields
{
    internal static readonly PvfField<int> DurabilityField =
        PvfField.Int32("[durability]", optional: true,
            minimum: 0, maximum: ushort.MaxValue);
}

var document = PvfDocument.Open(archive, path).AtRoot();
return document.TryRead(Fields.DurabilityField, out var value)
    ? checked((ushort)value)
    : (ushort)0;
```

存在但类型、数量、范围不合法时抛 InvalidDataException，包含来源路径和字段；不会进入缺失分支。其他调用方若需要缺失返回null，可以声明 OptionalUInt16 并使用 Read，其结果为 ushort?。

## 合法重复字段

当前 DropItemPool 对根层重复声明逐个验证，然后保留旧的首个非负值投影：

```csharp
var document = PvfDocument.Open(archive, path).AtRoot();
var declarations = document.ReadAll(Fields.CreationRateField);
```

重复不应一律视为错误。例如副本 minimum required level 合法使用最后一次声明；每次声明仍完整验证，错误的旧声明不能被后续合法值掩盖。

## 混合表和嵌套块

纯整数列表使用 Int32List，数字列表使用 Numbers，字符串列表使用 Strings。真正混合的行可使用 Custom<T>，完整验证后返回解析结果；保留专业流式解析器的块使用 Tokens 声明，由该解析器检查行宽、类型、范围和作用域。Tokens 本身不会替专业解析器完成这些检查。

QuotedString 载荷中的 minimap、hellparty 标记指定对应 token kind；普通 Identifier 字段不能与它们混用。未知字段和运行时探针查询仍可保留，固定查询通过声明访问。
