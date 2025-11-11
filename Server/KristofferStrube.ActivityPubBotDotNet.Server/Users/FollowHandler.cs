using System.Diagnostics.CodeAnalysis;
using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class FollowHandler(ActivityPubDbContext dbContext, ActivityPubService activityPub, IConfiguration configuration)
{
    public async Task<string> Follow(string userId, Follow follow)
    {
        Guard(userId, follow.Actor, follow.Object);

        var follower = follow.Actor.First();
        var inbox = await TestableGetInboxUrl(follower);

        await TestAbleAccept(userId, follow, inbox);

        var followerId = activityPub.GetPersonId(follower) ?? throw new Exception("The Actor was not a Link or did not have a id.");

        return await TestableFollow(userId, followerId);
    }

    private async Task<Uri> TestableGetInboxUrl(IObjectOrLink follower)
    {
        var inbox = await InboxUrl(follower);
        return inbox ?? throw new Exception("The User had no inbox specified.");
    }

    private async Task TestAbleAccept(string userId, Follow follow, Uri inbox)
    {
        var response = await Accept(userId, follow, inbox);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Could not send Accept message.");
        }
    }

    private void Guard(string userId, [NotNull]IEnumerable<IObjectOrLink>? objectOrLinks, IEnumerable<IObjectOrLink>? followObject)
    {
        if (objectOrLinks is null)
        {
            throw new Exception("Follow request had no actor.");
        }

        var objectPerson = followObject?.First();
        var personId = activityPub.GetPersonId(objectPerson) ?? throw new Exception("The Object was not a Link or did not have a id.");

        GuardInvalidUserId(userId, personId);
    }

    private void GuardInvalidUserId(string userId, string personId)
    {
        if (personId != $"{configuration["HostUrls:Server"]}/Users/{userId}")
        {
            throw new Exception("The Object Id did not match the address of this inbox.");
        }
    }

    protected virtual async Task<Uri?> InboxUrl(IObjectOrLink objectOrLink) => await activityPub.GetInboxUriAsync(objectOrLink);

    protected virtual async Task<string> TestableFollow(string userId,
        string followerId)
    {
        if (await dbContext.FollowRelations.FindAsync(followerId, userId) is not null)
        {
            return "Accepted as the Actor already followed the Object.";
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

        return "Accepted";
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