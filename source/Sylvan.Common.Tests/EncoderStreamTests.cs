using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Sylvan.IO;

public sealed class EncoderStreamTests
{
	[Fact]
	public void Constructor_WithNullStream_Throws()
	{
		var encoder = FinalizationEncoder.Complete();

		var exception = Assert.Throws<ArgumentNullException>(() => new EncoderStream(null!, encoder));

		Assert.Equal("stream", exception.ParamName);
	}

	[Fact]
	public void Constructor_WithNullEncoder_Throws()
	{
		var stream = new TrackingStream();

		var exception = Assert.Throws<ArgumentNullException>(() => new EncoderStream(stream, null!));

		Assert.Equal("encoder", exception.ParamName);
	}

	[Fact]
	public void ExistingConstructor_LeavesUnderlyingStreamOpen()
	{
		var stream = new TrackingStream();
		var encoderStream = new EncoderStream(stream, FinalizationEncoder.Complete());

		encoderStream.Dispose();

		Assert.Equal(0, stream.DisposeCount);
		stream.WriteByte(0x42);
	}

	[Fact]
	public void OwnershipConstructor_WhenFalse_LeavesUnderlyingStreamOpen()
	{
		var stream = new TrackingStream();
		var encoderStream = new EncoderStream(stream, FinalizationEncoder.Complete(), false);

		encoderStream.Dispose();

		Assert.Equal(0, stream.DisposeCount);
		stream.WriteByte(0x42);
	}

	[Fact]
	public void OwnershipConstructor_WhenTrue_DisposesUnderlyingStreamAfterFinalOutput()
	{
		var stream = new TrackingStream();
		var encoder = new FinalizationEncoder(
			new FinalizationStep(EncoderResult.Complete, 0x7f));
		var encoderStream = new EncoderStream(stream, encoder, true);

		encoderStream.Dispose();

		Assert.Equal(new byte[] { 0x7f }, stream.ToArray());
		Assert.Equal(1, stream.DisposeCount);
	}

	[Fact]
	public void Flush_WhenNoOutputIsBuffered_DoesNotWrite()
	{
		var stream = new TrackingStream();
		using var encoderStream = new EncoderStream(stream, FinalizationEncoder.Complete());

		encoderStream.Flush();

		Assert.Equal(0, stream.WriteCount);
		Assert.Equal(0, stream.ZeroLengthWriteCount);
	}

	[Fact]
	public async Task FlushAsync_WhenNoOutputIsBuffered_DoesNotWrite()
	{
		var stream = new TrackingStream();
		using var encoderStream = new EncoderStream(stream, FinalizationEncoder.Complete());

		await encoderStream.FlushAsync(CancellationToken.None);

		Assert.Equal(0, stream.WriteCount);
		Assert.Equal(0, stream.ZeroLengthWriteCount);
	}

	[Fact]
	public void Flush_WhenOutputIsBuffered_WritesTheOutput()
	{
		var stream = new TrackingStream();
		using var encoderStream = new EncoderStream(stream, FinalizationEncoder.Complete());
		var data = new byte[] { 1, 2, 3 };

		encoderStream.Write(data, 0, data.Length);
		encoderStream.Flush();

		Assert.Equal(data, stream.ToArray());
		Assert.Equal(1, stream.WriteCount);
		Assert.Equal(0, stream.ZeroLengthWriteCount);
	}

	[Fact]
	public void Dispose_WhenFinalizationRequiresOutputSpace_RetriesUntilComplete()
	{
		var fullBuffer = new byte[0x1000];
		Array.Fill(fullBuffer, (byte)0xa1);
		var stream = new TrackingStream();
		var encoder = new FinalizationEncoder(
			new FinalizationStep(EncoderResult.RequiresOutputSpace, fullBuffer),
			new FinalizationStep(EncoderResult.Complete, 0xb2));
		var encoderStream = new EncoderStream(stream, encoder);

		encoderStream.Dispose();

		var output = stream.ToArray();
		Assert.Equal(0x1001, output.Length);
		Assert.Equal(0xa1, output[0]);
		Assert.Equal(0xa1, output[0xfff]);
		Assert.Equal(0xb2, output[0x1000]);
		Assert.Equal(2, encoder.EmptyInputCallCount);
		Assert.Equal(2, stream.WriteCount);
	}

	[Fact]
	public void Dispose_WhenFinalizationRequestsOneFlush_RetriesUntilComplete()
	{
		var stream = new TrackingStream();
		var encoder = new FinalizationEncoder(
			new FinalizationStep(EncoderResult.Flush, 1),
			new FinalizationStep(EncoderResult.Complete, 2));
		var encoderStream = new EncoderStream(stream, encoder);

		encoderStream.Dispose();

		Assert.Equal(new byte[] { 1, 2 }, stream.ToArray());
		Assert.Equal(2, encoder.EmptyInputCallCount);
		Assert.Equal(2, stream.WriteCount);
	}

	[Fact]
	public void Dispose_WhenFinalizationRequestsMultipleFlushes_RetriesUntilComplete()
	{
		var stream = new TrackingStream();
		var encoder = new FinalizationEncoder(
			new FinalizationStep(EncoderResult.Flush, 1),
			new FinalizationStep(EncoderResult.Flush, 2),
			new FinalizationStep(EncoderResult.Flush, 3),
			new FinalizationStep(EncoderResult.Complete, 4));
		var encoderStream = new EncoderStream(stream, encoder);

		encoderStream.Dispose();

		Assert.Equal(new byte[] { 1, 2, 3, 4 }, stream.ToArray());
		Assert.Equal(4, encoder.EmptyInputCallCount);
		Assert.Equal(4, stream.WriteCount);
	}

	[Fact]
	public void Dispose_WhenCalledRepeatedly_FinalizesAndDisposesOnlyOnce()
	{
		var stream = new TrackingStream();
		var encoder = new FinalizationEncoder(
			new FinalizationStep(EncoderResult.Flush, 1),
			new FinalizationStep(EncoderResult.Complete, 2));
		var encoderStream = new EncoderStream(stream, encoder, true);

		encoderStream.Dispose();
		encoderStream.Dispose();

		Assert.Equal(new byte[] { 1, 2 }, stream.ToArray());
		Assert.Equal(2, encoder.EmptyInputCallCount);
		Assert.Equal(1, stream.DisposeCount);
	}

	[Fact]
	public void Close_UsesTheStandardDisposalLifecycle()
	{
		var stream = new TrackingStream();
		var encoder = new FinalizationEncoder(
			new FinalizationStep(EncoderResult.Complete, 0x5a));
		var encoderStream = new EncoderStream(stream, encoder, true);

		encoderStream.Close();

		Assert.Equal(new byte[] { 0x5a }, stream.ToArray());
		Assert.Equal(1, encoder.EmptyInputCallCount);
		Assert.Equal(1, stream.DisposeCount);
	}

	sealed class TrackingStream : MemoryStream
	{
		public int DisposeCount { get; private set; }

		public int WriteCount { get; private set; }

		public int ZeroLengthWriteCount { get; private set; }

		public override void Write(byte[] buffer, int offset, int count)
		{
			RecordWrite(count);
			base.Write(buffer, offset, count);
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			RecordWrite(count);
			base.Write(buffer, offset, count);
			return Task.CompletedTask;
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			RecordWrite(buffer.Length);
			base.Write(buffer.Span);
			return ValueTask.CompletedTask;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				DisposeCount++;

			base.Dispose(disposing);
		}

		void RecordWrite(int count)
		{
			WriteCount++;
			if (count == 0)
				ZeroLengthWriteCount++;
		}
	}

	sealed class FinalizationEncoder : Encoder
	{
		readonly FinalizationStep[] finalizationSteps;
		int finalizationStepIndex;

		public FinalizationEncoder(params FinalizationStep[] finalizationSteps)
		{
			this.finalizationSteps = finalizationSteps;
		}

		public int EmptyInputCallCount { get; private set; }

		public static FinalizationEncoder Complete()
		{
			return new FinalizationEncoder(new FinalizationStep(EncoderResult.Complete));
		}

		public override EncoderResult Encode(
			ReadOnlySpan<byte> src,
			Span<byte> dst,
			out int bytesConsumed,
			out int bytesWritten)
		{
			if (src.IsEmpty == false)
			{
				var count = Math.Min(src.Length, dst.Length);
				src.Slice(0, count).CopyTo(dst);
				bytesConsumed = count;
				bytesWritten = count;
				return count < src.Length
					? EncoderResult.RequiresOutputSpace
					: EncoderResult.RequiresInput;
			}

			EmptyInputCallCount++;
			if (finalizationStepIndex >= finalizationSteps.Length)
				throw new InvalidOperationException("The encoder was finalized more times than expected.");

			var step = finalizationSteps[finalizationStepIndex++];
			if (step.Output.Length > dst.Length)
				throw new InvalidOperationException("The configured finalization output does not fit in the destination buffer.");

			step.Output.AsSpan().CopyTo(dst);
			bytesConsumed = 0;
			bytesWritten = step.Output.Length;
			return step.Result;
		}
	}

	readonly struct FinalizationStep
	{
		public FinalizationStep(EncoderResult result, params byte[] output)
		{
			Result = result;
			Output = output;
		}

		public EncoderResult Result { get; }

		public byte[] Output { get; }
	}
}
