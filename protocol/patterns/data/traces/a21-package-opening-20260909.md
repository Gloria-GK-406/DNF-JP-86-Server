# A21 魔盒请求观察

2026-09-09 点击开启当前持有的 60 个魔盒；当时服务端尚无 decoder。以下是服务端解密后入站日志中的两次请求，不是客户端已接受成功回包的证据。

来源：`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Logs\server-20260909-222234.stdout.log`，第 82、84 行。

```text
Unhandled Game frame Game:01-00-00-00: selector=0x01 type=0x0450 seq=34 body(15B)=00420048B398007A0047B398003C00
Unhandled Game frame Game:01-00-00-00: selector=0x01 type=0x0450 seq=35 body(15B)=00420048B398007A0047B398003C00
```

body 15 字节，list=0，礼包 slot=66/item=10007368，钥匙 slot=122/item=10007367，count=60。重复点击使用不同 seq；seq 不能作为业务幂等键。
