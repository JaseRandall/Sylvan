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
}
