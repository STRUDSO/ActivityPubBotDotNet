using KristofferStrube.ActivityStreams;
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

    public static async Task<Results<BadRequest<string>, Accepted>> Inbox(string userId, [FromBody] IObject obj, IConfiguration configuration, ActivityPubDbContext dbContext, ActivityPubService activityPub)
    {
        var userUrl = $"{configuration["HostUrls:Server"]}/Users/{userId}";

        var user = await dbContext.Users.FindAsync(userUrl) ?? throw new BadHttpRequestException("User could not be found.");

        switch (obj)
        {
            case Follow follow:
                var clump = Clump.Create(follow, userUrl, user);
                return await Send(configuration, dbContext, activityPub, clump);
            case Undo undo:
                switch (undo.Object?.First())
                {
                    case Follow follow:
                        if (ActivityPubService.GetPersonId(follow.Actor?.First()) is not string actorId || follow.Object?.First() is not ILink { Href: Uri objectUri })
                        {
                            return TypedResults.BadRequest($"Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.");
                        }
                        FollowRelation? followRelation = dbContext.FollowRelations.Find(actorId, objectUri.ToString());
                        if (followRelation is null)
                        {
                            return TypedResults.BadRequest($"Could not Undo Follow because the Actor was not following the Object.");
                        }
                        dbContext.FollowRelations.Remove(followRelation);
                        dbContext.SaveChanges();
                        return TypedResults.Accepted("Accepted");
                    default:
                        return TypedResults.BadRequest(Serialize(undo.Object));
                }
            default:
                return TypedResults.BadRequest("The Object type was not supported.");
        }
    }

    private static async Task<Results<BadRequest<string>, Accepted>> Send(IConfiguration configuration, ActivityPubDbContext dbContext,
        ActivityPubService activityPub, Clump clump)
    {
        if (await ValidateInbox(configuration, activityPub, clump) is { } error)
        {
            return TypedResults.BadRequest(error);
        }

        var message = await Follow(dbContext, clump);
        return TypedResults.Accepted(message);
    }

    private static async Task<string> Follow(ActivityPubDbContext dbContext, Clump clump)
    {
        if (await dbContext.FollowRelations.FindAsync(clump.GetPersonId(), clump.FollowedId()) is not null)
        {
            return "Accepted as the Actor already followed the Object.";
        }

        UserInfo? dbFollower = await dbContext.Users.FindAsync(clump.GetPersonId());
        if (dbFollower is null)
        {
            dbFollower = new("Some Follower", clump.GetPersonId());
            dbContext.Users.Add(dbFollower);
        }

        dbContext.FollowRelations.Add(new FollowRelation(dbFollower.Id, clump.FollowedId()));
        await dbContext.SaveChangesAsync();

        return "Accepted";
    }

    private static async Task<string?> ValidateInbox(IConfiguration configuration, ActivityPubService activityPub, Clump clump)
    {
        if (await activityPub.GetInboxUriAsync(clump.Actor()) is { } inboxUri)
            return await CheckInbox(activityPub, inboxUri, clump.AcceptPayload(configuration));

        return "The User had no inbox specified.";
    }

    private static async Task<string?> CheckInbox(ActivityPubService activityPub,
        Uri inboxUri, Accept objectOrLink)
    {
        var response = await activityPub.PostAsync(objectOrLink, inboxUri);
        return response.IsSuccessStatusCode switch
        {
            false => "Could not send Accept message.",
            _ => null
        };
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
