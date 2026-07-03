using System;
using System.Globalization;

namespace Sylvan.Data;

/// <summary>
/// Specifies the modern temporal CLR types that <see cref="SchemaAnalyzer"/> may infer.
/// </summary>
[Flags]
public enum TemporalInferenceOptions
{
	/// <summary>Disables modern temporal inference.</summary>
	None = 0,
	/// <summary>Enables DateOnly inference on supported target frameworks.</summary>
	DateOnly = 1,
	/// <summary>Enables TimeOnly inference on supported target frameworks.</summary>
	TimeOnly = 2,
	/// <summary>Enables TimeSpan inference.</summary>
	TimeSpan = 4,
}

/// <summary>
/// Options for a data schema analysis operation.
/// </summary>
public sealed class SchemaAnalyzerOptions
{
	internal static SchemaAnalyzerOptions Default => new();

	/// <summary>Creates options with default values.</summary>
	public SchemaAnalyzerOptions()
	{
		AnalyzeRowCount = 10000;
		DetectSeries = false;
		TemporalInference = TemporalInferenceOptions.None;
		DateTimeStyles = DateTimeStyles.None;
		DurationColumnNameContains = Array.Empty<string>();
	}

	/// <summary>Gets or sets the number of rows to analyze.</summary>
	public int AnalyzeRowCount { get; set; }

	/// <summary>Gets or sets whether series detection is enabled.</summary>
	public bool DetectSeries { get; set; }

	/// <summary>Gets or sets the modern temporal CLR types that may be inferred.</summary>
	public TemporalInferenceOptions TemporalInference { get; set; }

	/// <summary>Gets or sets the optional culture used for parsing.</summary>
	public CultureInfo? Culture { get; set; }

	/// <summary>Gets or sets date and time parsing styles used with an explicit culture.</summary>
	public DateTimeStyles DateTimeStyles { get; set; }

	/// <summary>Gets or sets case-insensitive column-name tokens indicating duration semantics.</summary>
	public string[] DurationColumnNameContains { get; set; }
}
