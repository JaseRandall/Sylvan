# Sylvan Post-Sync Preservation Requirements

## Document status

**Status:** Draft  
**Revision:** 2  
**Working repository:** `https://github.com/JaseRandall/Sylvan`  
**Upstream repository:** `https://github.com/MarkPflug/Sylvan`  
**Target baseline:** Current upstream `main` after the fork has been synchronised  
**Supersedes:** The separate temporal-inference requirements and preservation-review documents

## 1. Purpose

Define the functionality that must be retained and reimplemented after discarding the historical `Upgrade_to_NET_9` branch.

The preserved work consists of three independent themes:

1. reliable, opt-in temporal inference for `SchemaAnalyzer`;
2. configurable ownership and robust finalization for `EncoderStream`;
3. a descriptive message for `InvalidEnumValueException`.

The work must be implemented against current upstream source. Code from the discarded branch is not authoritative and must not be copied mechanically.

## 2. Delivery boundaries

The three themes must remain independently implementable, reviewable and mergeable.

No theme may require another preservation theme to be accepted first.

The intended branch boundaries are:

```text
feature/schema-analyzer-temporal-inference
feature/encoder-stream-ownership-finalization
fix/invalid-enum-value-exception-message
```

These branch names are informative rather than normative, but the separation of concerns is required.

## 3. Shared baseline requirements

### PSR-REQ-001 — Current upstream baseline

Implementation must begin from current `MarkPflug/Sylvan:main`.

Before work begins, the fork must be synchronised so that its `main` points to the same commit as upstream `main`.

### PSR-REQ-002 — Preserve upstream behaviour

Unless a requirement in this document explicitly changes behaviour, the implementation must preserve:

- current target frameworks;
- current public API;
- current GUID inference;
- existing Boolean, numeric, date/time, string, null and series behaviour;
- current synchronous and asynchronous execution paths;
- current schema parsing and serialization behaviour;
- current data-binding behaviour.

### PSR-REQ-003 — No unrelated changes

The preservation work must not include:

- target-framework changes;
- broad C# modernization;
- unrelated formatting;
- source-generated regular-expression conversion;
- unrelated exception-constructor expansion;
- file reorganization without a functional need;
- drive-by cleanup.

### PSR-REQ-004 — Test-first regression protection

Each theme must include focused automated tests for:

- the required new behaviour;
- the existing behaviour most likely to regress;
- all supported upstream target frameworks affected by the change.

### PSR-REQ-005 — Upstream coordination

Before opening a pull request, the implementation must be rebased onto current upstream `main` and checked for overlap with active upstream work.

For temporal schema work, the implementation must specifically inspect `MarkPflug/Sylvan#281`, which currently proposes changing the general mapping of `DbType.Date`. The preservation work must not duplicate, replace or silently conflict with whatever behaviour upstream has accepted at implementation time.

---

# Part A — SchemaAnalyzer temporal inference

## 4. Purpose and scope

Extend `Sylvan.Data.SchemaAnalyzer` with reliable, opt-in inference for:

- `DateOnly` on .NET 6 or later;
- `TimeOnly` on .NET 6 or later;
- `TimeSpan` on every target framework where the type is available.

The enhancement must also provide:

- deterministic culture-aware parsing when explicitly configured;
- order-independent distinction between `TimeOnly` and `TimeSpan`;
- explicit schema serialization for modern temporal CLR types;
- direct data-binding compatibility;
- no change to default inference behaviour.

## 5. Temporal non-goals

The temporal work must not:

- change the general meaning of `DbType.Date`;
- introduce process-global `AppContext` switches for analyzer behaviour;
- depend on the `AppContext` switch proposed by PR #281;
- add general `DateTime` to `DateOnly` binder conversion unless separately approved;
- alter direct accessor selection for physical `DateOnly` or `TimeOnly` columns;
- infer `DateTimeOffset`, time zones or offsets;
- interpret business semantics beyond documented parsing and ambiguity rules.

## 6. Public configuration contract

### SATI-REQ-001 — Explicit opt-in selection

`SchemaAnalyzerOptions` must expose an additive mechanism for selecting temporal types.

The preferred contract is:

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

The default must be `TemporalInferenceOptions.None`.

An equivalent API is acceptable only when it:

- is explicit;
- is additive;
- has no contradictory combinations;
- leaves all temporal inference disabled by default.

### SATI-REQ-002 — Explicit culture

`SchemaAnalyzerOptions` must permit an explicit `CultureInfo`.

When no culture is supplied, current upstream parsing behaviour must remain unchanged.

When a culture is supplied:

- date parsing must use it;
- time-of-day parsing must use it;
- duration parsing must use it where supported;
- numeric parsing must retain the grammar normally accepted for its numeric category.

A single `NumberStyles` value must not be applied indiscriminately to integers, floating-point numbers and decimals.

### SATI-REQ-003 — Optional duration-name hints

The options may expose caller-supplied, case-insensitive column-name tokens that indicate duration semantics.

If supported:

- the default collection must be empty;
- null, empty and whitespace tokens must be ignored;
- matching must be ordinal and case-insensitive;
- matching must be computed once per column;
- hints may affect selection only when both `TimeOnly` and `TimeSpan` remain viable.

## 7. Default compatibility

### SATI-REQ-004 — No behavioural change by default

When temporal inference is disabled:

- analyzer results must match current upstream;
- date-like strings must continue to follow existing `DateTime` inference;
- time-like strings must not begin producing `TimeOnly`;
- duration-like strings must not begin producing `TimeSpan`;
- no environment variable, `AppContext` switch or process-global state may enable inference.

### SATI-REQ-005 — Preserve upstream `DbType.Date` semantics

The implementation must preserve the general `DbType.Date` to CLR mapping present in upstream at the time of implementation.

It must not independently introduce:

```text
DbType.Date -> DateOnly
```

Temporal inference must represent `DateOnly` explicitly through the schema column's CLR `DataType`, not by redefining the global meaning of `DbType.Date`.

### SATI-REQ-006 — PR #281 isolation

The implementation must remain correct whether `MarkPflug/Sylvan#281` is:

- open;
- merged;
- closed;
- superseded.

Temporal analyzer output and schema round-tripping must not depend on PR #281's proposed `AppContext` switch or its chosen general `DbType.Date` mapping.

## 8. Temporal inference

### SATI-REQ-007 — DateOnly inference

When `DateOnly` inference is enabled on .NET 6 or later:

- every non-null sampled value must parse successfully as `DateOnly`;
- configured culture must be respected;
- mixed date-only and date-time values must not produce `DateOnly`;
- invalid values must eliminate the candidate;
- null-like values must affect nullability without providing positive evidence;
- the result must use `DataType = typeof(DateOnly)`;
- the result must use `CommonDataType = DbType.Date`.

On earlier target frameworks, the option must be unavailable or ignored without changing existing behaviour.

### SATI-REQ-008 — TimeOnly inference

When `TimeOnly` inference is enabled on .NET 6 or later:

- viability must use `TimeOnly` parsing semantics;
- `TimeSpan.TryParse` must not be used as a substitute for `TimeOnly` parsing;
- every non-null sampled value must be a valid time of day;
- valid culture-specific 12-hour representations must be supported;
- values outside one day must eliminate the candidate;
- the result must use `DataType = typeof(TimeOnly)`;
- the result must use `CommonDataType = DbType.Time`.

### SATI-REQ-009 — TimeSpan inference

When `TimeSpan` inference is enabled:

- every non-null sampled value must parse successfully as `TimeSpan`;
- negative durations must be supported;
- explicit positive durations must be supported where accepted;
- day components must be supported;
- durations of at least 24 hours must be supported;
- invalid values must eliminate the candidate;
- the result must use `DataType = typeof(TimeSpan)`;
- the result must use `CommonDataType = DbType.Time`.

### SATI-REQ-010 — Plain integer protection

Enabling `TimeSpan` inference must not cause plain integer columns such as:

```text
1
2
3
```

to be inferred as day-based durations merely because a framework parser can interpret them that way.

Duration inference must require time- or duration-like syntax or other explicit evidence.

## 9. Candidate tracking and final selection

### SATI-REQ-011 — Independent candidates

The analyzer must track temporal candidates independently throughout the complete sample.

At minimum, state equivalent to the following must be retained per column:

- all observed values parse as `DateOnly`;
- all observed values parse as `TimeOnly`;
- all observed values parse as `TimeSpan`;
- an explicit sign was observed;
- a day component was observed;
- a negative duration was observed;
- a duration of at least 24 hours was observed;
- the column name matches a configured duration hint.

Candidate state must be monotonic: viable candidates may become invalid, but invalid candidates must not be restored.

### SATI-REQ-012 — No early winner selection

The analyzer must not choose a final temporal type while rows are still being sampled.

An ambiguous row must not permanently discard another still-viable candidate.

### SATI-REQ-013 — Order independence

Given the same sampled values, inference must produce the same result regardless of row order.

Both permutations of the following examples must produce the same type:

```text
01:30:00
1.02:03:04
```

```text
01:00:00
25:00:00
```

### SATI-REQ-014 — TimeOnly versus TimeSpan resolution

When both `TimeOnly` and `TimeSpan` remain viable after sampling:

1. select `TimeSpan` when strong duration evidence exists;
2. otherwise select `TimeSpan` when a configured duration-name hint matches;
3. otherwise select `TimeOnly`.

Strong duration evidence must include at least:

- an explicit positive or negative sign;
- a day component;
- a negative duration;
- an absolute duration of at least 24 hours.

### SATI-REQ-015 — Temporal precedence

Final selection must use deterministic precedence:

1. `DateOnly` for exclusively date-only values;
2. `TimeSpan` when duration evidence resolves a time ambiguity;
3. `TimeOnly` for time-of-day values without duration evidence;
4. existing `DateTime` inference;
5. existing string fallback.

Existing Boolean, integer, floating-point, decimal and GUID precedence must remain unchanged.

## 10. Typed input and binder compatibility

### SATI-REQ-016 — Preserve typed temporal sources

If the physical reader reports any of the following CLR types, the analyzer must preserve them without string parsing:

- `DateOnly`;
- `TimeOnly`;
- `TimeSpan`;
- `Guid`.

### SATI-REQ-017 — Direct typed accessors

Accessor selection must continue to use direct typed access:

```text
DateOnly -> GetFieldValue<DateOnly>
TimeOnly -> GetFieldValue<TimeOnly>
TimeSpan -> GetFieldValue<TimeSpan>
```

The implementation must not:

- substitute `GetDateTime` for a physical `DateOnly`;
- substitute `TimeSpan` access for a physical `TimeOnly`;
- depend on a process-global mapping switch.

### SATI-REQ-018 — Matching property binding

Binding to matching nullable and non-nullable properties must succeed for:

- `DateOnly`;
- `TimeOnly`;
- `TimeSpan`.

### SATI-REQ-019 — No general DateTime-to-DateOnly conversion

This work must not introduce general binding from a physical `DateTime` source to a `DateOnly` property unless the upstream maintainer explicitly requests that design.

If requested later, it must be handled as separate conversion-policy work defining:

- whether non-midnight values fail;
- whether truncation is permitted;
- nullable behaviour;
- scope of the conversion.

## 11. Schema metadata and serialization

### SATI-REQ-020 — Metadata consistency

Inferred temporal columns must use:

| CLR type | Common data type |
|---|---|
| `DateOnly` | `DbType.Date` |
| `TimeOnly` | `DbType.Time` |
| `TimeSpan` | `DbType.Time` |

`DataType`, `DataTypeName`, `CommonDataType`, nullability and ordinal metadata must remain internally consistent.

### SATI-REQ-021 — Distinct schema tokens

The schema language must define distinct, case-insensitive tokens:

```text
dateonly
timeonly
timespan
```

`TimeOnly` and `TimeSpan` must not share an ambiguous token.

### SATI-REQ-022 — Direct CLR token resolution

The parser must resolve temporal tokens directly:

```text
dateonly -> typeof(DateOnly)
timeonly -> typeof(TimeOnly)
timespan -> typeof(TimeSpan)
```

It must not resolve them indirectly through the general `DbType.Date` or `DbType.Time` mapping.

### SATI-REQ-023 — Round-trip stability

The following operation must preserve the original temporal CLR type:

```csharp
Schema parsed = Schema.Parse(schema.ToString());
```

Round-tripping must remain stable regardless of:

- target framework;
- the status of PR #281;
- the general `DbType.Date` mapping;
- any `AppContext` setting.

### SATI-REQ-024 — Reverse CLR-to-DbType mapping

The data layer must support:

```text
DateOnly -> DbType.Date
TimeOnly -> DbType.Time
TimeSpan -> DbType.Time
```

The common ADO.NET mapping must continue to support:

```text
DbType.Time -> TimeSpan
```

No requirement in this document changes the upstream general `DbType.Date` mapping.

## 12. Synchronous and asynchronous behaviour

### SATI-REQ-025 — Genuine synchronous analysis

`SchemaAnalyzer.Analyze` must use synchronous reader operations and must not call `AnalyzeAsync(...).GetAwaiter().GetResult()`.

### SATI-REQ-026 — Genuine asynchronous analysis

`SchemaAnalyzer.AnalyzeAsync` must use asynchronous reader operations.

### SATI-REQ-027 — Equivalent results

For equivalent readers, options and sampled rows, synchronous and asynchronous analysis must produce equivalent schema and analysis results.

Shared initialization and final-selection helpers should be used where practical.

### SATI-REQ-028 — Row-count semantics

`AnalyzeRowCount` behaviour must remain consistent with upstream.

Both paths must:

- analyze no more than the configured count;
- use identical limits;
- avoid off-by-one differences;
- preserve existing zero-row and positive-row behaviour.

## 13. Series compatibility

### SATI-REQ-029 — No silent degradation

When `DetectSeries` is enabled, homogeneous columns of these types must not silently degrade:

- `Guid`;
- `DateOnly`;
- `TimeOnly`;
- `TimeSpan`.

The implementation must either:

- represent and preserve the types in the internal series lattice; or
- deliberately exclude them from series collapsing.

The chosen behaviour must be documented and tested.

## 14. Nulls, failures and performance

### SATI-REQ-030 — Null and empty handling

Existing null and empty-string conventions must remain unchanged.

For temporal inference:

- database nulls must mark the column nullable;
- null-like values must not provide positive temporal evidence;
- null-like values must not independently eliminate candidates;
- a column containing only null-like values must retain current upstream fallback behaviour.

### SATI-REQ-031 — Failure and fallback

Normal temporal parse failures must not throw.

A failed candidate must fall through the existing inference pipeline.

Unexpected reader exceptions must preserve current upstream handling.

No new broad exception swallowing may be added.

### SATI-REQ-032 — Performance

The implementation must:

- remain single-pass over sampled rows;
- use constant state per column;
- avoid buffering all sampled values;
- avoid regular-expression evaluation per cell;
- precompute duration-name hints;
- avoid process-global mutable state;
- avoid synchronous waits over asynchronous operations;
- preserve current asymptotic behaviour.

A benchmark is recommended only if tests or review indicate material regression risk.

## 15. Temporal automated tests

### SATI-TEST-001 — Default regression

Tests must prove that default options preserve:

- Boolean inference;
- integer inference;
- floating-point inference, including exponent syntax;
- decimal inference;
- `DateTime` inference;
- GUID inference;
- string fallback;
- null handling;
- current upstream `DbType.Date` behaviour.

Existing upstream tests must not be weakened by introducing framework-dependent `DateOnly`/`DateTime` aliases.

### SATI-TEST-002 — DateOnly

Tests must cover:

- ISO dates;
- culture-specific dates;
- nullable values;
- empty values;
- mixed date-only and date-time values;
- invalid values;
- typed `DateOnly` sources;
- schema round-trip;
- sync/async parity.

### SATI-TEST-003 — TimeOnly

Tests must cover:

- 24-hour values;
- culture-specific 12-hour values;
- values near midnight;
- nullable values;
- invalid times;
- values outside one day;
- typed `TimeOnly` sources;
- schema round-trip;
- sync/async parity.

### SATI-TEST-004 — TimeSpan

Tests must cover:

- sub-day durations;
- negative durations;
- explicit positive durations;
- day components;
- durations of at least 24 hours;
- nullable values;
- invalid durations;
- typed `TimeSpan` sources;
- schema round-trip;
- sync/async parity.

### SATI-TEST-005 — Ambiguity and order

Tests must verify:

- both row orders of the documented ambiguous examples;
- ambiguous sub-day values default to `TimeOnly`;
- configured duration hints select `TimeSpan`;
- enabling only one candidate selects that candidate;
- plain integer columns remain numeric.

### SATI-TEST-006 — Culture

Tests must verify:

- configured date culture;
- configured 12-hour time culture;
- integer grammar remains appropriate;
- floating-point exponent support remains available;
- decimal separators behave as configured;
- tests do not accidentally depend on machine culture.

### SATI-TEST-007 — Binder compatibility

Tests must verify direct nullable and non-nullable binding for:

- `DateOnly`;
- `TimeOnly`;
- `TimeSpan`.

The reader must report the corresponding physical CLR type.

### SATI-TEST-008 — Schema token independence

Tests must prove that:

- each temporal token resolves directly to the intended CLR type;
- `TimeOnly` and `TimeSpan` remain distinct;
- round-tripping does not depend on general `DbType.Date` behaviour;
- no `AppContext` switch is required.

### SATI-TEST-009 — Series behaviour

Tests must verify the selected series strategy and prove no silent temporal or GUID degradation.

## 16. Temporal documentation

Documentation must cover:

- the opt-in API;
- explicit culture;
- `DateOnly`, `TimeOnly` and `TimeSpan`;
- ambiguity rules;
- duration-name hints, if implemented;
- schema tokens;
- series behaviour;
- default compatibility;
- the deliberate decision not to redefine general `DbType.Date`.

Release notes must state that default `SchemaAnalyzer` behaviour is unchanged.

---

# Part B — EncoderStream ownership and finalization

## 17. Purpose

Add explicit control over ownership of the underlying stream and ensure encoder finalization drains all remaining output before disposal.

### ES-REQ-001 — Additive ownership constructor

`EncoderStream` must support an additive constructor or equivalent API:

```csharp
public EncoderStream(
    Stream stream,
    Encoder encoder,
    bool ownsStream)
```

The existing constructor must remain available and must retain its existing non-owning behaviour.

### ES-REQ-002 — Constructor validation

Null `Stream` and `Encoder` arguments must throw `ArgumentNullException`.

### ES-REQ-003 — Ownership semantics

When disposed:

- `ownsStream: true` must dispose the underlying stream;
- `ownsStream: false` must leave the underlying stream open;
- the existing constructor must behave as `ownsStream: false`.

### ES-REQ-004 — Empty flush behaviour

`Flush` and `FlushAsync` must not issue a zero-length write when no output is buffered.

### ES-REQ-005 — Complete finalization

Disposal must invoke the encoder with empty input until it reports completion.

The loop must correctly handle:

- `EncoderResult.RequiresOutputSpace` by flushing and retrying;
- `EncoderResult.Flush` by flushing and retrying;
- `EncoderResult.Complete` by flushing remaining output and finishing;
- multiple successive empty-input calls.

Finalization must not assume one or two calls are sufficient.

### ES-REQ-006 — Disposal ordering

All final output must be written before an owned underlying stream is disposed.

### ES-REQ-007 — Idempotent lifecycle

Disposal must:

- finalize output at most once;
- be safe when invoked repeatedly;
- follow normal `Stream.Dispose(bool)` behaviour;
- call the base implementation;
- preserve standard `Close()` routing through disposal.

### ES-TEST-001 — Ownership tests

Tests must cover:

- existing constructor;
- `ownsStream: false`;
- `ownsStream: true`;
- null arguments;
- repeated disposal.

### ES-TEST-002 — Flush tests

Tests must cover:

- empty synchronous flush;
- empty asynchronous flush;
- buffered flush;
- no unnecessary zero-length underlying writes.

### ES-TEST-003 — Finalization tests

Tests must use controlled encoders that require:

- `RequiresOutputSpace`;
- one `Flush`;
- multiple `Flush` results;
- multiple empty-input calls;
- emission of final bytes during disposal.

Tests must prove all output is preserved.

### ES-DOC-001 — Documentation

XML documentation must describe:

- ownership semantics;
- existing-constructor compatibility;
- disposal and finalization behaviour.

---

# Part C — InvalidEnumValueException diagnostics

## 18. Purpose

Improve invalid enum-binding diagnostics without changing binding semantics or expanding unrelated public API.

### IEV-REQ-001 — Descriptive message

The constructor receiving the enum type and rejected textual value must initialize the base `FormatException` with a descriptive message.

The message must identify:

- the rejected value;
- the target enum type.

A suitable form is:

```text
The value "InvalidValue" is not a valid member of the enum type Namespace.ExampleEnum.
```

Exact punctuation is not normative.

### IEV-REQ-002 — Preserve properties

The existing `Value` and `EnumType` properties must retain the rejected value and target enum type.

### IEV-REQ-003 — Preserve exception contract

The exception must remain a `FormatException`.

No unrelated public constructors are required.

### IEV-TEST-001 — Diagnostic tests

Tests must verify:

- the exception type;
- `FormatException` inheritance;
- message contains the rejected value;
- message identifies the enum type;
- `Value` is correct;
- `EnumType` is correct;
- binding behaviour is otherwise unchanged.

---

# Part D — Final acceptance

## 19. Theme-level acceptance

Each theme is acceptable independently when:

- its required behaviour is implemented;
- its focused tests pass;
- current upstream behaviour outside its scope remains unchanged;
- all affected target frameworks build;
- the pull request contains no unrelated modernization.

## 20. Temporal acceptance

Temporal work is complete when:

1. default analyzer output matches upstream;
2. `Analyze` remains synchronous;
3. `AnalyzeAsync` remains asynchronous;
4. GUID inference remains intact;
5. temporal inference is opt-in;
6. inference is order-independent;
7. plain integers are not inferred as durations;
8. typed temporal columns are preserved;
9. schema tokens resolve directly to CLR types;
10. round-tripping preserves all temporal CLR types;
11. general `DbType.Date` behaviour remains upstream-owned;
12. no `AppContext` switch is required;
13. direct binder access works;
14. series handling does not silently degrade types;
15. all temporal tests and documentation are complete.

## 21. EncoderStream acceptance

`EncoderStream` work is complete when:

1. existing construction remains non-owning;
2. ownership is explicitly configurable;
3. null arguments fail immediately;
4. empty flushes avoid unnecessary writes;
5. finalization continues until `Complete`;
6. all final output is preserved;
7. disposal is idempotent;
8. owned and non-owned streams behave correctly;
9. all lifecycle tests pass.

## 22. InvalidEnumValueException acceptance

The diagnostic work is complete when:

1. invalid enum values produce an actionable message;
2. the rejected value and enum type remain available through properties;
3. the exception remains a `FormatException`;
4. binding semantics remain unchanged;
5. all focused tests pass.

## 23. Excluded preservation work

The following items do not require preservation from `Upgrade_to_NET_9`:

- target-framework consolidation;
- broad C# modernization;
- source-generated regular expressions;
- file reorganization;
- broad public exception-constructor expansion;
- unrelated readonly-field changes;
- unrelated CSV reader or writer rewrites;
- embedded `IsoDate` retention where current upstream package composition already provides the required functionality.
