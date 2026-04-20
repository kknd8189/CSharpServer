using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Server.Data
{
	[Serializable]
	public class ServerConfig
	{
		public string dataPath;
		public string connectionString;
		public string worldName;
		public string redisConnectionString;
        public int port;
    }

    public class ConfigManager
	{
		public static ServerConfig Config { get; private set; }

		public static void LoadConfig()
		{
			string text = File.ReadAllText("../../../../../Common/config.json");
			Config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(text);
		}
	}
}
