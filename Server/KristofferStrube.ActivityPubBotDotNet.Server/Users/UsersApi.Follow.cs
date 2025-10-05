using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public static partial class UsersApi
{
    private static async Task<Results<BadRequest<string>, Accepted>> Follow(
        IConfiguration configuration, ActivityPubDbContext dbContext, ActivityPubService activityPub, Follow follow, string userUrl, UserInfo user)
    {
        try
        {
            var command = FollowCommand.Create(follow, userUrl, user);
            var ioStuff = await Handle(configuration, dbContext, activityPub, command);
            return TypedResults.Accepted(ioStuff);
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    private static async Task<string> Handle(IConfiguration configuration, ActivityPubDbContext dbContext,
        ActivityPubService activityPub, FollowCommand command)
    {
        if (await activityPub.GetInboxUriAsync(command.ActorOrLink) is not {}  inbox)
        {
            throw new Exception("The User had no inbox specified.");
        }
        await AcceptFollowerInApi(configuration, activityPub, command, inbox);
        return await StoreAcceptInDatabase(dbContext, command);
    }

    private static async Task AcceptFollowerInApi(IConfiguration configuration, ActivityPubService activityPub,
        FollowCommand followCommand, Uri inbox)
    {
        if (!(await activityPub.PostAsync(followCommand.AcceptRequest(configuration), inbox)).IsSuccessStatusCode)
        {
            throw new Exception("Could not send Accept message.");
        }
    }

    private static async Task<string> StoreAcceptInDatabase(ActivityPubDbContext dbContext, FollowCommand followCommand)
    {
        if (await AlreadyFollowed(dbContext, followCommand))
        {
            return "Accepted as the Actor already followed the Object.";
        }

        var dbFollower = await GetorCreateFollower(dbContext, followCommand);

        dbContext.Add(new FollowRelation(dbFollower.Id, followCommand.UserId));
        await dbContext.SaveChangesAsync();

        return "Accepted";
    }

    private static async Task<bool> AlreadyFollowed(ActivityPubDbContext dbContext, FollowCommand followCommand)
    {
        return await dbContext.FollowRelations.FindAsync(followCommand.FollowerId, followCommand.UserId) is not null;
    }

    private static async Task<UserInfo> GetorCreateFollower(ActivityPubDbContext dbContext, FollowCommand followCommand)
    {
        if (await dbContext.Users.FindAsync(followCommand.FollowerId) is { } dbFollower) return dbFollower;

        dbFollower = new("Some Follower", followCommand.FollowerId);
        dbContext.Add(dbFollower);

        return dbFollower;
    }
}