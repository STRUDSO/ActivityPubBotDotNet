using KristofferStrube.ActivityStreams;
using System.Net;
using static KristofferStrube.ActivityPubBotDotNet.Server.Tests.TestHelpers;

namespace KristofferStrube.ActivityPubBotDotNet.Server.Tests;

public class InboxDispatcherTests
{
    [Fact]
    public async Task User_Not_Found()
    {
        var db = CreateDb();
        var result = await UsersApi.Inbox("nonexistent", new Note(), Config(), db, new InboxService(Config(), db, new FakeActivityPubService()));
        Assert.Equal("400: User could not be found.", Describe(result));
    }

    [Fact]
    public async Task Unknown_Object_Type()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Bot", "https://test.example.com/Users/bot"));
        db.SaveChanges();

        var result = await UsersApi.Inbox("bot", new Note(), Config(), db, new InboxService(Config(), db, new FakeActivityPubService()));
        Assert.Equal("400: The Object type was not supported.", Describe(result));
    }

    [Fact]
    public async Task Routes_To_Follow_Happy_Path()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Bot", "https://test.example.com/Users/bot"));
        db.SaveChanges();

        var fake = new FakeActivityPubService(
            inboxUri: new Uri("https://follower.example.com/inbox"),
            postStatus: HttpStatusCode.OK);

        var follow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri("https://follower.example.com/users/follower") } },
            Object = new List<IObjectOrLink> { new Link { Href = new Uri("https://test.example.com/Users/bot") } }
        };

        var result = await UsersApi.Inbox("bot", follow, Config(), db, new InboxService(Config(), db, fake));
        Assert.Equal("202: Accepted", Describe(result));
    }
}
