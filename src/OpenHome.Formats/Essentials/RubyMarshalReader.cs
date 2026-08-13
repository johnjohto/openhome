namespace OpenHome.Formats.Essentials;

/// <summary>
/// Reader for the Ruby Marshal 4.8 subset that Pokémon Essentials (v21) writes.
/// Supported: nil, booleans, fixnums (all widths), floats, strings (with ivar
/// encoding wrapper), symbols and symbol links, arrays, hashes (with and without
/// default), plain objects, user-defined (_dump) and user-marshal values, bignums
/// that fit in a long, and object links. Structs, classes/modules, regexps and
/// extension wrappers are rejected with a clear error since Essentials never emits
/// them for party/PC storage data.
/// </summary>
public sealed class RubyMarshalReader
{
    private readonly byte[] _data;
    private int _pos;
    private readonly List<string> _symbols = [];
    private readonly List<RubyValue> _objects = [];

    private RubyMarshalReader(byte[] data) => _data = data;

    /// <summary>Parses one Marshal stream. <paramref name="trailing"/> is how many bytes follow the value.</summary>
    public static RubyValue Read(ReadOnlySpan<byte> data, out int trailing)
    {
        var reader = new RubyMarshalReader(data.ToArray());
        reader.ExpectHeader();
        var value = reader.ReadValue();
        trailing = reader._data.Length - reader._pos;
        return value;
    }

    public static RubyValue Read(ReadOnlySpan<byte> data) => Read(data, out _);

    private void ExpectHeader()
    {
        if (_data.Length < 2 || _data[0] != 0x04 || _data[1] != 0x08)
            throw new InvalidDataException("Not a Ruby Marshal 4.8 stream.");
        _pos = 2;
    }

    private byte ReadByte()
    {
        if (_pos >= _data.Length)
            throw new EndOfStreamException("Marshal stream ended unexpectedly.");
        return _data[_pos++];
    }

    private byte[] ReadBytes(int count)
    {
        if (count < 0 || _pos + count > _data.Length)
            throw new EndOfStreamException("Marshal stream ended unexpectedly.");
        var result = _data[_pos..(_pos + count)];
        _pos += count;
        return result;
    }

    /// <summary>Ruby's fixnum encoding: small values inline, wider ones with a byte-count prefix.</summary>
    private long ReadFixnum()
    {
        var c = (sbyte)ReadByte();
        if (c == 0)
            return 0;
        if (c > 4)
            return c - 5;
        if (c < -4)
            return c + 5;
        var length = Math.Abs(c);
        long value = c > 0 ? 0 : -1;
        for (var i = 0; i < length; i++)
        {
            var b = (long)ReadByte();
            value = (value & ~(0xFFL << (8 * i))) | (b << (8 * i));
        }
        return value;
    }

    private string ReadSymbolBody()
    {
        var length = ReadFixnum();
        if (length > 1 << 20)
            throw new InvalidDataException($"Implausible symbol length {length}.");
        return System.Text.Encoding.UTF8.GetString(ReadBytes((int)length));
    }

    private void Track(RubyValue value) => _objects.Add(value);

    private RubyValue ReadValue()
    {
        var type = (char)ReadByte();
        switch (type)
        {
            case '0': return RubyNil.Instance;
            case 'T': return RubyBool.FromBool(true);
            case 'F': return RubyBool.FromBool(false);
            case 'i': return new RubyInt(ReadFixnum());
            case 'f':
            {
                var text = ReadSymbolBody();
                var value = text switch
                {
                    "nan" => double.NaN,
                    "inf" => double.PositiveInfinity,
                    "-inf" => double.NegativeInfinity,
                    _ => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                };
                var f = new RubyFloat(value);
                Track(f);
                return f;
            }
            case ':':
            {
                var name = ReadSymbolBody();
                _symbols.Add(name);
                return new RubySymbol(name);
            }
            case ';':
            {
                var index = ReadFixnum();
                if ((ulong)index >= (ulong)_symbols.Count)
                    throw new InvalidDataException($"Symbol link {index} out of range.");
                return new RubySymbol(_symbols[(int)index]);
            }
            case '@':
            {
                var index = ReadFixnum();
                if ((ulong)index >= (ulong)_objects.Count)
                    throw new InvalidDataException($"Object link {index} out of range.");
                return _objects[(int)index];
            }
            case '"':
            {
                var length = ReadFixnum();
                if (length > _data.Length)
                    throw new InvalidDataException($"Implausible string length {length}.");
                var s = new RubyString(ReadBytes((int)length));
                Track(s);
                return s;
            }
            case 'I':
            {
                // Instance-variable wrapper (used for string encoding in Ruby 1.9+).
                var inner = ReadValue();
                var count = ReadFixnum();
                if (count > 100)
                    throw new InvalidDataException($"Implausible ivar count {count}.");
                var ivars = new Dictionary<string, RubyValue>();
                for (var i = 0; i < count; i++)
                {
                    var key = ReadValue();
                    if (key is not RubySymbol sym)
                        throw new InvalidDataException("Ivar key is not a symbol.");
                    ivars[sym.Name] = ReadValue();
                }
                switch (inner)
                {
                    case RubyString s: s.Ivars = ivars; break;
                    case RubyArray a: a.Ivars = ivars; break;
                    case RubyHash h: h.Ivars = ivars; break;
                    default:
                        // Ivars on other value kinds are irrelevant for save data; keep the value.
                        break;
                }
                return inner;
            }
            case '[':
            {
                var count = ReadFixnum();
                if (count > 1 << 22)
                    throw new InvalidDataException($"Implausible array length {count}.");
                var array = new RubyArray(new List<RubyValue>((int)Math.Min(count, 4096)));
                Track(array);
                for (var i = 0; i < count; i++)
                    array.Items.Add(ReadValue());
                return array;
            }
            case '{':
            case '}':
            {
                var count = ReadFixnum();
                if (count > 1 << 22)
                    throw new InvalidDataException($"Implausible hash size {count}.");
                var hash = new RubyHash(new List<KeyValuePair<RubyValue, RubyValue>>((int)Math.Min(count, 4096)));
                Track(hash);
                for (var i = 0; i < count; i++)
                    hash.Entries.Add(new KeyValuePair<RubyValue, RubyValue>(ReadValue(), ReadValue()));
                if (type == '}')
                    hash.Default = ReadValue();
                return hash;
            }
            case 'o':
            {
                if (ReadValue() is not RubySymbol className)
                    throw new InvalidDataException("Object class name is not a symbol.");
                var obj = new RubyObject(className.Name);
                Track(obj);
                var count = ReadFixnum();
                if (count > 1 << 16)
                    throw new InvalidDataException($"Implausible ivar count {count}.");
                for (var i = 0; i < count; i++)
                {
                    if (ReadValue() is not RubySymbol key)
                        throw new InvalidDataException("Ivar key is not a symbol.");
                    obj.Ivars[key.Name] = ReadValue();
                }
                return obj;
            }
            case 'u':
            {
                if (ReadValue() is not RubySymbol className)
                    throw new InvalidDataException("User-defined class name is not a symbol.");
                var length = ReadFixnum();
                if (length > _data.Length)
                    throw new InvalidDataException($"Implausible user-defined length {length}.");
                var u = new RubyUserDef(className.Name, ReadBytes((int)length));
                Track(u);
                return u;
            }
            case 'U':
            {
                if (ReadValue() is not RubySymbol className)
                    throw new InvalidDataException("User-marshal class name is not a symbol.");
                var u = new RubyUserMarshal(className.Name, ReadValue());
                Track(u);
                return u;
            }
            case 'l':
            {
                // Bignum: sign byte, then a fixnum count of little-endian u16 halves.
                var sign = ReadByte();
                var halves = ReadFixnum();
                if (halves > 8)
                    throw new InvalidDataException("Bignum too large for a long.");
                ulong magnitude = 0;
                for (var i = 0; i < halves; i++)
                    magnitude |= (ulong)(ReadByte() | (ReadByte() << 8)) << (16 * i);
                var value = sign == '-' ? -(long)magnitude : (long)magnitude;
                var big = new RubyInt(value);
                Track(big);
                return big;
            }
            default:
                throw new NotSupportedException(
                    $"Unsupported Marshal type '{type}' (0x{(byte)type:X2}) at offset {_pos - 1}. " +
                    "Only the Essentials save-data subset is supported.");
        }
    }
}
