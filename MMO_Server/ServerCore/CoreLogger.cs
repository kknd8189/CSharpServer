using System;
using System.Text;

namespace ServerCore
{
    public enum CoreLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    // ServerCore 는 외부 의존성 0 을 유지한다 — Serilog 같은 특정 구현에 묶이면
    // 이 라이브러리를 쓰는 쪽(Server / DummyClient)이 모두 그 패키지를 끌고 와야 한다.
    // 대신 Sink 델리게이트만 노출하고, 호스트가 시작 시 자기 로거를 꽂는다.
    //
    // 메시지를 문자열로 미리 합치지 않고 템플릿 + 인자를 그대로 넘기는 게 핵심이다.
    // Serilog 가 {Size} 같은 자리표시자를 구조화 프로퍼티로 색인해야
    // Kibana 에서 "Size > 10240" 같은 필드 검색과 집계가 가능해진다.
    // 여기서 string.Format 을 해버리면 ES 에는 문자열 한 덩어리만 남는다.
    public static class CoreLogger
    {
        // (level, category, exception, messageTemplate, args)
        // category 는 EventType 으로 승격되어 Kibana 필터에 쓰인다. (Net / Abuse / Session)
        public static Action<CoreLogLevel, string, Exception, string, object[]> Sink;

        public static void Info(string category, string template, params object[] args)
        {
            Write(CoreLogLevel.Info, category, null, template, args);
        }

        public static void Warn(string category, string template, params object[] args)
        {
            Write(CoreLogLevel.Warning, category, null, template, args);
        }

        public static void Error(string category, Exception ex, string template, params object[] args)
        {
            Write(CoreLogLevel.Error, category, ex, template, args);
        }

        static void Write(CoreLogLevel level, string category, Exception ex, string template, object[] args)
        {
            // 지역 변수로 캡처 — 호출 도중 Sink 가 교체/해제돼도 NRE 가 나지 않도록
            Action<CoreLogLevel, string, Exception, string, object[]> sink = Sink;

            if (sink != null)
            {
                // 로깅 실패가 게임 로직이나 IOCP 콜백을 죽이면 안 된다.
                try { sink(level, category, ex, template, args); }
                catch { }
                return;
            }

            // Sink 미주입(라이브러리 단독 사용 / 호스트 초기화 전) 폴백
            Console.WriteLine("[" + level + "][" + category + "] " + Render(template, args)
                              + (ex != null ? Environment.NewLine + ex : string.Empty));
        }

        // 폴백 전용 최소 렌더러. {Name} 자리표시자를 args 순서대로 치환한다.
        // 실제 구조화 색인은 Sink 쪽(Serilog)이 하므로 여기서는 사람이 읽을 정도면 충분하다.
        static string Render(string template, object[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(template))
                return template;

            StringBuilder sb = new StringBuilder(template.Length + 32);
            int argIndex = 0;

            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] != '{')
                {
                    sb.Append(template[i]);
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                sb.Append(argIndex < args.Length ? (args[argIndex]?.ToString() ?? "null") : "?");
                argIndex++;
                i = close;
            }

            return sb.ToString();
        }
    }
}
