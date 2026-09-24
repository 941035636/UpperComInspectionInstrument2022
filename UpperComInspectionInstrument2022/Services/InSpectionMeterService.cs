
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
    public class InspectionMeterService : IInspectionMeasurementReader
    {
        private readonly ModbusRtuClient _client;
        private bool _useCompatibleTemperatureBlock;
        private bool _useCompatibleHumidityBlock;

        /// <summary>使用已经由应用外壳管理生命周期的 Modbus 客户端创建协议服务。</summary>
        public InspectionMeterService(ModbusRtuClient client)
        {
            _client = client;
        }

        /// <summary>
        /// 从 CH1 开始读取本轮任务实际使用的连续温度通道。
        /// 协议从寄存器 0x0001 开始，每个 IEEE 754 单精度值占两个寄存器；
        /// 协议中 CH25～CH50 是当前设备未安装的预留容量，不参与日常采集。
        /// </summary>
        public List<InspectionChannelData> ReadTemperatures(
            byte slaveAddress,
            long acquisitionId,
            int channelCount)
        {
            var result = new List<InspectionChannelData>(channelCount);

            ushort startAddress = InspectionInstrumentProtocol.TemperatureStartAddress;
            ushort quantity = InspectionInstrumentProtocol.GetTemperatureRegisterQuantity(channelCount);

            ModbusResponse response = ReadWithCompatibleBlockFallback(
                slaveAddress,
                startAddress,
                quantity,
                InspectionInstrumentProtocol.CompatibleTemperatureBlockRegisterCount,
                "温度",
                ref _useCompatibleTemperatureBlock);

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

            if (responseBytes.Length < dataOffset + channelCount * bytesPerChannel + 2)
                throw new InvalidOperationException($"温度响应数据长度不足，无法解析 {channelCount} 个通道");

            for (int i = 0; i < channelCount; i++)
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
                        PhysicalChannel = i + 1,

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
            long acquisitionId,
            int temperatureChannelCount,
            int humidityChannelCount)
        {
            if (calibrationType == "湿度")
                return ReadHumidityChannels(slaveAddress, acquisitionId, humidityChannelCount);

            if (calibrationType == "温度+湿度" || calibrationType == "温湿度")
            {
                List<InspectionChannelData> result = ReadTemperatures(
                    slaveAddress,
                    acquisitionId,
                    temperatureChannelCount);
                // 新版固件在连续读取温度区后需要一定处理时间再准备湿度区。
                // 实测 200 ms 会出现温度/湿度响应交替丢帧，约 1 s 间隔可稳定返回完整帧。
                // 该等待只发生在同一轮温湿度读取内部，不改变用户设置的采样周期。
                System.Threading.Thread.Sleep(1000);
                result.AddRange(ReadHumidityChannels(slaveAddress, acquisitionId, humidityChannelCount));
                return result;
            }

            return ReadTemperatures(slaveAddress, acquisitionId, temperatureChannelCount);
        }

        /// <summary>
        /// 从 0x0065 开始读取本轮实际使用的湿度接口。
        /// 每个接口按“湿度值、伴随温度”占两个寄存器；第 10 组地址属于当前设备预留容量。
        /// </summary>
        private List<InspectionChannelData> ReadHumidityChannels(
            byte slaveAddress,
            long acquisitionId,
            int channelCount)
        {
            const ushort startAddress = InspectionInstrumentProtocol.HumidityStartAddress;
            ushort quantity = InspectionInstrumentProtocol.GetHumidityRegisterQuantity(channelCount);
            ModbusResponse response = ReadWithCompatibleBlockFallback(
                slaveAddress,
                startAddress,
                quantity,
                InspectionInstrumentProtocol.CompatibleHumidityBlockRegisterCount,
                "湿度",
                ref _useCompatibleHumidityBlock);
            if (!response.Success)
                throw new InvalidOperationException(response.ErrorMessage ?? "读取湿度数据失败");

            var result = new List<InspectionChannelData>(quantity);
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
                string status = valid
                    ? "有效"
                    : humidity
                        ? "湿度超出 0～100 %RH 有效范围"
                        : "湿度探头温度超出 -100～200 ℃ 有效范围";
                result.Add(new InspectionChannelData
                {
                    Channel = i / 2 + 1,
                    PhysicalChannel = i / 2 + 1,
                    Type = humidity ? ChannelType.Humidity : ChannelType.Temperature,
                    Role = humidity ? ChannelRole.Humidity : ChannelRole.HumidityProbeTemperature,
                    Value = value,
                    Unit = humidity ? "%RH" : "℃",
                    RegisterAddress1 = (ushort)(startAddress + i),
                    Register1 = rawRegister,
                    RawBytes = new[] { (byte)(rawRegister >> 8), (byte)(rawRegister & 0xFF) },
                    RawHex = $"{rawRegister:X4}",
                    DataStatus = valid ? DataStatus.Valid : DataStatus.Invalid,
                    Status = status,
                    Timestamp = timestamp,
                    AcquisitionId = acquisitionId,
                    IsValid = valid

                });
            }
            return result;
        }

        /// <summary>
        /// 优先按任务实际点数读取；可变长度请求无响应时，自动切换到 Qt 旧程序使用的固定协议块。
        /// 固定块一旦成功，本服务后续保持该兼容方式，避免每轮都先经历一次短帧超时。
        /// 调用方始终只解析任务要求的前 N 路，不会把预留通道加入矩阵、存储或校准计算。
        /// </summary>
        private ModbusResponse ReadWithCompatibleBlockFallback(
            byte slaveAddress,
            ushort startAddress,
            ushort requestedQuantity,
            ushort compatibleBlockQuantity,
            string channelName,
            ref bool useCompatibleBlock)
        {
            if (useCompatibleBlock)
            {
                return _client.ReadHoldingRegisters(
                    slaveAddress,
                    startAddress,
                    compatibleBlockQuantity);
            }

            ModbusResponse response = _client.ReadHoldingRegisters(
                slaveAddress,
                startAddress,
                requestedQuantity);
            if (response.Success || requestedQuantity >= compatibleBlockQuantity)
                return response;

            string requestedError = response.ErrorMessage ?? "设备未返回有效响应";
            System.Threading.Thread.Sleep(500);
            ModbusResponse fallback = _client.ReadHoldingRegisters(
                slaveAddress,
                startAddress,
                compatibleBlockQuantity);
            if (fallback.Success)
            {
                useCompatibleBlock = true;
                return fallback;
            }

            fallback.ErrorMessage =
                $"{channelName}按任务范围读取失败：{requestedError}；" +
                $"按固定协议块兼容读取仍失败：{fallback.ErrorMessage ?? "设备未返回有效响应"}";
            return fallback;
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
        /// 设备的常规数据是低字节在前，因此优先交换两个字节；部分设备/固件
        /// 在负数场景会按标准高字节在前返回，方法会在常规结果超出合理范围时
        /// 回退到未交换的有符号补码解释，确保负号不会被解析成正数。
        /// </summary>
        public static double DecodeSignedHundredths(ushort register)
        {
            // 当前设备使用的 0x0065～0x0076 区域通常按 REG_DATA_1 的低字节、
            // 高字节顺序存放。例如 0xAC02 应解释为 0x02AC，即 6.84，
            // 而不是 -215.02。
            ushort swapped = (ushort)((register >> 8) | (register << 8));
            double swappedValue = unchecked((short)swapped) / 100.0;
            double directValue = unchecked((short)register) / 100.0;

            // 湿度正常在 0～100 %RH；设备出现负湿度时仍要保留其符号，
            // 因此将 -100～100 作为有符号测量候选范围。优先常规字节序，
            // 只有常规结果明显不可能时才采用标准字节序结果。
            if (swappedValue is >= -100 and <= 100)
                return swappedValue;

            if (directValue is >= -100 and <= 100)
                return directValue;

            // 两种解释都超出范围时保留设备约定的常规字节序，交由上层标记异常，
            // 同时不篡改原始值，便于在原始记录中追溯报文。
            return swappedValue;
        }
    }
}
