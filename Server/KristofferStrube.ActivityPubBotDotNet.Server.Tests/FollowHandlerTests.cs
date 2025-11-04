using System.Net;
using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace KristofferStrube.ActivityPubBotDotNet.Server.Tests;

public class FollowHandlerTests
{
    class TestableFollowHandler(
        ActivityPubDbContext dbContext,
        ActivityPubService activityPub,
        IConfiguration configuration,
        HttpResponseMessage responseMessage,
        Uri inboxUri
        ) : Server.FollowHandler(dbContext, activityPub, configuration)
    {
        public List<string> Followers = new();
        protected override Task<Uri?> InboxUrl(Follow follow)
        {
            return Task.FromResult<Uri?>(inboxUri);
        }

        protected override Task<HttpResponseMessage> Accept(string userId, IConfiguration configuration, ActivityPubService activityPub, Follow follow, Uri inbox)
        {
            return Task.FromResult(responseMessage);
        }

        protected override Task<Results<BadRequest<string>, Accepted>> Follow(string userId, string followerId)
        {
            Followers.Add($"UserId: {userId}, FollowerId: {followerId}");

            return Task.FromResult<Results<BadRequest<string>, Accepted>>(null);
        }
    }
    [Fact]
    public async Task FollowHandler_Follow()
    {
        var userId = "42";
        await Combination().Verify((actor, objects, statusCode, inboxUri) =>
            {
                var activityPubService = new ActivityPubService(new HttpClient(),
                    null);
                var configurationManager = new ConfigurationManager();

                var acceptResponse = new HttpResponseMessage
                {
                    StatusCode = statusCode
                };
                var sut = new TestableFollowHandler(null,
                    activityPubService,
                    configurationManager,
                    acceptResponse,
                    inboxUri
                    );
                var follow = new Follow
                {
                    Actor = actor,
                    Object = objects
                };
                try
                {
                    var result = sut.Follow_(userId, follow).Result;
                    var re = result.Result switch
                    {
                        BadRequest<string> br => br.Value,
                        Accepted ac => ac.Location
                    } ;
                    var join = string.Join("|", sut.Followers);
                    return join + re;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            },
            [null, [], new[] { new ObjectOrLink() }, new[] { new Person(){ Id = "10"} }],
            [null, [], [new Person { Id = "-1" }], new[] { new Person { Id = $"/Users/{userId}" } }],
            [HttpStatusCode.OK, HttpStatusCode.BadRequest],
            [new Uri("http://localhost"), null]
        );
    }
}