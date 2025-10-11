using System.Net;
using KristofferStrube.ActivityPubBotDotNet.Server;
using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;

namespace KristofferStrube.ActivityPubBotDotNet.Tests;

public class UsersApi_Approvals
{
    [Fact]
    public async Task Follow()
    {
        string[] userIds = ["42"];
        IEnumerable<string[]> existingUserIds = [["42"], []];
        IEnumerable<FollowRelation[]> followRelations = [
            [new FollowRelation("42", "43")],
            []];
        IEnumerable<Follow> follows =
        [
            new(),
            new()
            {
                Actor = []
            },
            new()
            {
                Actor = [new Person { Id = FakeUserIdConfiguration.CreateUserId("43").Id }],
                Object = [new Person { Id = "NOT ID" }]
            },
            new ()
            {
                Actor = [new Person { Id = FakeUserIdConfiguration.CreateUserId("43").Id }],
                Object = [new Person { Id = FakeUserIdConfiguration.CreateUserId("42").Id }],
            },
            new ()
            {
                Actor = [new Person { Id = FakeUserIdConfiguration.CreateUserId("43").Id,
                    Inbox = new Link()
                {
                    Href = new Uri("https://api.kristoffer.com/users/43")
                }}],
                Object = [new Person { Id = FakeUserIdConfiguration.CreateUserId("42").Id }],
            },
            new ()
            {
                Actor = [new Person { Id = null, Inbox = new Link()
                {
                    Href = new Uri("https://api.kristoffer.com/users/43")
                }}],
                Object = [new Person { Id = FakeUserIdConfiguration.CreateUserId("42").Id }],
            },
        ];

        IEnumerable<(HttpResponseMessage, Uri)[]> inboxes = [[], [(new HttpResponseMessage(), new Uri("https://api.kristoffer.com/users/43"))]];

        var verifySettings = new VerifySettings();
        verifySettings.AutoVerify(false, true);
        await Combination(settings:verifySettings).Verify(DoFollow,
            userIds,
            existingUserIds,
            follows,
            followRelations,
            inboxes
            );
    }

    private static async Task<Results<BadRequest<string>, Accepted>> DoFollow(string userId,
        string[] existingUserIds,
        Follow follow,
        FollowRelation[] relations,
        (HttpResponseMessage, Uri)[] inboxes
        )
    {
        var humblDbContext = new FakeDbContext();
        var fakeUserIdConfiguration = new FakeUserIdConfiguration();
        var humbleActivityPubService = new FakeActivitiyPubService();

        foreach (var r in relations)
            humblDbContext.Add(new UserInfo($"User for: {r.FollowerId}", r.FollowerId), ToUserInfo(r.FollowedId));

        foreach (var existingUserId in existingUserIds)
            humblDbContext.Add(ToUserInfo(existingUserId));

        foreach (var inbox in inboxes)
        {
            humbleActivityPubService.accepts[inbox.Item2] = inbox.Item1;
        }

        return await UsersApi.Follow(humblDbContext, fakeUserIdConfiguration, humbleActivityPubService, userId, follow);

        UserInfo ToUserInfo(string existingUserId)
        {
            return new UserInfo($"User for: {existingUserId}", fakeUserIdConfiguration.UserUrl(existingUserId).Id);
        }
    }
}

public class FakeActivitiyPubService : IActivityPubService
{
    public Dictionary<Uri, HttpResponseMessage> accepts = new();
    public string? GetPersonId(IObjectOrLink? objectLink)
    {
        return ActivityPubService.PersonId(objectLink);
    }

    public async Task<Uri?> GetInboxUriAsync(IObjectOrLink person)
    {
        if (person is Actor actorObject)
        {
            return actorObject.Inbox?.Href;
        }

        return null;
    }

    public async Task<HttpResponseMessage> PostAsync(Accept accept, Uri inbox)
    {
        return accepts.GetValueOrDefault(inbox) ?? new HttpResponseMessage(HttpStatusCode.NotFound);;
    }
}

public class FakeUserIdConfiguration : IUserIdConfiguration
{
    public UserId UserUrl(string userId)
    {
        return CreateUserId(userId);
    }

    public static UserId CreateUserId(string userId)
    {
        return new UserId(new Uri("http://localhost/" + userId).ToString());
    }

    public string? Activity()
    {
        return CreateUserId($"activity/{Guid.NewGuid()}").Id;
    }
}

public class FakeDbContext : IDbContext
{
    private Dictionary<UserId, UserInfo> users = new();
    private Dictionary<(string,string), FollowRelation> followrelations = new();

    public UserInfo? Find(UserId userUrl)
    {
        return users.GetValueOrDefault(userUrl);
    }

    public FollowRelation? Find(string userId, string followerId)
    {
        return followrelations.GetValueOrDefault((userId, followerId));
    }

    public UserInfo? DbFollower(string followerId)
    {
        return users.GetValueOrDefault(new UserId(followerId));
    }

    public void Add(UserInfo dbFollower)
    {
        users.Add(new UserId(dbFollower.Id), dbFollower);
    }

    public void Add(UserInfo dbFollower, UserInfo userInfo)
    {
        followrelations[(dbFollower.Id, userInfo.Id)] = new FollowRelation(dbFollower.Id, userInfo.Id);
    }

    public async Task SaveChanges()
    {
    }
}

internal class Any
{
}
