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
    public async Task Object_Not_A_Follow()
    {
        var undo = new Undo { Object = new List<IObjectOrLink> { new Note() } };
        var result = UsersApi.HandleUndo(undo, CreateDb(), new FakeActivityPubService());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task Follow_Has_Bad_Actor_Or_Object()
    {
        // Actor is a Note (not ILink/Person) → actorId is null
        var innerFollow = new Follow
        {
            Actor  = new List<IObjectOrLink> { new Note() },
            Object = new List<IObjectOrLink> { new Note() }
        };
        var undo = new Undo { Object = new List<IObjectOrLink> { innerFollow } };
        var result = UsersApi.HandleUndo(undo, CreateDb(), new FakeActivityPubService());
        await Verify(Describe(result));
    }

    [Fact]
    public async Task Follow_Relation_Not_Found()
    {
        var undo = new Undo { Object = new List<IObjectOrLink> { ValidInnerFollow() } };
        var result = UsersApi.HandleUndo(undo, CreateDb(), new FakeActivityPubService()); // empty DB
        await Verify(Describe(result));
    }

    [Fact]
    public async Task Success_Removes_Relation()
    {
        var db = CreateDb();
        db.Users.Add(new UserInfo("Follower", "https://follower.example.com/users/follower"));
        db.Users.Add(new UserInfo("Bot",      "https://test.example.com/Users/bot"));
        db.FollowRelations.Add(new FollowRelation(
            "https://follower.example.com/users/follower",
            "https://test.example.com/Users/bot"));
        db.SaveChanges();

        var undo = new Undo { Object = new List<IObjectOrLink> { ValidInnerFollow() } };
        var result = UsersApi.HandleUndo(undo, db, new FakeActivityPubService());
        await Verify(Describe(result));
    }
}
