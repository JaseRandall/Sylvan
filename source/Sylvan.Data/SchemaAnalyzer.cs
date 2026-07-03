using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;

namespace Sylvan.Data;

/// <summary>
/// Analyzes weakly-typed string data to determine schema information.
/// </summary>
public sealed partial class SchemaAnalyzer
{
	readonly int rowCount;
	readonly bool detectSeries;
	readonly TemporalInferenceOptions temporalInference;
	readonly CultureInfo? culture;
	readonly DateTimeStyles dateTimeStyles;
	readonly string[] durationNameContains;

	/// <summary>
	/// Creates a new <see cref="SchemaAnalyzer"/>.
	/// </summary>
	public SchemaAnalyzer(SchemaAnalyzerOptions? options = null)
	{
		options ??= SchemaAnalyzerOptions.Default;
		rowCount = options.AnalyzeRowCount;
		detectSeries = options.DetectSeries;
		temporalInference = options.TemporalInference;
		culture = options.Culture;
		dateTimeStyles = options.DateTimeStyles;
		durationNameContains = NormalizeDurationTokens(options.DurationColumnNameContains);
	}

	/// <summary>
	/// Analyzes a data set using synchronous reader operations.
	/// </summary>
	public AnalysisResult Analyze(DbDataReader dataReader)
	{
		if (dataReader == null) throw new ArgumentNullException(nameof(dataReader));

		var colInfos = CreateColumnInfo(dataReader);
		int count = 0;
		while (count < rowCount && dataReader.Read())
		{
			count++;
			for (int i = 0; i < dataReader.FieldCount; i++)
			{
				colInfos[i].Analyze(dataReader, i);
			}
		}

		return new AnalysisResult(detectSeries, colInfos);
	}

	/// <summary>
	/// Analyzes a data set using asynchronous reader operations.
	/// </summary>
	public async Task<AnalysisResult> AnalyzeAsync(DbDataReader dataReader)
	{
		if (dataReader == null) throw new ArgumentNullException(nameof(dataReader));

		var colInfos = CreateColumnInfo(dataReader);
		int count = 0;
		while (count < rowCount && await dataReader.ReadAsync().ConfigureAwait(false))
		{
			count++;
			for (int i = 0; i < dataReader.FieldCount; i++)
			{
				colInfos[i].Analyze(dataReader, i);
			}
		}

		return new AnalysisResult(detectSeries, colInfos);
	}

	ColumnInfo[] CreateColumnInfo(DbDataReader dataReader)
	{
		var columns = new ColumnInfo[dataReader.FieldCount];
		for (int i = 0; i < columns.Length; i++)
		{
			columns[i] = new ColumnInfo(
				dataReader,
				i,
				temporalInference,
				culture,
				dateTimeStyles,
				durationNameContains);
		}
		return columns;
	}

	static string[] NormalizeDurationTokens(string[]? tokens)
	{
		if (tokens == null || tokens.Length == 0) return Array.Empty<string>();

		var result = new List<string>(tokens.Length);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var token in tokens)
		{
			if (string.IsNullOrWhiteSpace(token)) continue;
			var value = token.Trim();
			if (value.Length != 0 && seen.Add(value)) result.Add(value);
		}
		return result.ToArray();
	}
}
