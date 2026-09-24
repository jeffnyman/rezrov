using Rezrov.Mapping;
using Rezrov.Watching;

namespace Rezrov.Tests;

/// <summary>
/// [mapping] Keeping a game's map from one session to the next.
/// </summary>
public class MapStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "rezrov-maps-" + Guid.NewGuid().ToString("N"));

    private static readonly byte[] Story = [3, 1, 4, 1, 5, 9, 2, 6];
    private static readonly byte[] Another = [2, 7, 1, 8, 2, 8, 1, 8];

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AStoryWithNoMapYetHasNone()
    {
        Assert.Null(new MapStore(Story, _folder).Load());
    }

    [Fact]
    public void TheMapComesBackTheNextTimeTheStoryIsOpened()
    {
        // The whole point: closing the window is not a reason to take
        // away what the player worked out.
        var walk = new MapWalk();
        walk.To("West of House");
        walk.To("North of House", "north");
        walk.To("Behind House", "east");

        Assert.Null(new MapStore(Story, _folder).Save(walk.Graph));

        var back = new MapStore(Story, _folder).Load();

        Assert.NotNull(back);
        Assert.Equal(MapFile.Written(walk.Graph), MapFile.Written(back));
    }

    [Fact]
    public void EachStoryKeepsItsOwnMap()
    {
        var walk = new MapWalk();
        walk.To("West of House");

        new MapStore(Story, _folder).Save(walk.Graph);

        Assert.Null(new MapStore(Another, _folder).Load());
        Assert.NotEqual(
            new MapStore(Story, _folder).Kept,
            new MapStore(Another, _folder).Kept);
    }

    [Fact]
    public void TheStoryIsKnownByItsBytesAndNotItsName()
    {
        // A renamed file keeps the map it had, and two releases of one
        // game keep their own.
        Assert.Equal(
            new MapStore(Story, _folder).Kept,
            new MapStore([.. Story], _folder).Kept);
    }

    [Fact]
    public void AnEmptyMapDoesNotWriteOverARealOne()
    {
        // A story that was opened and closed without reaching a room,
        // or one nothing knows how to map, must not take the map with
        // it on the way out.
        var walk = new MapWalk();
        walk.To("West of House");
        walk.To("North of House", "north");

        var store = new MapStore(Story, _folder);
        store.Save(walk.Graph);
        store.Save(new RoomGraph());

        Assert.Equal(2, store.Load()!.Rooms.Count);
    }

    [Fact]
    public void ForgettingThrowsTheKeptMapAway()
    {
        var walk = new MapWalk();
        walk.To("West of House");

        var store = new MapStore(Story, _folder);
        store.Save(walk.Graph);

        Assert.NotNull(store.Load());

        store.Forget();

        Assert.Null(store.Load());
    }

    [Fact]
    public void ForgettingAMapThatWasNeverKeptIsQuiet()
    {
        new MapStore(Story, _folder).Forget();
    }

    [Fact]
    public void AKeptMapIsWalkedOnRatherThanStartedOver()
    {
        // A room the player walks back into after the map came back is
        // the room that was already there, in the cell it was already
        // in, rather than a second room of the same name somewhere
        // else. That promise is what makes a kept map worth keeping.
        var walk = new MapWalk();
        walk.To("West of House");
        walk.To("North of House", "north");

        var store = new MapStore(Story, _folder);
        store.Save(walk.Graph);

        var watcher = new RoomWatcher(store.Load());
        var where = walk.Where("North of House");

        watcher.Standing(1, "West of House");
        watcher.Typed("north");
        watcher.Standing(2, "North of House");

        Assert.Equal(2, watcher.Graph.Rooms.Count);
        Assert.Equal(where, watcher.Graph.Current!.Position);
    }

    [Fact]
    public void ForgettingLeavesTheWatcherWithNothing()
    {
        var watcher = new RoomWatcher();
        watcher.Standing(1, "West of House");
        watcher.Typed("north");
        watcher.Standing(2, "North of House");

        var said = 0;
        watcher.Changed += () => said++;

        watcher.Forget();

        Assert.Empty(watcher.Graph.Rooms);
        Assert.Null(watcher.Graph.Current);
        Assert.Equal(1, said);
    }
}
