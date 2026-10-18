# CMD 0x03F5 WEDDING_USE_LOVE_POINT_ITEM

ClientStatic / Confirmed（A21 RVA00891250）：正文5B，u8 ok=1 + i32 status。status0成功不本地扣物；-2原生dstr72913心意点上限；-3..-5 dstr72844只能已婚使用；其它失败为通用错误。

CurrentImplementation：成功后绝对背包同步，再发送双方NOTI03D3；同序重发只恢复当前背包和点数，不重新发送成功ACK、不重复结算。详见[修复报告](../../../Document/MarriageWeddingSixIssues-20261017.md)。本期原生双端待用户验收，不能标ClientVerified。
