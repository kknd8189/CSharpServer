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
	}
}
