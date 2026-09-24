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
    public void AMacKeepsItsMapsWhereAMacKeepsThings()
    {
        // The runtime answers ~/.local/share on macOS as well, because
        // it shares the Unix implementation, and a file left there is
        // somewhere no Mac user would think to look. This is the one
        // platform choice that cannot be run on the machine it is
        // written on, so it is checked as a decision instead.
        Assert.Equal(
            Path.Combine("/Users/someone", "Library", "Application Support"),
            MapStore.Folder(apple: true, "/Users/someone", "/Users/someone/.local/share"));
    }

    [Fact]
    public void EverywhereElseTakesWhatTheRuntimeOffers()
    {
        // Windows gives %LOCALAPPDATA% and Linux gives XDG_DATA_HOME
        // or ~/.local/share, both of which are already right.
        Assert.Equal(
            @"C:\Users\someone\AppData\Local",
            MapStore.Folder(apple: false, @"C:\Users\someone", @"C:\Users\someone\AppData\Local"));

        Assert.Equal(
            "/home/someone/.local/share",
            MapStore.Folder(apple: false, "/home/someone", "/home/someone/.local/share"));
    }

    [Fact]
    public void AMacWithNowhereToCallHomeFallsBack()
    {
        Assert.Equal("/fallback", MapStore.Folder(apple: true, "", "/fallback"));
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
    public void ATurnThatFoundNothingNewWritesNothing()
    {
        // Kept once a turn, and most turns find no new room, so the
        // disk is touched when the map grows rather than for every
        // command the player types.
        var walk = new MapWalk();
        walk.To("West of House");

        var store = new MapStore(Story, _folder);
        store.Save(walk.Graph);

        File.Delete(store.Kept);
        store.Save(walk.Graph);

        // Written again, because the file it was compared against is
        // not there any more. Without that check a map deleted from
        // underneath would never come back.
        Assert.True(File.Exists(store.Kept));

        var written = File.GetLastWriteTimeUtc(store.Kept);
        File.SetLastWriteTimeUtc(store.Kept, written.AddDays(-1));

        store.Save(walk.Graph);

        Assert.Equal(written.AddDays(-1), File.GetLastWriteTimeUtc(store.Kept));
    }

    [Fact]
    public void ATurnThatFoundARoomWritesIt()
    {
        var walk = new MapWalk();
        walk.To("West of House");

        var store = new MapStore(Story, _folder);
        store.Save(walk.Graph);

        walk.To("North of House", "north");
        store.Save(walk.Graph);

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
