---
name: land
description: >
  Before-every-commit checklist for C# projects. Runs through plan, reformat, diagnostics, test, and commit in order.
  Use this skill when the user says "land this", "ready to commit", "let's land it", "/land", "land the change",
  "commit this", "run the checklist before committing", or any similar phrase indicating they're about to create a commit.
  Also trigger when the user asks to verify everything is clean before committing. Do NOT skip steps — this is a gate,
  not a suggestion.
---

# Land — Pre-Commit Checklist

Run every step in order. Do not skip any step. Each step is a gate: if it fails, stop and fix before continuing.

---

## Step 1: Plan

Before touching anything, answer these questions aloud:

1. What is the single responsibility of this commit? (One sentence.)
2. Is there any unrelated change mixed in that should be a separate commit?
3. What Conventional Commit type applies? (`feat`, `fix`, `refactor`, `test`, `docs`, `tidy`, `chore`)
4. Draft a commit message now: `<type>: <description>`

If the scope is unclear, ask the user to clarify before proceeding.

---

## Step 2: Reformat

Reformat every modified `.cs` file using JetBrains.

1. Get the list of modified files:
   ```bash
   git diff --name-only HEAD
   git diff --name-only --cached
   ```
2. For each `.cs` file in that list, call `mcp__jetbrains__reformat_file`.
3. If any file was reformatted, note it — the diff will change.

---

## Step 3: Diagnostics

Run diagnostics on all modified `.cs` files using `mcp__ide__getDiagnostics`.

- **Errors**: hard stop. Fix before continuing.
- **Warnings**: review each one. Fix or explicitly justify ignoring it.
- Do not proceed to tests if errors are present.

---

## Step 4: Test

Run the full test suite:

```bash
dotnet test
```

- All tests must be green before committing.
- If a test fails, stop. Diagnose the root cause — do not modify assertions to make tests pass.

---

## Step 5: Commit

1. Show a diff preview:
   ```bash
   git --no-pager diff HEAD
   ```
2. Present the proposed commit message from Step 1 (updated if reformatting changed scope).
3. Wait for the user to confirm the message.
4. Stage and commit — only files relevant to this commit:
   ```bash
   git add <specific files>
   git commit -m "<type>: <description>"
   ```

Do not use `git add .` or `git add -A` — stage files explicitly.

---

## Checklist summary

| Step        | Tool / Command                          | Gate condition              |
|-------------|------------------------------------------|-----------------------------|
| Plan        | (reasoning)                              | Scope is clear and atomic   |
| Reformat    | `mcp__jetbrains__reformat_file`          | All .cs files formatted     |
| Diagnostics | `mcp__ide__getDiagnostics`               | Zero errors                 |
| Test        | `dotnet test`                            | All tests green             |
| Commit      | `git add <files>` + `git commit -m ...`  | User confirms message       |