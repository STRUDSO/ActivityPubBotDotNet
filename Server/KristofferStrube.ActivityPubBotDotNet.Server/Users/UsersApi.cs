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

    public static async Task<Results<BadRequest<string>, Accepted>> Inbox(string userId, [FromBody] IObject obj, IConfiguration configuration, ActivityPubDbContext dbContext, InboxService inboxService)
    {
        UserInfo? user = dbContext.Users.Find($"{configuration["HostUrls:Server"]}/Users/{userId}");
        if (user is null)
            return TypedResults.BadRequest("User could not be found.");

        try
        {
            string location = obj switch
            {
                Follow follow => await inboxService.HandleFollow(userId, follow),
                Undo undo     => inboxService.HandleUndo(undo),
                _             => throw new InvalidOperationException("The Object type was not supported.")
            };
            return TypedResults.Accepted(location);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
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
