#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Xunit;

namespace Sylvan.Data
{
	public class SchemaTests
	{
		[Fact]
		public void Test1()
		{
			var spec = "A,B*";
			var s = Schema.Parse(spec);
			Assert.NotNull(s);
			var result = s.ToString();
			Assert.Equal(spec, result);
		}

		[Fact]
		public void SeriesTest()
		{
			var spec = "State:string[2],County:string[32],{Date}>Issues*:int?";
			var s = Schema.Parse(spec);
			Assert.NotNull(s);
			var result = s.ToString();
			Assert.Equal(spec, result, true);
			var issuesCol = s.GetColumnSchema()[2];
			Assert.Equal(typeof(int), issuesCol.DataType);
			Assert.True(issuesCol.AllowDBNull);
		}

		[Fact]
		public void TemporalDbTypeMappingsPreserveDateCompatibility()
		{
			var schema = new Schema.Builder()
				.Add(new Schema.Column.Builder("Date", DbType.Date, false))
				.Add(new Schema.Column.Builder("Time", DbType.Time, false))
				.Add<TimeSpan>("Duration")
#if NET6_0_OR_GREATER
				.Add<DateOnly>("DateOnly")
				.Add<TimeOnly>("TimeOnly")
#endif
				.Build();

			Assert.Equal(typeof(DateTime), schema[0].DataType);
			Assert.Equal(typeof(TimeSpan), schema[1].DataType);
			Assert.Equal(DbType.Time, schema[2].CommonDataType);
#if NET6_0_OR_GREATER
			Assert.Equal(DbType.Date, schema[3].CommonDataType);
			Assert.Equal(DbType.Time, schema[4].CommonDataType);
#endif
		}

		[Fact]
		public void TypedTimeSpanSourcesArePreservedAndBound()
		{
			var rows = new[]
			{
				new DurationRow { Duration = TimeSpan.FromHours(3), NullableDuration = TimeSpan.FromDays(2) },
				new DurationRow { Duration = TimeSpan.FromMinutes(30) },
			};
			using (var reader = CreateDurationReader(rows))
			{
				var schema = new SchemaAnalyzer().Analyze(reader).GetSchema();
				Assert.Equal(typeof(TimeSpan), schema[0].DataType);
				Assert.Equal(typeof(TimeSpan), schema[1].DataType);
				Assert.Equal(DbType.Time, schema[0].CommonDataType);
			}
			using var bindingReader = CreateDurationReader(rows);
			var bound = bindingReader.GetRecords<DurationTarget>().ToArray();
			Assert.Equal(rows[0].Duration, bound[0].Duration);
			Assert.Equal(rows[0].NullableDuration, bound[0].NullableDuration);
			Assert.Null(bound[1].NullableDuration);
		}

#if NET6_0_OR_GREATER
		[Fact]
		public void TypedModernTemporalSourcesArePreservedAndBound()
		{
			var rows = new[]
			{
				new TemporalRow
				{
					Date = new DateOnly(2024, 1, 2), Time = new TimeOnly(3, 4, 5),
					Duration = TimeSpan.FromHours(27), Id = Guid.NewGuid(),
					NullableDate = new DateOnly(2025, 6, 7), NullableTime = new TimeOnly(8, 9, 10),
					NullableDuration = TimeSpan.FromDays(2),
				},
				new TemporalRow
				{
					Date = new DateOnly(2024, 2, 3), Time = new TimeOnly(4, 5, 6),
					Duration = TimeSpan.FromMinutes(30), Id = Guid.NewGuid(),
				},
			};
			using (var reader = CreateTemporalReader(rows))
			{
				var schema = new SchemaAnalyzer().Analyze(reader).GetSchema();
				Assert.Equal(typeof(DateOnly), schema[0].DataType);
				Assert.Equal(typeof(TimeOnly), schema[1].DataType);
				Assert.Equal(typeof(TimeSpan), schema[2].DataType);
				Assert.Equal(typeof(Guid), schema[3].DataType);
			}
			using var bindingReader = CreateTemporalReader(rows);
			var bound = bindingReader.GetRecords<TemporalTarget>().ToArray();
			Assert.Equal(rows[0].Date, bound[0].Date);
			Assert.Equal(rows[0].Time, bound[0].Time);
			Assert.Equal(rows[0].Duration, bound[0].Duration);
			Assert.Equal(rows[0].NullableDate, bound[0].NullableDate);
			Assert.Equal(rows[0].NullableTime, bound[0].NullableTime);
			Assert.Equal(rows[0].NullableDuration, bound[0].NullableDuration);
			Assert.Null(bound[1].NullableDate);
			Assert.Null(bound[1].NullableTime);
			Assert.Null(bound[1].NullableDuration);
		}
#endif

		[Fact]
		public void SeriesPreserveGuidAndTemporalTypesWithoutMixedDegradation()
		{
			Assert.Equal(typeof(Guid), AnalyzePair(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), new SchemaAnalyzerOptions { DetectSeries = true })[0].DataType);
			Assert.Equal(typeof(TimeSpan), AnalyzePair("01:00:00", "02:00:00", new SchemaAnalyzerOptions { DetectSeries = true, TemporalInference = TemporalInferenceOptions.TimeSpan })[0].DataType);
#if NET6_0_OR_GREATER
			Assert.Equal(typeof(DateOnly), AnalyzePair("2024-01-01", "2024-01-02", new SchemaAnalyzerOptions { DetectSeries = true, TemporalInference = TemporalInferenceOptions.DateOnly })[0].DataType);
			Assert.Equal(typeof(TimeOnly), AnalyzePair("01:00:00", "02:00:00", new SchemaAnalyzerOptions { DetectSeries = true, TemporalInference = TemporalInferenceOptions.TimeOnly })[0].DataType);
			var mixed = AnalyzePair("2024-01-01", "01:00:00", new SchemaAnalyzerOptions { DetectSeries = true, TemporalInference = TemporalInferenceOptions.DateOnly | TemporalInferenceOptions.TimeOnly });
			Assert.Equal(2, mixed.Count);
			Assert.Equal(typeof(DateOnly), mixed[0].DataType);
			Assert.Equal(typeof(TimeOnly), mixed[1].DataType);
#endif
		}

		static DbDataReader CreateDurationReader(IEnumerable<DurationRow> rows) => ObjectDataReader.CreateBuilder<DurationRow>()
			.AddColumn("Duration", row => row.Duration)
			.AddColumn<TimeSpan>("NullableDuration", row => row.NullableDuration)
			.Build(rows);

#if NET6_0_OR_GREATER
		static DbDataReader CreateTemporalReader(IEnumerable<TemporalRow> rows) => ObjectDataReader.CreateBuilder<TemporalRow>()
			.AddColumn("Date", row => row.Date)
			.AddColumn("Time", row => row.Time)
			.AddColumn("Duration", row => row.Duration)
			.AddColumn("Id", row => row.Id)
			.AddColumn<DateOnly>("NullableDate", row => row.NullableDate)
			.AddColumn<TimeOnly>("NullableTime", row => row.NullableTime)
			.AddColumn<TimeSpan>("NullableDuration", row => row.NullableDuration)
			.Build(rows);
#endif

		static Schema AnalyzePair(string first, string second, SchemaAnalyzerOptions options)
		{
			using var reader = ObjectDataReader.CreateBuilder<PairRow>()
				.AddColumn("Value1", row => row.First)
				.AddColumn("Value2", row => row.Second)
				.Build(new[] { new PairRow { First = first, Second = second } });
			return new SchemaAnalyzer(options).Analyze(reader).GetSchema();
		}

		sealed class PairRow
		{
			public string First { get; set; } = string.Empty;
			public string Second { get; set; } = string.Empty;
		}
		sealed class DurationRow
		{
			public TimeSpan Duration { get; set; }
			public TimeSpan? NullableDuration { get; set; }
		}
		sealed class DurationTarget
		{
			public TimeSpan Duration { get; set; }
			public TimeSpan? NullableDuration { get; set; }
		}
#if NET6_0_OR_GREATER
		sealed class TemporalRow
		{
			public DateOnly Date { get; set; }
			public TimeOnly Time { get; set; }
			public TimeSpan Duration { get; set; }
			public Guid Id { get; set; }
			public DateOnly? NullableDate { get; set; }
			public TimeOnly? NullableTime { get; set; }
			public TimeSpan? NullableDuration { get; set; }
		}
		sealed class TemporalTarget
		{
			public DateOnly Date { get; set; }
			public TimeOnly Time { get; set; }
			public TimeSpan Duration { get; set; }
			public Guid Id { get; set; }
			public DateOnly? NullableDate { get; set; }
			public TimeOnly? NullableTime { get; set; }
			public TimeSpan? NullableDuration { get; set; }
		}
#endif
	}
}
