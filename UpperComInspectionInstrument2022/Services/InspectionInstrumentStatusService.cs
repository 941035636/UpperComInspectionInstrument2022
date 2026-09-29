using System;
using UpperComInspectionInstrument2022.Communication;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 读取巡检仪不会参与校准计算的辅助状态，例如剩余电量。
    /// 状态查询失败只影响状态显示，不应改变设备响应状态或中断实时采集。
    /// </summary>
    public sealed class InspectionInstrumentStatusService
    {
        private readonly ModbusRtuClient _client;

        public InspectionInstrumentStatusService(ModbusRtuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 读取保持寄存器 0x017F，并返回 0～100 的剩余电量百分比。
        /// 部分固件的单寄存器数据可能出现字节颠倒，因此在标准值无效时尝试交换高低字节。
        /// </summary>
        public int ReadRemainingBatteryPercentage(byte slaveAddress)
        {
            ModbusResponse response = _client.ReadHoldingRegisters(
                slaveAddress,
                InspectionInstrumentProtocol.RemainingBatteryLevelAddress,
                1,
                InspectionInstrumentProtocol.BatteryStatusResponseTimeoutMilliseconds,
                1);
            if (!response.Success)
                throw new InvalidOperationException(response.ErrorMessage ?? "读取巡检仪剩余电量失败");
            if (response.Registers is not { Length: > 0 })
                throw new InvalidOperationException("巡检仪剩余电量响应中没有寄存器数据");

            return DecodeRemainingBatteryPercentage(response.Registers[0]);
        }

        /// <summary>
        /// 把设备寄存器转换为百分比。标准 Modbus 字节序优先；仅在标准值超过100时尝试兼容字节交换。
        /// </summary>
        public static int DecodeRemainingBatteryPercentage(ushort registerValue)
        {
            if (registerValue <= 100) return registerValue;

            ushort swapped = (ushort)((registerValue >> 8) | (registerValue << 8));
            if (swapped <= 100) return swapped;

            throw new InvalidOperationException(
                $"巡检仪返回的剩余电量值 0x{registerValue:X4} 不在 0～100 范围内");
        }
    }
}
