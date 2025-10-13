using KristofferStrube.ActivityStreams;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class HumbleActivityPub
{
    public ActivityPubService ActivityPub { get; }

    public HumbleActivityPub(ActivityPubService activityPub)
    {
        ActivityPub = activityPub;
    }

    public string? FollowerId(Follow follow)
    {
        var followerId = ActivityPub.GetPersonId(follow.Actor.First());
        return followerId;
    }

    public string? ObjectPersonId(Follow follow)
    {
        var objectPersonId = ActivityPub.GetPersonId(follow.Object?.First());
        return objectPersonId;
    }

    public async Task<Uri?> Uri(Follow follow)
    {
        Uri? inbox = await ActivityPub.GetInboxUriAsync(follow.Actor.First());
        return inbox;
    }

    public async Task<HttpResponseMessage> Accept(Accept accept, Uri inbox)
    {
        HttpResponseMessage response = await ActivityPub.PostAsync(accept, inbox);
        return response;
    }

    public string? FollowerId2(Follow follow)
    {
        return ActivityPub.GetPersonId(follow.Actor?.First());
    }
}