using System;

namespace Server.DB.LogDB
{
    public class Log_LoginDb
    {
        public int PlayerDbId { get; set; }
        public bool IsLogin { get; set; }
        public string IpAddress { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
