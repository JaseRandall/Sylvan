# Sylvan Post-Sync Preservation Implementation Plan

## Document status

**Status:** Draft  
**Working repository:** `https://github.com/JaseRandall/Sylvan`  
**Upstream pull-request target:** `https://github.com/MarkPflug/Sylvan`  
**Baseline:** Current `MarkPflug/Sylvan:main`, synchronised into `JaseRandall/Sylvan:main`  
**Related upstream work:** `MarkPflug/Sylvan#281` and merged `MarkPflug/Sylvan.Data.Excel#198`

## 1. Purpose

Implement the three preserved areas of functionality from the discarded `Upgrade_to_NET_9` branch using three independent branches in `JaseRandall/Sylvan`:

1. temporal schema inference;
2. `EncoderStream` ownership and finalization;
3. `InvalidEnumValueException` diagnostics.

Each theme must remain independently reviewable and mergeable so upstream maintainers can accept, reject or defer it without requiring an all-or-nothing merge.

## 2. Branch structure

| Branch | Theme | Upstream pull request |
|---|---|---|
| `feature/schema-analyzer-temporal-inference` | Complete temporal schema inference requirements | `JaseRandall:feature/schema-analyzer-temporal-inference` → `MarkPflug:main` |
| `feature/encoder-stream-ownership-finalization` | Configurable ownership and complete encoder finalization | `JaseRandall:feature/encoder-stream-ownership-finalization` → `MarkPflug:main` |
| `fix/invalid-enum-value-exception-message` | Descriptive invalid-enum binding diagnostics | `JaseRandall:fix/invalid-enum-value-exception-message` → `MarkPflug:main` |

All three branches must start independently from the same clean, synchronized upstream baseline. They must not be branched from each other.

The temporal branch must account for the current status of `MarkPflug/Sylvan#281`. It must not duplicate or compete with that pull request's proposed general `DbType.Date` mapping change.

## 3. Shared implementation rules

All branches must:

- preserve current upstream target frameworks;
- preserve current public API unless a requirement explicitly calls for an additive API;
- exclude unrelated modernization, formatting and cleanup;
- avoid process-global `AppContext` switches for analyzer behaviour;
- preserve the upstream general `DbType.Date` mapping;
- preserve current GUID support;
- retain genuinely synchronous and asynchronous paths where required;
- include focused automated tests;
- update only relevant documentation;
- remain buildable and testable independently;
- be checked against current upstream before submission.

Do not copy commits mechanically from `Upgrade_to_NET_9`. Reimplement the accepted behaviour against synchronized upstream source.

## 4. Branch 1 — Temporal schema inference

### 4.1 Objective

Implement the complete temporal requirements on:

```text
feature/schema-analyzer-temporal-inference
```

The branch adds reliable, opt-in inference for `DateOnly`, `TimeOnly` and `TimeSpan` while preserving all default behaviour.

### 4.2 Upstream coordination

Before implementation and before submission:

1. inspect `MarkPflug/Sylvan#281`;
2. inspect current `DataBinder.GetDataType(DbType)` behaviour;
3. preserve the accepted upstream general `DbType.Date` mapping;
4. do not add `DbType.Date -> DateOnly` independently;
5. keep temporal tokens independent of general `DbType` mappings;
6. do not add general `DateTime` to `DateOnly` binding unless separately requested.

The merged `MarkPflug/Sylvan.Data.Excel#198` establishes schema-directed `DateOnly`, `TimeOnly` and `TimeSpan` access through `GetFieldValue<T>` as an appropriate integration pattern.

### 4.3 Expected implementation areas

```text
source/Sylvan.Data/SchemaAnalyzer.cs
source/Sylvan.Data/SchemaAnalyzerOptions.cs
source/Sylvan.Data/SchemaAnalysisResult.cs
source/Sylvan.Data/SchemaSerializer.cs
source/Sylvan.Data/DataBinderAccessors.cs
source/Sylvan.Data.Tests/SchemaAnalyzerTests.cs
source/Sylvan.Data.Tests/SchemaTests.cs
docs/Data/*
```

Do not reproduce historical file organization unless it is justified by the synchronized upstream code.

### 4.4 Implementation sequence

Use coherent commits equivalent to:

1. add temporal contract and regression tests;
2. add temporal options and typed schema plumbing;
3. add unambiguous schema serialization;
4. implement opt-in `DateOnly` and `TimeOnly` inference;
5. implement order-independent `TimeSpan` inference;
6. prevent temporal and GUID series degradation;
7. complete documentation and release notes.

Minor changes to commit boundaries are acceptable where compilation dependencies require them.

### 4.5 Public configuration

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
- culture configuration is local to `SchemaAnalyzerOptions`;
- duration column-name hints are caller supplied, trimmed and case-insensitive;
- no environment or global switch enables inference;
- direct typed access remains based on physical CLR type.

### 4.6 Type mappings and schema tokens

Preserve the current upstream general `DbType.Date` mapping.

Support reverse mappings:

```text
DateOnly -> DbType.Date
TimeOnly -> DbType.Time
TimeSpan -> DbType.Time
```

Support the common mapping:

```text
DbType.Time -> TimeSpan
```

Define distinct, case-insensitive schema tokens:

```text
dateonly
timeonly
timespan
```

Each token must resolve directly to its CLR type. Temporal token parsing and schema round-tripping must not depend on `DataBinder.GetDataType(DbType)`, PR #281 or an `AppContext` switch.

### 4.7 Candidate-state design

Track temporal candidates independently for the complete sample. Candidate viability is monotonic: a candidate may become invalid but must not be restored, and no final winner is selected while rows are still being sampled.

Maintain state equivalent to:

```text
allValuesParseAsDateOnly
allValuesParseAsTimeOnly
allValuesParseAsTimeSpan
sawExplicitSign
sawDayComponent
sawNegativeDuration
sawDurationAtLeastThreshold
columnNameIndicatesDuration
```

Keep analysis single-pass with constant state per column. Do not buffer all sampled values or evaluate regular expressions per cell. Precompute duration-name matching once per column.

### 4.8 Temporal selection

Use `DateOnly.TryParse` for `DateOnly` and `TimeOnly.TryParse` for `TimeOnly`. Do not use `TimeSpan.TryParse` as a substitute for `TimeOnly` parsing.

Duration inference must require time- or duration-like syntax so plain integers such as `1`, `2` and `3` remain numeric.

When both `TimeOnly` and `TimeSpan` remain viable after sampling:

1. select `TimeSpan` when strong duration evidence exists;
2. otherwise select `TimeSpan` when a configured duration-name hint matches;
3. otherwise select `TimeOnly`.

Strong evidence includes explicit signs, day components, negative durations and durations whose absolute value is at least 24 hours.

### 4.9 Synchronous and asynchronous execution

`Analyze` must remain genuinely synchronous. `AnalyzeAsync` must use asynchronous reader operations. Both paths must share candidate initialization and final selection, apply identical row limits and produce equivalent results.

### 4.10 Typed binding and series

Preserve typed `DateOnly`, `TimeOnly`, `TimeSpan` and `Guid` sources without string parsing. Use matching typed accessors and support nullable and non-nullable properties.

Series handling must preserve homogeneous GUID and temporal types. If candidate series columns have no compatible common protected type, leave them uncollapsed rather than silently converting them to `string` or `DateTime`.

### 4.11 Required tests

Regression coverage must include Boolean, integer, floating-point exponent, decimal, `DateTime`, GUID, string, null and current `DbType.Date` behaviour.

Temporal coverage must include:

- ISO and culture-specific `DateOnly` values;
- nullable, empty, mixed and invalid dates;
- 24-hour and culture-specific 12-hour `TimeOnly` values;
- invalid and outside-day times;
- sub-day, signed, negative, day-component and at-least-24-hour durations;
- invalid and nullable durations;
- typed temporal sources;
- direct binder compatibility;
- distinct token round-tripping;
- both row orders of documented ambiguous examples;
- duration-name hints;
- candidate disabling;
- integer protection;
- synchronous/asynchronous parity and row-count semantics;
- protected series preservation and incompatible-series exclusion.

### 4.12 Documentation and acceptance

Update API documentation, schema-format documentation, examples and release notes. State clearly that default `SchemaAnalyzer` behaviour is unchanged.

The branch is complete when:

- default output matches upstream;
- temporal inference is opt-in and order-independent;
- GUID inference remains intact;
- `Analyze` and `AnalyzeAsync` retain their execution models;
- typed temporal columns and schema tokens preserve CLR types;
- general `DbType.Date` behaviour remains upstream-owned;
- no `AppContext` switch is required;
- series handling does not silently degrade protected types;
- all affected frameworks build and all relevant tests pass.

## 5. Branch 2 — EncoderStream ownership and finalization

### 5.1 Objective

On `feature/encoder-stream-ownership-finalization`, add an additive constructor accepting `ownsStream`, retain the existing non-owning constructor, validate null arguments and ensure disposal drains the encoder until `Complete`.

### 5.2 Required behaviour

- `ownsStream: true` disposes the underlying stream after final output is written;
- `ownsStream: false` leaves it open;
- empty `Flush` and `FlushAsync` do not issue zero-length writes;
- finalization handles repeated `RequiresOutputSpace` and `Flush` results;
- disposal is idempotent and follows normal `Stream.Dispose(bool)` behaviour;
- all final bytes are preserved.

### 5.3 Tests and documentation

Cover existing-constructor compatibility, both ownership values, null arguments, empty and buffered flushes, multiple finalization iterations and repeated disposal. Document ownership and finalization semantics.

## 6. Branch 3 — InvalidEnumValueException message

### 6.1 Objective

On `fix/invalid-enum-value-exception-message`, give the existing internal exception constructor a descriptive base `FormatException` message containing the rejected value and target enum type.

### 6.2 Constraints and tests

Preserve constructor visibility, inheritance, `Value`, `EnumType` and binding semantics. Do not add unrelated public constructors. Tests must verify the message, properties and exception contract.

## 7. Development and submission order

Recommended order:

1. `fix/invalid-enum-value-exception-message`;
2. `feature/encoder-stream-ownership-finalization`;
3. `feature/schema-analyzer-temporal-inference`.

The temporal branch remains independent of the two smaller branches. If upstream changes while work is in progress, update each branch only from current upstream and resolve genuine overlap without merging preservation branches into each other.

## 8. Pull-request content

Each pull request should identify the problem, requirement identifiers, compatibility impact, files changed, tests added, validation performed, excluded work and framework results.

Temporal compatibility statement:

```text
Default SchemaAnalyzer behaviour is unchanged unless temporal inference is explicitly enabled.

This change does not redefine the general DbType.Date mapping and does not duplicate MarkPflug/Sylvan#281. Explicit temporal schema tokens resolve directly to their CLR types.
```

## 9. Review and merge independence

The three pull requests must remain independently mergeable. No pull request may depend on types, APIs or commits introduced only by another preservation branch.

## 10. Final validation

After upstream decisions, synchronize the fork and run the repository's full CI-equivalent restore, build and test process, including `Sylvan.Common.Tests`, `Sylvan.Data.Tests` and `Sylvan.Data.Csv.Tests`.

Verify that accepted functionality is present, upstream `DbType.Date` behaviour remains intact, temporal round-tripping does not depend on general `DbType` mappings, and no implementation depends on `Upgrade_to_NET_9`.

## 11. Completion criteria

The programme is complete when each branch contains only its assigned theme, has focused tests, can be submitted independently, and the historical branch can be deleted without losing required functionality.
