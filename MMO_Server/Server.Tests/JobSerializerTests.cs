using Server.Game;

namespace Server.Tests
{
	public class JobSerializerTests
	{
		[Fact]
		public void Push_And_Flush_ExecutesJob()
		{
			var serializer = new JobSerializer();
			int result = 0;

			serializer.Push(() => { result = 42; });
			serializer.Flush();

			Assert.Equal(42, result);
		}

		[Fact]
		public void Push_MultiplJobs_ExecutesInOrder()
		{
			var serializer = new JobSerializer();
			var results = new List<int>();

			serializer.Push(() => results.Add(1));
			serializer.Push(() => results.Add(2));
			serializer.Push(() => results.Add(3));
			serializer.Flush();

			Assert.Equal(new[] { 1, 2, 3 }, results);
		}

		[Fact]
		public void Flush_WithoutPush_DoesNothing()
		{
			var serializer = new JobSerializer();

			// Should not throw
			serializer.Flush();
		}

		[Fact]
		public void Push_WithParameters_ExecutesCorrectly()
		{
			var serializer = new JobSerializer();
			int result = 0;

			serializer.Push((int a, int b) => { result = a + b; }, 10, 20);
			serializer.Flush();

			Assert.Equal(30, result);
		}

		[Fact]
		public void CancelledJob_IsSkipped()
		{
			var serializer = new JobSerializer();
			int result = 0;

			serializer.Push(() => { result = 1; });
			var cancelledJob = new Job(() => { result = 999; });
			cancelledJob.Cancel = true;
			serializer.Push(cancelledJob);
			serializer.Push(() => { result += 1; });
			serializer.Flush();

			Assert.Equal(2, result);
		}

		[Fact]
		public void Flush_ClearsQueue()
		{
			var serializer = new JobSerializer();
			int counter = 0;

			serializer.Push(() => { counter++; });
			serializer.Flush();
			serializer.Flush(); // second flush should do nothing

			Assert.Equal(1, counter);
		}

		[Fact]
		public void Push_ThreeParameters_ExecutesCorrectly()
		{
			var serializer = new JobSerializer();
			string result = "";

			serializer.Push((string a, string b, string c) => { result = a + b + c; }, "Hello", " ", "World");
			serializer.Flush();

			Assert.Equal("Hello World", result);
		}
	}
}
