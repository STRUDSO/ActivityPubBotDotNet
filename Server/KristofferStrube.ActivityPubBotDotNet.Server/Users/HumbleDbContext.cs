namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class HumbleDbContext
{
    public ActivityPubDbContext DbContext { get; }

    public HumbleDbContext(ActivityPubDbContext dbContext)
    {
        DbContext = dbContext;
    }

    public UserInfo? Find(string userUrl)
    {
        return DbContext.Users.Find(userUrl);
    }

    public FollowRelation? FindRelations(string userId, string followerId)
    {
        return DbContext.FollowRelations.Find(followerId, userId);
    }

    public void AddUser(UserInfo dbFollower)
    {
        DbContext.Add(dbFollower);
    }

    public void AddRelation(UserInfo dbFollower, UserInfo userInfo)
    {
        DbContext.Add(new FollowRelation(dbFollower.Id, userInfo.Id));
    }

    public void SaveChanges()
    {
        DbContext.SaveChanges();
    }

    public void RemoveRelation(FollowRelation followRelation)
    {
        DbContext.FollowRelations.Remove(followRelation);
    }
}