
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
using DocumentFormat.OpenXml.Spreadsheet;
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
        /// <summary>测量数据到达软件的时间。</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>解析后的温度值，单位为 ℃。</summary>
        public double Temperature { get; set; }

        /// <summary>解析后的相对湿度值，单位为 %RH。</summary>
        public double Humidity { get; set; }

        /// <summary>解析前的原始报文文本，用于调试和追溯。</summary>
        public string RawData { get; set; } = string.Empty;

        /// <summary>报文格式和值域是否通过检查。</summary>
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// 早期文本协议解析器的占位类型。
    /// 当前巡检仪使用 Modbus RTU，实际解析由 <see cref="Services.InspectionMeterService"/> 完成。
    /// </summary>
    internal class InspectionMeterParser
    {


    }


    }
