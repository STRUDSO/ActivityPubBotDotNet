using KristofferStrube.ActivityStreams;
using System.Net;
using static KristofferStrube.ActivityPubBotDotNet.Server.Tests.TestHelpers;

namespace KristofferStrube.ActivityPubBotDotNet.Server.Tests;

public class HandleFollowTests
{
    private static Follow ValidFollow(string actorUrl  = "https://follower.example.com/users/follower",
                                      string objectUrl = "https://test.example.com/Users/bot") =>
        new()
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri(actorUrl) } },
            Object = new List<IObjectOrLink> { new Link { Href = new Uri(objectUrl) } }
        };

    private static FakeActivityPubService DefaultFake() =>
        new(inboxUri: new Uri("https://follower.example.com/inbox"), postStatus: HttpStatusCode.OK);

    [Fact]
    public async Task Actor_Is_Null()
    {
        var follow = new Follow
        {
            Actor  = null,
            Object = new List<IObjectOrLink> { new Link { Href = new Uri("https://test.example.com/Users/bot") } }
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), DefaultFake()).HandleFollow("bot", follow));
        Assert.Equal("Follow request had no actor.", ex.Message);
    }

    [Fact]
    public async Task Object_Not_A_Link()
    {
        var follow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri("https://follower.example.com/users/follower") } },
            Object = new List<IObjectOrLink> { new Note() }
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), DefaultFake()).HandleFollow("bot", follow));
        Assert.Equal("The Object was not a Link or did not have a id.", ex.Message);
    }

    [Fact]
    public async Task Object_Id_Does_Not_Match_Inbox()
    {
        var follow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri("https://follower.example.com/users/follower") } },
            Object = new List<IObjectOrLink> { new Link { Href = new Uri("https://other.example.com/Users/bot") } }
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), DefaultFake()).HandleFollow("bot", follow));
        Assert.Equal("The Object Id did not match the address of this inbox.", ex.Message);
    }

    [Fact]
    public async Task GetInboxUri_Returns_Null()
    {
        var fake = new FakeActivityPubService(inboxUri: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), fake).HandleFollow("bot", ValidFollow()));
        Assert.Equal("The User had no inbox specified.", ex.Message);
    }

    [Fact]
    public async Task PostAsync_Fails()
    {
        var fake = new FakeActivityPubService(
            inboxUri: new Uri("https://follower.example.com/inbox"),
            postStatus: HttpStatusCode.InternalServerError);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), fake).HandleFollow("bot", ValidFollow()));
        Assert.Equal("Could not send Accept message.", ex.Message);
    }

    [Fact]
    public async Task Actor_Not_A_Link_After_Post()
    {
        // Actor is a Note: GetInboxUriAsync (fake) returns inbox, POST succeeds,
        // but GetPersonId(Note) returns null → actor id cannot be determined
        var follow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Note() },
            Object = new List<IObjectOrLink> { new Link { Href = new Uri("https://test.example.com/Users/bot") } }
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), DefaultFake()).HandleFollow("bot", follow));
        Assert.Equal("The Actor was not a Link or did not have a id.", ex.Message);
    }

    [Fact]
    public async Task Already_Following()
    {
        var db = CreateDb();
        // FollowRelations.Find uses (followerId, userId) where userId is the route param "bot"
        db.FollowRelations.Add(new FollowRelation("https://follower.example.com/users/follower", "bot"));
        db.SaveChanges();

        var result = await new InboxService(Config(), db, DefaultFake()).HandleFollow("bot", ValidFollow());
        Assert.Equal("Accepted as the Actor already followed the Object.", result);
    }

    [Fact]
    public async Task New_Follower_Not_In_Db()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Bot", "https://test.example.com/Users/bot"));
        db.SaveChanges();

        var fake = DefaultFake();
        var result = await new InboxService(Config(), db, fake).HandleFollow("bot", ValidFollow());
        Assert.Equal("Accepted", result);

        Assert.Equal(1, db.FollowRelations.Count());
        Assert.Equal(2, db.Users.Count()); // bot + follower added
        var accept = Assert.IsType<Accept>(fake.LastPostedObject);
        Assert.Single(accept.Actor!);
        Assert.Single(accept.Object!);
        Assert.StartsWith("https://test.example.com/Activity/", accept.Id);
        Assert.Equal("https://test.example.com/Users/bot", Assert.IsType<Link>(accept.Actor!.First()).Href!.ToString());
    }

    [Fact]
    public async Task New_Follower_Already_In_Users()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Bot",      "https://test.example.com/Users/bot"));
        db.Users.Add(new UserInfo("Follower", "https://follower.example.com/users/follower"));
        db.SaveChanges();

        var fake = DefaultFake();
        var result = await new InboxService(Config(), db, fake).HandleFollow("bot", ValidFollow());
        Assert.Equal("Accepted", result);

        Assert.Equal(1, db.FollowRelations.Count());
        var accept = Assert.IsType<Accept>(fake.LastPostedObject);
        Assert.Single(accept.Actor!);
        Assert.Single(accept.Object!);
        Assert.StartsWith("https://test.example.com/Activity/", accept.Id);
        Assert.Equal("https://test.example.com/Users/bot", Assert.IsType<Link>(accept.Actor!.First()).Href!.ToString());
    }
}
