namespace KristofferStrube.ActivityPubBotDotNet.Server;

public interface IUserConfiguration
{
    string UserUrl(string userId);
    string Activity();
}

public class HumbleConfiguration : IUserConfiguration
{
    private readonly IConfiguration _configuration;

    public HumbleConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string UserUrl(string userId)
    {
        var userUrl = $"{_configuration["HostUrls:Server"]}/Users/{userId}";
        return userUrl;
    }

    public string Activity()
    {
        return $"{_configuration["HostUrls:Server"]}/Activity/{Guid.NewGuid()}";
    }
}