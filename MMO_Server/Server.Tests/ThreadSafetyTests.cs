using Server.Game;

namespace Server.Tests
{
	public class ThreadSafetyTests
	{
		[Fact]
		public void SessionManager_ConcurrentGenerateAndRemove()
		{
			var manager = new SessionManager();
			int threadCount = 10;
			int sessionsPerThread = 100;
			var sessions = new System.Collections.Concurrent.ConcurrentBag<ClientSession>();
			var barrier = new Barrier(threadCount);

			// 여러 스레드에서 동시에 Generate
			var generateTasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < sessionsPerThread; i++)
				{
					var session = manager.Generate();
					sessions.Add(session);
				}
			})).ToArray();

			Task.WaitAll(generateTasks);

			Assert.Equal(threadCount * sessionsPerThread, manager.GetPlayerCount());

			// 여러 스레드에서 동시에 Remove
			var removeBarrier = new Barrier(threadCount);
			var sessionList = sessions.ToList();
			int chunkSize = sessionList.Count / threadCount;

			var removeTasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
			{
				removeBarrier.SignalAndWait();
				int start = t * chunkSize;
				int end = (t == threadCount - 1) ? sessionList.Count : start + chunkSize;
				for (int i = start; i < end; i++)
				{
					manager.Remove(sessionList[i]);
				}
			})).ToArray();

			Task.WaitAll(removeTasks);

			Assert.Equal(0, manager.GetPlayerCount());
		}

		[Fact]
		public void SessionManager_GenerateProducesUniqueIds()
		{
			var manager = new SessionManager();
			int threadCount = 10;
			int sessionsPerThread = 100;
			var ids = new System.Collections.Concurrent.ConcurrentBag<int>();
			var barrier = new Barrier(threadCount);

			var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < sessionsPerThread; i++)
				{
					var session = manager.Generate();
					ids.Add(session.SessionId);
				}
			})).ToArray();

			Task.WaitAll(tasks);

			// 모든 ID가 유니크한지 확인
			Assert.Equal(threadCount * sessionsPerThread, ids.Distinct().Count());
		}

		[Fact]
		public void JobSerializer_ConcurrentPush_SingleFlush()
		{
			var serializer = new JobSerializer();
			int threadCount = 10;
			int jobsPerThread = 1000;
			int counter = 0;
			var barrier = new Barrier(threadCount);

			// 여러 스레드에서 동시에 Push
			var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < jobsPerThread; i++)
				{
					serializer.Push(() => Interlocked.Increment(ref counter));
				}
			})).ToArray();

			Task.WaitAll(tasks);

			// 단일 스레드에서 Flush
			serializer.Flush();

			Assert.Equal(threadCount * jobsPerThread, counter);
		}

		[Fact]
		public void ServerMetrics_ConcurrentIncrements()
		{
			// 프로메테우스 카운터는 단조 증가라 리셋할 수 없다(초당 처리량은 rate()가
			// 쿼리 시점에 계산한다). 그래서 시작값을 찍어두고 증가분을 검증한다.
			double startRecv = ServerMetrics.PacketsReceivedValue;
			double startSent = ServerMetrics.PacketsSentValue;

			int threadCount = 10;
			int incrementsPerThread = 10000;
			var barrier = new Barrier(threadCount);

			var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < incrementsPerThread; i++)
				{
					ServerMetrics.IncrementPacketsReceived();
					ServerMetrics.IncrementPacketsSent();
				}
			})).ToArray();

			Task.WaitAll(tasks);

			double deltaRecv = ServerMetrics.PacketsReceivedValue - startRecv;
			double deltaSent = ServerMetrics.PacketsSentValue - startSent;

			Assert.Equal(threadCount * incrementsPerThread, deltaRecv);
			Assert.Equal(threadCount * incrementsPerThread, deltaSent);
		}

		[Fact]
		public void SessionManager_ConcurrentFindDuringGenerate()
		{
			var manager = new SessionManager();
			int generateCount = 500;
			int findThreads = 5;
			var barrier = new Barrier(1 + findThreads);
			bool running = true;

			// Generate 스레드
			var generateTask = Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < generateCount; i++)
				{
					manager.Generate();
				}
				Volatile.Write(ref running, false);
			});

			// Find 스레드들 - Generate 도중에 Find 호출
			var findTasks = Enumerable.Range(0, findThreads).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				while (Volatile.Read(ref running))
				{
					// 존재하지 않는 ID로 Find해도 예외 없이 null 반환해야 함
					var result = manager.Find(999999);
				}
			})).ToArray();

			Task.WaitAll(new[] { generateTask }.Concat(findTasks).ToArray());

			Assert.Equal(generateCount, manager.GetPlayerCount());
		}
	}
}
