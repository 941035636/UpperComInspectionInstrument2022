using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UpperComInspectionInstrument2022.Communication
{
    /// <summary>
    /// 早期设计保留的 Modbus 客户端占位类型。
    /// 当前运行代码直接使用 <see cref="ModbusRtuClient"/>；后续若需要支持多种通信实现，可将本类型改造成接口。
    /// </summary>
    internal class IModbusClient
    {
    }
}
