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

    public override Task<HttpResponseMessage> PostAsync(IObjectOrLink obj, Uri uri)
        => Task.FromResult(new HttpResponseMessage(postStatus));
}
