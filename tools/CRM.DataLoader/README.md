# CRM.DataLoader — one-off migration tool

Loads the **signed-off review sheet** into an empty `Participants` table, writing the audit
rows the application would have written had a person typed each record in.

**Delete this folder once the migration is done.** It is deliberately outside `CRM.sln` so CI
never builds it and `dotnet publish` cannot ship it — verified: the publish output contains
only `CRM.API`.

## The three-step flow

1. `python3 xls/build_review_sheet.py` — turns the messy contact workbook into one clean
   review worksheet. Surfaces hidden rows, exposes shifted rows, cross-checks both
   anaphylaxis signals, clusters each person onto one line, flags every judgement call.
2. **A person who knows the families reviews it** and resolves every flag. This is the step
   that matters; the tooling exists to make it possible, not to replace it.
3. Save the reviewed sheet as `.csv` and run this loader.

## Running it

Dry run first — validates the sheet, writes nothing, and does not need an empty table:

```
dotnet run --project tools/CRM.DataLoader -- \
  --csv "../xls/REVIEW - Stars to enter.csv" \
  --conn "<connection string>"
```

Then, against the empty production database:

```
dotnet run --project tools/CRM.DataLoader -- \
  --csv "../xls/REVIEW - Stars to enter.csv" \
  --conn "<connection string>" \
  --commit
```

`--conn` may be omitted if `CRM_CONN` is set in the environment.

## What it refuses to do

It aborts the entire load — writing nothing — if any of these hold:

- `Participants` is not empty (asserted twice: before the transaction, and again inside it)
- any row still carries an unresolved `REVIEW`, `CONFLICT`, `SHIFT` or `MISSING` flag
- `Anaphylactic?` still reads `REVIEW` on any row
- a start date is missing or not `YYYY-MM-DD` (the entity defaults it to *today* otherwise)
- a programme slug is blank or unknown, or a status is not a real `ParticipantStatus`
- any value exceeds its column limit — it fails loudly rather than truncating, because a
  silently shortened guardian phone is a number nobody can call

There is no fallback behaviour for any of these on purpose. A fallback is a guess, and the
guesses are what made a naive import unsafe in the first place.

## Why it does not call IAuditService

The application's audit writer is deliberately **fail-open on a separate DbContext**, so a
failed audit write never blocks a teacher halfway through attendance. That is the right trade
for the app and the wrong one here: a bulk load that half-commits its audit trail is worse
than one that does not run.

So the loader builds rows with the same `AuditEventFactory` the application uses — identical
sanitising and length caps, so the rows cannot drift from real ones — and inserts them in the
**same transaction** as the participants. Either every record and its audit row lands, or
nothing does.

It writes one `participant.import` row per record plus one `participant.import.batch` summary
row, each carrying the source filename and a SHA-256 prefix of the reviewed sheet, so a second
run against a different file is detectable after the fact.

Because `EnableRetryOnFailure` installs an execution strategy — and an execution strategy
refuses a user-initiated transaction — the whole unit runs through
`Database.CreateExecutionStrategy().ExecuteAsync(...)`. This is the pattern the comment in
`CRM.Persistence/DependencyInjection.cs` points at.
