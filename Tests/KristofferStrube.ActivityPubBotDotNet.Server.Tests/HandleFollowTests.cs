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
        var result = await UsersApi.HandleFollow("bot", follow, Config(), CreateDb(), DefaultFake());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task Object_Not_A_Link()
    {
        var follow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri("https://follower.example.com/users/follower") } },
            Object = new List<IObjectOrLink> { new Note() }
        };
        var result = await UsersApi.HandleFollow("bot", follow, Config(), CreateDb(), DefaultFake());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task Object_Id_Does_Not_Match_Inbox()
    {
        var follow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri("https://follower.example.com/users/follower") } },
            Object = new List<IObjectOrLink> { new Link { Href = new Uri("https://other.example.com/Users/bot") } }
        };
        var result = await UsersApi.HandleFollow("bot", follow, Config(), CreateDb(), DefaultFake());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task GetInboxUri_Returns_Null()
    {
        var fake = new FakeActivityPubService(inboxUri: null);
        var result = await UsersApi.HandleFollow("bot", ValidFollow(), Config(), CreateDb(), fake);
        await Verify(Describe(result));
    }

    [Fact]
    public async Task PostAsync_Fails()
    {
        var fake = new FakeActivityPubService(
            inboxUri: new Uri("https://follower.example.com/inbox"),
            postStatus: HttpStatusCode.InternalServerError);
        var result = await UsersApi.HandleFollow("bot", ValidFollow(), Config(), CreateDb(), fake);
        await Verify(Describe(result));
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
        var result = await UsersApi.HandleFollow("bot", follow, Config(), CreateDb(), DefaultFake());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task Already_Following()
    {
        var db = CreateDb();
        // FollowRelations.Find uses (followerId, userId) where userId is the route param "bot"
        db.FollowRelations.Add(new FollowRelation("https://follower.example.com/users/follower", "bot"));
        db.SaveChanges();

        var result = await UsersApi.HandleFollow("bot", ValidFollow(), Config(), db, DefaultFake());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task New_Follower_Not_In_Db()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Bot", "https://test.example.com/Users/bot"));
        db.SaveChanges();

        var fake = DefaultFake();
        var result = await UsersApi.HandleFollow("bot", ValidFollow(), Config(), db, fake);
        await Verify(Describe(result));

        Assert.Equal(1, db.FollowRelations.Count());
        Assert.Equal(2, db.Users.Count()); // bot + follower added
        var accept = Assert.IsType<Accept>(fake.LastPostedObject);
        Assert.Single(accept.Actor!);
        Assert.Single(accept.Object!);
        Assert.Equal("https://test.example.com/Users/bot", Assert.IsType<Link>(accept.Actor!.First()).Href!.ToString());
        Assert.StartsWith("https://test.example.com/Activity/", accept.Id);
    }

    [Fact]
    public async Task New_Follower_Already_In_Users()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Bot",      "https://test.example.com/Users/bot"));
        db.Users.Add(new UserInfo("Follower", "https://follower.example.com/users/follower"));
        db.SaveChanges();

        var fake = DefaultFake();
        var result = await UsersApi.HandleFollow("bot", ValidFollow(), Config(), db, fake);
        await Verify(Describe(result));

        Assert.Equal(1, db.FollowRelations.Count());
        var accept = Assert.IsType<Accept>(fake.LastPostedObject);
        Assert.Single(accept.Actor!);
        Assert.Single(accept.Object!);
        Assert.Equal("https://test.example.com/Users/bot", Assert.IsType<Link>(accept.Actor!.First()).Href!.ToString());
        Assert.StartsWith("https://test.example.com/Activity/", accept.Id);
    }
}
