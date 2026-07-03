using Sylvan.Data.Csv;
using System;
using System.IO;
using Xunit;

namespace Sylvan.Data
{
	public class InvalidEnumValueExceptionTests
	{
		[Fact]
		public void InvalidEnumTextIncludesValueAndEnumType()
		{
			const string invalidValue = "NotASeverity";
			var schema = Schema.Parse(":int,:string,:string").GetColumnSchema();
			var csvData = $"Id,Name,Severity\r\n1,Olive,{invalidValue}";
			using var data = CsvDataReader.Create(
				new StringReader(csvData),
				new CsvDataReaderOptions { Schema = new CsvSchema(schema) });
			var binder = new CompiledDataBinder<EnumRecord>(
				DataBinderOptions.Default,
				data.GetColumnSchema());

			Assert.True(data.Read());
			var exception = Assert.Throws<InvalidEnumValueException>(
				() => binder.GetRecord(data));

			Assert.IsAssignableFrom<FormatException>(exception);
			Assert.Contains(invalidValue, exception.Message);
			Assert.Contains(typeof(Severity).FullName!, exception.Message);
			Assert.Equal(invalidValue, exception.Value);
			Assert.Equal(typeof(Severity), exception.EnumType);
		}

		public enum Severity
		{
			Warning = 1,
		}

		public sealed class EnumRecord
		{
			public int Id { get; set; }
			public string Name { get; set; } = string.Empty;
			public Severity Severity { get; set; }
		}
	}
}
