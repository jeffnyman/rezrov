using Rezrov.Glulx.Glk;

namespace Rezrov.Tests;

public class GlkPrototypeTests
{
    [Fact]
    public void ParsesValuesReferencesAndAReturn()
    {
        // [glk #dispatching_func] window_iterate: a window by value, an
        // integer passed out, and a window returned.
        var prototype = GlkPrototype.Parse("3Qa<Iu:Qa");

        Assert.Equal(2, prototype.Parameters.Count);
        Assert.Equal((GlkParameterType.Opaque, 0, GlkReference.None), (prototype.Parameters[0].Type, prototype.Parameters[0].ObjectClass, prototype.Parameters[0].Reference));
        Assert.Equal((GlkParameterType.Number, GlkReference.Out, false), (prototype.Parameters[1].Type, prototype.Parameters[1].Reference, prototype.Parameters[1].AllowsNegative));
        Assert.True(prototype.Parameters[1].PassesOut);
        Assert.False(prototype.Parameters[1].PassesIn);
        Assert.Equal(GlkParameterType.Opaque, prototype.Result!.Type);
    }

    [Fact]
    public void ParsesStructuresArraysAndStrings()
    {
        // [glk #dispatching_func] The specification's own example for
        // select: one mandatory pass-out structure of four fields.
        var select = GlkPrototype.Parse("1<+[4IuQaIuIu]:");
        var structure = Assert.Single(select.Parameters);
        Assert.True(structure.IsStructure);
        Assert.True(structure.Mandatory);
        Assert.Equal(4, structure.Fields!.Count);
        Assert.Equal(GlkParameterType.Opaque, structure.Fields[1].Type);
        Assert.Null(select.Result);

        // stream_open_memory: a retained array of characters passed in
        // and out, two integers, and a stream returned.
        var memory = GlkPrototype.Parse("4&#!CnIuIu:Qb");
        Assert.True(memory.Parameters[0].IsArray);
        Assert.True(memory.Parameters[0].Retained);
        Assert.Equal(GlkReference.InOut, memory.Parameters[0].Reference);
        Assert.Equal(GlkParameterType.Character, memory.Parameters[0].Type);
        Assert.Equal(1, memory.Result!.ObjectClass);

        var putString = GlkPrototype.Parse("2QbS:");
        Assert.Equal(GlkParameterType.Text, putString.Parameters[1].Type);

        var signed = GlkPrototype.Parse("3QbIsIu:");
        Assert.True(signed.Parameters[1].AllowsNegative);

        Assert.Empty(GlkPrototype.Parse("0:").Parameters);
    }

    [Fact]
    public void EveryPublishedPrototypeParses()
    {
        // [glk #selectors] The whole table, as the dispatch layer
        // publishes it.
        Assert.Equal(124, GlkSelectors.All.Count);

        foreach (var function in GlkSelectors.All)
        {
            var prototype = GlkPrototype.Parse(function.Prototype);
            Assert.NotNull(prototype);
        }

        Assert.Equal("window_open", GlkSelectors.Find(0x0023)!.Name);
        Assert.Null(GlkSelectors.Find(0x0006));
    }

    [Theory]
    [InlineData("Qa")]
    [InlineData("1Qz")]
    [InlineData("2Iu:")]
    [InlineData("1I:")]
    public void RejectsMalformedPrototypes(string text)
    {
        Assert.Throws<FormatException>(() => GlkPrototype.Parse(text));
    }
}
