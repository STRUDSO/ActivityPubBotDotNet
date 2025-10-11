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
        IEnumerable<Follow> follows =
        [
            new(),
            new()
            {
                Actor = []
            },
            new()
            {
                Actor = [new Person { Id = "43" }],
                Object = [new Person { Id = "NOT ID" }]
            },
            new ()
            {
                Actor = [new Person { Id = "43" }],
                Object = [new Person { Id = FakeUserIdConfiguration.CreateUserId("42").Id }],
            }
        ];

        var verifySettings = new VerifySettings();
        verifySettings.AutoVerify(false, true);
        await Combination(settings:verifySettings).Verify(DoFollow, userIds, existingUserIds, follows);
    }

    private static async Task<Results<BadRequest<string>, Accepted>> DoFollow(string userId, string[] existingUserIds, Follow follow)
    {
        var humblDbContext = new FakeDbContext();
        var fakeUserIdConfiguration = new FakeUserIdConfiguration();
        var humbleActivityPubService = new FakeActivitiyPubService();

        foreach (var existingUserId in existingUserIds)
            humblDbContext.Add(new UserInfo("User for: {existingUserId}", fakeUserIdConfiguration.UserUrl(existingUserId).Id));

        return await UsersApi.Follow(humblDbContext, fakeUserIdConfiguration, humbleActivityPubService, userId, follow);
    }
}

public class FakeActivitiyPubService : IActivityPubService
{
    private Dictionary<IObjectOrLink, Uri> inboxes = new();

    public string? GetPersonId(IObjectOrLink? objectLink)
    {
        return ActivityPubService.PersonId(objectLink);
    }

    public async Task<Uri?> GetInbox(IObjectOrLink actorLink)
    {
        return inboxes.GetValueOrDefault(actorLink);
    }

    public async Task<HttpResponseMessage> PostAsync(Accept accept, Uri inbox)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }
}

public class FakeDbContext : IDbContext
{
    private Dictionary<UserId, UserInfo> users = new();

    public UserInfo? Find(UserId userUrl)
    {
        return users.GetValueOrDefault(userUrl);
    }

    public FollowRelation? Find(string userId, string followerId)
    {
        throw new NotImplementedException();
    }

    public UserInfo? DbFollower(string followerId)
    {
        throw new NotImplementedException();
    }

    public void Add(UserInfo dbFollower)
    {
        users.Add(new UserId(dbFollower.Id), dbFollower);
    }

    public void Add(UserInfo dbFollower, UserInfo userInfo)
    {
        throw new NotImplementedException();
    }

    public async Task SaveChanges()
    {
        throw new NotImplementedException();
    }
}

class Any
{
}
