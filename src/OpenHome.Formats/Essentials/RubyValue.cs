namespace OpenHome.Formats.Essentials;

/// <summary>
/// A node in a Ruby Marshal (4.8) object graph. Only the subset of Ruby types that
/// Pokémon Essentials writes into Game.rxdata is modeled; everything Essentials
/// needs (objects as ivar bags, arrays, hashes, strings, symbols, numbers) round-trips
/// losslessly through these types.
/// </summary>
public abstract record RubyValue
{
    public static readonly RubyNil Nil = RubyNil.Instance;
    public static readonly RubyBool True = new(true);
    public static readonly RubyBool False = new(false);

    public static RubyBool FromBool(bool value) => value ? True : False;
}

public sealed record RubyNil : RubyValue
{
    public static readonly RubyNil Instance = new();
    private RubyNil() { }
}

public sealed record RubyBool(bool Value) : RubyValue;

public sealed record RubyInt(long Value) : RubyValue;

public sealed record RubyFloat(double Value) : RubyValue;

/// <summary>A Ruby symbol, e.g. <c>:PIKACHU</c> or the ivar key <c>:@species</c>.</summary>
public sealed record RubySymbol(string Name) : RubyValue;

/// <summary>
/// A Ruby string as raw bytes. Essentials runs RPG Maker XP, whose strings are
/// effectively UTF-8 in v21; <see cref="Ivars"/> carries the encoding marker
/// (ivar <c>E</c> = true) when the source had one.
/// </summary>
public sealed record RubyString(byte[] Bytes) : RubyValue
{
    public Dictionary<string, RubyValue>? Ivars { get; set; }

    public static RubyString FromText(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    public string AsText()
    {
        var span = Bytes.AsSpan();
        var zero = span.IndexOf((byte)0);
        if (zero >= 0)
            span = span[..zero];
        return System.Text.Encoding.UTF8.GetString(span);
    }
}

public sealed record RubyArray(List<RubyValue> Items) : RubyValue
{
    public Dictionary<string, RubyValue>? Ivars { get; set; }
}

public sealed record RubyHash(List<KeyValuePair<RubyValue, RubyValue>> Entries) : RubyValue
{
    public Dictionary<string, RubyValue>? Ivars { get; set; }

    /// <summary>Default value for the '}' hash-with-default form; null for plain hashes.</summary>
    public RubyValue? Default { get; set; }

    public RubyValue? this[string symbolKey]
    {
        get
        {
            foreach (var (k, v) in Entries)
            {
                if (k is RubySymbol s && s.Name == symbolKey)
                    return v;
            }
            return null;
        }
    }
}

/// <summary>A plain Ruby object: a class name plus its instance variables (keys include the '@').</summary>
public sealed record RubyObject(string ClassName) : RubyValue
{
    public Dictionary<string, RubyValue> Ivars { get; } = new(StringComparer.Ordinal);

    public RubyValue? Get(string ivar) => Ivars.TryGetValue(ivar, out var v) ? v : null;

    public string? GetString(string ivar) => Get(ivar) is RubyString s ? s.AsText() : null;

    public long? GetInt(string ivar) => Get(ivar) is RubyInt i ? i.Value : null;

    public RubySymbol? GetSymbol(string ivar) => Get(ivar) as RubySymbol;

    public RubyArray? GetArray(string ivar) => Get(ivar) as RubyArray;

    public RubyObject? GetObject(string ivar) => Get(ivar) as RubyObject;
}

/// <summary>A Ruby object serialized via its _dump method (class name + opaque bytes).</summary>
public sealed record RubyUserDef(string ClassName, byte[] Data) : RubyValue;

/// <summary>A Ruby object serialized via marshal_dump (class name + the dumped value).</summary>
public sealed record RubyUserMarshal(string ClassName, RubyValue Value) : RubyValue;
