using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Sylvan.Data.Csv
{
	public class CustomFieldAccessorTests
	{
		[Fact]
		public void Test()
		{
			const string Data =
								"""
								a,b
								1,2022-01-13T12:33:45.1234567
								2,2022-01-13T12:33:45.1234567-08:00
								3,2022-01-13T12:33:45.1234567Z
								""";

			var schema = new CsvSchema(Schema.Parse("a:int,b:datetime"));
			var options = new CsvDataReaderOptions { Schema = schema, HasHeaders = true, CustomFieldAccessors = new Dictionary<Type, IFieldAccessor> { { typeof(DateTime), MyDateTimeAccessor.Instance } } };

			var csvReader = CsvDataReader.Create(new StringReader(Data), options);
			csvReader.Read();
			var x = csvReader[1];
			var y = csvReader.GetDateTime(1);
			Assert.Equal(DateTimeKind.Utc, ((DateTime)x).Kind);
			Assert.Equal(DateTimeKind.Unspecified, y.Kind);
		}
	}

	sealed class MyDateTimeAccessor : FieldAccessor<DateTime>
	{
		internal static readonly MyDateTimeAccessor Instance = new();

		public override DateTime GetValue(CsvDataReader reader, int ordinal)
		{
			var value = reader.GetDateTime(ordinal);
			if (value.Kind == DateTimeKind.Unspecified)
			{
				value = new DateTime(value.Ticks, DateTimeKind.Utc);
			}
			return value;
		}
	}
}
