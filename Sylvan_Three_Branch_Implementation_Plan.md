# Sylvan Post-Sync Preservation Implementation Plan

## Document status

**Status:** Draft  
**Working repository:** `https://github.com/JaseRandall/Sylvan`  
**Upstream pull-request target:** `https://github.com/MarkPflug/Sylvan`  
**Baseline:** Current `MarkPflug/Sylvan:main`, synchronised into `JaseRandall/Sylvan:main`  
**Source requirements:** `Sylvan_Post_Sync_Preservation_Review_Updated.md` and `Sylvan_SchemaAnalyzer_Temporal_Inference_Requirements.md`  
**Related upstream work:** `MarkPflug/Sylvan#281` and merged `MarkPflug/Sylvan.Data.Excel#198`

## 1. Purpose

Implement the three preserved areas of functionality from the discarded `Upgrade_to_NET_9` branch using three independent branches in `JaseRandall/Sylvan`.

Each branch will produce a separate pull request to `MarkPflug/Sylvan:main`:

1. temporal schema inference;
2. `EncoderStream` ownership and finalization;
3. `InvalidEnumValueException` diagnostics.

This division keeps the themes independently reviewable and allows upstream maintainers to accept, reject or defer each area without requiring an all-or-nothing merge.

## 2. Branch structure

Create exactly these three branches in `JaseRandall/Sylvan`:

| Branch | Theme | Upstream pull request |
|---|---|---|
| `feature/schema-analyzer-temporal-inference` | Complete temporal schema inference requirements | `JaseRandall:feature/schema-analyzer-temporal-inference` → `MarkPflug:main` |
| `feature/encoder-stream-ownership-finalization` | Configurable ownership and complete encoder finalization | `JaseRandall:feature/encoder-stream-ownership-finalization` → `MarkPflug:main` |
| `fix/invalid-enum-value-exception-message` | Descriptive invalid-enum binding diagnostics | `JaseRandall:fix/invalid-enum-value-exception-message` → `MarkPflug:main` |

All three branches must start from the same clean, synchronized upstream baseline. They must not be branched from each other.

The temporal branch must also account for the current status of `MarkPflug/Sylvan#281`, which proposes changing the general `DbType.Date` mapping on supported frameworks. The temporal implementation must not duplicate or compete with that pull request.

## 3. Repository preparation

Before creating any implementation branch:

```bash
git remote add upstream https://github.com/MarkPflug/Sylvan.git
git fetch upstream
git switch main
git reset --hard upstream/main
git push origin main --force-with-lease
```

Confirm:

```bash
git status
git rev-parse main
git rev-parse upstream/main
```

The two commit SHAs must match and the working tree must be clean.

Create each branch independently:

```bash
git switch main
git switch -c feature/schema-analyzer-temporal-inference
git push -u origin feature/schema-analyzer-temporal-inference
```

```bash
git switch main
git switch -c feature/encoder-stream-ownership-finalization
git push -u origin feature/encoder-stream-ownership-finalization
```

```bash
git switch main
git switch -c fix/invalid-enum-value-exception-message
git push -u origin fix/invalid-enum-value-exception-message
```

Do not copy commits from `Upgrade_to_NET_9`. Reimplement the accepted behaviour against the synchronized upstream source.

Before creating `feature/schema-analyzer-temporal-inference`, inspect `MarkPflug/Sylvan#281`:

- if it remains open, preserve current upstream `DbType.Date` behaviour and do not duplicate its mapping change;
- if it has merged, preserve the merged behaviour and remove any overlapping mapping work from this plan;
- in either case, keep analyzer opt-in behaviour local to `SchemaAnalyzerOptions`;
- do not introduce or depend on an `AppContext` switch for temporal inference;
- ensure explicit schema tokens resolve directly to CLR types rather than through the general `DbType.Date` or `DbType.Time` mappings.

## 4. Shared implementation rules

All branches must comply with the following controls:

- preserve the current upstream target frameworks;
- preserve current upstream public API unless the requirements explicitly call for an additive API;
- do not introduce unrelated C# modernization;
- do not reformat unrelated files;
- do not add process-global `AppContext` switches for analyzer behaviour;
- preserve the upstream general `DbType.Date` mapping rather than redefining it in the temporal branch;
- do not duplicate functionality proposed by `MarkPflug/Sylvan#281`;
- preserve current GUID support;
- retain genuinely synchronous and asynchronous code paths where required;
- add focused automated tests with each behavioural change;
- update documentation only where relevant to the branch;
- keep each branch buildable and testable in isolation;
- rebase each branch onto the latest `upstream/main` immediately before opening its pull request.

## 5. Branch 1 — Temporal schema inference

### 5.1 Branch

```text
feature/schema-analyzer-temporal-inference
```

### 5.2 Objective

Implement the complete `Sylvan SchemaAnalyzer Temporal Inference Requirements` as one coherent upstream contribution.

The branch must add reliable, opt-in inference for:

- `DateOnly`;
- `TimeOnly`;
- `TimeSpan`;

while preserving all existing default behaviour.

### 5.3 Requirements covered

This branch covers:

- SATI-REQ-001 through SATI-REQ-017;
- all temporal performance requirements;
- all target-framework requirements;
- all temporal automated-test requirements;
- all temporal documentation requirements;
- all temporal acceptance criteria.

The `DbType.Time` to `TimeSpan` and `TimeSpan` to `DbType.Time` mapping work is part of this branch under SATI-REQ-013. It is not a separate branch.

The branch must not independently change the general `DbType.Date` to CLR mapping. That mapping is upstream-owned behaviour and is already the subject of `MarkPflug/Sylvan#281`. Temporal inference must represent `DateOnly` explicitly through schema CLR metadata rather than by redefining what `DbType.Date` means globally.

### 5.4 Expected implementation areas

Review the synchronized upstream layout before modifying files. Expected areas include:

```text
source/Sylvan.Data/SchemaAnalyzer.cs
source/Sylvan.Data/SchemaAnalyzerOptions.cs
source/Sylvan.Data/SchemaAnalysisResult.cs
source/Sylvan.Data/SchemaSerializer.cs
source/Sylvan.Data/DataBinderAccessors.cs
source/Sylvan.Data/CompiledDataBinder.cs
source/Sylvan.Data.Tests/SchemaAnalyzerTests.cs
source/Sylvan.Data.Tests/SchemaSerializerTests.cs
source/Sylvan.Data.Tests/DataBinderTests.cs
docs/Data/*
```

Do not reproduce the discarded branch's file split unless that split is independently justified by the current upstream code.

### 5.5 Internal commit sequence

Although this is one branch and one pull request, implement it through reviewable commits.

#### Commit 1 — Add regression and temporal contract tests

Add failing tests that establish:

- default inference remains unchanged;
- existing Boolean, numeric, `DateTime`, GUID, string and null inference remains unchanged;
- synchronous analysis does not call asynchronous reads;
- synchronous and asynchronous row limits match;
- typed `DateOnly`, `TimeOnly` and `TimeSpan` columns are preserved;
- serializer round-trip expectations for the three temporal CLR types;
- binder expectations for nullable and non-nullable temporal properties.

Suggested commit message:

```text
Add temporal schema inference contract tests
```

#### Commit 2 — Add temporal options and typed schema plumbing

Implement the additive public configuration contract.

Preferred API:

```csharp
[Flags]
public enum TemporalInferenceOptions
{
    None = 0,
    DateOnly = 1,
    TimeOnly = 2,
    TimeSpan = 4
}
```

```csharp
public TemporalInferenceOptions TemporalInference { get; set; }
```

Requirements:

- default value is `None`;
- no environment or global switch enables inference;
- explicit culture configuration is local to `SchemaAnalyzerOptions`;
- direct typed reader access remains based on physical CLR type;
- preserve the current upstream general `DbType.Date` mapping;
- do not add `DbType.Date -> DateOnly` as part of this branch;
- `DbType.Time` maps to `TimeSpan` for common ADO.NET mapping;
- reverse CLR-to-`DbType` mappings support `DateOnly -> DbType.Date`, `TimeOnly -> DbType.Time` and `TimeSpan -> DbType.Time`;
- the implementation must remain correct whether `MarkPflug/Sylvan#281` is open, merged, closed or superseded.

Suggested commit message:

```text
Add temporal schema options and type mappings
```

#### Commit 3 — Add unambiguous schema serialization

Add distinct case-insensitive schema tokens:

```text
dateonly
timeonly
timespan
```

Requirements:

- the serializer emits tokens accepted by the parser;
- `dateonly` resolves directly to `typeof(DateOnly)`;
- `timeonly` resolves directly to `typeof(TimeOnly)`;
- `timespan` resolves directly to `typeof(TimeSpan)`;
- temporal tokens must not be resolved indirectly through `DataBinder.GetDataType(DbType)`;
- `TimeOnly` and `TimeSpan` remain distinguishable even though both use `DbType.Time`;
- schema round-tripping must not depend on the outcome of `MarkPflug/Sylvan#281` or an `AppContext` setting;
- `Schema.Parse(schema.ToString())` preserves the CLR type;
- existing schema tokens remain compatible.

Suggested commit message:

```text
Add temporal schema serialization tokens
```

#### Commit 4 — Implement DateOnly and TimeOnly inference

Implement independent candidates using:

- `DateOnly.TryParse`;
- `TimeOnly.TryParse`.

Requirements:

- do not infer `TimeOnly` through `TimeSpan.TryParse`;
- do not select `DateOnly` when any non-null sampled value has a time component;
- invalid values eliminate only the affected candidate;
- null and empty values affect nullability without providing positive evidence;
- default inference remains unchanged when the options are disabled;
- existing upstream tests must continue asserting existing upstream behaviour rather than using a framework-dependent `DateOnly`/`DateTime` alias;
- new tests must demonstrate that enabling `DateOnly` inference changes only the explicitly opted-in analyzer result;
- `Analyze` remains genuinely synchronous;
- `AnalyzeAsync` remains genuinely asynchronous;
- both paths use equivalent candidate initialization and finalization rules.

Suggested commit message:

```text
Implement opt-in DateOnly and TimeOnly inference
```

Do not add general `DateTime` to `DateOnly` binder conversion in this branch. Mark Pflug suggested that as a possible alternative direction in `MarkPflug/Sylvan#281`, but it introduces separate conversion-policy questions and is outside the approved inference scope unless explicitly requested during review.

#### Commit 5 — Implement TimeSpan inference and ambiguity handling

Track independent column-level state equivalent to:

```text
allValuesParseAsTimeOnly
allValuesParseAsTimeSpan
sawExplicitSign
sawDayComponent
sawNegativeDuration
sawDurationAtLeastThreshold
columnNameIndicatesDuration
```

Final selection must occur after the sample has been processed.

When both `TimeOnly` and `TimeSpan` remain viable:

1. choose `TimeSpan` when strong duration evidence exists;
2. otherwise choose `TimeSpan` when a configured duration-name hint matches;
3. otherwise choose `TimeOnly`.

Prevent plain integer values such as `"1"`, `"2"` and `"3"` from being inferred as durations merely because `TimeSpan.TryParse` can treat them as days.

Suggested commit message:

```text
Implement order-independent TimeSpan inference
```

#### Commit 6 — Handle series compatibility

Choose and document one of these valid approaches:

- explicitly preserve GUID, `DateOnly`, `TimeOnly` and `TimeSpan` in the series type lattice; or
- prevent unsupported temporal columns from being collapsed into a series.

Silent degradation to `DateTime` or `string` is prohibited.

Prefer the narrower exclusion approach when full temporal series support would materially enlarge the upstream change.

Suggested commit message:

```text
Prevent temporal series type degradation
```

#### Commit 7 — Complete documentation and release notes

Update:

- `SchemaAnalyzer` API documentation;
- `SchemaAnalyzerOptions` documentation;
- schema-format documentation;
- `Sylvan.Data` release notes;
- examples for opt-in temporal inference;
- examples for explicit culture;
- `TimeOnly` versus `TimeSpan` ambiguity rules.

State explicitly:

```text
Default SchemaAnalyzer behaviour is unchanged unless temporal inference is enabled.
```

Suggested commit message:

```text
Document temporal schema inference
```

### 5.6 Candidate-state design

Candidate viability must be monotonic:

- candidates may move from viable to invalid;
- candidates must not be re-enabled;
- an ambiguous row must not choose the final type;
- row order must not affect the result.

Maintain constant state per column. Do not buffer all sampled values.

Precompute duration-name hint matching once per column. Do not use per-cell regular expressions.

### 5.7 Culture handling

When no explicit culture is configured, preserve current upstream parsing behaviour.

When a culture is configured:

- apply it to dates;
- apply it to time-of-day values;
- apply it to durations where supported;
- preserve appropriate numeric parsing grammar.

Do not apply one `NumberStyles.Number` value indiscriminately to integers, floating-point numbers and decimals. Either preserve existing numeric parsing or introduce category-specific styles with equivalent defaults.

### 5.8 Required tests

#### Regression tests

Verify:

- Boolean inference;
- integer inference;
- floating-point inference, including exponent syntax;
- decimal inference;
- `DateTime` inference;
- GUID inference;
- string fallback;
- null and empty handling;
- synchronous analysis uses synchronous reads;
- async analysis uses asynchronous reads.

#### DateOnly tests

Cover:

- ISO dates;
- culture-specific dates;
- nullable dates;
- empty values;
- mixed dates and date-times;
- invalid values;
- typed `DateOnly` source;
- serializer round-trip;
- sync/async parity.

#### TimeOnly tests

Cover:

- 24-hour values;
- 12-hour culture-specific values;
- values near midnight;
- nullable values;
- invalid times;
- values outside a day;
- typed `TimeOnly` source;
- serializer round-trip;
- sync/async parity.

#### TimeSpan tests

Cover:

- ambiguous sub-day durations;
- negative durations;
- explicit positive durations;
- day components;
- durations at or above 24 hours;
- nullable values;
- invalid durations;
- typed `TimeSpan` source;
- serializer round-trip;
- sync/async parity.

#### Ambiguity tests

Test both orders of:

```text
01:30:00
1.02:03:04
```

and:

```text
01:00:00
25:00:00
```

Both orders must produce the same result.

Also verify:

- ambiguous sub-day values choose `TimeOnly` when both candidates are enabled and there is no duration evidence;
- duration-name hints choose `TimeSpan`;
- enabling only one candidate chooses that viable candidate;
- plain integer columns remain numeric.

#### Binder tests

Verify nullable and non-nullable binding for:

- `DateOnly`;
- `TimeOnly`;
- `TimeSpan`.

Use readers whose physical schema reports the actual CLR types.

Do not add a general `DateTime` source to `DateOnly` property conversion test unless the upstream maintainer explicitly requests that design during review.

#### Series tests

Verify homogeneous GUID and temporal columns are either preserved in a series or deliberately left uncollapsed, according to the selected design.

### 5.9 Upstream coordination for PR #281

Immediately before implementation and again before opening the temporal pull request:

1. inspect the status and diff of `MarkPflug/Sylvan#281`;
2. inspect the latest upstream `DataBinder.GetDataType(DbType)` behaviour;
3. confirm whether the general `DbType.Date` mapping is still `DateTime`, has changed to `DateOnly`, or has gained compatibility-switch behaviour;
4. preserve that accepted upstream behaviour without copying or replacing it;
5. keep temporal schema tokens and analyzer output independent of that mapping;
6. describe the relationship to PR #281 in the temporal pull-request body.

The merged `MarkPflug/Sylvan.Data.Excel#198` confirms that schema-directed `DateOnly`, `TimeOnly` and `TimeSpan` access through `GetFieldValue<T>` is an accepted integration pattern. Prefer explicit schema CLR types and typed accessors over changing general `DbType` interpretation.

### 5.10 Validation

At minimum:

```bash
dotnet test source/Sylvan.Data.Tests/Sylvan.Data.Tests.csproj
dotnet test source/Sylvan.Data.Csv.Tests/Sylvan.Data.Csv.Tests.csproj
```

Then run the repository's complete build and test workflow across every upstream target framework.

### 5.11 Pull-request review checkpoints

The pull request description should divide the review into these checkpoints:

1. additive API and defaults;
2. schema tokens and mappings;
3. synchronous/asynchronous analyzer structure;
4. temporal candidate logic;
5. ambiguity rules;
6. series behaviour;
7. tests and compatibility.

This allows reviewers to assess the large branch by coherent sections while retaining one temporal pull request.

### 5.12 Acceptance criteria

The branch is complete when:

- all SATI requirements are implemented or explicitly documented as deferred;
- the current status of `MarkPflug/Sylvan#281` has been reviewed and documented;
- the branch does not redefine the upstream general `DbType.Date` mapping;
- temporal schema tokens resolve directly to their CLR types;
- default analyzer results match upstream;
- `Analyze` remains synchronous;
- `AnalyzeAsync` remains asynchronous;
- GUID behaviour remains intact;
- temporal inference is opt-in;
- inference is row-order independent;
- typed temporal columns are preserved;
- serializer round-tripping preserves CLR types;
- direct binder access works;
- no temporal series silently degrades;
- all upstream target frameworks build;
- all existing and new tests pass.

## 6. Branch 2 — EncoderStream ownership and finalization

### 6.1 Branch

```text
feature/encoder-stream-ownership-finalization
```

### 6.2 Objective

Add explicit ownership of the underlying stream and ensure final encoded output is fully drained before disposal.

### 6.3 Requirements covered

- ES-REQ-001 through ES-REQ-005.

### 6.4 Expected files

```text
source/Sylvan.Common/IO/EncoderStream.cs
source/Sylvan.Common.Tests/IO/EncoderStreamTests.cs
```

### 6.5 Internal commit sequence

#### Commit 1 — Add lifecycle tests

Add test doubles and failing tests for:

- existing constructor leaves the stream open;
- `ownsStream: false`;
- `ownsStream: true`;
- null constructor arguments;
- empty `Flush`;
- empty `FlushAsync`;
- repeated `RequiresOutputSpace`;
- repeated `Flush`;
- multiple empty-input finalization calls;
- repeated disposal;
- preservation of final encoded bytes.

Suggested commit message:

```text
Add EncoderStream lifecycle tests
```

#### Commit 2 — Add ownership constructor

Add:

```csharp
public EncoderStream(
    Stream stream,
    Encoder encoder,
    bool ownsStream)
```

Make the existing constructor delegate to it with `ownsStream: false`.

Validate both dependencies with `ArgumentNullException`.

Suggested commit message:

```text
Add configurable EncoderStream ownership
```

#### Commit 3 — Implement robust finalization

Move final draining into the normal `Dispose(bool)` lifecycle.

On empty-input encoding:

- `RequiresOutputSpace` → flush and retry;
- `Flush` → flush and retry;
- `Complete` → flush remaining output and finish.

Requirements:

- finalization runs at most once;
- all output is written before the owned stream is disposed;
- repeated disposal is harmless;
- the base `Stream` implementation is called;
- `Close` follows standard stream disposal routing.

Suggested commit message:

```text
Complete EncoderStream finalization during disposal
```

#### Commit 4 — Avoid empty writes and document ownership

Add early return from `Flush` and `FlushAsync` when no bytes are buffered.

Update XML documentation for both constructors and disposal semantics.

Suggested commit message:

```text
Avoid empty EncoderStream writes
```

### 6.6 Implementation caution

A malformed custom encoder could theoretically return `Flush` forever. Do not add an arbitrary iteration limit unless upstream maintainers request defensive protection; normal encoder contracts should eventually return `Complete`.

Do not introduce unrelated changes to encoder implementations.

### 6.7 Validation

```bash
dotnet test source/Sylvan.Common.Tests/Sylvan.Common.Tests.csproj
```

Run the full repository build afterward.

### 6.8 Acceptance criteria

- existing constructor behaviour is unchanged;
- ownership is additive and explicit;
- null arguments fail immediately;
- empty flushes do not write;
- finalization handles every required encoder result;
- final bytes are preserved;
- disposal is idempotent;
- owned and non-owned streams behave correctly;
- all target frameworks build and test.

## 7. Branch 3 — InvalidEnumValueException message

### 7.1 Branch

```text
fix/invalid-enum-value-exception-message
```

### 7.2 Objective

Provide actionable enum-binding failure diagnostics without changing binding behaviour or expanding the public API.

### 7.3 Requirements covered

- IEV-REQ-001 through IEV-REQ-003.

### 7.4 Expected files

```text
source/Sylvan.Data/DataBinderExceptions.cs
source/Sylvan.Data.Tests/DataBinderTests.cs
```

A focused exception test file may be added if that better matches the current upstream test layout.

### 7.5 Internal commit sequence

#### Commit 1 — Add exception diagnostic test

Trigger a binder conversion using an invalid textual enum value.

Assert:

- the exception is `InvalidEnumValueException`;
- it remains a `FormatException`;
- the message includes the rejected value;
- the message identifies the enum type;
- `Value` contains the rejected value;
- `EnumType` contains the target enum type.

Suggested commit message:

```text
Add invalid enum binding diagnostic test
```

#### Commit 2 — Add descriptive exception message

Update the existing internal constructor to call the base constructor with a descriptive message.

Suitable form:

```csharp
: base(
    $"The value \"{value}\" is not a valid member of the enum type {enumType}.")
```

Preserve:

- constructor visibility;
- exception inheritance;
- `Value`;
- `EnumType`.

Do not add unrelated public constructors.

Suggested commit message:

```text
Improve InvalidEnumValueException message
```

### 7.6 Validation

```bash
dotnet test source/Sylvan.Data.Tests/Sylvan.Data.Tests.csproj
```

### 7.7 Acceptance criteria

- the message is descriptive;
- both diagnostic properties remain correct;
- binding semantics are unchanged;
- no public API expansion occurs;
- all tests pass.

## 8. Development and submission order

Recommended order:

1. `fix/invalid-enum-value-exception-message`;
2. `feature/encoder-stream-ownership-finalization`;
3. `feature/schema-analyzer-temporal-inference`.

The enum branch is lowest risk and provides a small initial upstream contribution.

The `EncoderStream` branch is independent and moderately scoped.

The temporal branch is the largest and should be submitted after the smaller pull requests, but it must remain based only on upstream `main`. Do not make it depend on either smaller branch.

If upstream merges one of the smaller pull requests before the temporal pull request is ready, rebase the temporal branch onto the new upstream `main`. Resolve only genuine overlap; do not merge the other feature branch directly into it.

## 9. Pre-pull-request synchronization

Before opening each pull request:

```bash
git fetch upstream
git switch <branch-name>
git rebase upstream/main
```

For `feature/schema-analyzer-temporal-inference`, re-check `MarkPflug/Sylvan#281` after rebasing and remove or revise any overlap before submission.

Then run the full affected tests again and push:

```bash
git push --force-with-lease
```

Confirm the pull request contains only the intended theme:

```bash
git diff --stat upstream/main...HEAD
git log --oneline upstream/main..HEAD
```

## 10. Pull-request content

Each pull request must include:

- the problem statement;
- branch theme;
- requirement identifiers addressed;
- compatibility statement;
- files changed;
- tests added;
- commands used for validation;
- deliberately excluded work;
- target-framework validation results.

### Temporal pull request compatibility statement

```text
Default SchemaAnalyzer behaviour is unchanged unless temporal inference is explicitly enabled.

This change does not redefine the general DbType.Date mapping and does not duplicate MarkPflug/Sylvan#281. Explicit temporal schema tokens resolve directly to their CLR types.
```

### EncoderStream pull request compatibility statement

```text
The existing two-argument constructor remains non-owning. Stream ownership is available only through the new additive constructor.
```

### Enum pull request compatibility statement

```text
This change affects only the exception message produced for invalid enum binding values.
```

## 11. Review and merge independence

The three pull requests must remain independently mergeable:

- rejecting temporal inference must not block the stream or exception improvements;
- rejecting stream ownership must not block temporal inference or enum diagnostics;
- rejecting the enum message must not block the other two themes.

No pull request may reference types, APIs or commits introduced only by another preservation branch.

## 12. Final validation

After all accepted upstream pull requests have merged:

```bash
git fetch upstream
git switch main
git reset --hard upstream/main
git push origin main --force-with-lease
```

Run the repository's full CI-equivalent build and test process.

At minimum:

```bash
dotnet restore source
dotnet build source --no-restore
dotnet test source/Sylvan.Common.Tests/Sylvan.Common.Tests.csproj --no-build
dotnet test source/Sylvan.Data.Tests/Sylvan.Data.Tests.csproj --no-build
dotnet test source/Sylvan.Data.Csv.Tests/Sylvan.Data.Csv.Tests.csproj --no-build
```

Verify:

- all accepted functionality is present in synchronized `main`;
- the accepted upstream `DbType.Date` behaviour remains intact;
- temporal schema round-tripping does not depend on the general `DbType.Date` mapping or an `AppContext` switch;
- no implementation still depends on `Upgrade_to_NET_9`;
- the three working branches may be deleted after their pull requests are merged or closed;
- the discarded branch contains no remaining undocumented functionality.

## 13. Completion criteria

The implementation programme is complete when:

1. all three branches exist in `JaseRandall/Sylvan`;
2. each branch started from synchronized upstream `main`;
3. each branch contains only its assigned theme;
4. each branch has focused automated tests;
5. each branch has a separate upstream pull request;
6. accepted pull requests are merged independently;
7. rejected or deferred themes do not prevent acceptance of the others;
8. the fork is resynchronized after upstream decisions;
9. `Upgrade_to_NET_9` can be deleted without losing required functionality.
