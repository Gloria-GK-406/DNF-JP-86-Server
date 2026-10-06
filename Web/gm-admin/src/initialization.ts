export type InitialContract = { type: number; days: number };
export type InitialItem = { itemId: number; count: number };
export type AccountInitialization = { cera: number; tokenCera: number; happyTokenCera: number; accountCargoLevel: number; contracts: InitialContract[] };
export type CharacterInitialization = { level: number; personalCargoLevel: number; items: InitialItem[] };
export type InitializationDocument<T> = { revision: number; input: T; updatedAt: string | null; updatedBy: string | null };
export type InitializationChoice = { value: number; label: string };
export type InitializationViewData = { account: InitializationDocument<AccountInitialization>; character: InitializationDocument<CharacterInitialization>;
  options: { maximumLevel: number; contracts: InitializationChoice[]; personalCargo: InitializationChoice[]; accountCargo: InitializationChoice[] } };
