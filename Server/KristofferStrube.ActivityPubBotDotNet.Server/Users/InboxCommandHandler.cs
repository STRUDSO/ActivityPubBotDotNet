using System.Text.Json;
using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class InboxCommandHandler
{
    public IConfiguration configuration;
    public ActivityPubDbContext dbContext;
    public ActivityPubService activityPub;

    public InboxCommandHandler(IConfiguration configuration, ActivityPubDbContext dbContext,
        ActivityPubService activityPub)
    {
        this.configuration = configuration;
        this.dbContext = dbContext;
        this.activityPub = activityPub;
    }

    public Results<BadRequest<string>, Accepted> BadRequest()
    {
        _ = this;
        return TypedResults.BadRequest("The Object type was not supported.");
    }

    public Results<BadRequest<string>, Accepted> Undo(Undo undo)
    {
        switch (undo.Object?.First())
        {
            case Follow follow:
                if (activityPub.GetPersonId(follow.Actor?.First()) is not string actorId ||
                    follow.Object?.First() is not ILink { Href: Uri objectUri })
                {
                    return TypedResults.BadRequest(
                        $"Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.");
                }

                FollowRelation? followRelation = dbContext.FollowRelations.Find(actorId, objectUri.ToString());
                if (followRelation is null)
                {
                    return TypedResults.BadRequest(
                        $"Could not Undo Follow because the Actor was not following the Object.");
                }

                dbContext.FollowRelations.Remove(followRelation);
                dbContext.SaveChanges();
                return TypedResults.Accepted("Accepted");
            default:
                return TypedResults.BadRequest(JsonSerializer.Serialize(undo.Object));
        }
    }

    public async Task<Results<BadRequest<string>, Accepted>> Follow(string userId, Follow follow)
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

        Uri? inbox = await activityPub.GetInboxUriAsync(follow.Actor.First());
        if (inbox is null)
        {
            return TypedResults.BadRequest("The User had no inbox specified.");
        }

        Accept accept = new Accept()
        {
            Actor = new List<Link>()
                { new() { Href = new($"{configuration["HostUrls:Server"]}/Users/{userId}") } },
            Id = $"{configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}",
            Object = new List<IObject>() { follow }
        };
        HttpResponseMessage response = await activityPub.PostAsync(accept, inbox);

        if (!response.IsSuccessStatusCode)
        {
            return TypedResults.BadRequest("Could not send Accept message.");
        }

        if (activityPub.GetPersonId(follow.Actor.First()) is not string followerId)
        {
            return TypedResults.BadRequest("The Actor was not a Link or did not have a id.");
        }

        if (dbContext.FollowRelations.Find(followerId, userId) is not null)
        {
            return TypedResults.Accepted("Accepted as the Actor already followed the Object.");
        }

        UserInfo dbUser = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}")!;
        UserInfo? dbFollower = dbContext.Users.Find(followerId);
        if (dbFollower is null)
        {
            dbFollower = new("Some Follower", followerId);
            dbContext.Add(dbFollower);
        }

        dbContext.Add(new FollowRelation(dbFollower.Id, dbUser.Id));
        dbContext.SaveChanges();

        return TypedResults.Accepted("Accepted");
    }

    public UserInfo? LookupUser(string userId)
    {
        return dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}");
    }

    public async Task<Results<BadRequest<string>, Accepted>> Execute(string userId, IObject obj)
    {
        if (LookupUser(userId) is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        return obj switch
        {
            Follow follow => await Follow(userId, follow),
            Undo undo => Undo(undo),
            _ => BadRequest()
        };
    }
}