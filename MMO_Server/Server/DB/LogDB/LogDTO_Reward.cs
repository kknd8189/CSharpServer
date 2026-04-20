using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.DB.LogDB
{
    public class Log_RewardDb
    {
        public int PlayerDbId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
