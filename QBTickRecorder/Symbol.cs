using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QBTickRecorder
{
    public class Symbol
    {
        public string Name { get; set; }
        public int Id { get; set; }

        public Symbol() { }

        public Symbol(int id, string name)
        {
            Name = name;
            Id = id;
        }
    }
}
