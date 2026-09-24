using System;
using System.Threading;
using UpperComInspectionInstrument2022.Communication;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 管理巡检仪自身的传感器扫描状态。
    /// 串口打开、测量寄存器可读只表示通信链路可用；设备停止扫描时寄存器仍会保留最后值，
    /// 因此开始上位机采集前必须通过运行状态线圈确认下位机正在产生新数据。
    /// </summary>
    public sealed class InspectionInstrumentRunStateService
    {
        private readonly ModbusRtuClient _client;

        public InspectionInstrumentRunStateService(ModbusRtuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 若设备尚未扫描则发送启动命令，并在限定时间内轮询运行状态。
        /// 启动命令即使没有回显也继续读取状态，因为部分固件会执行线圈命令但不返回功能码05回显。
        /// </summary>
        public void EnsureStarted(byte slaveAddress)
        {
            ModbusResponse stateResponse = ReadRunningState(slaveAddress);
            if (IsRunning(stateResponse)) return;

            // 上次配置异常退出时设备可能仍处于配置模式。退出命令是状态设置，发送一次即可；
            // 不以回显作为启动前提，后续统一以只读运行状态作为最终判据。
            _client.WriteSingleCoil(
                slaveAddress,
                InspectionInstrumentProtocol.ConfigurationModeCoilAddress,
                false,
                InspectionInstrumentProtocol.ConfigurationControlResponseTimeoutMilliseconds);
            Thread.Sleep(InspectionInstrumentProtocol.ConfigurationRequestIntervalMilliseconds);

            ModbusResponse startResponse = _client.WriteSingleCoil(
                slaveAddress,
                InspectionInstrumentProtocol.StartAcquisitionCoilAddress,
                true,
                InspectionInstrumentProtocol.RunStateResponseTimeoutMilliseconds);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(
                InspectionInstrumentProtocol.StartConfirmationTimeoutMilliseconds);
            ModbusResponse lastStateResponse = stateResponse;
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(InspectionInstrumentProtocol.StartConfirmationPollIntervalMilliseconds);
                lastStateResponse = ReadRunningState(slaveAddress);
                if (IsRunning(lastStateResponse)) return;
            }

            string startDetails = startResponse.Success
                ? "启动命令已回显"
                : $"启动命令未回显：{startResponse.ErrorMessage}";
            string stateDetails = lastStateResponse.Success
                ? "设备运行状态仍为停止"
                : $"无法读取设备运行状态：{lastStateResponse.ErrorMessage}";
            throw new InvalidOperationException(
                $"巡检仪没有进入传感器采集状态，已禁止读取并保存寄存器缓存值。{startDetails}；{stateDetails}。" +
                "请确认巡检仪已退出通道配置界面、从站地址正确，然后重试。");
        }

        /// <summary>读取协议线圈 0x0000；只执行一次短事务，外层负责按启动时间窗口轮询。</summary>
        private ModbusResponse ReadRunningState(byte slaveAddress)
        {
            return _client.ReadCoils(
                slaveAddress,
                InspectionInstrumentProtocol.AcquisitionRunningStateCoilAddress,
                1,
                InspectionInstrumentProtocol.RunStateResponseTimeoutMilliseconds,
                1);
        }

        private static bool IsRunning(ModbusResponse response) =>
            response.Success && response.Coils is { Length: > 0 } && response.Coils[0];
    }
}
