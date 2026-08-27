using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ServerCore
{
    public class Listener
    {
        Socket _listenSocket;
        Func<Session> _sessionFactory;

        public void Init(IPEndPoint endPoint, Func<Session> sessionFactory, int register = 10, int backlog = 100)
        {
            _listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _sessionFactory = sessionFactory;

            _listenSocket.Bind(endPoint);

            // backlog : 최대 대기수
            _listenSocket.Listen(backlog);

            for (int i = 0; i < register; i++)
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();

                //이벤트 핸들러를 람다가 아닌 메서드로 연결 (GC 오버헤드 최소화)
                /*차이점 1: 힙 할당(GC)과 성능★
                메서드 직접 연결:
                컴파일러가 최적화를 잘해줍니다. 특히 OnAcceptCompleted가 인스턴스 메서드라면 this 객체의 메서드를 가리키는 대리자 객체를 한 번 만들고 재사용할 가능성이 높습니다.
                람다 방식:
                만약 람다 내부에서 외부 변수를 사용하면(Closure), 호출할 때마다 새로운 클래스 인스턴스가 힙 메모리에 할당됩니다. (Garbage 생성)
                외부 변수를 안 써도, 람다를 감싸는 대리자 객체가 매번 새로 생길 수 있습니다. 1초에 1만 번 연결이 들어오는 서버라면 불필요한 쓰레기를 1만 개 만드는 꼴입니다.*/

                // 굳이 new EventHandler<> 하지 않아도 컴파일러가 알아서 캐스팅해줍니다. (Syntactic Sugar)
                //args.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);
                args.Completed += OnAcceptCompleted;
                RegisterAccept(args);
            }
        }

        public void Stop()
        {
            _listenSocket.Close();
            _listenSocket.Dispose();
            _listenSocket = null;
        }

        void RegisterAccept(SocketAsyncEventArgs args)
        {
            args.AcceptSocket = null;

            try
            {
                bool pending = _listenSocket.AcceptAsync(args);
                if (pending == false)
                    OnAcceptCompleted(null, args);
            }
            catch (Exception e)
            {
                CoreLogger.Error("Net", e, "Accept failed.");
                // 예외가 발생해도 accept 슬롯을 재등록하여 영구 손실 방지
                RegisterAccept(args);
            }
        }

        void OnAcceptCompleted(object sender, SocketAsyncEventArgs args)
        {
            //서버가 종료될때 AcceptAsync가 OperationAborted나 Interrupted 에러를 반환할 수 있습니다. 이 경우는 무시하고 종료합니다.
            if (args.SocketError == SocketError.OperationAborted || args.SocketError == SocketError.Interrupted)
            {
                return;
            }

            try
            {
                if (args.SocketError == SocketError.Success)
                {
                    // RemoteEndPoint 를 Start 전에 읽어 둔다.
                    // Start 안의 RegisterRecv 가 동기 완료되면 그 자리에서 패킷이 처리되고,
                    // 조작된 패킷이면 Disconnect → 소켓 Dispose 까지 끝난 뒤 여기로 돌아온다.
                    // 그 상태에서 RemoteEndPoint 를 읽으면 ObjectDisposedException.
                    EndPoint remote = null;
                    try { remote = args.AcceptSocket.RemoteEndPoint; }
                    catch { }

                    Session session = _sessionFactory.Invoke();
                    session.Start(args.AcceptSocket);
                    session.OnConnected(remote);
                }
                else
                    CoreLogger.Warn("Net", "Accept socket error. Error={SocketError}", args.SocketError);
            }
            catch (Exception e)
            {
                CoreLogger.Error("Net", e, "Accept failed.");
            }

            RegisterAccept(args);
        }
    }
}
