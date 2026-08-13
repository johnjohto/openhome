using System.Text.Json;

namespace OpenHome.Formats.Profiles;

/// <summary>
/// A JSON-described save layout for Generation III GBA romhacks that keep the 32-sector
/// flash structure but shift the block contents, designed around pokeemerald-expansion.
/// All offsets are decimal in JSON; hex strings ("0xF2C") are also accepted.
/// </summary>
public sealed record Gba3Profile
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Family { get; init; } = "gba-gen3";

    public int SaveSize { get; init; } = 128 * 1024;
    public string Version { get; init; } = "E";
    public int Language { get; init; } = (int)PKHeX.Core.LanguageID.English;

    public int SectorSize { get; init; } = 0x1000;
    public int SectorDataSize { get; init; } = 0xF80;
    public int MainSectorCount { get; init; } = 14;
    public int SaveSlotCount { get; init; } = 2;

    public SectorFooterLayout Footer { get; init; } = new();
    public BlockLayout TrainerBlock { get; init; } = new() { SectorStart = 0, SectorCount = 1, Size = 0xF2C };
    public BlockLayout PartyBlock { get; init; } = new() { SectorStart = 1, SectorCount = 4, Size = 4 * 0xF80 };
    public BlockLayout StorageBlock { get; init; } = new() { SectorStart = 5, SectorCount = 9, Size = 9 * 0xF80 };
    public TrainerLayout Trainer { get; init; } = new();
    public PartyLayout Party { get; init; } = new();
    public BoxLayout Boxes { get; init; } = new();
    public PokemonLayout Pokemon { get; init; } = new();
    public DetectionRules Detection { get; init; } = new();

    public sealed record SectorFooterLayout
    {
        public int IdOffset { get; init; } = 0xFF4;
        public int ChecksumOffset { get; init; } = 0xFF6;
        public int SignatureOffset { get; init; } = 0xFF8;
        public int CounterOffset { get; init; } = 0xFFC;
        public uint Signature { get; init; } = 0x08012025;
    }

    /// <summary>Which sectors a save block occupies; Size drives per-sector checksum coverage.</summary>
    public sealed record BlockLayout
    {
        public int SectorStart { get; init; }
        public int SectorCount { get; init; }
        public int Size { get; init; }
    }

    public sealed record TrainerLayout
    {
        public int NameOffset { get; init; }
        public int NameMaxLength { get; init; } = 7;
        public int NameStride { get; init; } = 8;
        public int GenderOffset { get; init; } = 8;
        public int IdOffset { get; init; } = 0x0A;
        public int PlayTimeHoursOffset { get; init; } = 0x0E;
        public int PlayTimeMinutesOffset { get; init; } = 0x10;
        public int PlayTimeSecondsOffset { get; init; } = 0x11;
    }

    public sealed record PartyLayout
    {
        public int CountOffset { get; init; } = 0x234;
        public int Offset { get; init; } = 0x238;
        public int Count { get; init; } = 6;
    }

    public sealed record BoxLayout
    {
        public int CurrentBoxOffset { get; init; }
        public int DataOffset { get; init; } = 4;
        public int BoxCount { get; init; } = 14;
        public int SlotsPerBox { get; init; } = 30;
        public int BoxNameOffset { get; init; } = 0x8344;
        public int BoxNameMaxLength { get; init; } = 8;
        public int BoxNameStride { get; init; } = 9;
        public int WallpaperOffset { get; init; } = 0x83C2;
    }

    public sealed record PokemonLayout
    {
        /// <summary>"national" (pokeemerald-expansion) or "gen3Internal" (vanilla RSE/FRLG order).</summary>
        public string SpeciesOrder { get; init; } = "national";
        public ushort MaxSpeciesID { get; init; } = 1500;
        public ushort MaxMoveID { get; init; } = 919;
        public int MaxAbilityID { get; init; } = 310;
        public int MaxItemID { get; init; } = 2300;
        public int MaxBallID { get; init; } = 30;
    }

    public sealed record DetectionRules
    {
        /// <summary>
        /// Claim the save when an occupied slot holds a species value that is impossible in
        /// vanilla gen-3 internal order (252-276 gap, 387-411 gap, or above 412).
        /// </summary>
        public bool ClaimIfNationalSpecies { get; init; } = true;

        /// <summary>Claim every checksum-valid gen-3 save for this profile. Off by default; hijacks vanilla saves.</summary>
        public bool ClaimAllGen3 { get; init; }

        /// <summary>Optional content probes: absolute file offset + expected hex bytes.</summary>
        public ContentRule[] ContentRules { get; init; } = [];
    }

    public sealed record ContentRule
    {
        public int Offset { get; init; }
        public string HexBytes { get; init; } = "";
    }

    public bool NationalSpeciesOrder => !string.Equals(Pokemon.SpeciesOrder, "gen3Internal", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        Converters = { new HexIntConverter(), new HexUIntConverter() },
    };

    public static Gba3Profile Parse(string json)
    {
        var profile = JsonSerializer.Deserialize<Gba3Profile>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty profile JSON.");
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidDataException("Profile is missing a name.");
        return profile;
    }

    public static Gba3Profile Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Accepts "0xF2C" as well as plain numbers, since save layouts are documented in hex.</summary>
    private sealed class HexIntConverter : System.Text.Json.Serialization.JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()!;
                return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt32(text[2..], 16)
                    : int.Parse(text);
            }
            return reader.GetInt32();
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class HexUIntConverter : System.Text.Json.Serialization.JsonConverter<uint>
    {
        public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()!;
                return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToUInt32(text[2..], 16)
                    : uint.Parse(text);
            }
            return reader.GetUInt32();
        }

        public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }
}
