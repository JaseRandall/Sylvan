using System.IO;
using System.Reflection;
using Xunit;

namespace Sylvan.IO;

public sealed class EncoderStreamApiTests
{
	[Fact]
	public void Close_RemainsDeclaredOverride()
	{
		var method = typeof(EncoderStream).GetMethod(
			nameof(Stream.Close),
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

		Assert.NotNull(method);
		Assert.Equal(typeof(Stream), method!.GetBaseDefinition().DeclaringType);
	}
}
