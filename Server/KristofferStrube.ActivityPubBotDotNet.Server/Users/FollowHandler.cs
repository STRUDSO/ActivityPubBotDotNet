using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class FollowHandler(ActivityPubDbContext dbContext, ActivityPubService activityPub, IConfiguration configuration)
{
    public async Task<Results<BadRequest<string>, Accepted>> Follow_(string userId, Follow follow)
    {
        if (follow.Actor is null)
        {
            return TypedResults.BadRequest("Follow request had no actor.");
        }
        if (activityPub.GetPersonId(follow.Object?.First()) is not string objectPersonId)
        {
            return TypedResults.BadRequest("The Object was not a Link or did not have a id.");
        }
        if (objectPersonId != $"{configuration["HostUrls:Server"]}/Users/{userId}")
        {
            return TypedResults.BadRequest("The Object Id did not match the address of this inbox.");
        }
        var inbox = await InboxUrl(follow);
        if (inbox is null)
        {
            return TypedResults.BadRequest("The User had no inbox specified.");
        }

        var response = await Accept(userId, follow, inbox);

        if (!response.IsSuccessStatusCode)
        {
            return TypedResults.BadRequest("Could not send Accept message.");
        }
        if (activityPub.GetPersonId(follow.Actor.First()) is not string followerId)
        {
            return TypedResults.BadRequest("The Actor was not a Link or did not have a id.");
        }

        return await Follow(userId, followerId);
    }

    protected virtual async Task<Uri?> InboxUrl(Follow follow)
    {
        Uri? inbox = await activityPub.GetInboxUriAsync(follow.Actor.First());
        return inbox;
    }

    protected virtual async Task<Results<BadRequest<string>, Accepted>> Follow(string userId,
        string followerId)
    {
        if (await dbContext.FollowRelations.FindAsync(followerId, userId) is not null)
        {
            return TypedResults.Accepted("Accepted as the Actor already followed the Object.");
        }
        UserInfo dbUser = (await dbContext.Users.FindAsync($"{configuration["HostUrls:Server"]}/Users/{userId}"))!;
        UserInfo? dbFollower = await dbContext.Users.FindAsync(followerId);
        if (dbFollower is null)
        {
            dbFollower = new("Some Follower", followerId);
            dbContext.Add(dbFollower);
        }
        dbContext.Add(new FollowRelation(dbFollower.Id, dbUser.Id));
        await dbContext.SaveChangesAsync();

        return TypedResults.Accepted("Accepted");
    }

    protected virtual async Task<HttpResponseMessage> Accept(string userId,
        Follow follow, Uri inbox)
    {
        Accept accept = new Accept()
        {
            Actor = new List<Link>() { new() { Href = new($"{configuration["HostUrls:Server"]}/Users/{userId}") } },
            Id = $"{configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}",
            Object = new List<IObject>() { follow }
        };
        HttpResponseMessage response = await activityPub.PostAsync(accept, inbox);
        return response;
    }
}