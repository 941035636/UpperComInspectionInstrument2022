
/**************************************************************************
 巡检仪
                │
               RS232
                ↓
         SerialPortService
                ↓
           原始数据
                ↓
     InspectionMeterParser
                ↓
    InspectionMeterData
                ↓
       CalibrationViewModel
                ↓
    ┌───────────┴───────────┐
    ↓                       ↓
实时数据显示              数据缓存
                            ↓
                       稳定性判断
                            ↓
                        正式采样
                            ↓
                       校准数据表
                            ↓
                        数据计算
                            ↓
                        最终结果

******************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//把巡检仪的数据转换成程序可以识别的格式，解析数据，提取有用信息
namespace UpperComInspectionInstrument2022.Communication
{
    /// <summary>
    /// 数据模型类，表示巡检仪的测量数据，包括温度、湿度、时间戳等信息。
    /// </summary>
    public class InspectionMeterData
    {
        public DateTime Timestamp { get; set; }

        public double Temperature { get; set; }

        public double Humidity { get; set; }

        public string RawData { get; set; }

        public bool IsValid { get; set; }
    }
    internal class InspectionMeterParser
    {
    }
}
