using System.Runtime.InteropServices;
using System.Text;
using OpenHome.Core;
using PKHeX.Core;

namespace OpenHome.Formats.Essentials;

/// <summary>
/// A Pokémon Essentials (v21) Game.rxdata exposed as a PKHeX <see cref="SaveFile"/>.
/// The Marshal object tree is kept whole so unknown game state round-trips untouched;
/// party and PC boxes are projected into PK8-backed buffers so the standard SaveFile
/// plumbing (box listing, deposit clear, withdraw write) works unchanged. On write the
/// tree is re-synchronized from the buffers: untouched slots keep their original Pokemon
/// objects (preserving fangame-specific ivars), cleared slots become nil, changed or new
/// slots are rebuilt from the PK8.
/// </summary>
public sealed class EssentialsSaveFile : SaveFile, IBoxDetailName, ICustomSaveDisplayName
{
    public const int PartySlots = 6;
    public const int SlotsPerBox = 30;

    /// <inheritdoc />
    public string SaveDisplayName => "Pokémon Essentials (fangame)";

    private readonly RubyHash _root;
    private readonly RubyObject _player;
    private readonly RubyObject _storage;
    private readonly RubyArray _partyArray;
    private readonly RubyArray _boxes;

    private readonly byte[] _partyBuffer;
    private readonly byte[] _boxBuffer;
    private readonly byte[]?[] _partyOriginal = new byte[PartySlots][];
    private readonly RubyValue?[][] _boxObjects;
    private readonly byte[]?[][] _boxOriginalBytes;

    public string EssentialsVersion { get; }

    public EssentialsSaveFile(Memory<byte> data) : base(data)
    {
        var parsed = RubyMarshalReader.Read(data.Span);
        if (parsed is not RubyHash root)
            throw new InvalidDataException("Not an Essentials v21+ save (top-level Marshal value is not a Hash).");
        _root = root;

        if (root["player"] is not RubyObject player || player.GetArray("@party") is not { } party)
            throw new InvalidDataException("Essentials save has no :player with a @party.");
        if (root["storage_system"] is not RubyObject storage || storage.GetArray("@boxes") is not { } boxes)
            throw new InvalidDataException("Essentials save has no :storage_system with @boxes.");
        _player = player;
        _partyArray = party;
        _storage = storage;
        _boxes = boxes;

        EssentialsVersion = root["essentials_version"] is RubyString v ? v.AsText() : "unknown";

        _partyBuffer = new byte[PartySlots * SIZE_PARTY];
        _boxBuffer = new byte[BoxCount * SlotsPerBox * SIZE_STORED];
        _boxObjects = new RubyValue?[BoxCount][];
        _boxOriginalBytes = new byte[]?[BoxCount][];

        for (var i = 0; i < _partyArray.Items.Count && i < PartySlots; i++)
        {
            if (!EssentialsMapper.IsPokemon(_partyArray.Items[i]))
                continue;
            var pk = EssentialsMapper.ToPK8((RubyObject)_partyArray.Items[i]);
            pk.Data[..SIZE_PARTY].CopyTo(_partyBuffer.AsSpan(GetPartyOffset(i)));
            _partyOriginal[i] = _partyBuffer.AsSpan(GetPartyOffset(i), SIZE_PARTY).ToArray();
        }

        for (var box = 0; box < BoxCount; box++)
        {
            _boxObjects[box] = new RubyValue?[SlotsPerBox];
            _boxOriginalBytes[box] = new byte[]?[SlotsPerBox];
            var slots = GetBoxSlots(box);
            for (var slot = 0; slot < SlotsPerBox; slot++)
            {
                var value = slot < slots.Items.Count ? slots.Items[slot] : RubyNil.Instance;
                _boxObjects[box][slot] = value;
                if (!EssentialsMapper.IsPokemon(value))
                    continue;
                var pk = EssentialsMapper.ToPK8((RubyObject)value);
                var span = _boxBuffer.AsSpan(GetBoxOffset(box) + (slot * SIZE_STORED), SIZE_STORED);
                pk.Data[..SIZE_STORED].CopyTo(span);
                _boxOriginalBytes[box][slot] = span.ToArray();
            }
        }
    }

    private RubyArray GetBoxSlots(int box) =>
        _boxes.Items[box] is RubyObject b && b.GetArray("@pokemon") is { } slots
            ? slots
            : throw new InvalidDataException($"Essentials storage box {box} has no @pokemon array.");

    #region SaveFile contract

    protected override string ShortSummary => $"{OT} - Pokémon Essentials {EssentialsVersion} ({BoxCount} boxes)";
    public override string Extension => "rxdata";
    public override GameVersion Version { get => GameVersion.Any; set { } }
    public override bool ChecksumsValid => true;
    public override string ChecksumInfo => "Ruby Marshal stream; no checksums.";
    public override byte Generation => 8;
    public override EntityContext Context => EntityContext.Gen8;
    public override IPersonalTable Personal => PersonalTable.SWSH;
    public override int MaxStringLengthTrainer => 12;
    public override int MaxStringLengthNickname => 12;
    public override ushort MaxMoveID => (ushort)(GameInfo.Strings.movelist.Length - 1);
    public override ushort MaxSpeciesID => (ushort)(GameInfo.Strings.specieslist.Length - 1);
    public override int MaxAbilityID => GameInfo.Strings.abilitylist.Length - 1;
    public override int MaxItemID => GameInfo.Strings.itemlist.Length - 1;
    public override int MaxBallID => GameInfo.Strings.balllist.Length - 1;
    public override GameVersion MaxGameID => GameVersion.SV;
    public override int MaxEV => 252;
    public override ReadOnlySpan<ushort> HeldItems => [];
    public override Type PKMType => typeof(PK8);
    public override PK8 BlankPKM => new();
    public override int SIZE_STORED => 328;
    public override int SIZE_PARTY => 344;
    public override int BoxCount => _boxes.Items.Count;
    public override int BoxSlotCount => SlotsPerBox;

    public override int GetBoxOffset(int box) => box * SlotsPerBox * SIZE_STORED;
    public override int GetPartyOffset(int slot) => slot * SIZE_PARTY;

    protected override Span<byte> BoxBuffer => _boxBuffer;
    protected override Span<byte> PartyBuffer => _partyBuffer;

    protected override PKM GetPKM(Memory<byte> data) => new PK8(data);
    protected override void DecryptPKM(Span<byte> data) { } // gen 8 entities are not encrypted
    protected override void SetChecksums() { } // Marshal stream has no checksums

    protected override SaveFile CloneInternal() => new EssentialsSaveFile(Write().ToArray());

    public override int PartyCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < PartySlots; i++)
            {
                if (EntityDetection.IsPresent(_partyBuffer.AsSpan(GetPartyOffset(i), SIZE_PARTY)))
                    count++;
            }
            return count;
        }
        protected set { }
    }

    public override string OT
    {
        get => _player.GetString("@name") ?? "";
        set => _player.Ivars["@name"] = RubyString.FromText(value);
    }

    public override uint ID32
    {
        get => (uint)(_player.GetInt("@id") ?? 0);
        set => _player.Ivars["@id"] = new RubyInt(value);
    }

    public override ushort TID16 { get => (ushort)ID32; set => ID32 = (ID32 & 0xFFFF_0000) | value; }
    public override ushort SID16 { get => (ushort)(ID32 >> 16); set => ID32 = (ID32 & 0xFFFF) | ((uint)value << 16); }

    public override int CurrentBox
    {
        get => (int)(_storage.GetInt("@currentBox") ?? 0);
        set => _storage.Ivars["@currentBox"] = new RubyInt(value);
    }

    public override string GetString(ReadOnlySpan<byte> data)
    {
        var chars = MemoryMarshal.Cast<byte, char>(data);
        var end = chars.IndexOf('\0');
        return new string(end < 0 ? chars : chars[..end]);
    }

    public override int LoadString(ReadOnlySpan<byte> data, Span<char> text)
    {
        var s = GetString(data);
        s.AsSpan().CopyTo(text);
        return s.Length;
    }

    public override int SetString(Span<byte> destBuffer, ReadOnlySpan<char> value, int maxLength, StringConverterOption option)
    {
        if (option == StringConverterOption.ClearZero)
            destBuffer.Clear();
        var chars = MemoryMarshal.Cast<byte, char>(destBuffer);
        var length = Math.Min(value.Length, maxLength);
        value[..length].CopyTo(chars);
        if (length < chars.Length)
            chars[length] = '\0';
        return length;
    }

    #endregion

    #region Box names

    public string GetBoxName(int box) =>
        _boxes.Items[box] is RubyObject b ? b.GetString("@name") ?? $"Box {box + 1}" : $"Box {box + 1}";

    public void SetBoxName(int box, ReadOnlySpan<char> value)
    {
        if (_boxes.Items[box] is RubyObject b)
        {
            b.Ivars["@name"] = RubyString.FromText(new string(value));
            State.Edited = true;
        }
    }

    #endregion

    /// <summary>Re-serializes the (synchronized) Marshal tree.</summary>
    protected override Memory<byte> GetFinalData()
    {
        SyncBuffersToTree();
        return RubyMarshalWriter.Write(_root);
    }

    private void SyncBuffersToTree()
    {
        // Party: compact array of present slots, keeping original objects where unchanged.
        var newParty = new RubyArray([]);
        for (var i = 0; i < PartySlots; i++)
        {
            var span = _partyBuffer.AsSpan(GetPartyOffset(i), SIZE_PARTY);
            if (!EntityDetection.IsPresent(span))
                continue;
            if (i < _partyArray.Items.Count && _partyOriginal[i] is { } original &&
                span.SequenceEqual(original) && EssentialsMapper.IsPokemon(_partyArray.Items[i]))
            {
                newParty.Items.Add(_partyArray.Items[i]);
            }
            else
            {
                newParty.Items.Add(EssentialsMapper.FromPK8(new PK8(span.ToArray())));
            }
        }
        _player.Ivars["@party"] = newParty;

        for (var box = 0; box < BoxCount; box++)
        {
            var slots = GetBoxSlots(box);
            while (slots.Items.Count < SlotsPerBox)
                slots.Items.Add(RubyNil.Instance);
            for (var slot = 0; slot < SlotsPerBox; slot++)
            {
                var span = _boxBuffer.AsSpan(GetBoxOffset(box) + (slot * SIZE_STORED), SIZE_STORED);
                if (!EntityDetection.IsPresent(span))
                {
                    slots.Items[slot] = RubyNil.Instance;
                    continue;
                }
                if (_boxOriginalBytes[box][slot] is { } original && span.SequenceEqual(original))
                    continue; // untouched: keep the original Pokemon object with all its fangame ivars
                slots.Items[slot] = EssentialsMapper.FromPK8(new PK8(span.ToArray()));
            }
        }
    }
}
