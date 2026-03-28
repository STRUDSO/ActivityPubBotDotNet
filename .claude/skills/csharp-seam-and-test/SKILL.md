---
name: csharp-seam-and-test
description: "Decompose a large C# method into testable slices using extract-and-override seams, scaffold an xUnit test project, and drive mutation score to ≥80%."
user-invocable: true
argument-hint: '<TargetClass.Method> - e.g. UsersApi.Inbox'
---

# CSHARP-SEAM-AND-TEST

Decompose a large C# endpoint/method into a thin dispatcher + internal slices, create a focused xUnit test project, and iterate until Stryker mutation score ≥ 80% on the covered mutants.

## Phase 1 — Read Before Touching

1. Read the target file in full. Identify:
   - All distinct case/branch arms (e.g. `switch (obj) { case Follow: ... case Undo: ... }`)
   - Every external dependency that needs faking (HTTP clients, config, DB)
   - Which dependency methods need to be `virtual` seams

2. Read the service class(es) that will be faked. Note which methods are called and their signatures.

3. Read the existing test project if one exists; otherwise note that a new one is needed.

## Phase 2 — Production Code Changes (minimal diff)

**Rules:**
- Preserve all original parameter names, brace style, and variable names exactly.
- Do not remove braces, rename locals, or change style in lines you are not restructuring.
- Only add `virtual` to methods that need to be overridable for faking. Do not change anything else in those methods.

### 2a. Add virtual seams

For each service method that must be faked, add `virtual`:

```csharp
public virtual async Task<Uri?> GetInboxUriAsync(...)   // was non-virtual
public virtual async Task<HttpResponseMessage> PostAsync(...)
```

### 2b. Re-compose the target method

Split the large method into:

```
public static <ReturnType> <OriginalMethod>(...)   // thin dispatcher — user-facing
internal static <SliceReturn> Handle<CaseA>(...)   // one per case arm
internal static <SliceReturn> Handle<CaseB>(...)
```

**Dispatcher pattern:**

```csharp
public static async Task<Results<BadRequest<string>, Accepted>> Inbox(...)
{
    // guard that stays in dispatcher (user lookup, etc.)
    if (user is null)
        return TypedResults.BadRequest("User could not be found.");

    try
    {
        string location = obj switch
        {
            CaseA a => await HandleCaseA(...),
            CaseB b => HandleCaseB(...),
            _       => throw new InvalidOperationException("The Object type was not supported.")
        };
        return TypedResults.Accepted(location);
    }
    catch (InvalidOperationException ex)
    {
        return TypedResults.BadRequest(ex.Message);
    }
}
```

**Slice pattern** — throws on error, returns string on success:

```csharp
internal static async Task<string> HandleCaseA(...)
{
    if (badCondition)
    {
        throw new InvalidOperationException("Descriptive message.");
    }
    // ... logic ...
    return "Accepted";
}

internal static string HandleCaseB(...)
{
    // ...
    throw new InvalidOperationException("...");
    // ...
    return "Accepted";
}
```

Never duplicate logic from a service to avoid passing it as a parameter. If a service method is needed in a slice, pass the service instance to that slice.

### 2c. Add InternalsVisibleTo

Create `AssemblyInfo.cs` in the server project root:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("<TestProjectName>")]
```

### 2d. Commit

```
refactor: extract Handle<X> and Handle<Y> from <OriginalMethod> dispatcher
```

One commit, only the production files touched.

## Phase 3 — Test Project

If no test project exists, create one:

```
Tests/<ServerProject>.Tests/
  <ServerProject>.Tests.csproj
  GlobalUsings.cs
  FakeService.cs
  TestHelpers.cs
  Handle<CaseA>Tests.cs
  Handle<CaseB>Tests.cs
  <DispatcherName>Tests.cs
  stryker-config.json
```

Add to solution:
```bash
dotnet sln add Tests/<ServerProject>.Tests/<ServerProject>.Tests.csproj
```

### csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Server\<ServerProject>\<ServerProject>.csproj" />
  </ItemGroup>
</Project>
```

### GlobalUsings.cs

```csharp
global using Xunit;
```

### FakeService.cs

Override only the virtual seam methods. Capture posted objects for payload assertions.

```csharp
internal class Fake<ServiceName>(
    <ReturnType>? <result> = null,
    HttpStatusCode postStatus = HttpStatusCode.OK)
    : <ServiceName>(new HttpClient(), new ConfigurationBuilder().Build())
{
    public IObjectOrLink? LastPostedObject { get; private set; }

    public override Task<Uri?> Get<X>Async(IObjectOrLink person)
        => Task.FromResult(<result>);

    public override Task<HttpResponseMessage> PostAsync(IObjectOrLink obj, Uri uri)
    {
        LastPostedObject = obj;
        return Task.FromResult(new HttpResponseMessage(postStatus));
    }
}
```

### TestHelpers.cs

```csharp
internal static class TestHelpers
{
    internal static ActivityPubDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ActivityPubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static IConfiguration Config(string server = "https://test.example.com") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["HostUrls:Server"] = server })
            .Build();

    // Keep Describe only if the dispatcher tests need it (Results<BadRequest,Accepted>)
    internal static string Describe(Results<BadRequest<string>, Accepted> r) =>
        r.Result switch
        {
            BadRequest<string> b => $"400: {b.Value}",
            Accepted a           => $"202: {a.Location}",
            _                    => "???"
        };
}
```

### stryker-config.json

```json
{
  "stryker-config": {
    "project": "<ServerProject>.csproj",
    "reporters": ["html", "progress"],
    "mutation-level": "Standard",
    "target-framework": "net10.0"
  }
}
```

### Test structure

**For each slice (`Handle<CaseX>Tests`):**

| Scenario | Assert style |
|---|---|
| Error path (throws) | `Assert.ThrowsAsync<InvalidOperationException>(() => ...)` + `Assert.Equal("message", ex.Message)` |
| Success + DB state | `Assert.Equal("Accepted", result)` then `Assert.Equal(expected, db.Table.Count())` |
| Success + payload | `var x = Assert.IsType<AcceptType>(fake.LastPostedObject); Assert.Single(x.Field); ...` |

Cover every branch: each early `throw`, each `return`, the DB-not-found path, already-exists path, new-record path.

**For the dispatcher (`<DispatcherName>Tests`):** 3 thin tests — user not found, unknown type, happy-path smoke test. Use `Describe(result)`.

**Do not use snapshot/approval libraries.** Inline expected strings directly with `Assert.Equal`.

## Phase 4 — First Run and Green

```bash
dotnet test Tests/<Project>.Tests/
```

All tests must pass. If any fail, fix before continuing.

Commit:
```
test: add xUnit scaffolding for <Handle<X>> and <Handle<Y>>
```

Only test project files in this commit.

## Phase 5 — Mutation Testing

```bash
cd Tests/<Project>.Tests
dotnet stryker --reporter json --mutate "**/<TargetFile>.cs"
```

Parse the JSON report. For each **Survived** mutant in the target file:

| Mutant type | Fix |
|---|---|
| Statement removed (`db.Add(...)`, `db.SaveChanges()`) | Add `Assert.Equal(expected, db.Table.Count())` after the call |
| Object/collection initializer emptied | Assert the field via `fake.LastPostedObject` |
| String mutation on a meaningful field | Assert `Assert.StartsWith(...)` or `Assert.Equal(...)` on that field |
| `First()` → `FirstOrDefault()` | Leave — behaviour-identical when collection is guaranteed non-empty |

Iterate until covered kill rate ≥ 80%. Then commit:

```
test: add DB state and payload assertions to kill surviving mutants
```

## Phase 6 — Clean Commits

Final history should be exactly:

1. `refactor: extract Handle<X> and Handle<Y> from <Dispatcher>` — production files only
2. `test: add xUnit scaffolding for Handle<X> and Handle<Y>` — test project only
3. `test: add DB state and payload assertions to kill surviving mutants` — test files only (if needed)

Use `git reset HEAD~N` + selective `git add` to get there if commits got messy.

## Constraints

- Never use snapshot/approval testing libraries (Verify, ApprovalTests). Always inline expected strings.
- Never duplicate logic from a service into a slice to avoid a parameter — just pass the service.
- Never change brace style, parameter names, or variable names outside the lines being restructured.
- Never commit both production and test changes in the same commit.
