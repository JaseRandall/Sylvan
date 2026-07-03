using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sylvan.IO;

/// <summary>
/// A stream that encodes data written to it.
/// </summary>
public sealed class EncoderStream : Stream
{
	readonly bool ownsStream;
	readonly Stream stream;
	readonly Encoder encoder;
	readonly byte[] buffer;
	bool isClosed;
	int bufferIdx;

	/// <summary>
	/// Creates a new non-owning <see cref="EncoderStream"/>.
	/// </summary>
	/// <param name="stream">The underlying stream to write to. The stream remains open when this instance is disposed.</param>
	/// <param name="encoder">The encoder to use to write to the stream.</param>
	/// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="encoder"/> is <see langword="null"/>.</exception>
	public EncoderStream(Stream stream, Encoder encoder)
		: this(stream, encoder, false)
	{
	}

	/// <summary>
	/// Creates a new <see cref="EncoderStream"/> with configurable ownership of the underlying stream.
	/// </summary>
	/// <param name="stream">The underlying stream to write to.</param>
	/// <param name="encoder">The encoder to use to write to the stream.</param>
	/// <param name="ownsStream">
	/// <see langword="true"/> to dispose <paramref name="stream"/> after all encoded output has been finalized;
	/// otherwise, <see langword="false"/> to leave it open.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="encoder"/> is <see langword="null"/>.</exception>
	public EncoderStream(Stream stream, Encoder encoder, bool ownsStream)
	{
		this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
		this.encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
		this.buffer = new byte[0x1000];
		this.ownsStream = ownsStream;
	}

	/// <inheritdoc/>
	public override bool CanRead => false;

	/// <inheritdoc/>
	public override bool CanSeek => false;

	/// <inheritdoc/>
	public override bool CanWrite => true;

	/// <inheritdoc/>
	public override long Length => throw new NotSupportedException();

	/// <inheritdoc/>
	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <inheritdoc/>
	public override void Flush()
	{
		if (bufferIdx == 0)
			return;

		this.stream.Write(this.buffer, 0, bufferIdx);
		bufferIdx = 0;
	}

	/// <inheritdoc/>
	public override async Task FlushAsync(CancellationToken cancel)
	{
		if (bufferIdx == 0)
			return;

#if NETSTANDARD2_1 || NETCOREAPP3_0_OR_GREATER
		await this.stream.WriteAsync(this.buffer.AsMemory().Slice(0, bufferIdx), cancel).ConfigureAwait(false);
#else
		await this.stream.WriteAsync(this.buffer, 0, bufferIdx, cancel).ConfigureAwait(false);
#endif
		bufferIdx = 0;
	}

	/// <inheritdoc/>
	public override int Read(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	/// <inheritdoc/>
	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotSupportedException();
	}

	/// <inheritdoc/>
	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	EncoderResult Encode(byte[] buffer, ref int offset, ref int count)
	{
		var src = buffer.AsSpan().Slice(offset, count);
		var dst = this.buffer.AsSpan().Slice(bufferIdx);
		var result = this.encoder.Encode(src, dst, out var srcCount, out var dstCount);

		offset += srcCount;
		count -= srcCount;
		this.bufferIdx += dstCount;
		return result;
	}

	void FinalizeEncoding()
	{
		while (true)
		{
			var dst = this.buffer.AsSpan().Slice(bufferIdx);
			var result = this.encoder.Encode(ReadOnlySpan<byte>.Empty, dst, out _, out var dstCount);
			this.bufferIdx += dstCount;

			switch (result)
			{
				case EncoderResult.RequiresOutputSpace:
				case EncoderResult.Flush:
					Flush();
					break;
				case EncoderResult.Complete:
					Flush();
					return;
				default:
					throw new InvalidOperationException($"The encoder returned {result} while finalizing its output.");
			}
		}
	}

	/// <inheritdoc/>
	public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		while (count > 0)
		{
			var result = Encode(buffer, ref offset, ref count);
			if (result == EncoderResult.RequiresOutputSpace)
			{
				await FlushAsync(cancellationToken).ConfigureAwait(false);
			}
		}
	}

	/// <inheritdoc/>
	public override void Write(byte[] buffer, int offset, int count)
	{
		while (count > 0)
		{
			var result = Encode(buffer, ref offset, ref count);
			if (result == EncoderResult.RequiresOutputSpace)
			{
				Flush();
			}
		}
	}

	/// <summary>
	/// Releases the resources used by this stream after finalizing all pending encoder output.
	/// </summary>
	/// <param name="disposing"><see langword="true"/> to release managed resources; otherwise, <see langword="false"/>.</param>
	protected override void Dispose(bool disposing)
	{
		try
		{
			if (disposing && isClosed == false)
			{
				isClosed = true;
				try
				{
					FinalizeEncoding();
				}
				finally
				{
					if (ownsStream)
						this.stream.Dispose();
				}
			}
		}
		finally
		{
			base.Dispose(disposing);
		}
	}
}
