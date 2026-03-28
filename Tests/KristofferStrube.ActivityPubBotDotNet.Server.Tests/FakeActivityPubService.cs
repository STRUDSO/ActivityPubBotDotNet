using KristofferStrube.ActivityStreams;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace KristofferStrube.ActivityPubBotDotNet.Server.Tests;

internal class FakeActivityPubService(
    Uri? inboxUri = null,
    HttpStatusCode postStatus = HttpStatusCode.OK)
    : ActivityPubService(new HttpClient(), new ConfigurationBuilder().Build())
{
    public override Task<Uri?> GetInboxUriAsync(IObjectOrLink person)
        => Task.FromResult(inboxUri);

    public IObjectOrLink? LastPostedObject { get; private set; }

    public override Task<HttpResponseMessage> PostAsync(IObjectOrLink obj, Uri uri)
    {
        LastPostedObject = obj;
        return Task.FromResult(new HttpResponseMessage(postStatus));
    }
}
