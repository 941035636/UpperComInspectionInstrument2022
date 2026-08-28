using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//负责具体调用串口，Com口、波特率设置
namespace UpperComInspectionInstrument2022.Communication
{
    /// <summary>
    /// 早期设计保留的串口实现占位类型。
    /// 该类型目前没有被业务代码调用，实际通信实现位于 <see cref="ModbusRtuClient"/>。
    /// </summary>
    internal class SerialPortService
    {
    }
}
