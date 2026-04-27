using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedDB.Redis
{
    public static class RedisAuth
    {
        //accountServer가 호출할 함수
        public static bool SaveSessionToken(int accountId, string token)
        {
            var db = RedisManager.Instance.GetDatabase();
            string key = $"Session:{accountId}";

            return db.StringSet(key, token, TimeSpan.FromSeconds(300));
        }

        //GameServer가 C_Auth 패킷을 받았을 때 호출할 함수
        public static bool VerifyToken(int accountId, string clientToken)
        {
            var db = RedisManager.Instance.GetDatabase();
            string key = $"Session:{accountId}";
            string storedToken = db.StringGet(key);

            //Redis에 저장된 토큰이 있고, 클라이언트가 보낸 것과 일치 하면 통과
            if(storedToken != null && storedToken == clientToken)
            {
                db.KeyDelete(key);
                return true;
            }

            return false;
        }
    }
}
