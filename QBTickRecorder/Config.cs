using System;
using System.IO;

namespace QBTickRecorder
{
    public class Config
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string ApiHost { get; set; }
        public int ApiPort { get; set; }

        private void Init()
        {
            ClientId = "";
            ClientSecret = "";
            ApiHost = "";
            ApiPort = 0;
        }

        public Config() { Init(); }

        public Config(string filepath)
        {
            Init();

            filepath += "config.txt";

            //loads the config file from the filepath
            string[] lines = File.ReadAllLines(filepath);

            //stores the data as a dictionary
            foreach (string line in lines)
            {
                string[] parts = line.Split(new char[] { '=' });

                if (parts.Length != 2)
                    throw new InvalidDataException("Invalid config.txt format. All lines must be in the format field=value");

                if (parts[0] == "ClientId")
                    ClientId = parts[1];
                else if (parts[0] == "ClientSecret")
                    ClientSecret = parts[1];               
                else if (parts[0] == "ApiHost")
                    ApiHost = parts[1];
                else if (parts[0] == "ApiPort")
                    ApiPort = Convert.ToInt32(parts[1]);
                
            }

            //check all required config fields are available
            if (ClientId == "")
                throw new MissingFieldException("ClientId is a required field in Config.txt but was not found.");
            if (ClientSecret == "")
                throw new MissingFieldException("ClientSecret is a required field in Config.txt but was not found.");            
            if (ApiHost == "")
                throw new MissingFieldException("ApiHost is a required field in Config.txt but was not found.");
            if (ApiPort == 0)
                throw new MissingFieldException("ApiPort is a required field in Config.txt but was not found.");
            
        }

        public void SaveToFile(string filepath=@"local\")
        {
            filepath += "config.txt";

            //converts this object to a string and saves to file
            string data = ToString();
            File.WriteAllText(filepath, data);
        }

        public override string ToString()
        {
            string val = "ClientId=" + ClientId + "\n" +
                "ClientSecret=" + ClientSecret + "\n" +                
                "ApiHost=" + ApiHost + "\n" +
                "ApiPort=" + ApiPort;

            return val;
        }

    }
}
