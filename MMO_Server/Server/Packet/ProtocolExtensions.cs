using System.Reflection;

namespace Protocol
{
    // Protobuf의 MergeFrom(ISource) 대체.
    // 생성된 패킷/구조체 POCO들은 모두 public field만 쓰므로 리플렉션으로 전체 복사.
    // 핫패스가 아니므로(로그인/인벤토리 동기화 등) 성능보다 범용성 우선.
    public static class ProtocolExtensions
    {
        public static T MergeFrom<T>(this T dst, T src) where T : class
        {
            if (dst == null || src == null) return dst;
            foreach (FieldInfo f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(dst, f.GetValue(src));
            return dst;
        }
    }
}
