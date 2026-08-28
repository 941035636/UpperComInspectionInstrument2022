using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//定义串口属性接口，打开串口，关闭串口，发送数据，接收数据，串口状态等方法
namespace UpperComInspectionInstrument2022.Communication
{
    /// <summary>
    /// 早期设计保留的串口服务占位类型。
    /// 当前串口的打开、读取和关闭由 <see cref="ModbusRtuClient"/> 负责。
    /// </summary>
    internal class ISerialportService
    {
    }
}
