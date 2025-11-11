using System.Net;
using KristofferStrube.ActivityStreams;
using Microsoft.AspNetCore.Http;
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
        protected override Task<Uri?> InboxUrl(IObjectOrLink objectOrLink)
        {
            return Task.FromResult<Uri?>(inboxUri);
        }

        protected override Task<HttpResponseMessage> Accept(string userId, Follow follow, Uri inbox)
        {
            return Task.FromResult(responseMessage);
        }

        protected override Task<string> TestableFollow(string userId, string followerId)
        {
            Followers.Add($"UserId: {userId}, FollowerId: {followerId}");

            return Task.FromResult("Test");
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
                    string result;
                    try
                    {
                        result = sut.Follow(userId, follow).Result;
                    }
                    catch (AggregateException e)
                    {
                        result = e.InnerExceptions.First().Message;
                    }
                    var join = string.Join("|", sut.Followers);
                    return join + result;
            },
            [null, new[] { new ObjectOrLink() }, new[] { new Person(){ Id = "10"} }],
            [null, [new Person { Id = "-1" }], new[] { new Person { Id = $"/Users/{userId}" } }],
            [HttpStatusCode.OK, HttpStatusCode.BadRequest],
            [new Uri("http://localhost"), null]
        );
    }
}