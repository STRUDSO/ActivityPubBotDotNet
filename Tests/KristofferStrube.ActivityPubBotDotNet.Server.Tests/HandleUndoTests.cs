using KristofferStrube.ActivityStreams;
using static KristofferStrube.ActivityPubBotDotNet.Server.Tests.TestHelpers;

namespace KristofferStrube.ActivityPubBotDotNet.Server.Tests;

public class HandleUndoTests
{
    private static Follow ValidInnerFollow(
        string actorUrl  = "https://follower.example.com/users/follower",
        string objectUrl = "https://test.example.com/Users/bot") =>
        new()
        {
            Actor  = new List<IObjectOrLink> { new Link { Href = new Uri(actorUrl) } },
            Object = new List<IObjectOrLink> { new Link { Href = new Uri(objectUrl) } }
        };

    [Fact]
    public void Object_Not_A_Follow()
    {
        var undo = new Undo { Object = new List<IObjectOrLink> { new Note() } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), new FakeActivityPubService()).HandleUndo(undo));
        Assert.Equal("[{\"@context\":\"https://www.w3.org/ns/activitystreams\",\"type\":\"Note\"}]", ex.Message);
    }

    [Fact]
    public void Follow_Has_Bad_Actor_Or_Object()
    {
        // Actor is a Note (not ILink/Person) → actorId is null
        var innerFollow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Note() },
            Object = new List<IObjectOrLink> { new Note() }
        };
        var undo = new Undo { Object = new List<IObjectOrLink> { innerFollow } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), new FakeActivityPubService()).HandleUndo(undo));
        Assert.Equal("Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.", ex.Message);
    }

    [Fact]
    public void Follow_Relation_Not_Found()
    {
        var undo = new Undo { Object = new List<IObjectOrLink> { ValidInnerFollow() } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => new InboxService(Config(), CreateDb(), new FakeActivityPubService()).HandleUndo(undo));
        Assert.Equal("Could not Undo Follow because the Actor was not following the Object.", ex.Message);
    }

    [Fact]
    public void Success_Removes_Relation()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Follower", "https://follower.example.com/users/follower"));
        db.Users.Add(new UserInfo("Bot",      "https://test.example.com/Users/bot"));
        db.FollowRelations.Add(new FollowRelation(
            "https://follower.example.com/users/follower",
            "https://test.example.com/Users/bot"));
        db.SaveChanges();

        var undo = new Undo { Object = new List<IObjectOrLink> { ValidInnerFollow() } };
        var result = new InboxService(Config(), db, new FakeActivityPubService()).HandleUndo(undo);
        Assert.Equal("Accepted", result);

        Assert.Equal(0, db.FollowRelations.Count());
    }
}
