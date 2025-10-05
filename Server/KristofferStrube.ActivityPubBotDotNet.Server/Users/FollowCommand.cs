using KristofferStrube.ActivityStreams;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class FollowCommand
{
    private FollowCommand(Follow follow, string userUrl, UserInfo user, IObjectOrLink actorOrLink, string followerId)
    {
        Follow = follow;
        UserUrl = userUrl;
        User = user;
        ActorOrLink = actorOrLink;
        FollowerId = followerId;
    }

    public string UserId => User.Id;
    private Follow Follow { get; }
    private string UserUrl { get; }
    private UserInfo User { get; }
    public IObjectOrLink ActorOrLink { get; }
    public string FollowerId { get; }

    public static FollowCommand Create(Follow follow, string userUrl, UserInfo user)
    {
        var actorOrLink = follow.Actor?.First();
        var objectOrLink = follow.Object?.First();
        string? id = ActivityPubService.GetPersonId(actorOrLink);
        string? personId = ActivityPubService.GetPersonId(objectOrLink);
        var errorList = new List<string>();
        if (actorOrLink is null)
        {
            errorList.Add("Follow request had no actor.");
        }

        if (personId is null)
        {
            errorList.Add("The Object was not a Link or did not have a id.");
        }

        if (personId != userUrl)
        {
            errorList.Add("The Object Id did not match the address of this inbox.");
        }

        if (id is null)
        {
            errorList.Add("The Actor was not a Link or did not have a id.");
        }

        if(errorList.Any())
            throw new Exception(errorList.First());

        return new FollowCommand(follow, userUrl, user, actorOrLink!, id!);
    }

    public Accept AcceptRequest(IConfiguration configuration)
    {
        Accept accept = new Accept()
        {
            Actor = new List<Link>() { new() { Href = new(UserUrl) } },
            Id = $"{configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}",
            Object = new List<IObject>() { Follow }
        };
        return accept;
    }
}