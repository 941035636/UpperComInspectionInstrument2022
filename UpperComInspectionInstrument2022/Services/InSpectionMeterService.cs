
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using System;
using System.Collections.Generic;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 巡检仪协议适配层：把 Modbus 寄存器转换为有业务含义的温度、湿度通道。
    /// 本类只负责寄存器地址、字节序、特殊值和通道角色，不负责页面显示与校准判定。
    /// </summary>
    public class InspectionMeterService
    {
        private readonly ModbusRtuClient _client;

        /// <summary>使用已经由应用外壳管理生命周期的 Modbus 客户端创建协议服务。</summary>
        public InspectionMeterService(ModbusRtuClient client)
        {
            _client = client;
        }

        /// <summary>
        /// 一次性读取 CH1～CH50 温度通道。
        /// 协议从寄存器 0x0001 开始，每个 IEEE 754 单精度值占两个寄存器。
        /// </summary>

        public List<InspectionChannelData> ReadTemperatures(
    byte slaveAddress,
    long acquisitionId)
        {
            var result =
                new List<InspectionChannelData>();

            // 50 个温度通道 × 每通道 2 个寄存器 = 一次读取 100 个寄存器。
            ushort startAddress = 0x0001;
            ushort quantity = 100;

            ModbusResponse response =
                _client.ReadHoldingRegisters(
                    slaveAddress,
                    startAddress,
                    quantity);

            if (!response.Success)
            {
                throw new Exception(
                    response.ErrorMessage);
            }

            DateTime timestamp =
                DateTime.Now;

            byte[] responseBytes = response.RawData
                ?? throw new InvalidOperationException("温度响应没有原始数据");

            // Modbus 响应帧格式：地址(1) + 功能码(1) + 字节数(1) + 数据区。
            // 协议规定 CH1 从 0x0001/0x0002 开始，每个温度占两个寄存器，
            // 因此温度 CH(i+1) 在原始帧中的数据偏移为 3 + i * 4。
            const int dataOffset = 3;
            const int bytesPerChannel = 4;

            if (responseBytes.Length < dataOffset + 50 * bytesPerChannel + 2)
                throw new InvalidOperationException("温度响应数据长度不足，无法解析 50 个通道");

            for (int i = 0; i < 50; i++)
            {
                int registerIndex = i * 2;
                int byteIndex = dataOffset + i * bytesPerChannel;

                ushort register1 =
                    (ushort)((responseBytes[byteIndex] << 8) |
                             responseBytes[byteIndex + 1]);

                ushort register2 =
                    (ushort)((responseBytes[byteIndex + 2] << 8) |
                             responseBytes[byteIndex + 3]);

                ushort address1 =
                    (ushort)(startAddress + registerIndex);

                ushort address2 =
                    (ushort)(startAddress +
                             registerIndex + 1);

                byte[] raw =
                {
                    responseBytes[byteIndex],
                    responseBytes[byteIndex + 1],
                    responseBytes[byteIndex + 2],
                    responseBytes[byteIndex + 3]
                };

                string rawHex =
                    BitConverter
                        .ToString(raw)
                        .Replace("-", " ");

                double value;

                DataStatus dataStatus;

                string status;

                try
                {
                    // 温度区每 4 字节按大端 IEEE 754 浮点数解析。
                    value =
                        DecodeFloatBigEndian(raw);

                    if (IsDeviceSpecialValue(value))
                    {
                        dataStatus =
                            DataStatus.DeviceSpecialValue;

                        status =
                            "设备特殊值";
                    }
                    else if (!IsValidTemperature(value))
                    {
                        dataStatus =
                            DataStatus.Invalid;

                        status =
                            "超出温度量程或字节序异常";
                    }
                    else
                    {
                        dataStatus =
                            DataStatus.Valid;

                        status =
                            "有效";
                    }
                }
                catch
                {
                    value = double.NaN;

                    dataStatus =
                        DataStatus.ParseError;

                    status =
                        "解析错误";
                }

                result.Add(
                    new InspectionChannelData
                    {
                        Channel = i + 1,

                        Type =
                            ChannelType.Temperature,

                        Role = ChannelRole.PrimaryTemperature,

                        Value = value,

                        Unit = "℃",

                        RegisterAddress1 =
                            address1,

                        RegisterAddress2 =
                            address2,

                        Register1 =
                            register1,

                        Register2 =
                            register2,

                        RawBytes = raw,

                        RawHex = rawHex,

                        DataStatus =
                            dataStatus,

                        Status = status,

                        Timestamp =
                            timestamp,

                        AcquisitionId =
                            acquisitionId,

                        IsValid =
                            dataStatus ==
                            DataStatus.Valid
                    });
            }

            return result;
        }

        /// <summary>
        /// 根据任务类型选择读取温度区、湿度区或两者。
        /// 温湿度模式使用两帧读取，并保留设备协议要求的帧间间隔。
        /// </summary>
        public List<InspectionChannelData> ReadMeasurements(
            string calibrationType,
            byte slaveAddress,
            long acquisitionId)
        {
            if (calibrationType == "湿度")
                return ReadHumidityChannels(slaveAddress, acquisitionId);

            if (calibrationType == "温度+湿度" || calibrationType == "温湿度")
            {
                List<InspectionChannelData> result = ReadTemperatures(slaveAddress, acquisitionId);
                // 协议及 Qt 原程序在两帧之间保留约 200 ms，避免巡检仪接收器连续帧处理不完整。
                System.Threading.Thread.Sleep(200);
                result.AddRange(ReadHumidityChannels(slaveAddress, acquisitionId));
                return result;
            }

            return ReadTemperatures(slaveAddress, acquisitionId);
        }

        /// <summary>
        /// 读取 0x0065～0x0078 共 20 个寄存器。
        /// 协议按“湿度1、伴随温度1、湿度2、伴随温度2……”交替排列，共形成 10 支湿度探头数据。
        /// </summary>
        private List<InspectionChannelData> ReadHumidityChannels(
            byte slaveAddress,
            long acquisitionId)
        {
            const ushort startAddress = 0x0065;
            const ushort quantity = 20;
            ModbusResponse response = _client.ReadHoldingRegisters(slaveAddress, startAddress, quantity);
            if (!response.Success)
                throw new InvalidOperationException(response.ErrorMessage ?? "读取湿度数据失败");

            var result = new List<InspectionChannelData>();
            DateTime timestamp = DateTime.Now;
            for (int i = 0; i < quantity; i++)
            {
                ushort rawRegister = response.Registers![i];
                double value = DecodeSignedHundredths(rawRegister);
                // 偶数索引是相对湿度，奇数索引是该湿度探头自己的温度补偿值。
                bool humidity = i % 2 == 0;
                bool valid = humidity
                    ? value is >= 0 and <= 100
                    : value is >= -100 and <= 200;
                result.Add(new InspectionChannelData
                {
                    Channel = i / 2 + 1,
                    Type = humidity ? ChannelType.Humidity : ChannelType.Temperature,
                    Role = humidity ? ChannelRole.Humidity : ChannelRole.HumidityProbeTemperature,
                    Value = value,
                    Unit = humidity ? "%RH" : "℃",
                    RegisterAddress1 = (ushort)(startAddress + i),
                    Register1 = rawRegister,
                    RawBytes = new[] { (byte)(rawRegister >> 8), (byte)(rawRegister & 0xFF) },
                    RawHex = $"{rawRegister:X4}",
                    DataStatus = valid ? DataStatus.Valid : DataStatus.Invalid,
                    Status = valid ? "有效" : "无效数据",
                    Timestamp = timestamp,
                    AcquisitionId = acquisitionId,
                    IsValid = valid

                });
            }
            return result;
        }
        /// <summary>
        /// 判断温度值是否为有限数且位于本系统支持的传感器总量程内。
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool IsValidTemperature(double value)
        {
            if (double.IsNaN(value))
                return false;

            if (double.IsInfinity(value))
                return false;

            // 覆盖常见热电阻及热电偶范围，同时拦截错误字节序产生的极大值/极小值。
            return value is >= -250 and <= 2000;
        }
        /// <summary>
        /// 判断设备是否返回了协议约定的断线或无效特殊值。
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool IsDeviceSpecialValue(
    double value)
        {
            // 当前根据实际设备测试结果处理。
            // -9999.9 是目前观察到的特殊值。

            if (Math.Abs(value + 9999.9) < 0.01)
                return true;

            return false;
        }
        /// <summary>
        /// 将 Modbus 数据区中 4 个大端字节解析为 IEEE 754 单精度浮点数。
        /// </summary>
        public static double DecodeFloatBigEndian(
            byte[] bytes)
        {
            if (bytes == null ||
                bytes.Length != 4)
            {
                throw new ArgumentException(
                    "Float数据必须为4字节");
            }

            // 协议和 Qt 原程序均按 Modbus 数据区的 4 字节顺序
            // AB CD EF GH 组成一个 32 位浮点值。
            // BitConverter 在 Windows 上按小端内存解释，因此这里反转
            // 后再转换，等价于 Qt 中 pMem[0]=byte[3] ... 的处理。

            byte[] temp =
            {
                bytes[0],
                bytes[1],
                bytes[2],
                bytes[3]
            };

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(temp);
            }

            return BitConverter.ToSingle(
                temp,
                0);
        }

        /// <summary>
        /// 解析湿度区域的有符号百分之一单位值。
        /// 设备在单个 Modbus 寄存器内部使用低字节在前，因此必须先交换两个字节。
        /// </summary>
        public static double DecodeSignedHundredths(ushort register)
        {
            // 该巡检仪的 0x0065～0x0078 区域虽然通过 Modbus 寄存器返回，
            // 但数值本身按 REG_DATA_1 的低字节、高字节顺序存放。
            // 例如现场响应 AC 02 应解释为 0x02AC，即 6.84，而不是 -215.02。
            ushort swapped = (ushort)((register >> 8) | (register << 8));
            return unchecked((short)swapped) / 100.0;
        }
    }
}
