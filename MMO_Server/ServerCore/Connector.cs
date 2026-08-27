using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerCore
{
	public class Connector
	{
		// UserToken에 Socket과 SessionFactory를 함께 전달하기 위한 컨텍스트
		// 인스턴스 필드 대신 각 연결마다 독립적인 컨텍스트를 사용하여 경합 방지
		class ConnectContext
		{
			public Socket Socket { get; }
			public Func<Session> SessionFactory { get; }

			public ConnectContext(Socket socket, Func<Session> sessionFactory)
			{
				Socket = socket;
				SessionFactory = sessionFactory;
			}
		}

		public void Connect(IPEndPoint endPoint, Func<Session> sessionFactory, int count = 1)
		{
			for (int i = 0; i < count; i++)
			{
				Socket socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

				SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += OnConnectCompleted;
				args.RemoteEndPoint = endPoint;
				args.UserToken = new ConnectContext(socket, sessionFactory);

				RegisterConnect(args);
			}
		}

		void RegisterConnect(SocketAsyncEventArgs args)
		{
			ConnectContext context = args.UserToken as ConnectContext;
			if (context == null)
				return;

			try
			{
				bool pending = context.Socket.ConnectAsync(args);
				if (pending == false)
					OnConnectCompleted(null, args);
			}
			catch (Exception e)
			{
				CoreLogger.Error("Net", e, "Connect failed.");
			}
		}

		void OnConnectCompleted(object sender, SocketAsyncEventArgs args)
		{
			try
			{
				if (args.SocketError == SocketError.Success)
				{
					ConnectContext context = args.UserToken as ConnectContext;
					Session session = context.SessionFactory.Invoke();
					session.Start(args.ConnectSocket);
					session.OnConnected(args.RemoteEndPoint);
				}
				else
				{
					CoreLogger.Warn("Net", "Connect socket error. Error={SocketError}", args.SocketError);
				}
			}
			catch (Exception e)
			{
				CoreLogger.Error("Net", e, "Connect failed.");
			}
			finally
			{
				// 연결 완료 후 더 이상 필요 없으므로 네이티브 리소스 해제
				args.UserToken = null;
				args.Dispose();
			}
		}
	}
}
