using System;
using System.Collections.Generic;
using System.Text;
using ServerCore;

namespace Server.Game
{
	struct JobTimerElem : IComparable<JobTimerElem>
	{
		public long execTick; // 실행 시간
		public IJob job;

		public int CompareTo(JobTimerElem other)
		{
            return other.execTick.CompareTo(execTick);
        }
	}

	public class JobTimer
	{
		PriorityQueue<JobTimerElem> _pq = new PriorityQueue<JobTimerElem>();
		object _lock = new object();

		public void Push(IJob job, int tickAfter = 0)
		{
			JobTimerElem jobElement;
			jobElement.execTick = System.Environment.TickCount64 + tickAfter;
			jobElement.job = job;

			lock (_lock)
			{
				_pq.Push(jobElement);
			}
		}

		public void Flush()
		{
			while (true)
			{
                long now = System.Environment.TickCount64;

                JobTimerElem jobElement;

				lock (_lock)
				{
					if (_pq.Count == 0)
						break;

					jobElement = _pq.Peek();
					if (jobElement.execTick > now)
						break;

					_pq.Pop();
				}

				jobElement.job.Execute();
			}
		}
	}
}
