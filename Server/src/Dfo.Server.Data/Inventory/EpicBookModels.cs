namespace Dfo.Server.Data.Inventory;

public sealed record EpicPieceDefinition(int PieceId, int EquipmentId);
public sealed record EpicPieceBalance(int PieceId, int Count);
public sealed record EpicBookEntry(EpicPieceDefinition Definition, int Count);
public sealed record EpicPieceAddition(int PieceId, int Requested, int Added, int Total);
