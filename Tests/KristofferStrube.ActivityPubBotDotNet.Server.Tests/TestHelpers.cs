using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KristofferStrube.ActivityPubBotDotNet.Server.Tests;

internal static class TestHelpers
{
    internal static ActivityPubDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ActivityPubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static IConfiguration Config(string server = "https://test.example.com") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["HostUrls:Server"] = server })
            .Build();

    internal static string Describe(Results<BadRequest<string>, Accepted> r) =>
        r.Result switch
        {
            BadRequest<string> b => $"400: {b.Value}",
            Accepted a           => $"202: {a.Location}",
            _                    => "???"
        };
}
