using System.Collections.Generic;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 定义“一次读取一组巡检仪通道”的最小协议边界。
    /// 生产环境由 <see cref="InspectionMeterService"/> 实现，自动测试可替换为不依赖串口的模拟读取器。
    /// </summary>
    public interface IInspectionMeasurementReader
    {
        /// <summary>
        /// 按校准类型读取一组完整通道数据；通信或协议解析失败时抛出异常。
        /// </summary>
        List<InspectionChannelData> ReadMeasurements(string calibrationType, byte slaveAddress, long acquisitionId);
    }
}
