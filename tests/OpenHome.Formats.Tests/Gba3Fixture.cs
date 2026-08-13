using System.Buffers.Binary;
using OpenHome.Formats.Profiles;
using PKHeX.Core;

namespace OpenHome.Formats.Tests;

/// <summary>
/// Builds a synthetic pokeemerald-expansion-layout save in code: 32 sectors of 4 KB,
/// slot 0 with sector ids 0-13 (SaveBlock2 / SaveBlock1 / PokemonStorage per the shipped
/// profile), slot 1 blank. Species are written as raw national-order values (as the
/// expansion stores them). No copyrighted data: every byte is generated.
/// </summary>
public static class Gba3Fixture
{
    private const int SectorSize = 0x1000;
    private const int SectorData = 0xF80;

    public static string ShippedProfilePath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles", "pokeemerald-expansion.json"));

    public static byte[] BuildSave(Gba3Profile profile, bool nationalOrder = true)
    {
        var data = new byte[profile.SaveSize];

        var trainer = new byte[SectorData];
        StringConverter3.SetString(trainer, "RED", 7, 2);
        trainer[profile.Trainer.GenderOffset] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(trainer.AsSpan(profile.Trainer.IdOffset), 0x12345678);
        BinaryPrimitives.WriteUInt16LittleEndian(trainer.AsSpan(profile.Trainer.PlayTimeHoursOffset), 1);
        trainer[profile.Trainer.PlayTimeMinutesOffset] = 2;
        trainer[profile.Trainer.PlayTimeSecondsOffset] = 3;

        var large = new byte[4 * SectorData];
        large[profile.Party.CountOffset] = 1;
        var partyMon = PartyMon(nationalOrder ? (ushort)255 : (ushort)280, nickname: "Ember"); // Torchic
        partyMon.CopyTo(large.AsSpan(profile.Party.Offset));

        var storage = new byte[9 * SectorData];
        storage[profile.Boxes.CurrentBoxOffset] = 0;
        var pikachu = StoredMon(25, 0x11112222, "Pika", moves: [84, 98, 45, 39]);
        pikachu.CopyTo(storage.AsSpan(profile.Boxes.DataOffset));
        // Treecko: raw 252 (national, conclusive) or 277 (gen-3 internal, vanilla-ambiguous).
        var treecko = StoredMon(nationalOrder ? (ushort)252 : (ushort)277, 0x33334444, "Woody", moves: [45, 73],
            ivs: (31, 30, 29, 28, 27, 26), evs: (4, 252, 0, 252, 0, 0));
        treecko.CopyTo(storage.AsSpan(profile.Boxes.DataOffset + 80));
        for (var box = 0; box < profile.Boxes.BoxCount; box++)
            StringConverter3.SetString(
                storage.AsSpan(profile.Boxes.BoxNameOffset + (box * profile.Boxes.BoxNameStride), profile.Boxes.BoxNameStride),
                $"BOX {box + 1}", profile.Boxes.BoxNameMaxLength, 2);

        WriteBlock(data, profile, 0, trainer, profile.TrainerBlock, 1);
        WriteBlock(data, profile, 1, large, profile.PartyBlock, 2);
        WriteBlock(data, profile, 5, storage, profile.StorageBlock, 3);
        return data;
    }

    private static void WriteBlock(byte[] data, Gba3Profile profile, int firstSectorId, byte[] block, Gba3Profile.BlockLayout layout, uint counter)
    {
        for (var i = 0; i < layout.SectorCount; i++)
        {
            var sector = data.AsSpan((firstSectorId + i) * SectorSize, SectorSize);
            block.AsSpan(i * SectorData, SectorData).CopyTo(sector);
            var checksumSize = Math.Min(layout.Size - (i * SectorData), SectorData);
            BinaryPrimitives.WriteInt16LittleEndian(sector[profile.Footer.IdOffset..], (short)(firstSectorId + i));
            BinaryPrimitives.WriteUInt16LittleEndian(sector[profile.Footer.ChecksumOffset..], CheckSum32(sector[..checksumSize]));
            BinaryPrimitives.WriteUInt32LittleEndian(sector[profile.Footer.SignatureOffset..], profile.Footer.Signature);
            BinaryPrimitives.WriteUInt32LittleEndian(sector[profile.Footer.CounterOffset..], counter);
        }
    }

    private static ushort CheckSum32(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i + 4 <= data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
        return (ushort)((sum >> 16) + (sum & 0xFFFF));
    }

    private static PK3 MakeMon(ushort rawSpecies, uint pid, string nickname, ushort[] moves, (int, int, int, int, int, int)? ivs, (int, int, int, int, int, int)? evs)
    {
        var pk = new PK3 { PID = pid, TID16 = 0x5678, SID16 = 0x1234, Language = 2 };
        pk.SpeciesInternal = rawSpecies; // raw value, exactly as the game stores it
        pk.Nickname = nickname;
        pk.OriginalTrainerName = "RED";
        if (moves.Length > 0) pk.Move1 = moves[0];
        if (moves.Length > 1) pk.Move2 = moves[1];
        if (moves.Length > 2) pk.Move3 = moves[2];
        if (moves.Length > 3) pk.Move4 = moves[3];
        pk.FixMoves();
        if (ivs is { } iv)
            (pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD) = iv;
        if (evs is { } ev)
            (pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPE, pk.EV_SPA, pk.EV_SPD) = ev;
        pk.RefreshChecksum();
        return pk;
    }

    private static byte[] StoredMon(ushort rawSpecies, uint pid, string nickname, ushort[] moves,
        (int, int, int, int, int, int)? ivs = null, (int, int, int, int, int, int)? evs = null)
    {
        var dest = new byte[80];
        MakeMon(rawSpecies, pid, nickname, moves, ivs, evs).WriteEncryptedDataStored(dest);
        return dest;
    }

    private static byte[] PartyMon(ushort rawSpecies, string nickname)
    {
        var dest = new byte[100];
        MakeMon(rawSpecies, 0x55556666, nickname, [33], null, null).WriteEncryptedDataParty(dest);
        return dest;
    }
}
