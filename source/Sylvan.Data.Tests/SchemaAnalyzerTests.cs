#nullable enable
using Sylvan.Data.Csv;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Sylvan.Data
{
	public class SchemaAnalyzerTests
	{
		[Fact]
		public static void StringSchema()
		{
			var data = TestData.GetData();
			var a = new SchemaAnalyzer();
			var result = a.Analyze(data);
			var schema = new Schema(result.GetSchema());
			var spec = schema.ToString();

			var ss = Schema.Parse(spec);
			Assert.NotNull(ss);
		}

		[Fact]
		public static void TypedSchema()
		{
			var data = TestData.GetTestData();
			var a = new SchemaAnalyzer();
			var result = a.Analyze(data);
			var schema = new Schema(result.GetSchema());
			var spec = schema.ToString();

			var ss = Schema.Parse(spec);
			Assert.NotNull(ss);
		}

		[Fact]
		public static void Boolean()
		{
			var data = "a,b,c,d\n1,name,T,F\n2,Test,F,F\n3,Foo,,T\n";
			using var csv = CsvDataReader.Create(new StringReader(data));

			var a = new SchemaAnalyzer();
			var result = a.Analyze(csv);
			var schema = new Schema(result.GetSchema());
			var spec = schema.ToString();

			var ss = Schema.Parse(spec);
			Assert.NotNull(ss);
		}

		[Fact]
		public static void BooleanInt()
		{
			var data = "a,b,c,d\n1,name,1,0\n2,Test,0,0\n3,Foo,,1\n";
			using var csv = CsvDataReader.Create(new StringReader(data));

			var a = new SchemaAnalyzer();
			var result = a.Analyze(csv);
			var schema = new Schema(result.GetSchema());
			var spec = schema.ToString();

			var ss = Schema.Parse(spec);
			Assert.NotNull(ss);
		}

		[Fact]
		public static void UnknownType()
		{
			var data = "a,b\n1,\n2,\n3,\n";
			using var csv = CsvDataReader.Create(new StringReader(data));

			var a = new SchemaAnalyzer();
			var result = a.Analyze(csv);
			var schema = result.GetSchema();
			var cols = schema.GetColumnSchema();
			Assert.Equal(typeof(int), cols[0].DataType);
			Assert.Equal(typeof(string), cols[1].DataType);
			Assert.Equal(true, cols[1].AllowDBNull);
			Assert.Equal((int?)null, cols[1].ColumnSize);
		}

		[Fact]
		public static void StringSize()
		{
			var data = "a,b\n1,asdf\n2,qwer\n3,zxcv\n";
			using var csv = CsvDataReader.Create(new StringReader(data));

			var a = new SchemaAnalyzer();
			var result = a.Analyze(csv);
			var schema = result.GetSchema();
			var cols = schema.GetColumnSchema();
			Assert.Equal(typeof(int), cols[0].DataType);
			Assert.Equal(typeof(string), cols[1].DataType);
			Assert.Equal(false, cols[1].AllowDBNull);
			Assert.Equal(4, cols[1].ColumnSize);
		}
	}

	public class TemporalSchemaAnalyzerTests
	{
		[Fact]
		public void DefaultsRemainCompatible()
		{
			Assert.Equal(typeof(bool), Analyze("true", "false")[0].DataType);
			Assert.Equal(typeof(int), Analyze("1", "2")[0].DataType);
			Assert.Equal(typeof(double), Analyze("1e3", "2e3")[0].DataType);
			Assert.Equal(typeof(decimal), Analyze(new[] { "1.25", "2.50" }, CultureInfo.InvariantCulture)[0].DataType);
			Assert.Equal(typeof(DateTime), Analyze("2024-01-01", "2024-01-02")[0].DataType);
			Assert.Equal(typeof(Guid), Analyze(Guid.NewGuid().ToString(), Guid.NewGuid().ToString())[0].DataType);
			Assert.Equal(typeof(string), Analyze("alpha", "beta")[0].DataType);
			Assert.Equal(typeof(DateTime), new Schema.Column.Builder("Date", DbType.Date).DataType);
		}

		[Fact]
		public void TimeSpanInferenceIsOptInAndDoesNotStealIntegers()
		{
			var options = new SchemaAnalyzerOptions { TemporalInference = TemporalInferenceOptions.TimeSpan };
			Assert.Equal(typeof(TimeSpan), Analyze(new[] { "01:30:00", "25:00:00" }, options: options)[0].DataType);
			Assert.Equal(typeof(TimeSpan), Analyze(new[] { "-01:00:00", "+02:00:00" }, options: options)[0].DataType);
			Assert.Equal(typeof(TimeSpan), Analyze(new[] { "1.02:03:04", "2.03:04:05" }, options: options)[0].DataType);
			Assert.Equal(typeof(int), Analyze(new[] { "1", "2", "3" }, options: options)[0].DataType);
			Assert.Equal(typeof(string), Analyze(new[] { "01:00:00", "invalid" }, options: options)[0].DataType);
		}

		static Schema Analyze(params string[] values) => Analyze(values, null, null);
		static Schema Analyze(IEnumerable<string?> values, CultureInfo? culture = null, SchemaAnalyzerOptions? options = null)
		{
			options ??= new SchemaAnalyzerOptions { Culture = culture };
			using var reader = ObjectDataReader.CreateBuilder<ValueRow>().AddColumn("Value", row => row.Value).Build(values.Select(value => new ValueRow { Value = value }));
			return new SchemaAnalyzer(options).Analyze(reader).GetSchema();
		}
		sealed class ValueRow { public string? Value { get; set; } }
	}

	public class ModernTemporalSchemaAnalyzerTests
	{
#if NET6_0_OR_GREATER
		[Fact]
		public void DateOnlyInferenceHandlesCultureNullsMixedAndInvalidValues()
		{
			var options = Options(TemporalInferenceOptions.DateOnly);
			Assert.Equal(typeof(DateOnly), Analyze(new[] { "2024-01-01", "2024-01-02" }, options: options)[0].DataType);
			Assert.Equal(typeof(DateOnly), Analyze(new[] { "31/12/2024", "01/01/2025" }, options: Options(TemporalInferenceOptions.DateOnly, new CultureInfo("en-AU")))[0].DataType);
			var nullable = Analyze(new string?[] { "2024-01-01", null, "" }, options: options);
			Assert.Equal(typeof(DateOnly), nullable[0].DataType);
			Assert.True(nullable[0].AllowDBNull == true);
			Assert.Equal(typeof(DateTime), Analyze(new[] { "2024-01-01", "2024-01-02 12:30:00" }, options: options)[0].DataType);
			Assert.Equal(typeof(string), Analyze(new[] { "2024-01-01", "invalid" }, options: options)[0].DataType);
		}

		[Fact]
		public void TimeOnlyInferenceUsesTimeOnlySemantics()
		{
			var options = Options(TemporalInferenceOptions.TimeOnly);
			Assert.Equal(typeof(TimeOnly), Analyze(new[] { "00:00:00", "23:59:59" }, options: options)[0].DataType);
			Assert.Equal(typeof(TimeOnly), Analyze(new[] { "1:14 PM", "11:45 AM" }, options: Options(TemporalInferenceOptions.TimeOnly, new CultureInfo("en-US")))[0].DataType);
			Assert.Equal(typeof(string), Analyze(new[] { "01:00:00", "invalid" }, options: options)[0].DataType);
			Assert.Equal(typeof(string), Analyze(new[] { "01:00:00", "25:00:00" }, options: options)[0].DataType);
		}

		[Fact]
		public void TimeOnlyAndTimeSpanResolutionIsOrderIndependent()
		{
			var options = Options(TemporalInferenceOptions.TimeOnly | TemporalInferenceOptions.TimeSpan);
			AssertOrder(new[] { "01:30:00", "1.02:03:04" }, typeof(TimeSpan), options);
			AssertOrder(new[] { "01:00:00", "25:00:00" }, typeof(TimeSpan), options);
			Assert.Equal(typeof(TimeOnly), Analyze(new[] { "01:00:00", "02:30:00" }, options: options)[0].DataType);
			options.DurationColumnNameContains = new[] { " duration ", "", "DURATION" };
			Assert.Equal(typeof(TimeSpan), Analyze(new[] { "01:00:00", "02:30:00" }, "ElapsedDuration", options)[0].DataType);
			Assert.Equal(typeof(TimeOnly), Analyze(new[] { "01:00:00", "02:30:00" }, "StartTime", options)[0].DataType);
			Assert.Equal(typeof(TimeSpan), Analyze(new[] { "01:00:00", "02:30:00" }, options: Options(TemporalInferenceOptions.TimeSpan))[0].DataType);
		}

		[Fact]
		public void TemporalTokensResolveDirectlyAndRoundTrip()
		{
			var schema = Schema.Parse("Date:DaTeOnLy?,Time:TIMEONLY,Duration:TimeSpan");
			Assert.Equal(typeof(DateOnly), schema[0].DataType);
			Assert.Equal(typeof(TimeOnly), schema[1].DataType);
			Assert.Equal(typeof(TimeSpan), schema[2].DataType);
			Assert.Equal(DbType.Date, schema[0].CommonDataType);
			Assert.Equal(DbType.Time, schema[1].CommonDataType);
			Assert.Equal(DbType.Time, schema[2].CommonDataType);
			var roundTrip = Schema.Parse(schema.ToString());
			Assert.Equal(typeof(DateOnly), roundTrip[0].DataType);
			Assert.Equal(typeof(TimeOnly), roundTrip[1].DataType);
			Assert.Equal(typeof(TimeSpan), roundTrip[2].DataType);
		}
#endif

		static SchemaAnalyzerOptions Options(TemporalInferenceOptions inference, CultureInfo? culture = null) => new() { TemporalInference = inference, Culture = culture };
		static void AssertOrder(string[] values, Type type, SchemaAnalyzerOptions options)
		{
			Assert.Equal(type, Analyze(values, options: options)[0].DataType);
			Assert.Equal(type, Analyze(values.Reverse().ToArray(), options: options)[0].DataType);
		}
		static Schema Analyze(IEnumerable<string?> values, string name = "Value", SchemaAnalyzerOptions? options = null)
		{
			using var reader = ObjectDataReader.CreateBuilder<ValueRow>().AddColumn(name, row => row.Value).Build(values.Select(value => new ValueRow { Value = value }));
			return new SchemaAnalyzer(options).Analyze(reader).GetSchema();
		}
		sealed class ValueRow { public string? Value { get; set; } }
	}

	public class TemporalSchemaAnalyzerExecutionTests
	{
		[Fact]
		public async Task SynchronousAndAsynchronousAnalysisUseMatchingLimits()
		{
			var options = new SchemaAnalyzerOptions { AnalyzeRowCount = 1 };
			using var sync = new TrackingReader(CreateReader("1", "2", "3"));
			var syncSchema = new SchemaAnalyzer(options).Analyze(sync).GetSchema();
			Assert.Equal(1, sync.SyncReads);
			Assert.Equal(0, sync.AsyncReads);

			using var asyncReader = new TrackingReader(CreateReader("1", "2", "3"));
			var asyncSchema = (await new SchemaAnalyzer(options).AnalyzeAsync(asyncReader)).GetSchema();
			Assert.Equal(0, asyncReader.SyncReads);
			Assert.Equal(1, asyncReader.AsyncReads);
			Assert.Equal(syncSchema[0].DataType, asyncSchema[0].DataType);
			Assert.Equal(syncSchema[0].AllowDBNull, asyncSchema[0].AllowDBNull);
		}

		[Fact]
		public void ExplicitCultureRetainsNumericGrammar()
		{
			Assert.Equal(typeof(int), Analyze(CultureInfo.InvariantCulture, "1", "2")[0].DataType);
			Assert.Equal(typeof(double), Analyze(CultureInfo.InvariantCulture, "1.2e3", "2.4e3")[0].DataType);
			Assert.Equal(typeof(decimal), Analyze(new CultureInfo("fr-FR"), "1,25", "2,50")[0].DataType);
		}

		static Schema Analyze(CultureInfo culture, params string[] values)
		{
			using var reader = CreateReader(values);
			return new SchemaAnalyzer(new SchemaAnalyzerOptions { Culture = culture }).Analyze(reader).GetSchema();
		}
		static DbDataReader CreateReader(params string[] values) => ObjectDataReader.CreateBuilder<ValueRow>()
			.AddColumn("Value", row => row.Value)
			.Build(values.Select(value => new ValueRow { Value = value }));
		sealed class ValueRow { public string Value { get; set; } = string.Empty; }
		sealed class TrackingReader : DataReaderAdapter
		{
			public TrackingReader(DbDataReader reader) : base(reader) { }
			public int SyncReads { get; private set; }
			public int AsyncReads { get; private set; }
			public override bool Read() { SyncReads++; return Reader.Read(); }
			public override Task<bool> ReadAsync(CancellationToken cancellationToken) { AsyncReads++; return Reader.ReadAsync(cancellationToken); }
		}
	}
}
