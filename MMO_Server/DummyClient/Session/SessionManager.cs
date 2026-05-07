using System;
using System.Collections.Generic;

namespace DummyClient.Session
{
	public class SessionManager
	{
		public static SessionManager Instance { get; } = new SessionManager();

		HashSet<ServerSession> _sessions = new HashSet<ServerSession>();
		object _lock = new object();
		int _dummyId = 1;

		public int Count
		{
			get { lock (_lock) return _sessions.Count; }
		}

		public ServerSession Generate(int accountId, string token)
		{
			lock (_lock)
			{
				ServerSession session = new ServerSession();
				session.DummyId = _dummyId++;
				session.AccountId = accountId;
				session.Token = token;

				_sessions.Add(session);
				Console.WriteLine($"Connected ({_sessions.Count}) Players");
				return session;
			}
		}

		public void Remove(ServerSession session)
		{
			lock (_lock)
			{
				_sessions.Remove(session);
				Console.WriteLine($"Connected ({_sessions.Count}) Players");
			}
		}

		// HashSet 순서는 보장 안 되지만, 어차피 측정용이라 임의 N개를 끊어도 무방.
		// Disconnect → 서버가 OnDisconnected 처리 → Remove() 콜백으로 _sessions에서 빠짐.
		public void DisconnectN(int n)
		{
			List<ServerSession> targets = new List<ServerSession>(n);
			lock (_lock)
			{
				foreach (ServerSession s in _sessions)
				{
					if (targets.Count >= n) break;
					targets.Add(s);
				}
			}

			foreach (ServerSession s in targets)
				s.Disconnect();
		}
	}
}
