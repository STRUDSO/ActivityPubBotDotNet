namespace KristofferStrube.ActivityPubBotDotNet.Server;

public interface IDbContext
{
    UserInfo? Find(string userUrl);
    FollowRelation? FindRelations(string userId, string followerId);
    void AddUser(UserInfo dbFollower);
    void AddRelation(UserInfo dbFollower, UserInfo userInfo);
    void SaveChanges();
    void RemoveRelation(FollowRelation followRelation);
}

public class DbContext : IDbContext
{
    private readonly ActivityPubDbContext _dbContext;

    public DbContext(ActivityPubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public UserInfo? Find(string userUrl)
    {
        return _dbContext.Users.Find(userUrl);
    }

    public FollowRelation? FindRelations(string userId, string followerId)
    {
        return _dbContext.FollowRelations.Find(followerId, userId);
    }

    public void AddUser(UserInfo dbFollower)
    {
        _dbContext.Add(dbFollower);
    }

    public void AddRelation(UserInfo dbFollower, UserInfo userInfo)
    {
        _dbContext.Add(new FollowRelation(dbFollower.Id, userInfo.Id));
    }

    public void SaveChanges()
    {
        _dbContext.SaveChanges();
    }

    public void RemoveRelation(FollowRelation followRelation)
    {
        _dbContext.FollowRelations.Remove(followRelation);
    }
}