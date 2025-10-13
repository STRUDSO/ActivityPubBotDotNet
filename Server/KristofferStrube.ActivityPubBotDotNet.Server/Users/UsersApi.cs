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

    public static async Task<Results<BadRequest<string>, Accepted>> Inbox(string userId, [FromBody] IObject obj, IConfiguration configuration_, ActivityPubDbContext dbContext_, ActivityPubService activityPub_)
    {
        var humbleConfiguration = new HumbleConfiguration(configuration_);
        var humbleDbContext = new HumbleDbContext(dbContext_);
        var humbleActivityPub = new HumbleActivityPub(activityPub_);

        var userUrl = humbleConfiguration.UserUrl(userId);
        var userInfo = humbleDbContext.Find(userUrl);
        if (userInfo is null)
        {
            return TypedResults.BadRequest("User could not be found.");
        }

        switch (obj)
        {
            case Follow follow:
            {
                if (follow.Actor is null)
                {
                    return TypedResults.BadRequest("Follow request had no actor.");
                }

                var followerId = humbleActivityPub.FollowerId(follow);
                var objectPersonId = humbleActivityPub.ObjectPersonId(follow);
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

                var inbox = await humbleActivityPub.Uri(follow);
                if (inbox is null)
                {
                    return TypedResults.BadRequest("The User had no inbox specified.");
                }

                Accept accept = new Accept()
                {
                    Actor = new List<Link>() { new() { Href = new(userUrl) } },
                    Id = humbleConfiguration.Activity(),
                    Object = new List<IObject>() { follow }
                };
                var response = await humbleActivityPub.Accept(accept, inbox);

                if (!response.IsSuccessStatusCode)
                {
                    return TypedResults.BadRequest("Could not send Accept message.");
                }

                if (humbleDbContext.FindRelations(userId, followerId) is not null)
                {
                    return TypedResults.Accepted("Accepted as the Actor already followed the Object.");
                }

                UserInfo? dbFollower = humbleDbContext.Find(followerId);
                if (dbFollower is null)
                {
                    dbFollower = new("Some Follower", followerId);
                    humbleDbContext.AddUser(dbFollower);
                }

                humbleDbContext.AddRelation(dbFollower, userInfo);
                humbleDbContext.SaveChanges();

                return TypedResults.Accepted("Accepted");
            }
            case Undo undo:
                switch (undo.Object?.First())
                {
                    case Follow follow:
                        var followerId = humbleActivityPub.FollowerId2(follow);
                        var objectPersonId = follow.Object?.First();
                        if (followerId is null || objectPersonId is not ILink { Href: { } followedId })
                        {
                            return TypedResults.BadRequest("Could not Undo Follow either because the actor was not a Link or did not have an id or because the Object was not a Link.");
                        }

                        FollowRelation? followRelation = humbleDbContext.FindRelations(followerId, followedId.ToString());
                        if (followRelation is null)
                        {
                            return TypedResults.BadRequest("Could not Undo Follow because the Actor was not following the Object.");
                        }
                        humbleDbContext.RemoveRelation(followRelation);
                        humbleDbContext.SaveChanges();
                        return TypedResults.Accepted("Accepted");
                    default:
                        return TypedResults.BadRequest(Serialize(undo.Object));
                }
            default:
                return TypedResults.BadRequest("The Object type was not supported.");
        }
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