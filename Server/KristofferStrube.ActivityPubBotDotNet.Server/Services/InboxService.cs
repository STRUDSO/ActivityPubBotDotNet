using KristofferStrube.ActivityStreams;
using static System.Text.Json.JsonSerializer;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class InboxService
{
    private readonly IConfiguration configuration;
    private readonly ActivityPubDbContext dbContext;
    private readonly ActivityPubService activityPub;

    public InboxService(IConfiguration configuration, ActivityPubDbContext dbContext, ActivityPubService activityPub)
    {
        this.configuration = configuration;
        this.dbContext = dbContext;
        this.activityPub = activityPub;
    }

    public async Task<string> HandleFollow(string userId, Follow follow)
    {
        ValidatedFollow validated = await ParseFollowAsync(userId, follow);
        return await AcceptAndPersistFollow(userId, validated);
    }

    public string HandleUndo(Undo undo)
    {
        ValidatedUndo validated = ParseUndo(undo);
        return RemoveFollowRelation(validated);
    }

    // --- Phase 1: Validation ---

    private async Task<ValidatedFollow> ParseFollowAsync(string userId, Follow follow)
    {
        if (follow.Actor is null)
            throw new InvalidOperationException("Follow request had no actor.");

        if (activityPub.GetPersonId(follow.Object?.First()) is not string objectPersonId)
            throw new InvalidOperationException("The Object was not a Link or did not have a id.");

        if (objectPersonId != $"{configuration["HostUrls:Server"]}/Users/{userId}")
            throw new InvalidOperationException("The Object Id did not match the address of this inbox.");

        Uri? inbox = await activityPub.GetInboxUriAsync(follow.Actor.First());
        if (inbox is null)
            throw new InvalidOperationException("The User had no inbox specified.");

        if (activityPub.GetPersonId(follow.Actor.First()) is not string followerId)
            throw new InvalidOperationException("The Actor was not a Link or did not have a id.");

        return new ValidatedFollow(followerId, inbox, follow);
    }

    private ValidatedUndo ParseUndo(Undo undo)
    {
        if (undo.Object?.First() is not Follow follow)
            throw new InvalidOperationException(Serialize(undo.Object));

        if (activityPub.GetPersonId(follow.Actor?.First()) is not string actorId
            || follow.Object?.First() is not ILink { Href: Uri objectUri })
            throw new InvalidOperationException("Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.");

        return new ValidatedUndo(actorId, objectUri.ToString());
    }

    // --- Phase 2: Business Logic ---

    private async Task<string> AcceptAndPersistFollow(string userId, ValidatedFollow validated)
    {
        Accept accept = new()
        {
            Actor  = new List<Link>() { new() { Href = new($"{configuration["HostUrls:Server"]}/Users/{userId}") } },
            Id     = $"{configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}",
            Object = new List<IObject>() { validated.Follow }
        };
        HttpResponseMessage response = await activityPub.PostAsync(accept, validated.FollowerInbox);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Could not send Accept message.");

        if (dbContext.FollowRelations.Find(validated.FollowerId, userId) is not null)
            return "Accepted as the Actor already followed the Object.";

        UserInfo dbUser = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}")!;
        UserInfo? dbFollower = dbContext.Users.Find(validated.FollowerId);
        if (dbFollower is null)
        {
            dbFollower = new("Some Follower", validated.FollowerId);
            dbContext.Add(dbFollower);
        }
        dbContext.Add(new FollowRelation(dbFollower.Id, dbUser.Id));
        dbContext.SaveChanges();
        return "Accepted";
    }

    private string RemoveFollowRelation(ValidatedUndo validated)
    {
        FollowRelation? followRelation = dbContext.FollowRelations.Find(validated.ActorId, validated.ObjectUri);
        if (followRelation is null)
            throw new InvalidOperationException("Could not Undo Follow because the Actor was not following the Object.");

        dbContext.FollowRelations.Remove(followRelation);
        dbContext.SaveChanges();
        return "Accepted";
    }

    // --- Intermediate data structures ---

    private record ValidatedFollow(string FollowerId, Uri FollowerInbox, Follow Follow);
    private record ValidatedUndo(string ActorId, string ObjectUri);
}
