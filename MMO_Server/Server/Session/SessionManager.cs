using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Serilog;

namespace Server
{
	class SessionManager
	{
		static SessionManager _session = new SessionManager();
		public static SessionManager Instance { get { return _session; } }

		int _sessionId = 0;
		ConcurrentDictionary<int, ClientSession> _sessions = new ConcurrentDictionary<int, ClientSession>();

		public int GetBusyScore()
		{
			return _sessions.Count / 100;
		}

		public int GetPlayerCount()
		{
			return _sessions.Count;
		}

		public List<ClientSession> GetSessions()
		{
			return _sessions.Values.ToList();
		}

		public ClientSession Generate()
		{
			int sessionId = Interlocked.Increment(ref _sessionId);

			ClientSession session = new ClientSession();
			session.SessionId = sessionId;
			_sessions.TryAdd(sessionId, session);

			Log.Information("Player connected. Total={PlayerCount}", _sessions.Count);

			return session;
		}

		public ClientSession Find(int id)
		{
			_sessions.TryGetValue(id, out ClientSession session);
			return session;
		}

		public void Remove(ClientSession session)
		{
			_sessions.TryRemove(session.SessionId, out _);
			Log.Information("Player disconnected. Total={PlayerCount}", _sessions.Count);
		}
	}
}
