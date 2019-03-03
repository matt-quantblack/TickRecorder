using System;
using System.IO;

namespace QBTickRecorder
{
    public class UserConfig
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public long AccountId { get; set; }
        public string DataPath { get; set; }

        public string Filename { get; set; }

        public SymbolCollection Symbols { get; set; }

        private void Init()
        {
            Token = "";
            RefreshToken = "";
            AccountId = 0;
            DataPath = "";

            Symbols = new SymbolCollection();
        }

        public UserConfig(string filename)
        {

            Init();

            Filename = filename;

            //loads the config file from the filepath
            string[] lines = File.ReadAllLines(filename);

            //stores the data as a dictionary
            foreach (string line in lines)
            {
                string[] parts = line.Split(new char[] { '=' });

                if (parts.Length != 2)
                    throw new InvalidDataException("Invalid user config.txt format. All lines must be in the format field=value");

                //requrired config lines include the auth and refresh token, cTrader accountid and datapath to store the tick data
                else if (parts[0] == "Token")
                    Token = parts[1];
                else if (parts[0] == "RefreshToken")
                    RefreshToken = parts[1];
                else if (parts[0] == "AccountId")
                    AccountId = Convert.ToInt64(parts[1]);
                else if (parts[0] == "DataPath")
                    DataPath = parts[1];

                //all other lines should be assets
                else
                {
                    int id = 0;
                    try
                    {
                        id = Convert.ToInt32(parts[1]);
                    }
                    catch (InvalidCastException)
                    {
                        throw new InvalidCastException("The symbol " + parts[0] + " has an invalid integer value. Must be in the format asset=id");
                    }

                    Symbols.Add(new Symbol(id, parts[0]));
                }
            }

            //check all required config fields are available
            if (Token == "")
                throw new MissingFieldException("Token is a required field in Config.txt but was not found.");
            if (RefreshToken == "")
                throw new MissingFieldException("RefreshToken is a required field in Config.txt but was not found.");
            if (DataPath == "")
                throw new MissingFieldException("DataPath is a required field in Config.txt but was not found.");

            
        }

        public void SaveToFile()
        {

            //converts this object to a string and saves to file
            string data = ToString();

            File.WriteAllText(Filename, data);
        }

        public override string ToString()
        {
            string val = "Token=" + Token + "\n" +
                "RefreshToken=" + RefreshToken + "\n" +
                "DataPath=" + DataPath + "\n" +
                "AccountId=" + AccountId + "\n";

            //write all the symbols
            foreach (Symbol symbol in Symbols)
                val += symbol.Name + "=" + symbol.Id + "\n";

            return val;
        }
    }
}
