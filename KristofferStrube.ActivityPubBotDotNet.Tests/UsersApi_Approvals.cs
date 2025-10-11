using KristofferStrube.ActivityPubBotDotNet.Server;
using KristofferStrube.ActivityStreams;

namespace KristofferStrube.ActivityPubBotDotNet.Tests;

public class UsersApi_Approvals
{
    [Fact]
    public async Task Follow()
    {
        var inbox = await UsersApi.Follow(new FakeDbContext(), new FakeUserIdConfiguration(), new FakeActivitiyPubService(), Any.UserId, Any.Follow());

        await Verify(inbox);
    }
}

public class FakeActivitiyPubService : IActivityPubService
{
    public string? GetPersonId(IObjectOrLink? objectLink)
    {
        throw new NotImplementedException();
    }

    public async Task<Uri?> GetInbox(IObjectOrLink actorLink)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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
    public static string UserId => Guid.NewGuid().ToString();

    public static Follow Follow()
    {
        return new Follow();
    }
}
