using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Objects;
using Rezrov.ZMachine.Text;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Turning the name on a Version 4 status line back into the
/// object that is the room.
/// </summary>
public class RoomSenseTests
{
    /// <summary>
    /// A small Version 5 world: objects with names and parents, and
    /// nothing else, which is all this reads.
    /// </summary>
    private sealed class World
    {
        private const int Table = 0x100;
        private const int Entry = 14;
        private const int Defaults = 126;

        private readonly byte[] _bytes = new byte[0x4000];
        private readonly List<string> _names = [];
        private readonly List<int> _parents = [];

        public World Add(string name, int parent = 0)
        {
            _names.Add(name);
            _parents.Add(parent);
            return this;
        }

        public ObjectTable Build()
        {
            _bytes[0x00] = 5;
            PutWord(0x04, 0x2000);
            PutWord(0x0E, 0x2000);
            PutWord(0x0A, Table);
            PutWord(0x18, 0x0080);

            var entries = Table + Defaults;

            // [zm 12] The object count is deduced from where the first
            // property table sits, so they begin right after the entries.
            var at = entries + (_names.Count * Entry);

            for (var i = 0; i < _names.Count; i++)
            {
                var address = entries + (i * Entry);
                PutWord(address + 6, _parents[i]);
                PutWord(address + 12, at);

                var text = ZChars.Text(_names[i]);
                _bytes[at] = (byte)(text.Length / 2);
                text.CopyTo(_bytes, at + 1);
                _bytes[at + 1 + text.Length] = 0;
                at += 1 + text.Length + 1;
            }

            var memory = new ZMemory(_bytes);
            var header = new StoryHeader(memory);
            return new ObjectTable(memory, header, new ZTextDecoder(memory, header));
        }

        private void PutWord(int address, int value)
        {
            _bytes[address] = (byte)(value >> 8);
            _bytes[address + 1] = (byte)value;
        }
    }

    // A wood with two rooms of the same name in it, which is the whole
    // difficulty: 1 is the player, 2 and 3 are both called Forest, 4 is
    // the Clearing, and the last two never move.
    private const int Player = 1;
    private const int NorthWood = 2;
    private const int SouthWood = 3;
    private const int Clearing = 4;

    private static ObjectTable Wood() => new World()
        .Add("yourself", Clearing)
        .Add("forest")
        .Add("forest")
        .Add("clearing")
        .Add("lamp", Clearing)
        .Add("leaves", NorthWood)
        .Build();

    /// <summary>
    /// Walks the player around until the sense has seen enough to tell
    /// which object is the player.
    /// </summary>
    private static RoomSense Settled(ObjectTable objects)
    {
        var sense = new RoomSense(objects);

        foreach (var room in new[] { Clearing, NorthWood, Clearing, SouthWood, Clearing, NorthWood })
        {
            objects.SetParent(Player, room);
            sense.Standing(objects.ShortName(room));
        }

        return sense;
    }

    [Fact]
    public void ThePlayerIsTheObjectThatKeepsTurningUpInTheNamedRoom()
    {
        var objects = Wood();

        Assert.Equal(Player, Settled(objects).Player);
    }

    [Fact]
    public void TwoRoomsOfOneNameAreToldApartOnceThePlayerIsKnown()
    {
        // The whole point. Zork has a dozen rooms called Forest, and a
        // map keyed by the name folds them into one.
        var objects = Wood();
        var sense = Settled(objects);

        objects.SetParent(Player, NorthWood);
        Assert.Equal(NorthWood, sense.Standing("Forest")!.Value.Room);

        objects.SetParent(Player, SouthWood);
        Assert.Equal(SouthWood, sense.Standing("Forest")!.Value.Room);
    }

    [Fact]
    public void ASharedNameIsRefusedBeforeThePlayerIsKnown()
    {
        // Two rooms answer to "Forest" and nothing yet says which, so the
        // honest answer is none. A room named once is still an answer.
        var sense = new RoomSense(Wood());

        Assert.Null(sense.Standing("Forest"));
        Assert.Equal(Clearing, sense.Standing("Clearing")!.Value.Room);
    }

    [Fact]
    public void ABarNamingNothingInTheStoryIsNotARoom()
    {
        var sense = new RoomSense(Wood());

        Assert.Null(sense.Standing("Score: 0      Moves: 12"));
        Assert.Null(sense.Standing(""));
        Assert.Null(sense.Standing(null));
    }

    [Fact]
    public void ALabelledFieldIsOfferedToTheObjectTreeLikeAnyOther()
    {
        // One game's bar reads "Year: 2001  Place: Front Lawn", and which
        // label means the room is not something to assume here. Every
        // piece is offered and the story picks.
        var sense = new RoomSense(Wood());

        Assert.Equal(Clearing, sense.Standing("Year: 2001  Place: Clearing")!.Value.Room);
    }

    [Fact]
    public void ThePieceIsFoundWithAScoreBesideIt()
    {
        var sense = new RoomSense(Wood());

        var standing = sense.Standing("Clearing                     Score: 0    Moves: 3");

        Assert.Equal(Clearing, standing!.Value.Room);

        // The name reported is the room object's own, not the strip of
        // the bar it was recognized by.
        Assert.Equal("clearing", standing.Value.Name);
    }

    [Fact]
    public void APlayerInsideSomethingIsStillInTheRoomTheBarNames()
    {
        // Get in the boat and the bar still says which stretch of river
        // you are on. Taking the parent alone would put the boat on the
        // map as a room and keep it there the whole way down.
        var objects = new World()
            .Add("yourself", Clearing)
            .Add("forest")
            .Add("forest")
            .Add("clearing")
            .Add("lamp", Clearing)
            .Add("leaves", NorthWood)
            .Add("boat", Clearing)
            .Build();

        var sense = Settled(objects);
        const int Boat = 7;

        objects.SetParent(Boat, NorthWood);
        objects.SetParent(Player, Boat);

        Assert.Equal(NorthWood, sense.Standing("Forest")!.Value.Room);
    }

    [Fact]
    public void NoPlayerIsFoundWhereTwoThingsNeverPartCompany()
    {
        // A game that spends its whole walkthrough in one room cannot say
        // which of the things in it is the player, so it does not guess.
        // The name still answers, because only one room answers to it.
        var objects = new World()
            .Add("yourself", Clearing)
            .Add("forest")
            .Add("forest")
            .Add("clearing")
            .Add("lamp", Clearing)
            .Build();

        var sense = new RoomSense(objects);
        for (var turn = 0; turn < 8; turn++)
        {
            Assert.Equal(Clearing, sense.Standing("Clearing")!.Value.Room);
        }

        Assert.Equal(0, sense.Player);
    }

    [Fact]
    public void TheRoomIsNotGuessedFromTooFewTurns()
    {
        var objects = Wood();
        var sense = new RoomSense(objects);

        objects.SetParent(Player, NorthWood);
        sense.Standing("Forest");

        // One turn is not evidence, so the shared name is still refused.
        Assert.Equal(0, sense.Player);
        Assert.Null(sense.Standing("Forest"));
    }
}
