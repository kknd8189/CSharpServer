using ServerCore;

namespace Server.Tests
{
	public class RecvBufferSpanTests
	{
		[Fact]
		public void NewBuffer_DataSizeIsZero()
		{
			using var buffer = new RecvBufferSpan(1024);

			Assert.Equal(0, buffer.DataSize);
		}

		[Fact]
		public void NewBuffer_FreeSizeEqualsCapacity()
		{
			using var buffer = new RecvBufferSpan(1024);

			Assert.Equal(1024, buffer.FreeSize);
		}

		[Fact]
		public void OnWrite_IncreasesDataSize()
		{
			using var buffer = new RecvBufferSpan(1024);

			buffer.OnWrite(100);

			Assert.Equal(100, buffer.DataSize);
			Assert.Equal(924, buffer.FreeSize);
		}

		[Fact]
		public void OnRead_DecreasesDataSize()
		{
			using var buffer = new RecvBufferSpan(1024);
			buffer.OnWrite(100);

			buffer.OnRead(60);

			Assert.Equal(40, buffer.DataSize);
		}

		[Fact]
		public void OnWrite_ExceedsFreeSize_ReturnsFalse()
		{
			using var buffer = new RecvBufferSpan(1024);

			Assert.False(buffer.OnWrite(2000));
		}

		[Fact]
		public void OnRead_ExceedsDataSize_ReturnsFalse()
		{
			using var buffer = new RecvBufferSpan(1024);
			buffer.OnWrite(50);

			Assert.False(buffer.OnRead(100));
		}

		[Fact]
		public void Clean_NoData_ResetsCursors()
		{
			using var buffer = new RecvBufferSpan(1024);
			buffer.OnWrite(100);
			buffer.OnRead(100);

			buffer.Clean();

			Assert.Equal(0, buffer.DataSize);
			Assert.Equal(1024, buffer.FreeSize);
		}

		[Fact]
		public void Clean_WithRemainingData_CompactsBuffer()
		{
			using var buffer = new RecvBufferSpan(1024);
			buffer.OnWrite(100);
			buffer.OnRead(60);

			buffer.Clean();

			Assert.Equal(40, buffer.DataSize);
			Assert.Equal(984, buffer.FreeSize);
		}

		[Fact]
		public void WriteSpan_HasCorrectLength()
		{
			using var buffer = new RecvBufferSpan(1024);
			buffer.OnWrite(200);

			Assert.Equal(824, buffer.WriteSpan.Length);
		}

		[Fact]
		public void ReadSpan_HasCorrectLength()
		{
			using var buffer = new RecvBufferSpan(1024);
			buffer.OnWrite(200);
			buffer.OnRead(50);

			Assert.Equal(150, buffer.ReadSpan.Length);
		}

		[Fact]
		public void WriteAndRead_DataIntegrity()
		{
			using var buffer = new RecvBufferSpan(1024);

			// Write some data
			var writeSpan = buffer.WriteSpan;
			writeSpan[0] = 0xAA;
			writeSpan[1] = 0xBB;
			writeSpan[2] = 0xCC;
			buffer.OnWrite(3);

			// Read and verify
			var readSpan = buffer.ReadSpan;
			Assert.Equal(0xAA, readSpan[0]);
			Assert.Equal(0xBB, readSpan[1]);
			Assert.Equal(0xCC, readSpan[2]);
		}

		[Fact]
		public void Clean_PreservesDataIntegrity()
		{
			using var buffer = new RecvBufferSpan(1024);

			// Write 10 bytes
			var writeSpan = buffer.WriteSpan;
			for (int i = 0; i < 10; i++)
				writeSpan[i] = (byte)(i + 1);
			buffer.OnWrite(10);

			// Read 5 bytes (discard first 5)
			buffer.OnRead(5);

			// Clean compacts the remaining 5 bytes
			buffer.Clean();

			// Remaining data should be bytes 6,7,8,9,10
			var readSpan = buffer.ReadSpan;
			Assert.Equal(5, readSpan.Length);
			Assert.Equal(6, readSpan[0]);
			Assert.Equal(10, readSpan[4]);
		}
	}
}
