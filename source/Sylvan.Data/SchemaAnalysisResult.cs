using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sylvan.Data;

/// <summary>
/// The result of a data analysis process.
/// </summary>
public class AnalysisResult : IEnumerable<ColumnInfo>
{
	readonly ColumnInfo[] columns;
	readonly bool detectSeries;

	internal AnalysisResult(bool detectSeries, ColumnInfo[] columns)
	{
		this.detectSeries = detectSeries;
		this.columns = columns;
	}

	/// <summary>
	/// Enumerates the columns in the analysis result.
	/// </summary>
	public IEnumerator<ColumnInfo> GetEnumerator()
	{
		foreach (var column in columns)
		{
			yield return column;
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	/// <summary>
	/// Gets a schema representing the analysis result.
	/// </summary>
	public Schema GetSchema()
	{
		return GetSchemaBuilder().Build();
	}

	/// <summary>
	/// Gets the schema builder for the analysis result.
	/// </summary>
	public Schema.Builder GetSchemaBuilder()
	{
		var series = detectSeries ? DetectSeries(columns) : null;
		var schema = new Schema.Builder();

		for (int i = 0; i < columns.Length; i++)
		{
			var column = columns[i];
			if (series?.seriesStart == i)
			{
				int seriesEnd = series.seriesEnd;
				var types = column.GetColType();
				bool allowNull = false;
				for (int j = i; j <= seriesEnd; j++)
				{
					var seriesColumn = columns[j];
					allowNull |= seriesColumn.AllowDbNull;
					types &= seriesColumn.GetColType();
				}

				if (types == ColumnInfo.ColType.None)
				{
					// Preserve incompatible columns individually rather than silently
					// degrading a temporal or GUID series to string.
					for (int j = i; j <= seriesEnd; j++)
					{
						schema.Add(columns[j].CreateColumnSchema());
					}
					i = seriesEnd;
					continue;
				}

				var dataType = ColumnInfo.GetType(types);
				var name = string.IsNullOrEmpty(series.prefix) ? "Values" : series.prefix;
				var builder = new Schema.Column.Builder(name + "*", dataType, allowNull)
				{
					IsSeries = true,
					SeriesName = name,
					SeriesOrdinal = 0,
					SeriesType = series.type == SeriesType.Integer ? typeof(int) : typeof(DateTime),
					SeriesHeaderFormat = series.prefix + "{" + series.type + "}",
				};

				i = seriesEnd;
				schema.Add(builder);
				continue;
			}

			schema.Add(column.CreateColumnSchema());
		}
		return schema;
	}

	sealed class SeriesInfo
	{
		public SeriesInfo(int index)
		{
			seriesStart = index;
			seriesEnd = index;
		}

		public SeriesType type;
		public string? prefix;
		public int value = -1;
		public int step;
		public int seriesStart;
		public int seriesEnd;

		public int Length => seriesEnd - seriesStart + 1;
	}

	static string? GetDateSeriesPrefix(string name)
	{
		for (int i = 0; i < name.Length - 4; i++)
		{
			if (DateTime.TryParse(name.Substring(i), out _))
			{
				return name.Substring(0, i);
			}
		}
		return null;
	}

	SeriesInfo? DetectSeries(ColumnInfo[] columns)
	{
		var series = new SeriesInfo[columns.Length];
		SeriesInfo? selected = null;

		for (int i = 0; i < columns.Length; i++)
		{
			var current = series[i] = new SeriesInfo(i);
			var name = columns[i].Name;
			if (name == null)
			{
				continue;
			}

			var dateSeriesPrefix = GetDateSeriesPrefix(name);
			if (dateSeriesPrefix != null)
			{
				current.prefix = dateSeriesPrefix;
				current.type |= SeriesType.Date;
			}
			else
			{
				var match = Regex.Match(name, @"\d+$");
				if (match.Success)
				{
					current.prefix = name.Substring(0, name.Length - match.Length);
					current.value = int.Parse(match.Captures[0].Value);
					current.type |= SeriesType.Integer;
				}
			}

			if (i > 0 && current.type != SeriesType.None)
			{
				var previous = series[i - 1];
				var start = series[previous.seriesStart];
				var step = current.value - previous.value;
				if (previous.type == current.type &&
					StringComparer.InvariantCultureIgnoreCase.Equals(previous.prefix, current.prefix))
				{
					current.seriesStart = previous.seriesStart;
					selected = selected == null
						? start
						: selected != start && start.Length > selected.Length ? start : selected;
					current.step = step;
					series[current.seriesStart].seriesEnd = i;
				}
			}
		}
		return selected;
	}
}
