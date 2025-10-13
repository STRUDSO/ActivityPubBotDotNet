using KristofferStrube.ActivityStreams;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public interface IActivityPub
{
    string? FollowerId(Follow follow);
    string? ObjectPersonId(Follow follow);
    Task<Uri?> Uri(Follow follow);
    Task<HttpResponseMessage> Accept(Accept accept, Uri inbox);
    string? FollowerId2(Follow follow);
}

public class HumbleActivityPub : IActivityPub
{
    private readonly ActivityPubService _activityPub;

    public HumbleActivityPub(ActivityPubService activityPub)
    {
        _activityPub = activityPub;
    }

    public string? FollowerId(Follow follow)
    {
        var followerId = _activityPub.GetPersonId(follow.Actor.First());
        return followerId;
    }

    public string? ObjectPersonId(Follow follow)
    {
        var objectPersonId = _activityPub.GetPersonId(follow.Object?.First());
        return objectPersonId;
    }

    public async Task<Uri?> Uri(Follow follow)
    {
        Uri? inbox = await _activityPub.GetInboxUriAsync(follow.Actor.First());
        return inbox;
    }

    public async Task<HttpResponseMessage> Accept(Accept accept, Uri inbox)
    {
        HttpResponseMessage response = await _activityPub.PostAsync(accept, inbox);
        return response;
    }

    public string? FollowerId2(Follow follow)
    {
        return _activityPub.GetPersonId(follow.Actor?.First());
    }
}