using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Notices;

public enum NoticeGroup { Primary = 0, Secondary = 1 }
public enum NoticeOwnerType { Account = 0, Character = 1 }

public readonly record struct NoticeOwner(NoticeOwnerType Type, int Id)
{
    public static NoticeOwner ForAccount(AccountId id) => new(NoticeOwnerType.Account, id.Value);
    public static NoticeOwner ForCharacter(CharacterId id) => new(NoticeOwnerType.Character, id.Value);
}
