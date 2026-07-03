using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace Sylvan.Data;

sealed class SimpleSchemaSerializer
{
	internal static readonly SimpleSchemaSerializer SingleLine = new(false);
	internal static readonly SimpleSchemaSerializer MultiLine = new(true);

	const string SeriesSymbol = "*";

	static readonly Regex ColSpecRegex =
		new(
			@"^((?<BaseName>[^\>]+)\>)?(?<Name>[^\:]+)?(?::(?<Type>[a-z0-9]+)(\[(?<Size>\d+)\])?(?<AllowNull>\?)?(\{(?<Format>[^\}]+)\})?)?$",
			RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
		);

	static readonly Regex SeriesFormatRegex =
		new("^(?<prefix>.*){{(Date|Integer)}}(?<suffix>.*)$");

	static readonly Regex NewLineRegex =
		new(
			"\r\n|\n",
			RegexOptions.Multiline | RegexOptions.Compiled
		);

	static readonly Lazy<Dictionary<string, DbType>> ColumnTypeMap = new(InitializeTypeMap);

	static Dictionary<string, DbType> InitializeTypeMap()
	{
		var map = new Dictionary<string, DbType>(StringComparer.OrdinalIgnoreCase);
		var values = (DbType[])Enum.GetValues(typeof(DbType));
		foreach (DbType type in values)
		{
			map.Add(type.ToString(), type);
		}
		map.Add("bool", DbType.Boolean);
		map.Add("short", DbType.Int16);
		map.Add("int", DbType.Int32);
		map.Add("integer", DbType.Int32);
		map.Add("long", DbType.Int64);
		map.Add("float", DbType.Single);
		return map;
	}

	readonly bool multiLine;

	internal SimpleSchemaSerializer(bool multiLine)
	{
		this.multiLine = multiLine;
	}

	/// <summary>
	/// Attempts to parse a schema specification.
	/// </summary>
	/// <param name="spec">The schema specification string.</param>
	/// <returns>A Schema.</returns>
	public static Schema Parse(string spec)
	{
		var builder = new Schema.Builder();
		var map = ColumnTypeMap.Value;
		var colSpecs = NewLineRegex.Replace(spec, "").Split(',');

		foreach (var colSpec in colSpecs)
		{
			var match = ColSpecRegex.Match(colSpec);
			if (!match.Success)
			{
				throw new ArgumentException();
			}

			var typeGroup = match.Groups["Type"];
			var formatGroup = match.Groups["Format"];
			var baseNameGroup = match.Groups["BaseName"];
			var baseName = baseNameGroup.Success ? baseNameGroup.Value : null;
			var name = match.Groups["Name"].Value;
			bool allowNull = false;
			int size = -1;
			Schema.Column.Builder columnBuilder;

			if (typeGroup.Success)
			{
				var typeName = typeGroup.Value;
				allowNull = match.Groups["AllowNull"].Success;
				var sizeGroup = match.Groups["Size"];
				size = sizeGroup.Success ? int.Parse(sizeGroup.Value) : -1;

				if (TryGetExplicitClrType(typeName, out var clrType))
				{
					columnBuilder = new Schema.Column.Builder(name, clrType, allowNull);
					columnBuilder.CommonDataType = GetExplicitCommonDataType(clrType);
				}
				else if (map.TryGetValue(typeName, out var dbType))
				{
					columnBuilder = new Schema.Column.Builder(name, dbType, allowNull);
				}
				else
				{
					throw new ArgumentException();
				}
			}
			else
			{
				columnBuilder = new Schema.Column.Builder(name, DbType.String, false);
			}

			columnBuilder.BaseColumnName = baseName;
			columnBuilder.ColumnSize = size == -1 ? null : (int?)size;
			if (formatGroup.Success)
			{
				columnBuilder.Format = formatGroup.Value;
			}

			if (name.EndsWith(SeriesSymbol))
			{
				columnBuilder.IsSeries = true;
				columnBuilder.ColumnName = "";
				columnBuilder.SeriesHeaderFormat = columnBuilder.BaseColumnName;
				columnBuilder.SeriesOrdinal = 0;
				columnBuilder.SeriesName = name.Substring(0, name.Length - 1);
				if (columnBuilder.BaseColumnName != null)
				{
					var seriesMatch = DataBinder.SeriesKeyRegex.Match(columnBuilder.BaseColumnName);
					if (seriesMatch.Success)
					{
						var seriesTypeName = seriesMatch.Groups[1].Value;
						if (TryGetExplicitClrType(seriesTypeName, out var seriesClrType))
						{
							columnBuilder.SeriesType = seriesClrType;
						}
						else if (map.TryGetValue(seriesTypeName, out var seriesDbType))
						{
							columnBuilder.SeriesType = DataBinder.GetDataType(seriesDbType);
						}
						else
						{
							throw new ArgumentException();
						}
					}
				}
			}

			builder.Add(columnBuilder);
		}
		return builder.Build();
	}

	static bool TryGetExplicitClrType(string typeName, out Type type)
	{
		if (string.Equals(typeName, "timespan", StringComparison.OrdinalIgnoreCase))
		{
			type = typeof(TimeSpan);
			return true;
		}
#if NET6_0_OR_GREATER
		if (string.Equals(typeName, "dateonly", StringComparison.OrdinalIgnoreCase))
		{
			type = typeof(DateOnly);
			return true;
		}
		if (string.Equals(typeName, "timeonly", StringComparison.OrdinalIgnoreCase))
		{
			type = typeof(TimeOnly);
			return true;
		}
#endif
		type = null!;
		return false;
	}

	static DbType GetExplicitCommonDataType(Type type)
	{
		if (type == typeof(TimeSpan))
		{
			return DbType.Time;
		}
#if NET6_0_OR_GREATER
		if (type == typeof(DateOnly))
		{
			return DbType.Date;
		}
		if (type == typeof(TimeOnly))
		{
			return DbType.Time;
		}
#endif
		throw new ArgumentException();
	}

	/// <summary>
	/// Gets the specification string for this schema.
	/// </summary>
	public string GetSchemaSpec(Schema schema)
	{
		var writer = new StringWriter();
		bool first = true;
		foreach (var column in schema)
		{
			if (first)
			{
				first = false;
			}
			else
			{
				writer.Write(",");
				if (multiLine)
				{
					writer.WriteLine();
				}
			}

			if (column.IsSeries == true)
			{
				if (column.SeriesHeaderFormat != null)
				{
					writer.Write(column.SeriesHeaderFormat);
					writer.Write(">");
				}
				writer.Write(column.SeriesName + "*");
			}
			else
			{
				if (column.BaseColumnName != null && column.BaseColumnName != column.ColumnName)
				{
					writer.Write(column.BaseColumnName);
					writer.Write(">");
				}
				writer.Write(column.ColumnName);
			}
			WriteType(writer, column);
		}

		return writer.ToString();
	}

	static void WriteType(TextWriter writer, Schema.Column column)
	{
		if (column.DataType == typeof(string) && column.AllowDBNull == false && column.ColumnSize == null)
		{
			return;
		}

		var typeName = GetExplicitTypeName(column.DataType) ??
			column.CommonDataType switch
			{
				DbType.String => "string",
				DbType.Int32 => "int",
				DbType.Double => "double",
				DbType.Decimal => "decimal",
				DbType.Boolean => "bool",
				_ => null
			};

		if (typeName == null)
		{
			typeName = column.DataType?.Name;
		}
		if (typeName == null)
		{
			return;
		}

		writer.Write(":");
		writer.Write(typeName);

		if (column.CommonDataType != null && HasLength(column.CommonDataType.Value) && column.ColumnSize != null)
		{
			writer.Write("[");
			writer.Write(column.ColumnSize.Value);
			writer.Write("]");
		}

		if (column.AllowDBNull != false)
		{
			writer.Write("?");
		}

		if (column.Format != null)
		{
			writer.Write("{");
			writer.Write(column.Format);
			writer.Write("}");
		}
	}

	static string? GetExplicitTypeName(Type? type)
	{
		if (type == typeof(TimeSpan))
		{
			return "timespan";
		}
#if NET6_0_OR_GREATER
		if (type == typeof(DateOnly))
		{
			return "dateonly";
		}
		if (type == typeof(TimeOnly))
		{
			return "timeonly";
		}
#endif
		return null;
	}

	static bool HasLength(DbType type)
	{
		return type == DbType.String ||
			type == DbType.AnsiString ||
			type == DbType.Binary;
	}
}
