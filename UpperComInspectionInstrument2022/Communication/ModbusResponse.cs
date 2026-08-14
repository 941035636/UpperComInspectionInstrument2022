using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpperComInspectionInstrument2022.Communication
{
  
    public class ModbusResponse
    {
        public bool Success { get; set; }

        public string ? ErrorMessage { get; set; }

        public byte[] ? RawData { get; set; }

        public ushort[] ? Registers { get; set; }
    }
}
