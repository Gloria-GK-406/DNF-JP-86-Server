using System.Security.Cryptography;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentMagicSealSeedSource : IEquipmentMagicSealSeedSource
{
    public int NextSeed() => RandomNumberGenerator.GetInt32(int.MaxValue);
}
