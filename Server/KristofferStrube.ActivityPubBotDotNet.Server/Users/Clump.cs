using KristofferStrube.ActivityStreams;

namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class Clump
{
    private readonly IEnumerable<IObjectOrLink> _actor;
    private readonly IEnumerable<IObjectOrLink> _obj;
    private readonly Follow _follow;
    private readonly string _userUrl;
    private readonly UserInfo _user;

    private Clump(Follow follow,
        string userUrl,
        UserInfo user,
        IEnumerable<IObjectOrLink> actor,
        IEnumerable<IObjectOrLink> obj)
    {
        _actor = actor;
        _obj = obj;
        _follow = follow;
        _userUrl = userUrl;
        _user = user;
    }

    public static Clump Create(Follow follow, string userUrl, UserInfo user)
    {
        IReadOnlyCollection<IObjectOrLink> actor = (follow.Actor ?? Enumerable.Empty<IObjectOrLink>()).ToArray();
        IReadOnlyCollection<IObjectOrLink> obj = (follow.Object ?? Enumerable.Empty<IObjectOrLink>()).ToArray();

        var errors = Validate(actor, userUrl, obj);
        if (errors != null)
            throw new ArgumentException(errors);

        return new Clump(follow, userUrl, user, actor, obj);
    }

    private static string? Validate(IEnumerable<IObjectOrLink> actor, string userUrl, IEnumerable<IObjectOrLink> obj)
    {
        string? ret = null;
        if (ActivityPubService.GetPersonId(obj.First()) is not { } objectPersonId)
            ret = "The Object was not a Link or did not have a id.";
        else if (objectPersonId != userUrl)
            ret = "The Object Id did not match the address of this inbox.";
        else if (ActivityPubService.GetPersonId(actor.First()) is null)
            ret = "The Actor was not a Link or did not have a id.";
        return ret;
    }

    public string GetPersonId()
    {
        return ActivityPubService.GetPersonId(_obj.First())!;
    }

    public string FollowedId()
    {
        return _user.Id;
    }

    public IObjectOrLink Actor()
    {
        return _actor.First();
    }

    public Accept AcceptPayload(IConfiguration configuration)
    {
        var accept = new Accept
        {
            Actor = new List<Link> { new() { Href = new Uri(_userUrl) } },
            Id = $"{configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}",
            Object = new List<IObject> { _follow }
        };
        return accept;
    }
}