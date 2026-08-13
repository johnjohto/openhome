namespace OpenHome.Formats.Essentials;

/// <summary>
/// Writer for the Ruby Marshal 4.8 subset. Symbols are emitted inline every time
/// (symbol links and object links are a compression Ruby permits but does not
/// require), which keeps the writer trivially correct for any tree of
/// <see cref="RubyValue"/> nodes. Output loads with <c>Marshal.load</c> in a real
/// Essentials v21 game, and with <see cref="RubyMarshalReader"/>.
/// </summary>
public sealed class RubyMarshalWriter
{
    private readonly MemoryStream _stream = new();

    public static byte[] Write(RubyValue value)
    {
        var writer = new RubyMarshalWriter();
        writer._stream.WriteByte(0x04);
        writer._stream.WriteByte(0x08);
        writer.WriteValue(value);
        return writer._stream.ToArray();
    }

    private void WriteByte(byte b) => _stream.WriteByte(b);

    private void WriteBytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    private void WriteFixnum(long value)
    {
        if (value == 0)
        {
            WriteByte(0);
            return;
        }
        if (value is > 0 and < 123)
        {
            WriteByte((byte)(value + 5));
            return;
        }
        if (value is < 0 and > -124)
        {
            WriteByte((byte)(value - 5));
            return;
        }
        var bytes = new List<byte>(4);
        var x = value;
        do
        {
            bytes.Add((byte)(x & 0xFF));
            x >>= 8;
        }
        while (x is not (0 or -1));
        if (value > 0 && (bytes[^1] & 0x80) != 0)
            bytes.Add(0);
        if (value < 0 && (bytes[^1] & 0x80) == 0)
            bytes.Add(0xFF);
        if (bytes.Count > 4)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Fixnum long form holds at most 4 bytes.");
        WriteByte((byte)(sbyte)(value > 0 ? bytes.Count : -bytes.Count));
        WriteBytes(bytes.ToArray());
    }

    /// <summary>'i' fixnum for 31-bit-range values, 'l' bignum beyond that (as Ruby does).</summary>
    private void WriteInt(long value)
    {
        if (value >> 31 is 0 or -1)
        {
            WriteByte((byte)'i');
            WriteFixnum(value);
            return;
        }
        WriteByte((byte)'l');
        WriteByte((byte)(value < 0 ? '-' : '+'));
        var magnitude = (ulong)(value < 0 ? -value : value);
        var halves = new List<byte>(8);
        while (magnitude != 0)
        {
            halves.Add((byte)(magnitude & 0xFF));
            halves.Add((byte)((magnitude >> 8) & 0xFF));
            magnitude >>= 16;
        }
        WriteFixnum(halves.Count / 2);
        WriteBytes(halves.ToArray());
    }

    private void WriteSymbolName(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        WriteFixnum(bytes.Length);
        WriteBytes(bytes);
    }

    private void WriteIvars(Dictionary<string, RubyValue>? ivars)
    {
        if (ivars is null || ivars.Count == 0)
            WriteFixnum(1);
        else
            WriteFixnum(ivars.Count + 1);
        // Always mark strings as UTF-8, the Essentials v21 encoding.
        WriteValue(new RubySymbol("E"));
        WriteValue(RubyBool.True);
        if (ivars is not null)
        {
            foreach (var (key, v) in ivars)
            {
                WriteValue(new RubySymbol(key));
                WriteValue(v);
            }
        }
    }

    private void WriteValue(RubyValue value)
    {
        switch (value)
        {
            case RubyNil:
                WriteByte((byte)'0');
                break;
            case RubyBool b:
                WriteByte((byte)(b.Value ? 'T' : 'F'));
                break;
            case RubyInt i:
                WriteInt(i.Value);
                break;
            case RubyFloat f:
            {
                WriteByte((byte)'f');
                var text = f.Value switch
                {
                    double.NaN => "nan",
                    double.PositiveInfinity => "inf",
                    double.NegativeInfinity => "-inf",
                    _ => f.Value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
                };
                WriteSymbolName(text);
                break;
            }
            case RubySymbol sym:
                WriteByte((byte)':');
                WriteSymbolName(sym.Name);
                break;
            case RubyString s:
                WriteByte((byte)'I');
                WriteByte((byte)'"');
                WriteFixnum(s.Bytes.Length);
                WriteBytes(s.Bytes);
                WriteIvars(s.Ivars);
                break;
            case RubyArray a:
                if (a.Ivars is not null)
                    WriteByte((byte)'I');
                WriteByte((byte)'[');
                WriteFixnum(a.Items.Count);
                foreach (var item in a.Items)
                    WriteValue(item);
                if (a.Ivars is not null)
                    WriteIvars(a.Ivars);
                break;
            case RubyHash h:
                if (h.Ivars is not null)
                    WriteByte((byte)'I');
                WriteByte((byte)(h.Default is null ? '{' : '}'));
                WriteFixnum(h.Entries.Count);
                foreach (var (k, v) in h.Entries)
                {
                    WriteValue(k);
                    WriteValue(v);
                }
                if (h.Default is not null)
                    WriteValue(h.Default);
                if (h.Ivars is not null)
                    WriteIvars(h.Ivars);
                break;
            case RubyObject o:
                WriteByte((byte)'o');
                WriteValue(new RubySymbol(o.ClassName));
                WriteFixnum(o.Ivars.Count);
                foreach (var (key, v) in o.Ivars)
                {
                    WriteValue(new RubySymbol(key));
                    WriteValue(v);
                }
                break;
            case RubyUserDef u:
                WriteByte((byte)'u');
                WriteValue(new RubySymbol(u.ClassName));
                WriteFixnum(u.Data.Length);
                WriteBytes(u.Data);
                break;
            case RubyUserMarshal u:
                WriteByte((byte)'U');
                WriteValue(new RubySymbol(u.ClassName));
                WriteValue(u.Value);
                break;
            default:
                throw new NotSupportedException($"Cannot marshal {value.GetType().Name}.");
        }
    }
}
