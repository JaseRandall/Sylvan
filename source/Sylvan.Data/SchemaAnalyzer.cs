using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Sylvan.Data;

/// <summary>
/// Analyzes weakly-typed data to determine schema information.
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
	/// <param name="options">The optional schema-analysis configuration.</param>
	public SchemaAnalyzer(SchemaAnalyzerOptions? options = null)
	{
		options ??= SchemaAnalyzerOptions.Default;
		this.rowCount = options.AnalyzeRowCount;
		this.detectSeries = options.DetectSeries;
		this.temporalInference = options.TemporalInference;
		this.culture = options.Culture;
		this.dateTimeStyles = options.DateTimeStyles;
		this.durationNameContains = NormalizeDurationTokens(options.DurationColumnNameContains);
	}

	/// <summary>
	/// Analyzes a data set using synchronous reader operations.
	/// </summary>
	/// <param name="dataReader">The data reader to analyze.</param>
	/// <returns>The schema-analysis result.</returns>
	public AnalysisResult Analyze(DbDataReader dataReader)
	{
		if (dataReader == null)
		{
			throw new ArgumentNullException(nameof(dataReader));
		}
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
	/// <param name="dataReader">The data reader to analyze.</param>
	/// <returns>A task containing the schema-analysis result.</returns>
	public async Task<AnalysisResult> AnalyzeAsync(DbDataReader dataReader)
	{
		if (dataReader == null)
		{
			throw new ArgumentNullException(nameof(dataReader));
		}
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
		if (tokens == null || tokens.Length == 0)
		{
			return Array.Empty<string>();
		}
		var result = new List<string>(tokens.Length);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var token in tokens)
		{
			if (string.IsNullOrWhiteSpace(token))
			{
				continue;
			}
			var value = token.Trim();
			if (value.Length != 0 && seen.Add(value))
			{
				result.Add(value);
			}
		}
		return result.ToArray();
	}
}

/// <summary>
/// Provides schema-analysis information for a data column.
/// </summary>
public sealed partial class ColumnInfo
{
	static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
	readonly Type type;
	readonly int ordinal;
	readonly string? name;
	readonly CultureInfo? culture;
	readonly DateTimeStyles dateTimeStyles;
	readonly bool durationNameHint;
	readonly bool typedGuid;
	readonly bool typedTimeSpan;
#if NET6_0_OR_GREATER
	readonly bool typedDateOnly;
	readonly bool typedTimeOnly;
#endif
	readonly Dictionary<string, int> valueCount = new(StringComparer.OrdinalIgnoreCase);
	bool isAscii = true;
	bool isBoolean;
	bool isInt;
	bool isFloat;
	bool isDecimal;
	bool isDate;
	bool isDateTime;
	bool isGuid;
	bool isTimeSpan;
#if NET6_0_OR_GREATER
	bool isDateOnly;
	bool isTimeOnly;
#endif
	bool sawExplicitSign;
	bool sawDayComponent;
	bool sawNegativeDuration;
	bool sawDurationAtLeastDay;
	bool dateHasFractionalSeconds;
	bool isNullable;
	int count;
	int nullCount;
	int emptyStringCount;
	long intMin = long.MaxValue;
	long intMax = long.MinValue;
	int decimalScaleMax = int.MinValue;
	int stringLenMax;

	internal ColumnInfo(
		DbDataReader reader,
		int ordinal,
		TemporalInferenceOptions temporalInference,
		CultureInfo? culture,
		DateTimeStyles dateTimeStyles,
		string[] durationNameContains)
	{
		this.ordinal = ordinal;
		name = reader.GetName(ordinal);
		type = reader.GetFieldType(ordinal);
		this.culture = culture;
		this.dateTimeStyles = dateTimeStyles;
		durationNameHint = IsDurationLikeColumnName(name, durationNameContains);
		switch (Type.GetTypeCode(type))
		{
			case TypeCode.Boolean:
				isBoolean = true;
				break;
			case TypeCode.Int16:
			case TypeCode.Int32:
			case TypeCode.Int64:
				isInt = true;
				break;
			case TypeCode.Single:
			case TypeCode.Double:
				isFloat = true;
				break;
			case TypeCode.Decimal:
				isDecimal = true;
				break;
			case TypeCode.DateTime:
				isDate = isDateTime = true;
				break;
			case TypeCode.String:
				isBoolean = isInt = isFloat = isDecimal = isDate = isDateTime = isGuid = true;
				isTimeSpan = (temporalInference & TemporalInferenceOptions.TimeSpan) != 0;
#if NET6_0_OR_GREATER
				isDateOnly = (temporalInference & TemporalInferenceOptions.DateOnly) != 0;
				isTimeOnly = (temporalInference & TemporalInferenceOptions.TimeOnly) != 0;
#endif
				break;
			default:
				if (type == typeof(Guid))
				{
					typedGuid = isGuid = true;
				}
				else if (type == typeof(TimeSpan))
				{
					typedTimeSpan = isTimeSpan = true;
				}
#if NET6_0_OR_GREATER
				else if (type == typeof(DateOnly))
				{
					typedDateOnly = isDateOnly = true;
				}
				else if (type == typeof(TimeOnly))
				{
					typedTimeOnly = isTimeOnly = true;
				}
#endif
				break;
		}
	}

	/// <summary>
	/// Indicates whether the column allows database null values.
	/// </summary>
	public bool AllowDbNull => isNullable;

	/// <summary>
	/// Gets the column ordinal.
	/// </summary>
	public int Ordinal => ordinal;

	/// <summary>
	/// Gets the column name.
	/// </summary>
	public string? Name => name;
}

public sealed partial class ColumnInfo
{
	internal void Analyze(DbDataReader reader, int ordinal)
	{
		count++;
		if (reader.IsDBNull(ordinal))
		{
			nullCount++;
			isNullable = true;
			return;
		}
		long? intValue = null;
		decimal? decimalValue = null;
		DateTime? dateValue = null;
		string? stringValue = null;
		try
		{
			switch (Type.GetTypeCode(type))
			{
				case TypeCode.Boolean: reader.GetBoolean(ordinal); break;
				case TypeCode.Int16: intValue = reader.GetInt16(ordinal); break;
				case TypeCode.Int32: intValue = reader.GetInt32(ordinal); break;
				case TypeCode.Int64: intValue = reader.GetInt64(ordinal); break;
				case TypeCode.Single: reader.GetFloat(ordinal); break;
				case TypeCode.Double: reader.GetDouble(ordinal); break;
				case TypeCode.Decimal: decimalValue = reader.GetDecimal(ordinal); break;
				case TypeCode.DateTime: dateValue = reader.GetDateTime(ordinal); break;
				case TypeCode.String:
					stringValue = reader.GetString(ordinal);
					AnalyzeStringValue(stringValue, ref intValue, ref decimalValue, ref dateValue);
					break;
				default:
					if (typedGuid) reader.GetGuid(ordinal);
					else if (typedTimeSpan) reader.GetFieldValue<TimeSpan>(ordinal);
#if NET6_0_OR_GREATER
					else if (typedDateOnly) reader.GetFieldValue<DateOnly>(ordinal);
					else if (typedTimeOnly) reader.GetFieldValue<TimeOnly>(ordinal);
#endif
					break;
			}
		}
		catch (Exception)
		{
		}
		if (isDecimal && decimalValue.HasValue)
		{
			decimalScaleMax = Math.Max(decimalScaleMax, GetScale(decimalValue.Value));
		}
		if (isInt && intValue.HasValue)
		{
			intMin = Math.Min(intMin, intValue.Value);
			intMax = Math.Max(intMax, intValue.Value);
		}
		if (isDateTime && dateValue.HasValue)
		{
			var value = dateValue.Value;
			if (isDate && value.TimeOfDay != TimeSpan.Zero) isDate = false;
			if (!dateHasFractionalSeconds && value.Ticks % TimeSpan.TicksPerSecond != 0)
			{
				dateHasFractionalSeconds = true;
			}
		}
		if (stringValue != null) AnalyzeStringStatistics(stringValue);
	}

	void AnalyzeStringValue(string value, ref long? intValue, ref decimal? decimalValue, ref DateTime? dateValue)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			isNullable = true;
			return;
		}
		if (isBoolean && !bool.TryParse(value, out _)) isBoolean = false;
		if (isFloat && !TryParseDouble(value, out _)) isFloat = false;
		if (isDecimal)
		{
			if (TryParseDecimal(value, out var parsed)) decimalValue = parsed;
			else isDecimal = false;
		}
		if (isInt)
		{
			if (TryParseInt64(value, out var parsed)) intValue = parsed;
			else isInt = false;
		}
		if (isDateTime || isDate)
		{
			if (TryParseDateTime(value, out var parsed)) dateValue = parsed;
			else isDate = isDateTime = false;
		}
#if NET6_0_OR_GREATER
		if (isDateOnly && !TryParseDateOnly(value, out _)) isDateOnly = false;
		if (isTimeOnly && !TryParseTimeOnly(value, out _)) isTimeOnly = false;
#endif
		if (isTimeSpan)
		{
			if (TryParseTimeSpan(value, out var parsed, out var evidence))
			{
				sawExplicitSign |= evidence.ExplicitSign;
				sawDayComponent |= evidence.DayComponent;
				sawNegativeDuration |= parsed < TimeSpan.Zero;
				sawDurationAtLeastDay |= parsed <= -OneDay || parsed >= OneDay;
			}
			else isTimeSpan = false;
		}
		if (isGuid && !Guid.TryParse(value, out _)) isGuid = false;
	}

	void AnalyzeStringStatistics(string value)
	{
		stringLenMax = Math.Max(stringLenMax, value.Length);
		if (string.IsNullOrWhiteSpace(value))
		{
			nullCount++;
			emptyStringCount++;
			return;
		}
		if (isAscii)
		{
			foreach (var c in value)
			{
				if (c >= 128) { isAscii = false; break; }
			}
		}
		if (valueCount.Count < 100)
		{
			valueCount.TryGetValue(value, out var current);
			valueCount[value] = current + 1;
		}
	}
}

public sealed partial class ColumnInfo
{
	bool TryParseDateTime(string value, out DateTime result) =>
		culture == null
			? DateTime.TryParse(value, out result)
			: DateTime.TryParse(value, culture, dateTimeStyles, out result);
#if NET6_0_OR_GREATER
	bool TryParseDateOnly(string value, out DateOnly result)
	{
		bool parsed = culture == null
			? DateOnly.TryParse(value, out result)
			: DateOnly.TryParse(value, culture, dateTimeStyles, out result);
		return parsed && !ContainsExplicitTimeComponent(value);
	}

	bool ContainsExplicitTimeComponent(string value)
	{
		var format = (culture ?? CultureInfo.CurrentCulture).DateTimeFormat;
		if (value.IndexOf(':') >= 0) return true;
		for (int i = 1; i < value.Length - 1; i++)
		{
			if ((value[i] == 'T' || value[i] == 't') &&
				char.IsDigit(value[i - 1]) && char.IsDigit(value[i + 1])) return true;
		}
		return ContainsDesignator(value, format.AMDesignator) || ContainsDesignator(value, format.PMDesignator);
	}

	static bool ContainsDesignator(string value, string designator)
	{
		if (string.IsNullOrEmpty(designator)) return false;
		int index = value.IndexOf(designator, StringComparison.OrdinalIgnoreCase);
		while (index >= 0)
		{
			int end = index + designator.Length;
			if ((index == 0 || !char.IsLetter(value[index - 1])) &&
				(end == value.Length || !char.IsLetter(value[end]))) return true;
			index = value.IndexOf(designator, end, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	bool TryParseTimeOnly(string value, out TimeOnly result) =>
		culture == null
			? TimeOnly.TryParse(value, out result)
			: TimeOnly.TryParse(value, culture, dateTimeStyles, out result);
#endif

	bool TryParseInt64(string value, out long result) =>
		culture == null
			? long.TryParse(value, out result)
			: long.TryParse(value, NumberStyles.Integer, culture, out result);

	bool TryParseDouble(string value, out double result) =>
		culture == null
			? double.TryParse(value, out result)
			: double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture, out result);

	bool TryParseDecimal(string value, out decimal result) =>
		culture == null
			? decimal.TryParse(value, out result)
			: decimal.TryParse(value, NumberStyles.Number, culture, out result);
}

public sealed partial class ColumnInfo
{
	bool TryParseTimeSpan(string value, out TimeSpan result, out DurationEvidence evidence)
	{
		evidence = default;
		result = default;
		var text = value.Trim();
		if (text.Length == 0 || text.IndexOf(':') < 0) return false;
		evidence.ExplicitSign = text[0] == '+' || text[0] == '-';
		int colon = text.IndexOf(':');
		int dot = text.IndexOf('.');
		evidence.DayComponent = dot >= 0 && dot < colon;
		bool parsed = culture == null
			? TimeSpan.TryParse(text, out result)
			: TimeSpan.TryParse(text, culture, out result);
		return parsed || TryParseExtendedHourDuration(text, out result);
	}

	bool TryParseExtendedHourDuration(string value, out TimeSpan result)
	{
		result = default;
		int sign = 1;
		int offset = 0;
		if (value[0] == '+' || value[0] == '-')
		{
			sign = value[0] == '-' ? -1 : 1;
			offset = 1;
		}
		var parts = value.Substring(offset).Split(':');
		if (parts.Length < 2 || parts.Length > 3) return false;
		var provider = culture ?? CultureInfo.CurrentCulture;
		if (!long.TryParse(parts[0], NumberStyles.None, provider, out var hours) ||
			!int.TryParse(parts[1], NumberStyles.None, provider, out var minutes) ||
			minutes < 0 || minutes >= 60) return false;
		decimal seconds = 0;
		if (parts.Length == 3 &&
			(!decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, provider, out seconds) ||
			 seconds < 0 || seconds >= 60)) return false;
		try
		{
			decimal totalTicks = hours * TimeSpan.TicksPerHour +
				minutes * TimeSpan.TicksPerMinute + seconds * TimeSpan.TicksPerSecond;
			if (totalTicks > long.MaxValue) return false;
			var ticks = checked((long)totalTicks);
			result = new TimeSpan(checked(ticks * sign));
			return true;
		}
		catch (OverflowException) { return false; }
	}

	static bool IsDurationLikeColumnName(string? columnName, string[] tokens)
	{
		if (columnName == null || string.IsNullOrWhiteSpace(columnName)) return false;
		foreach (var token in tokens)
		{
			if (columnName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}
}

public sealed partial class ColumnInfo
{
	bool HasStrongDurationEvidence =>
		sawExplicitSign || sawDayComponent || sawNegativeDuration || sawDurationAtLeastDay;

	Type? GetTemporalType()
	{
#if NET6_0_OR_GREATER
		if (isTimeOnly && isTimeSpan)
			return HasStrongDurationEvidence || durationNameHint ? typeof(TimeSpan) : typeof(TimeOnly);
		if (isTimeOnly) return typeof(TimeOnly);
#endif
		return isTimeSpan ? typeof(TimeSpan) : null;
	}

	static int GetScale(decimal value) => new DecimalScale(value).Scale;

	[StructLayout(LayoutKind.Explicit)]
	struct DecimalScale
	{
		public DecimalScale(decimal value) { this = default; number = value; }
		[FieldOffset(0)] readonly decimal number;
		[FieldOffset(0)] readonly int flags;
		public int Scale => (flags >> 16) & 0xff;
	}

	struct DurationEvidence
	{
		public bool ExplicitSign;
		public bool DayComponent;
	}

	[Flags]
	internal enum ColType
	{
		None = 0, Boolean = 1, Date = 2, Integer = 4, Long = 8, Float = 16,
		Double = 32, Decimal = 64, String = 128, Guid = 256,
		DateOnly = 512, TimeOnly = 1024, TimeSpan = 2048,
	}

	internal ColType GetColType()
	{
		if (typedGuid) return ColType.Guid;
		if (typedTimeSpan) return ColType.TimeSpan;
#if NET6_0_OR_GREATER
		if (typedDateOnly) return ColType.DateOnly;
		if (typedTimeOnly) return ColType.TimeOnly;
#endif
		if (nullCount == count) return ColType.None;
		if (isBoolean) return ColType.Boolean;
#if NET6_0_OR_GREATER
		if (isDateOnly) return ColType.DateOnly;
#endif
		var temporalType = GetTemporalType();
		if (temporalType == typeof(TimeSpan)) return ColType.TimeSpan;
#if NET6_0_OR_GREATER
		if (temporalType == typeof(TimeOnly)) return ColType.TimeOnly;
#endif
		if (isDate || isDateTime) return ColType.Date;
		if (isInt)
			return intMin < int.MinValue || intMax > int.MaxValue
				? ColType.Long | ColType.Double | ColType.Decimal
				: ColType.Integer | ColType.Long | ColType.Double | ColType.Decimal;
		if (isFloat) return ColType.Double | ColType.Decimal;
		if (isGuid) return ColType.Guid;
		return ColType.String;
	}

	internal static Type GetType(ColType type)
	{
		if ((type & ColType.Boolean) != 0) return typeof(bool);
		if ((type & ColType.Date) != 0) return typeof(DateTime);
		if ((type & ColType.Integer) != 0) return typeof(int);
		if ((type & ColType.Long) != 0) return typeof(long);
		if ((type & ColType.Float) != 0) return typeof(float);
		if ((type & ColType.Double) != 0) return typeof(double);
		if ((type & ColType.Decimal) != 0) return typeof(decimal);
		if ((type & ColType.String) != 0) return typeof(string);
		if ((type & ColType.Guid) != 0) return typeof(Guid);
#if NET6_0_OR_GREATER
		if ((type & ColType.DateOnly) != 0) return typeof(DateOnly);
		if ((type & ColType.TimeOnly) != 0) return typeof(TimeOnly);
#endif
		if ((type & ColType.TimeSpan) != 0) return typeof(TimeSpan);
		return typeof(string);
	}
}

public sealed partial class ColumnInfo
{
	internal Schema.Column.Builder CreateColumnSchema()
	{
		var columnName = name ?? string.Empty;
		if (typedGuid) return new Schema.Column.Builder(columnName, typeof(Guid), isNullable);
		if (typedTimeSpan) return CreateTemporalColumn(columnName, typeof(TimeSpan), DbType.Time);
#if NET6_0_OR_GREATER
		if (typedDateOnly) return CreateTemporalColumn(columnName, typeof(DateOnly), DbType.Date);
		if (typedTimeOnly) return CreateTemporalColumn(columnName, typeof(TimeOnly), DbType.Time);
#endif
		if (nullCount == count) return new Schema.Column.Builder(columnName, typeof(string), true);
		if (isBoolean) return new Schema.Column.Builder(columnName, typeof(bool), isNullable);
#if NET6_0_OR_GREATER
		if (isDateOnly) return CreateTemporalColumn(columnName, typeof(DateOnly), DbType.Date);
#endif
		var temporalType = GetTemporalType();
		if (temporalType == typeof(TimeSpan)) return CreateTemporalColumn(columnName, typeof(TimeSpan), DbType.Time);
#if NET6_0_OR_GREATER
		if (temporalType == typeof(TimeOnly)) return CreateTemporalColumn(columnName, typeof(TimeOnly), DbType.Time);
#endif
		if (isDate || isDateTime)
		{
			return new Schema.Column.Builder(columnName, typeof(DateTime), isNullable)
			{
				NumericScale = isDate ? null : dateHasFractionalSeconds ? 7 : 0
			};
		}
		if (isInt)
		{
			if (intMin == 0 && intMax == 1 && count > 2)
				return new Schema.Column.Builder(columnName, typeof(bool), isNullable);
			var integerType = intMin < int.MinValue || intMax > int.MaxValue ? typeof(long) : typeof(int);
			return new Schema.Column.Builder(columnName, integerType, isNullable);
		}
		if (isFloat)
		{
			if (isDecimal && decimalScaleMax <= 6)
				return new Schema.Column.Builder(columnName, typeof(decimal), isNullable);
			return new Schema.Column.Builder(columnName, typeof(double), isNullable);
		}
		if (isGuid) return new Schema.Column.Builder(columnName, typeof(Guid), isNullable);
		if (stringLenMax < 6 && valueCount.Count <= 2)
		{
			string? trueValue = FindValue(TrueStrings);
			string? falseValue = FindValue(FalseStrings);
			if (trueValue != null || falseValue != null)
			{
				return new Schema.Column.Builder(columnName, typeof(bool), isNullable || emptyStringCount > 0)
				{
					CommonDataType = DbType.Boolean,
					Format = trueValue + "|" + falseValue,
				};
			}
		}
		return new Schema.Column.Builder(columnName, typeof(string), isNullable)
		{
			ColumnSize = stringLenMax,
			NumericPrecision = isAscii ? 2 : 1,
			CommonDataType = isAscii ? DbType.AnsiString : DbType.String,
		};
	}

	string? FindValue(string[] candidates)
	{
		foreach (var value in candidates)
			if (valueCount.ContainsKey(value)) return value;
		return null;
	}

	Schema.Column.Builder CreateTemporalColumn(string columnName, Type dataType, DbType commonDataType) =>
		new(columnName, dataType, isNullable) { CommonDataType = commonDataType };

	static readonly string[] TrueStrings = { "y", "yes", "t", "true" };
	static readonly string[] FalseStrings = { "n", "no", "f", "false" };
}

[Flags]
enum SeriesType
{
	None = 0,
	Integer = 1,
	Date = 2,
}
