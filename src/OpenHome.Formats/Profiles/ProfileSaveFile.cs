using System.Buffers.Binary;
using OpenHome.Core;
using PKHeX.Core;

namespace OpenHome.Formats.Profiles;

/// <summary>
/// A Generation III GBA save (32 flash sectors, two rotating slots) interpreted through a
/// <see cref="Gba3Profile"/> instead of hardcoded RSE/FRLG offsets, for romhacks such as
/// pokeemerald-expansion that keep the sector structure but change block contents and the
/// species table. Entities surface as PK3; with a national-order species table the raw
/// species value is re-mapped on read and restored on write so the game round-trips.
/// </summary>
public sealed class ProfileSaveFile : SaveFile, IBoxDetailName, IBoxDetailWallpaper, ICustomSaveDisplayName
{
    private const int StoredSize = 80;  // gen-3 stored BoxPokemon
    private const int PartySize = 100; // gen-3 party Pokemon

    public Gba3Profile Profile { get; }

    /// <inheritdoc />
    public string SaveDisplayName => $"{GameInfo.GetVersionName(Version)} ({Profile.Name})";

    private readonly byte[] _trainer; // "small" block (SaveBlock2 in pokeemerald terms)
    private readonly byte[] _large;   // SaveBlock1: party, bag, flags
    private readonly byte[] _storage; // PokemonStorage: boxes
    private readonly int _activeSlot;

    private int SectorSize => Profile.SectorSize;
    private int SectorDataSize => Profile.SectorDataSize;
    private int MainSize => Profile.MainSectorCount * SectorSize;

    public ProfileSaveFile(Gba3Profile profile, Memory<byte> data) : base(data)
    {
        Profile = profile;
        _trainer = new byte[profile.TrainerBlock.SectorCount * profile.SectorDataSize];
        _large = new byte[profile.PartyBlock.SectorCount * profile.SectorDataSize];
        _storage = new byte[profile.StorageBlock.SectorCount * profile.SectorDataSize];
        _activeSlot = FindActiveSlot(data.Span, profile);
        ReadSectors(data.Span, _activeSlot);
        Language = profile.Language;
    }

    protected override string ShortSummary => $"{OT} ({Profile.Name}) - {PartyCount} party, {BoxCount} boxes";
    public override string Extension => "sav";
    public override GameVersion Version { get; set; } = GameVersion.E;
    public override byte Generation => 3;
    public override EntityContext Context => EntityContext.Gen3;
    public override IPersonalTable Personal => PersonalTable.RS;
    public override int MaxStringLengthTrainer => Profile.Trainer.NameMaxLength;
    public override int MaxStringLengthNickname => 10;
    public override ushort MaxMoveID => Profile.Pokemon.MaxMoveID;
    public override ushort MaxSpeciesID => Profile.Pokemon.MaxSpeciesID;
    public override int MaxAbilityID => Profile.Pokemon.MaxAbilityID;
    public override int MaxItemID => Profile.Pokemon.MaxItemID;
    public override int MaxBallID => Profile.Pokemon.MaxBallID;
    public override GameVersion MaxGameID => GameVersion.E;
    public override int MaxEV => 255;
    public override ReadOnlySpan<ushort> HeldItems => [];
    public override Type PKMType => typeof(PK3);
    public override PK3 BlankPKM => new();
    public override int SIZE_STORED => StoredSize;
    public override int SIZE_PARTY => PartySize;
    public override int BoxCount => Profile.Boxes.BoxCount;
    public override int BoxSlotCount => Profile.Boxes.SlotsPerBox;
    public override int Language { get; set; }

    public override int GetBoxOffset(int box) => Profile.Boxes.DataOffset + (box * Profile.Boxes.SlotsPerBox * StoredSize);
    public override int GetPartyOffset(int slot) => slot * PartySize;

    protected override Span<byte> BoxBuffer => _storage;
    protected override Span<byte> PartyBuffer => _large.AsSpan(Profile.Party.Offset);

    protected override void DecryptPKM(Span<byte> data) => PokeCrypto.Decrypt3(data);

    protected override PKM GetPKM(Memory<byte> data)
    {
        var pk = new PK3(data);
        if (Profile.NationalSpeciesOrder && pk.SpeciesInternal != 0)
            pk.Species = pk.SpeciesInternal; // raw value is a national dex number; re-map into gen-3 internal order
        return pk;
    }

    protected override void WriteSlotStored(PKM pk, Span<byte> data) => base.WriteSlotStored(Nationalize(pk), data);
    protected override void WriteSlotParty(PKM pk, Span<byte> data) => base.WriteSlotParty(Nationalize(pk), data);

    /// <summary>Writes the raw species as a national number again, undoing the read-time re-map.</summary>
    private PKM Nationalize(PKM pk)
    {
        if (!Profile.NationalSpeciesOrder || pk.Species == 0)
            return pk;
        var clone = ((PK3)pk).Clone();
        clone.SpeciesInternal = pk.Species;
        return clone;
    }

    #region Trainer info

    public override string OT
    {
        get => StringConverter3.GetString(_trainer.AsSpan(Profile.Trainer.NameOffset, Profile.Trainer.NameStride), Language);
        set => StringConverter3.SetString(_trainer.AsSpan(Profile.Trainer.NameOffset, Profile.Trainer.NameStride), value, Profile.Trainer.NameMaxLength, Language);
    }

    public override uint ID32
    {
        get => BinaryPrimitives.ReadUInt32LittleEndian(_trainer.AsSpan(Profile.Trainer.IdOffset));
        set => BinaryPrimitives.WriteUInt32LittleEndian(_trainer.AsSpan(Profile.Trainer.IdOffset), value);
    }

    public override ushort TID16 { get => (ushort)ID32; set => ID32 = (ID32 & 0xFFFF_0000) | value; }
    public override ushort SID16 { get => (ushort)(ID32 >> 16); set => ID32 = (ID32 & 0xFFFF) | ((uint)value << 16); }

    public override byte Gender
    {
        get => _trainer[Profile.Trainer.GenderOffset];
        set => _trainer[Profile.Trainer.GenderOffset] = value;
    }

    public override int PlayedHours
    {
        get => BinaryPrimitives.ReadUInt16LittleEndian(_trainer.AsSpan(Profile.Trainer.PlayTimeHoursOffset));
        set => BinaryPrimitives.WriteUInt16LittleEndian(_trainer.AsSpan(Profile.Trainer.PlayTimeHoursOffset), (ushort)value);
    }

    public override int PlayedMinutes { get => _trainer[Profile.Trainer.PlayTimeMinutesOffset]; set => _trainer[Profile.Trainer.PlayTimeMinutesOffset] = (byte)value; }
    public override int PlayedSeconds { get => _trainer[Profile.Trainer.PlayTimeSecondsOffset]; set => _trainer[Profile.Trainer.PlayTimeSecondsOffset] = (byte)value; }

    public override int PartyCount
    {
        get => _large[Profile.Party.CountOffset];
        protected set => _large[Profile.Party.CountOffset] = (byte)Math.Min(value, Profile.Party.Count);
    }

    public override int CurrentBox
    {
        get => _storage[Profile.Boxes.CurrentBoxOffset];
        set => _storage[Profile.Boxes.CurrentBoxOffset] = (byte)value;
    }

    #endregion

    #region Box names & wallpapers

    public string GetBoxName(int box)
    {
        var layout = Profile.Boxes;
        return StringConverter3.GetString(_storage.AsSpan(layout.BoxNameOffset + (box * layout.BoxNameStride), layout.BoxNameStride), Language);
    }

    public void SetBoxName(int box, ReadOnlySpan<char> value)
    {
        var layout = Profile.Boxes;
        StringConverter3.SetString(_storage.AsSpan(layout.BoxNameOffset + (box * layout.BoxNameStride), layout.BoxNameStride), value, layout.BoxNameMaxLength, Language);
        State.Edited = true;
    }

    public int GetBoxWallpaper(int box) => _storage[Profile.Boxes.WallpaperOffset + box];

    public void SetBoxWallpaper(int box, int value)
    {
        _storage[Profile.Boxes.WallpaperOffset + box] = (byte)value;
        State.Edited = true;
    }

    #endregion

    #region Strings

    public override string GetString(ReadOnlySpan<byte> data) => StringConverter3.GetString(data, Language);
    public override int LoadString(ReadOnlySpan<byte> data, Span<char> text) => StringConverter3.LoadString(data, text, Language);
    public override int SetString(Span<byte> destBuffer, ReadOnlySpan<char> value, int maxLength, StringConverterOption option) =>
        StringConverter3.SetString(destBuffer, value, maxLength, Language, option);

    #endregion

    #region Sectors & checksums

    private Span<byte> BlockForSector(int sectorId)
    {
        if (InRange(sectorId, Profile.StorageBlock))
            return Chunk(_storage, sectorId - Profile.StorageBlock.SectorStart);
        if (InRange(sectorId, Profile.PartyBlock))
            return Chunk(_large, sectorId - Profile.PartyBlock.SectorStart);
        if (InRange(sectorId, Profile.TrainerBlock))
            return Chunk(_trainer, sectorId - Profile.TrainerBlock.SectorStart);
        return [];
    }

    private bool InRange(int sectorId, Gba3Profile.BlockLayout block) =>
        sectorId >= block.SectorStart && sectorId < block.SectorStart + block.SectorCount;

    private Span<byte> Chunk(byte[] buffer, int chunk) => buffer.AsSpan(chunk * SectorDataSize, SectorDataSize);

    private int ChunkChecksumSize(int sectorId)
    {
        Gba3Profile.BlockLayout? block =
            InRange(sectorId, Profile.StorageBlock) ? Profile.StorageBlock :
            InRange(sectorId, Profile.PartyBlock) ? Profile.PartyBlock :
            InRange(sectorId, Profile.TrainerBlock) ? Profile.TrainerBlock : null;
        if (block is null)
            return 0;
        var chunk = sectorId - block.SectorStart;
        return Math.Min(block.Size - (chunk * SectorDataSize), SectorDataSize);
    }

    private void ReadSectors(ReadOnlySpan<byte> data, int slot)
    {
        var start = slot * MainSize;
        for (var i = 0; i < Profile.MainSectorCount; i++)
        {
            var sector = data.Slice(start + (i * SectorSize), SectorSize);
            var id = BinaryPrimitives.ReadInt16LittleEndian(sector[Profile.Footer.IdOffset..]);
            var dest = BlockForSector(id);
            if (dest.Length != 0)
                sector[..SectorDataSize].CopyTo(dest);
        }
    }

    private void WriteSectors(Span<byte> data, int slot)
    {
        var start = slot * MainSize;
        for (var i = 0; i < Profile.MainSectorCount; i++)
        {
            var sector = data.Slice(start + (i * SectorSize), SectorSize);
            var id = BinaryPrimitives.ReadInt16LittleEndian(sector[Profile.Footer.IdOffset..]);
            var src = BlockForSector(id);
            if (src.Length != 0)
                src.CopyTo(sector);
        }
    }

    internal static int FindActiveSlot(ReadOnlySpan<byte> data, Gba3Profile profile)
    {
        var valid0 = SlotValid(data, profile, 0, out var sector0A);
        var valid1 = false;
        var sector0B = 0;
        if (profile.SaveSlotCount > 1)
            valid1 = SlotValid(data, profile, 1, out sector0B);
        if (!valid0)
            return valid1 ? 1 : 0;
        if (!valid1)
            return 0;
        var counterA = BinaryPrimitives.ReadUInt32LittleEndian(data[(sector0A + profile.Footer.CounterOffset)..]);
        var counterB = BinaryPrimitives.ReadUInt32LittleEndian(data[(sector0B + profile.Footer.CounterOffset)..]);
        return counterB > counterA ? 1 : 0;
    }

    internal static bool SlotValid(ReadOnlySpan<byte> data, Gba3Profile profile, int slot, out int sector0Offset)
    {
        var start = slot * profile.MainSectorCount * profile.SectorSize;
        sector0Offset = 0;
        var seen = 0;
        for (var i = 0; i < profile.MainSectorCount; i++)
        {
            var sector = data.Slice(start + (i * profile.SectorSize), profile.SectorSize);
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(sector[profile.Footer.SignatureOffset..]);
            if (signature != profile.Footer.Signature)
                return false;
            var id = BinaryPrimitives.ReadInt16LittleEndian(sector[profile.Footer.IdOffset..]);
            if ((uint)id >= (uint)profile.MainSectorCount)
                return false;
            seen |= 1 << id;
            if (id == 0)
                sector0Offset = start + (i * profile.SectorSize);
        }
        return seen == (1 << profile.MainSectorCount) - 1;
    }

    /// <summary>Gen-3 sector checksum: folded sum of little-endian u32 words over the used bytes.</summary>
    internal static ushort CheckSum32(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i + 4 <= data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
        return (ushort)((sum >> 16) + (sum & 0xFFFF));
    }

    public override bool ChecksumsValid
    {
        get
        {
            var data = Data;
            var start = _activeSlot * MainSize;
            for (var i = 0; i < Profile.MainSectorCount; i++)
            {
                var sector = data.Slice(start + (i * SectorSize), SectorSize);
                var id = BinaryPrimitives.ReadInt16LittleEndian(sector[Profile.Footer.IdOffset..]);
                var size = ChunkChecksumSize(id);
                if (size <= 0)
                    continue;
                var expected = CheckSum32(sector[..size]);
                var actual = BinaryPrimitives.ReadUInt16LittleEndian(sector[Profile.Footer.ChecksumOffset..]);
                if (expected != actual)
                    return false;
            }
            return true;
        }
    }

    public override string ChecksumInfo
    {
        get
        {
            var data = Data;
            var start = _activeSlot * MainSize;
            var bad = new List<int>();
            for (var i = 0; i < Profile.MainSectorCount; i++)
            {
                var sector = data.Slice(start + (i * SectorSize), SectorSize);
                var id = BinaryPrimitives.ReadInt16LittleEndian(sector[Profile.Footer.IdOffset..]);
                var size = ChunkChecksumSize(id);
                if (size <= 0)
                    continue;
                if (CheckSum32(sector[..size]) != BinaryPrimitives.ReadUInt16LittleEndian(sector[Profile.Footer.ChecksumOffset..]))
                    bad.Add(id);
            }
            return bad.Count == 0 ? "Checksums are valid." : $"Invalid sector checksums: {string.Join(", ", bad)}";
        }
    }

    protected override void SetChecksums()
    {
        var data = Data;
        var start = _activeSlot * MainSize;
        for (var i = 0; i < Profile.MainSectorCount; i++)
        {
            var sector = data.Slice(start + (i * SectorSize), SectorSize);
            var id = BinaryPrimitives.ReadInt16LittleEndian(sector[Profile.Footer.IdOffset..]);
            var size = ChunkChecksumSize(id);
            if (size <= 0)
                continue;
            BinaryPrimitives.WriteUInt16LittleEndian(sector[Profile.Footer.ChecksumOffset..], CheckSum32(sector[..size]));
        }
    }

    protected override Memory<byte> GetFinalData()
    {
        WriteSectors(Data, _activeSlot);
        return base.GetFinalData(); // SetChecksums + Data
    }

    protected override SaveFile CloneInternal() => new ProfileSaveFile(Profile, Data.ToArray());

    /// <summary>
    /// Raw (pre-remap) species values of every occupied party/box slot, used by detection.
    /// A slot counts as occupied when its 80-byte record decrypts to a valid gen-3 checksum
    /// with a nonzero species.
    /// </summary>
    internal IEnumerable<ushort> RawOccupiedSpecies()
    {
        for (var box = 0; box < BoxCount; box++)
        {
            for (var slot = 0; slot < BoxSlotCount; slot++)
            {
                if (TryReadRawSpecies(_storage.AsSpan(GetBoxOffset(box) + (slot * StoredSize), StoredSize).ToArray(), out var raw))
                    yield return raw;
            }
        }
        var partyCount = Math.Min(PartyCount, Profile.Party.Count);
        for (var i = 0; i < partyCount; i++)
        {
            if (TryReadRawSpecies(_large.AsSpan(Profile.Party.Offset + (i * PartySize), StoredSize).ToArray(), out var raw))
                yield return raw;
        }
    }

    private static bool TryReadRawSpecies(Span<byte> record, out ushort raw)
    {
        raw = 0;
        if (record.IndexOfAnyExcept((byte)0) < 0 || record.IndexOfAnyExcept((byte)0xFF) < 0)
            return false; // all zero (empty) or all 0xFF (erased flash)
        PokeCrypto.DecryptIfEncrypted3(record);
        var checksum = BinaryPrimitives.ReadUInt16LittleEndian(record[0x1C..]);
        uint sum = 0;
        for (var i = 0x20; i < 0x50; i += 2)
            sum += BinaryPrimitives.ReadUInt16LittleEndian(record[i..]);
        if ((ushort)sum != checksum)
            return false;
        raw = BinaryPrimitives.ReadUInt16LittleEndian(record[0x20..]);
        return raw != 0;
    }

    #endregion
}
