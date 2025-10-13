using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;
using static System.Text.Json.JsonSerializer;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class InboxCommand
{
    public InboxCommand(string userId, IObject obj)
    {
        UserId = userId;
        Obj = obj;
    }

    public string UserId { get; }
    public IObject Obj { get; }
}

public class HumbleInboxCommandHandler
{
    private readonly IUserConfiguration _userConfiguration;
    private readonly IDbContext _dbContext;
    private readonly IActivityPub _activityPub;

    public HumbleInboxCommandHandler(IUserConfiguration userConfiguration, IDbContext dbContext, IActivityPub activityPub)
    {
        _userConfiguration = userConfiguration;
        _dbContext = dbContext;
        _activityPub = activityPub;
    }

    public async Task<Results<BadRequest<string>, Accepted>> Execute(InboxCommand inboxCommand)
    {
        var userUrl = _userConfiguration.UserUrl(inboxCommand.UserId);
        var userInfo = _dbContext.Find(userUrl);
        if (userInfo is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        return inboxCommand.Obj switch
        {
            Follow follow => await Follow(inboxCommand, follow, userUrl, userInfo),
            Undo undo => Undo(undo),
            _ => TypedResults.BadRequest("The Object type was not supported.")
        };
    }

    private async Task<Results<BadRequest<string>, Accepted>> Follow(InboxCommand inboxCommand, Follow follow, string userUrl, UserInfo userInfo)
    {
        if (follow.Actor is null)
        {
            return TypedResults.BadRequest("Follow request had no actor.");
        }

        var followerId = _activityPub.FollowerId(follow);
        var objectPersonId = _activityPub.ObjectPersonId(follow);
        if (objectPersonId is null)
        {
            return TypedResults.BadRequest("The Object was not a Link or did not have a id.");
        }

        if (objectPersonId != userUrl)
        {
            return TypedResults.BadRequest("The Object Id did not match the address of this inbox.");
        }

        if (followerId is null)
        {
            return TypedResults.BadRequest("The Actor was not a Link or did not have a id.");
        }

        var inbox = await _activityPub.Uri(follow);
        if (inbox is null)
        {
            return TypedResults.BadRequest("The User had no inbox specified.");
        }

        Accept accept = new Accept()
        {
            Actor = new List<Link>() { new() { Href = new(userUrl) } },
            Id = _userConfiguration.Activity(),
            Object = new List<IObject>() { follow }
        };
        var response = await _activityPub.Accept(accept, inbox);

        if (!response.IsSuccessStatusCode)
        {
            return TypedResults.BadRequest("Could not send Accept message.");
        }

        if (_dbContext.FindRelations(inboxCommand.UserId, followerId) is not null)
        {
            return TypedResults.Accepted("Accepted as the Actor already followed the Object.");
        }

        UserInfo? dbFollower = _dbContext.Find(followerId);
        if (dbFollower is null)
        {
            dbFollower = new("Some Follower", followerId);
            _dbContext.AddUser(dbFollower);
        }

        _dbContext.AddRelation(dbFollower, userInfo);
        _dbContext.SaveChanges();

        return TypedResults.Accepted("Accepted");
    }

    private Results<BadRequest<string>, Accepted> Undo(Undo undo)
    {
        switch (undo.Object?.First())
        {
            case Follow follow:
                var followerId = _activityPub.FollowerId2(follow);
                var objectPersonId = follow.Object?.First();
                if (followerId is null || objectPersonId is not ILink { Href: { } followedId })
                {
                    return TypedResults.BadRequest("Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.");
                }

                FollowRelation? followRelation = _dbContext.FindRelations(followerId, followedId.ToString());
                if (followRelation is null)
                {
                    return TypedResults.BadRequest("Could not Undo Follow because the Actor was not following the Object.");
                }
                _dbContext.RemoveRelation(followRelation);
                _dbContext.SaveChanges();
                return TypedResults.Accepted("Accepted");
            default:
                return TypedResults.BadRequest(Serialize(undo.Object));
        }
    }
}