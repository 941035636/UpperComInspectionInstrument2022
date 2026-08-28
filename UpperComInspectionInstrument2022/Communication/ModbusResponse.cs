using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpperComInspectionInstrument2022.Communication
{
  
    /// <summary>
    /// 一次 Modbus 读操作的统一返回结果。
    /// 调用方应先检查 <see cref="Success"/>，成功后再读取 <see cref="Registers"/>。
    /// </summary>
    public class ModbusResponse
    {
        /// <summary>通信、校验和协议解析是否全部成功。</summary>
        public bool Success { get; set; }

        /// <summary>失败时供界面和日志显示的原因。</summary>
        public string ? ErrorMessage { get; set; }

        /// <summary>设备返回的完整原始帧，便于排查通信问题。</summary>
        public byte[] ? RawData { get; set; }

        /// <summary>从响应数据区解析出的 16 位寄存器。</summary>
        public ushort[] ? Registers { get; set; }
    }
}
