# 阻塞裁决

## Ready：ID `0x11` 主目录

- 网络态唯一生产链是 `0x0002 subtype 0/3/6 -> CNUser` 与 `0x0009 type 0 -> CParty/roster association`。
- `flushPartyList` 只枚举 CNUser；只有 leader CNUser 代表一个 party，partyId 再去重；renderer 按 partyId
  从 PartyManager 读标题、容量、人数、target 与状态。
- 远端 leader 的 `0x0002` 必须先于观察者收到该 party 的 `0x0009`；反序后仅补 `0x0002` 不会自动回填，
  必须重发 `0x0009`。
- 当前服务端已按 town-visible scope 给双方建立 `0x0002`，并已有 byte-exact `0x0009`；生产缺口是把 roster
  refresh/clear 以 exact-session、同 town-visible 范围补给非成员观察者，并接入 entry/move/mutation/cleanup。
- 满员 party 不被 flush 删除，只在 renderer 中置灰；实现不得把“满员即移出目录”写成规则。

## Removed：错误 blocker

- matching-info 来自本地 `Etc/PartyMatchingInfo.etc`，不是服务端协议前置。
- `0x023C` manager 与 `0xF88CA0/0xF89520` 的逐行消费属于 ID `0x14` 候补窗；ID `0x11` 的
  `0xF749xx` 只查询当前 party 是否已登记，不用它生成、筛选或渲染主目录行。
- `0x0183` 不在 ID `0x11` 最小网络数据链上；未知 sender/body 不阻塞主目录。
- 因此主目录不新增 `0x023C` codec、online directory store、record 字段或 per-channel directory gate。

## Preserved runtime frontier

- 用 final vptr `0x31FFFD4` 命中 ID `0x11` 的 `0xF73A10/0xF71950`。
- 两客户端证实 `0x0002 -> 0x0009` 后可见、设置/成员变化更新、离开/解散/换区/断线无幽灵行。
- category 过滤在当前静态控制流中被 `myServer` 分支覆盖；server-group 当前服务端只能表达 group 0。
  这些限制必须据实记录，不能把最小基础列表描述成完整三标签筛选。
