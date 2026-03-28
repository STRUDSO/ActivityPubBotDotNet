---
name: split-phase
description: >
  Guided refactoring workflow: extract business logic from an API/presentation layer
  into a dedicated application-layer service (constructor injection), then apply the
  Split Phase pattern to each service method — separating pure parsing/validation
  (no side effects) from business logic (HTTP calls, DB writes), connected by an
  intermediate data record. Use this skill when the user wants to push methods down
  from a controller/endpoint class, create an application or service layer, split
  validation from side effects, separate parsing from persistence, refactor a method
  that mixes input checking with DB writes or HTTP calls, or mentions "split phase",
  "application layer", "parse vs business logic", or "push down". Also trigger when
  the user points at an endpoint handler that does too much and asks how to structure it.
---

# Extract Service + Split Phase

You are pair-refactoring with a human. The goal is a two-step workflow:

1. **Extract Application Layer** — move business logic out of the API/presentation
   layer into a dedicated service class with constructor injection
2. **Split Phase** — divide each service method into a pure *parsing* phase and a
   *business logic* phase, connected by an intermediate data record

The result: endpoint handlers become thin dispatchers; service methods become thin
orchestrators; each concern is testable in isolation.

## Recognizing the Smell

Look for endpoint/handler methods that:

- Validate inputs **and** call external services (HTTP) **and** write to the DB — all
  in one flow
- Are `static` helpers sitting in an API/presentation class
- Are hard to unit-test without the full HTTP pipeline
- Mix `throw new InvalidOperationException(...)` (parsing) with
  `dbContext.SaveChanges()` (mutation) in the same method

**Example — before (everything in the endpoint class):**

```csharp
// In UsersApi (presentation layer)
internal static async Task<string> HandleFollow(
    string userId, Follow follow, IConfiguration config,
    ActivityPubDbContext db, ActivityPubService activityPub)
{
    if (follow.Actor is null)                          // parsing
        throw new InvalidOperationException("...");
    // ...validate object, resolve inbox URI...        // parsing
    var response = await activityPub.PostAsync(...);   // side effect (HTTP)
    if (!response.IsSuccessStatusCode)                 // parsing
        throw new InvalidOperationException("...");
    db.Add(new FollowRelation(...));                   // side effect (DB)
    db.SaveChanges();
    return "Accepted";
}
```

The mixing of concerns is the smell. Parsing throws on bad input; business logic
mutates state. When they're interleaved, it's hard to know what's a guard and what's
an action.

---

## Step 1: Extract the Application Layer

Move the static methods into a proper service class. The service owns its dependencies
via constructor injection.

### How to do it

1. **Create the service class** next to (or under) a `Services/` folder:

```csharp
public class InboxService
{
    private readonly IConfiguration configuration;
    private readonly ActivityPubDbContext dbContext;
    private readonly ActivityPubService activityPub;

    public InboxService(IConfiguration configuration,
                        ActivityPubDbContext dbContext,
                        ActivityPubService activityPub)
    {
        this.configuration = configuration;
        this.dbContext = dbContext;
        this.activityPub = activityPub;
    }

    // Move the methods here as instance methods.
    // Replace the static parameters with the injected fields.
    public async Task<string> HandleFollow(string userId, Follow follow) { ... }
    public string HandleUndo(Undo undo) { ... }
}
```

2. **Register the service** in DI:

```csharp
builder.Services.AddScoped<InboxService>();
```

3. **Update the endpoint** to inject the service instead of taking all dependencies
   directly. Keep only what the endpoint itself needs (e.g. user lookup):

```csharp
public static async Task<Results<BadRequest<string>, Accepted>> Inbox(
    string userId, [FromBody] IObject obj,
    IConfiguration configuration, ActivityPubDbContext dbContext,
    InboxService inboxService)          // <-- new
{
    // user lookup stays here (it's the endpoint's own guard)
    UserInfo? user = dbContext.Users.Find(...);
    if (user is null) return TypedResults.BadRequest("User could not be found.");

    string location = obj switch
    {
        Follow follow => await inboxService.HandleFollow(userId, follow),
        Undo undo     => inboxService.HandleUndo(undo),
        _             => throw new InvalidOperationException("...")
    };
    return TypedResults.Accepted(location);
}
```

4. **Update tests** — test classes that called `UsersApi.HandleFollow(...)` now
   construct `new InboxService(config, db, fakeService).HandleFollow(...)`.

Run tests. Everything should be green before moving to Step 2.

---

## Step 2: Apply Split Phase

Now refactor each service method into two private phases:

| Phase | Name convention | What it does | Side effects? |
|---|---|---|---|
| 1 | `Parse*` / `Parse*Async` | Validate inputs, resolve IDs, look up URIs | None — pure reads |
| 2 | Named after the action | HTTP calls, DB writes, mutations | Yes |

The two phases communicate through a **small private record** that carries exactly what
phase 2 needs — no more.

### The key insight

Sending an HTTP `Accept`, writing to the DB, removing a row — these are *business
logic*, not validation. It's tempting to put the Accept send in the "validation" phase
because it happens before the DB write, but ask yourself: *does this method have a
side effect?* If yes, it belongs in phase 2.

Pure phase 1:
- Check actor/object fields are present and well-formed
- Resolve person IDs from links
- Fetch the follower's inbox URI (read-only HTTP GET)
- Return structured data — or throw if invalid

Business logic phase 2:
- Build and POST the `Accept` activity
- Insert/remove rows
- Call `SaveChanges()`

### How to do it

**Before:**
```csharp
public async Task<string> HandleFollow(string userId, Follow follow)
{
    if (follow.Actor is null) throw ...;            // parse
    if (GetPersonId(...) is not string objectId)    // parse
        throw ...;
    if (objectId != $".../Users/{userId}") throw ...; // parse
    Uri? inbox = await GetInboxUriAsync(...);       // parse (read-only)
    if (inbox is null) throw ...;                   // parse
    var response = await activityPub.PostAsync(...); // SIDE EFFECT
    if (!response.IsSuccessStatusCode) throw ...;  // could be parse or business
    db.Add(new FollowRelation(...));                // SIDE EFFECT
    db.SaveChanges();
    return "Accepted";
}
```

**After — intermediate record:**
```csharp
private record ValidatedFollow(string FollowerId, Uri FollowerInbox, Follow Follow);
```

**After — phase 1 (pure):**
```csharp
private async Task<ValidatedFollow> ParseFollowAsync(string userId, Follow follow)
{
    if (follow.Actor is null)
        throw new InvalidOperationException("Follow request had no actor.");
    if (activityPub.GetPersonId(follow.Object?.First()) is not string objectPersonId)
        throw new InvalidOperationException("The Object was not a Link or did not have an id.");
    if (objectPersonId != $"{configuration["HostUrls:Server"]}/Users/{userId}")
        throw new InvalidOperationException("The Object Id did not match the address of this inbox.");
    Uri? inbox = await activityPub.GetInboxUriAsync(follow.Actor.First());
    if (inbox is null)
        throw new InvalidOperationException("The User had no inbox specified.");
    if (activityPub.GetPersonId(follow.Actor.First()) is not string followerId)
        throw new InvalidOperationException("The Actor was not a Link or did not have an id.");
    return new ValidatedFollow(followerId, inbox, follow);
}
```

**After — phase 2 (side effects):**
```csharp
private async Task<string> AcceptAndPersistFollow(string userId, ValidatedFollow validated)
{
    Accept accept = new()
    {
        Actor  = new List<Link> { new() { Href = new($".../Users/{userId}") } },
        Id     = $".../Activity/{Guid.NewGuid()}",
        Object = new List<IObject> { validated.Follow }
    };
    HttpResponseMessage response = await activityPub.PostAsync(accept, validated.FollowerInbox);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException("Could not send Accept message.");

    if (dbContext.FollowRelations.Find(validated.FollowerId, userId) is not null)
        return "Accepted as the Actor already followed the Object.";

    // ... create/find users, add relation, SaveChanges ...
    return "Accepted";
}
```

**After — thin orchestrator:**
```csharp
public async Task<string> HandleFollow(string userId, Follow follow)
{
    ValidatedFollow validated = await ParseFollowAsync(userId, follow);
    return await AcceptAndPersistFollow(userId, validated);
}
```

### Naming conventions

- Phase 1: `Parse*` or `Parse*Async` — not `Validate*`. "Parse" communicates that
  structured data comes *out*; "Validate" suggests a boolean pass/fail. If it returns
  a record, it's parsing.
- Intermediate records: `Validated*` or `Parsed*` — descriptive, not generic
  (`FollowData` is fine; `Data` is not).
- Phase 2: name the action (`AcceptAndPersistFollow`, `RemoveFollowRelation`) — not
  `ProcessFollow` or `HandleFollow2`.

---

## Working Style

- **One commit per step.** Extract the service in one commit; split each method in
  its own commit. Keep tests green at every commit.
- **Read the code before proposing.** Understand the existing dependencies before
  suggesting what the intermediate record should carry.
- **Phase 2 owns all side effects.** If you're unsure whether something is parse or
  business logic, ask: *does it mutate or call out to the world?* If yes → phase 2.
- **Keep the public methods thin.** `HandleFollow` and `HandleUndo` should be
  two-liners: call parse, call business logic, return result.
- **Propose before applying.** Show the human the intermediate record design and the
  phase split before making changes. They may know something about the domain that
  changes what the record should carry.

## References

- Fowler, M. *Refactoring* 2nd Ed. — "Split Phase" (Ch. 6)
- The core idea: a pipeline of two phases is easier to understand and test than a
  single procedure that switches between reading and writing.
