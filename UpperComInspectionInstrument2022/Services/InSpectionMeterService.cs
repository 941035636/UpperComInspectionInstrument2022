
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;
using System;
using System.Collections.Generic;

namespace UpperComInspectionInstrument2022.Services
{
    public class InspectionMeterService
    {
        private readonly ModbusRtuClient _client;

        public InspectionMeterService(ModbusRtuClient client)
        {
            _client = client;
        }

        /// <summary>
        /// 读取温度通道
        /// </summary>
   
        public List<InspectionChannelData> ReadTemperatures(
    byte slaveAddress,
    long acquisitionId)
        {
            var result =
                new List<InspectionChannelData>();

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

            for (int i = 0; i < 50; i++)
            {
                int registerIndex = i * 2;

                ushort register1 =
                    response.Registers[
                        registerIndex];

                ushort register2 =
                    response.Registers[
                        registerIndex + 1];

                ushort address1 =
                    (ushort)(startAddress + registerIndex);

                ushort address2 =
                    (ushort)(startAddress +
                             registerIndex + 1);

                byte[] raw =
                {
            (byte)(register1 >> 8),
            (byte)(register1 & 0xFF),

            (byte)(register2 >> 8),
            (byte)(register2 & 0xFF)
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
                    value =
                        ParseFloatBigEndian(raw);

                    if (IsDeviceSpecialValue(value))
                    {
                        dataStatus =
                            DataStatus.DeviceSpecialValue;

                        status =
                            "设备特殊值";
                    }
                    else if (!IsValidNumber(value))
                    {
                        dataStatus =
                            DataStatus.Invalid;

                        status =
                            "无效数据";
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
        /// 判断数值是否有效
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private bool IsValidNumber(double value)
        {
            if (double.IsNaN(value))
                return false;

            if (double.IsInfinity(value))
                return false;

            return true;
        }
        /// <summary>
        /// 标记特殊值
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private bool IsDeviceSpecialValue(
    double value)
        {
            // 当前根据实际设备测试结果处理。
            // -9999.9 是目前观察到的特殊值。

            if (Math.Abs(value + 9999.9) < 0.01)
                return true;

            return false;
        }
        /// <summary>
        /// 解析32位Float
        /// </summary>
        private double ParseFloatBigEndian(
            byte[] bytes)
        {
            if (bytes == null ||
                bytes.Length != 4)
            {
                throw new ArgumentException(
                    "Float数据必须为4字节");
            }

            // 这里暂时按照：
            //
            // AB CD EF GH
            //
            // 的标准大端字节顺序进行解析。
            //
            // 注意：
            // 目前没有传感器实际非零数据，
            // 所以暂时不能最终确认厂家字节序。

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
    }
}