using OpenHome.Formats.Essentials;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OpenHome.Formats.Tests;

/// <summary>Round-trips of the Marshal subset, plus exact-byte fixtures checked against real Ruby output.</summary>
public class MarshalSubsetTests
{
    [Theory]
    // Exact bytes produced by Ruby's Marshal.dump — writer must match, reader must accept.
    [InlineData(new byte[] { 0x04, 0x08, 0x30 }, "nil")]
    [InlineData(new byte[] { 0x04, 0x08, 0x54 }, "true")]
    [InlineData(new byte[] { 0x04, 0x08, 0x46 }, "false")]
    [InlineData(new byte[] { 0x04, 0x08, 0x69, 0x00 }, "0")]
    [InlineData(new byte[] { 0x04, 0x08, 0x69, 0x2F }, "42")] // i/
    [InlineData(new byte[] { 0x04, 0x08, 0x69, 0xF6 }, "-5")]
    [InlineData(new byte[] { 0x04, 0x08, 0x69, 0x02, 0xE8, 0x03 }, "1000")]
    [InlineData(new byte[] { 0x04, 0x08, 0x69, 0xFE, 0x18, 0xFC }, "-1000")]
    [InlineData(new byte[] { 0x04, 0x08, 0x3A, 0x0A, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, ":hello")]
    public void Exact_ruby_bytes_round_trip(byte[] expected, string kind)
    {
        RubyValue value = kind switch
        {
            "nil" => RubyNil.Instance,
            "true" => RubyBool.True,
            "false" => RubyBool.False,
            ":hello" => new RubySymbol("hello"),
            _ => new RubyInt(long.Parse(kind)),
        };

        Assert.Equal(expected, RubyMarshalWriter.Write(value));
        Assert.Equal(value, RubyMarshalReader.Read(expected));
    }

    [Fact]
    public void String_with_encoding_wrapper_matches_ruby()
    {
        // Marshal.dump("hi") in Ruby 1.9+: I-wrapped string with the E (UTF-8) ivar.
        byte[] expected = [0x04, 0x08, (byte)'I', (byte)'"', 0x07, (byte)'h', (byte)'i', 0x06, (byte)':', 0x06, (byte)'E', (byte)'T'];
        Assert.Equal(expected, RubyMarshalWriter.Write(RubyString.FromText("hi")));
        var read = Assert.IsType<RubyString>(RubyMarshalReader.Read(expected));
        Assert.Equal("hi", read.AsText());
        Assert.Equal(RubyBool.True, read.Ivars?["E"]);
    }

    [Fact]
    public void Wide_fixnums_round_trip()
    {
        foreach (var n in new[] { 122L, 123L, -123L, -124L, 65_535L, -65_536L, (1L << 40), -(1L << 40), long.MaxValue >> 1, long.MinValue >> 1 })
        {
            var read = Assert.IsType<RubyInt>(RubyMarshalReader.Read(RubyMarshalWriter.Write(new RubyInt(n))));
            Assert.Equal(n, read.Value);
        }
    }

    [Fact]
    public void Nested_tree_round_trips()
    {
        var obj = new RubyObject("Pokemon::Move");
        obj.Ivars["@id"] = new RubySymbol("THUNDERSHOCK");
        obj.Ivars["@pp"] = new RubyInt(30);

        var hash = new RubyHash([]);
        hash.Entries.Add(new(new RubySymbol("player"), obj));
        hash.Entries.Add(new(new RubySymbol("values"), new RubyArray([new RubyInt(1), new RubyFloat(2.5), RubyString.FromText("x"), RubyNil.Instance])));
        hash.Default = new RubyInt(0);

        var read = Assert.IsType<RubyHash>(RubyMarshalReader.Read(RubyMarshalWriter.Write(hash)));
        var readObj = Assert.IsType<RubyObject>(read["player"]);
        Assert.Equal("Pokemon::Move", readObj.ClassName);
        Assert.Equal(new RubySymbol("THUNDERSHOCK"), readObj.Get("@id"));
        Assert.Equal(new RubyInt(0), read.Default);
        var array = Assert.IsType<RubyArray>(read["values"]);
        Assert.Equal(new RubyFloat(2.5), array.Items[1]);
    }

    [Fact]
    public void Object_and_symbol_links_are_resolved()
    {
        // [:a, :a] where the second symbol is a link; and [self] where the element links to the array.
        byte[] symbolLinks = [0x04, 0x08, (byte)'[', 0x07, (byte)':', 0x06, (byte)'a', (byte)';', 0x00];
        var array = Assert.IsType<RubyArray>(RubyMarshalReader.Read(symbolLinks));
        Assert.Equal(new RubySymbol("a"), array.Items[0]);
        Assert.Equal(new RubySymbol("a"), array.Items[1]);

        byte[] selfLink = [0x04, 0x08, (byte)'[', 0x06, (byte)'@', 0x00];
        var recursive = Assert.IsType<RubyArray>(RubyMarshalReader.Read(selfLink));
        Assert.Same(recursive, recursive.Items[0]);
    }

    [Fact]
    public void Non_marshal_data_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => RubyMarshalReader.Read(new byte[] { 1, 2, 3, 4 }));
    }
}
