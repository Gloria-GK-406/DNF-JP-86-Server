using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

internal sealed class CeraShopPurchaseMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        byte first = 0, second = 0, coupon = 0;
        try
        {
            var cursor = new Cursor(body);
            first = cursor.Byte(); second = cursor.Byte();
            if (second != 0) throw new InvalidDataException("Gift or unknown purchase header is unsupported.");
            var count = cursor.Byte(); coupon = cursor.Byte();
            if (count == 0) throw new InvalidDataException("An empty shopping cart cannot be purchased.");
            var entries = ImmutableArray.CreateBuilder<CeraShopPurchaseEntry>(count);
            for (var index = 0; index < count; index++)
            {
                var mode = cursor.Byte(); var attribute = cursor.Byte(); var variant = cursor.Byte();
                var product = cursor.Int32();
                var countA = cursor.Byte();
                var avatars = ImmutableArray.CreateBuilder<CeraShopAvatarChoice>(countA);
                for (var option = 0; option < countA; option++) avatars.Add(new(cursor.Int32(), cursor.Byte()));
                var countB = cursor.Byte();
                var selections = ImmutableArray.CreateBuilder<CeraShopSelection>(countB);
                for (var option = 0; option < countB; option++) selections.Add(new(cursor.Int32(), cursor.UInt16(), cursor.UInt16()));
                entries.Add(new(mode, attribute, variant, product, avatars.ToImmutable(), selections.ToImmutable(), cursor.Int32(), cursor.UInt16()));
            }
            if (!cursor.AtEnd) throw new InvalidDataException("Trailing purchase bytes.");
            return new CeraShopPurchaseRequest(metadata, new(first, second, coupon, entries.ToImmutable()));
        }
        catch (InvalidDataException failure)
        {
            // A malformed cart is one rejected purchase, never a valid prefix of that purchase.
            return new CeraShopPurchaseRequest(metadata, new(first, second, coupon, [], failure.Message));
        }
    }

    private ref struct Cursor(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> remaining = bytes;
        public bool AtEnd => remaining.IsEmpty;
        private ReadOnlySpan<byte> Take(int count)
        {
            if (remaining.Length < count) throw new InvalidDataException("Truncated purchase entry.");
            var value = remaining[..count]; remaining = remaining[count..]; return value;
        }
        public byte Byte() => Take(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    }
}
