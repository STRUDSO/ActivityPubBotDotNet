using KristofferStrube.ActivityStreams;
using KristofferStrube.ActivityStreams.JsonLD;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static System.Text.Json.JsonSerializer;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public static class UsersApi
{
    public static RouteGroupBuilder MapUsers(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/users");
        group.WithTags("Users");

        group.MapGet("/{userId}", Index);
        group.MapPost("/{userId}/inbox", Inbox);
        group.MapGet("/{userId}/outbox", Outbox);
        group.MapGet("/{userId}/followers", Followers);
        group.MapGet("/{userId}/following", Following);

        return group;
    }

    public static Results<BadRequest<string>, Ok<IObject>> Index(string userId, IConfiguration configuration, ActivityPubDbContext dbContext)
    {
        UserInfo? user = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}");
        if (user is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        return TypedResults.Ok((IObject)new Person()
        {
            Id = $"{configuration["HostUrls:Server"]}/Users/{userId}",
            PreferredUsername = user.Name,
            Inbox = new Link() { Href = new Uri($"{configuration["HostUrls:Server"]}/Users/{userId}/inbox") },
            Outbox = new Link() { Href = new Uri($"{configuration["HostUrls:Server"]}/Users/{userId}/outbox") },
            Followers = new Link() { Href = new Uri($"{configuration["HostUrls:Server"]}/Users/{userId}/followers") },
            Following = new Link() { Href = new Uri($"{configuration["HostUrls:Server"]}/Users/{userId}/following") },
            Published = new DateTime(2022, 11, 27),
            Icon = new List<Image> {
                    new() {
                        Url = new Link[] { new() { Href = new("https://kristoffer-strube.dk/bot.png") } },
                        MediaType = "image/png",
                    }
                },
            Image = new List<Image> {
                    new() {
                        Url = new Link[] { new() { Href = new("https://kristoffer-strube.dk/bot_header.PNG") } },
                        MediaType = "image/png",
                    }
                },
            Summary = new string[] { "This is a ActivityPub bot written in .NET." },
            ExtensionData = new()
                {
                    { "manuallyApprovesFollowers", SerializeToElement(true) },
                    { "discoverable", SerializeToElement(true) },
                    {
                        "publicKey",
                        SerializeToElement(new
                        {
                            id = $"{configuration["HostUrls:Server"]}/Users/{userId}#main-key",
                            owner = $"{configuration["HostUrls:Server"]}/Users/{userId}",
                            publicKeyPem = configuration["PEM:Public"]
                        })
                    }
                }
        });
    }

    public static async Task<Results<BadRequest<string>, Accepted>> Inbox(
        string userId,
        [FromBody] IObject obj,
        IConfiguration configuration,
        ActivityPubDbContext dbContext,
        ActivityPubService activityPub)
    {
        var humbleDbContext = new HumbleDbContext(dbContext);
        var humbleUserIdConfiguration = new HumbleUserIdConfiguration(configuration);
        var humbleActivityPubService = new HumbleActivityPubService(activityPub);
        switch (obj)
        {
            case Follow follow:
                return await Follow(humbleDbContext, humbleUserIdConfiguration, humbleActivityPubService, userId, follow);
            case Undo undo:
                return Undo(humbleDbContext, humbleUserIdConfiguration, humbleActivityPubService, userId, undo);
            default:
                return TypedResults.BadRequest("The Object type was not supported.");
        }
    }

    private static Results<BadRequest<string>, Accepted> Undo(
        HumbleDbContext humbleDbContext,
        HumbleUserIdConfiguration humbleUserIdConfiguration,
        HumbleActivityPubService humbleActivityPubService,
        string userId,
        Undo undo)
    {
        switch (undo.Object?.First())
        {
            case Follow follow:
                var userUrl = humbleUserIdConfiguration.UserUrl(userId);
                var userInfo = humbleDbContext.DbContext.Users.Find(userUrl);
                if (userInfo is null)
                {
                    return TypedResults.BadRequest("User could not be found.");
                }
                if (humbleActivityPubService.ActivityPub.GetPersonId(follow.Actor?.First()) is not string actorId || follow.Object?.First() is not ILink { Href: Uri objectUri })
                {
                    return TypedResults.BadRequest($"Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.");
                }
                FollowRelation? followRelation = humbleDbContext.DbContext.FollowRelations.Find(actorId, objectUri.ToString());
                if (followRelation is null)
                {
                    return TypedResults.BadRequest($"Could not Undo Follow because the Actor was not following the Object.");
                }
                humbleDbContext.DbContext.FollowRelations.Remove(followRelation);
                humbleDbContext.DbContext.SaveChanges();
                return TypedResults.Accepted("Accepted");
            default:
                return TypedResults.BadRequest(Serialize(undo.Object));
        }
    }

    public static async Task<Results<BadRequest<string>, Accepted>> Follow(IDbContext humblDbContext, IUserIdConfiguration userIdConfiguration,
        IActivityPubService humbleActivityPubService, string userId, Follow follow)
    {
        var userUrl = userIdConfiguration.UserUrl(userId);
        var userInfo = humblDbContext.Find(userUrl);
        if (userInfo is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }
        if (follow.Actor is null)
        {
            return TypedResults.BadRequest("Follow request had no actor.");
        }

        if (humbleActivityPubService.GetPersonId(follow.Object?.First()) is not string objectPersonId)
        {
            return TypedResults.BadRequest("The Object was not a Link or did not have a id.");
        }
        if (objectPersonId != userUrl.Id)
        {
            return TypedResults.BadRequest("The Object Id did not match the address of this inbox.");
        }

        var inbox = await humbleActivityPubService.GetInbox(follow.Actor.First());
        if (inbox is null)
        {
            return TypedResults.BadRequest("The User had no inbox specified.");
        }

        Accept accept = new Accept()
        {
            Actor = new List<Link>() { new() { Href = new(userUrl.Id) } },
            Id = userIdConfiguration.Activity(),
            Object = new List<IObject>() { follow }
        };
        var response = await humbleActivityPubService.PostAsync(accept, inbox);

        if (!response.IsSuccessStatusCode)
        {
            return TypedResults.BadRequest("Could not send Accept message.");
        }

        if (humbleActivityPubService.GetPersonId(follow.Actor.First()) is not string followerId)
        {
            return TypedResults.BadRequest("The Actor was not a Link or did not have a id.");
        }

        if (humblDbContext.Find(userId, followerId) is not null)
        {
            return TypedResults.Accepted("Accepted as the Actor already followed the Object.");
        }

        var dbFollower = humblDbContext.DbFollower(followerId);
        if (dbFollower is null)
        {
            dbFollower = new("Some Follower", followerId);
            humblDbContext.Add(dbFollower);
        }
        humblDbContext.Add(dbFollower, userInfo);
        await humblDbContext.SaveChanges();

        return TypedResults.Accepted("Accepted");
    }

    public static Results<BadRequest<string>, Ok<IObjectOrLink>> Outbox(string userId, IConfiguration configuration, ActivityPubDbContext dbContext, IOutboxService outboxService)
    {
        UserInfo? user = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}");
        if (user is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        if (!outboxService.HasOutboxFor(userId))
        {
            return TypedResults.BadRequest("Did not have data for outbox of User.");
        }

        var outBoxItems = outboxService.GetOutboxItems(userId).ToList();

        IObjectOrLink collection = new OrderedCollection()
        {
            Id = $"{configuration["HostUrls:Server"]}/Users/{userId}/outbox",
            Items = outBoxItems,
            TotalItems = (uint)outBoxItems.Count()
        };

        return TypedResults.Ok(collection);
    }

    public static Results<BadRequest<string>, Ok<IObjectOrLink>> Followers(string userId, IConfiguration configuration, ActivityPubDbContext dbContext)
    {
        UserInfo? user = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}");
        if (user is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        var relations = dbContext.FollowRelations.Where(f => f.FollowedId == $"{configuration["HostUrls:Server"]}/Users/{userId}").ToList();

        IObjectOrLink collection = new Collection()
        {
            Id = $"{configuration["HostUrls:Server"]}/Users/{userId}/followers",
            Items = relations.Select(f => new Link() { Href = new(f.FollowerId) }).ToList(),
            TotalItems = (uint)relations.Count()
        };
        return TypedResults.Ok(collection);
    }

    public static Results<BadRequest<string>, Ok<IObjectOrLink>> Following(string userId, IConfiguration configuration, ActivityPubDbContext dbContext)
    {
        UserInfo? user = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}");
        if (user is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        var relations = dbContext.FollowRelations.Where(f => f.FollowerId == $"{configuration["HostUrls:Server"]}/Users/{userId}").ToList();

        IObjectOrLink collection = new Collection()
        {
            Id = $"{configuration["HostUrls:Server"]}/Users/{userId}/following",
            Items = relations.Select(f => new Link() { Href = new(f.FollowedId) }).ToList(),
            TotalItems = (uint)relations.Count()
        };
        return TypedResults.Ok(collection);
    }
}

public interface IActivityPubService
{
    string? GetPersonId(IObjectOrLink? objectLink);
    Task<Uri?> GetInbox(IObjectOrLink actorLink);

    Task<HttpResponseMessage> PostAsync(Accept accept,
        Uri inbox);
}

public class HumbleActivityPubService : IActivityPubService
{
    public ActivityPubService ActivityPub { get; }

    public HumbleActivityPubService(ActivityPubService activityPub)
    {
        ActivityPub = activityPub;
    }

    public string? GetPersonId(IObjectOrLink? objectLink)
    {
        var personId = ActivityPub.GetPersonId(objectLink);
        return personId;
    }

    public async Task<Uri?> GetInbox(IObjectOrLink actorLink)
    {
        Uri? inbox = await ActivityPub.GetInboxUriAsync(actorLink);
        return inbox;
    }

    public async Task<HttpResponseMessage> PostAsync(Accept accept,
        Uri inbox)
    {
        HttpResponseMessage response = await ActivityPub.PostAsync(accept, inbox);
        return response;
    }
}

public interface IDbContext
{
    UserInfo? Find(UserId userUrl);
    FollowRelation? Find(string userId, string followerId);
    UserInfo? DbFollower(string followerId);
    void Add(UserInfo dbFollower);
    void Add(UserInfo dbFollower, UserInfo userInfo);
    Task SaveChanges();
}

public class HumbleDbContext : IDbContext
{
    public ActivityPubDbContext DbContext { get; }

    public HumbleDbContext(ActivityPubDbContext dbContext)
    {
        DbContext = dbContext;
    }

    public UserInfo? Find(UserId userUrl)
    {
        return DbContext.Users.Find(userUrl.Id);
    }

    public FollowRelation? Find(string userId, string followerId)
    {
        return DbContext.FollowRelations.Find(followerId, userId);
    }

    public UserInfo? DbFollower(string followerId)
    {
        UserInfo? dbFollower = DbContext.Users.Find(followerId);
        return dbFollower;
    }

    public void Add(UserInfo dbFollower)
    {
        DbContext.Add(dbFollower);
    }

    public void Add(UserInfo dbFollower, UserInfo userInfo)
    {
        DbContext.Add(new FollowRelation(dbFollower.Id, userInfo.Id));
    }

    public async Task SaveChanges()
    {
        await DbContext.SaveChangesAsync();
    }
}

public interface IUserIdConfiguration
{
    UserId UserUrl(string userId);
    string? Activity();
}

public record struct UserId(string Id);

public class HumbleUserIdConfiguration : IUserIdConfiguration
{
    public IConfiguration Configuration { get; }

    public HumbleUserIdConfiguration(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public UserId UserUrl(string userId)
    {
        return new UserId($"{Configuration["HostUrls:Server"]}/Users/{userId}");
    }

    public string Activity()
    {
        return $"{Configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}";
    }
}
