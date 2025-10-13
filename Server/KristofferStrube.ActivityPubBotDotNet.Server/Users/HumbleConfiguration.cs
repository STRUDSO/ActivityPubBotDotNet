namespace KristofferStrube.ActivityPubBotDotNet.Server;

public class HumbleConfiguration
{
    public IConfiguration Configuration { get; }

    public HumbleConfiguration(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public string UserUrl(string userId)
    {
        var userUrl = $"{Configuration["HostUrls:Server"]}/Users/{userId}";
        return userUrl;
    }

    public string Activity()
    {
        return $"{Configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}";
    }
}