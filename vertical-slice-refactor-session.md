# Vertical Slice Refactor Session — 2026-03-28

A day-long refactoring of `ActivityPubBotDotNet` toward a ports-and-adapters architecture,
driven by testability and mutation safety.

---

## Session 1 — .NET Upgrade + Strategy (`7c92`)

**Goal:** Upgrade to .NET 10 and scope a testing strategy for `UsersApi.Inbox`.

### Instructions

- "upgrade to .net 10, try simple first just in the csproj"
- "I want to do some minimal refactorings so that I can get `UsersApi.Inbox` under test
  with approval testing and also mutation testing"
- "I want to move to a ports and adapter architecture and move this to an application layer,
  first step is okay if it's transaction script"
- "I would land at 100% coverage plus mutation testing with as few modifications as possible"
- "thinking about this maybe we need to create seams inside `ActivityPubService` and then a
  slice for this one: `HttpResponseMessage response = await activityPub.PostAsync(accept, inbox)`"
- "for the PostAsync I think we can make that overridable" — steered toward virtual method
  override as the seam strategy

### Outcome

- Both projects upgraded from `net7.0` → `net10.0`
- Seam strategy decided: `virtual` methods on `ActivityPubService` (`PostAsync`, `GetInboxUriAsync`)
  overridden in a `FakeActivityPubService` test double

---

## Session 2 — Implementation + Discipline (`b2d3`)

**Goal:** Implement the plan: seams, tests, approval testing, mutation testing.

### Instructions

- Handed a pre-written plan: *"Inbox — Seams + Re-composition + Approval + Mutation Tests"*
- "how's test coverage and mutation testing looking for the 2 methods?"
- "show me how you plan on fixing them?" → reviewed → "go ahead" → "yes go ahead"
- "run Stryker again and show me the final score"
- "lets remove all the verified.txt" (snapshot cleanup)
- "lets commit dotnet 10" (separate clean commit)
- "lets do an errorcode to exception refactor"
- "handlefollow and handleundo, badrequest should be exception and then just return a string for accept"
- `/land` — ran the pre-commit checklist
- "can we make this process into a reusable skill?" — extracted the `land` skill

### Corrections (frequent)

- **"i just like seeing clean commits, and renames all over is and also seems like minor changes
  has snuck in"** — unrelated changes had leaked into commits
- **"we need to stick to minimal prod changes, now you've duplicated logic without tests"** —
  untested duplication introduced
- **"can we make the commits clean?"** — commit hygiene still not right
- **"you forgot to keep the names and you also removed braces"** — formatting and names changed
  without being asked

### Outcome

- `FakeActivityPubService` test double with overridable seams
- `HandleFollow` and `HandleUndo` fully covered and mutation-tested
- `BadRequest` returns replaced with `InvalidOperationException` (cleaner exception-based flow)
- Snapshot files removed; plain `Assert.Equal` used throughout
- `land` skill extracted from the workflow

---

## Session 3 — Abandoned (`dc829eb3`)

- "lets push handleundo and handlefollow down to application layer"
- Interrupted immediately and restarted as Session 4

---

## Session 4 — Application Layer + Split Phase (`8cd2`)

**Goal:** Extract business logic from `UsersApi` into a proper application-layer service,
then apply Split Phase refactoring.

### Instructions in sequence

1. "let's create an application layer and push the 2 methods HandleFollow and HandleUndo down"
   → Created `InboxService` with constructor injection; registered in DI; updated all tests

2. "lets do a split phase of this, split into validation and business logic"
   → Split each method: pure `Validate*` phase → intermediate record → business logic phase

3. **"i think accept is business logic? it's mutating or what?"** ← correction
   → Sending `PostAsync(accept, inbox)` had been placed in the validation phase. You identified
   it as a side effect. Moved to phase 2; intermediate record expanded to carry
   `(FollowerId, FollowerInbox, Follow)` so phase 2 could build the `Accept` message itself.

4. "lets call the method parse instead of validate"
   → Renamed `Validate*` → `Parse*` throughout; the naming reflects that the phase returns
   structured data, not a boolean.

5. "lets extract this session as a skille"
   → Created `~/.claude/skills/split-phase/SKILL.md` capturing the full workflow

6. `/land`
   → Reformat → diagnostics (all pre-existing, none new) → 16/16 tests green → committed:
   `refactor: extract InboxService application layer with split-phase pattern`

### Outcome

`InboxService` structure after session:

```
HandleFollow(userId, follow)           // thin orchestrator
  └─ ParseFollowAsync()                // pure: validate inputs, resolve inbox URI
       → ValidatedFollow(FollowerId, FollowerInbox, Follow)
  └─ AcceptAndPersistFollow()          // side effects: HTTP POST + DB write

HandleUndo(undo)                       // thin orchestrator
  └─ ParseUndo()                       // pure: validate undo contains well-formed Follow
       → ValidatedUndo(ActorId, ObjectUri)
  └─ RemoveFollowRelation()            // side effect: DB delete
```

---

## Throughline

You had a clear architectural vision from the start: ports and adapters, minimal production
changes, testable boundaries, mutation-safe. The main friction in session 2 was scope creep —
unrelated renames, format changes, untested duplication. You consistently pulled back to
discipline: one concern per commit, no changes beyond what was asked.

By session 4 the pattern was clean: each instruction was a single, directed refactoring move.
The one correction (Accept belongs in phase 2) came from domain knowledge — you knew that
sending an HTTP message is a mutation, regardless of where it sits in the flow.

**Key design decisions made by you, not me:**
- Virtual overrides as the seam strategy (not interfaces, not wrapper classes)
- Exceptions over `BadRequest` return values
- `Parse*` over `Validate*` for the phase 1 naming
- Accept is business logic
