using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QBTickRecorder
{
    public class SymbolCollection : List<Symbol>
    {
        public SymbolCollection() { }

        public SymbolCollection(string accountId, string filepath)
        {

            filepath += accountId + "\\symbols.txt";

            if (File.Exists(filepath))
            {
                string[] lines = File.ReadAllLines(filepath);


                //stores the data as a dictionary
                foreach (string line in lines)
                {
                    string[] parts = line.Split(new char[] { '=' });

                    if (parts.Length != 2)
                        throw new InvalidDataException("Invalid symbols.txt format. All lines must be in the format asset=id");

                    int id = 0;
                    try
                    {
                        id = Convert.ToInt32(parts[1]);
                    }
                    catch (InvalidCastException)
                    {
                        throw new InvalidCastException("The symbol " + parts[0] + " has an invalid integer value. Must be in the format asset=id");
                    }

                    Add(new Symbol(id, parts[0]));

                }
            }

        }

        public void SaveToFile(string accountId, string filepath=@"local\")
        {
            filepath += accountId + "symbols.txt";

            string data = "";
            foreach(Symbol symbol in this)
            {
                data += symbol.Name + "=" + symbol.Id + "\n";
            }

            File.WriteAllText(filepath, data);
        }

        public string SymbolName(int id)
        {
            //Helper function to lookup a symbol by its id and return its name
            Symbol symbol = this.Where(x => x.Id == id).FirstOrDefault();
            if (symbol != null)
                return symbol.Name;

            return null;
        }
    }
}
