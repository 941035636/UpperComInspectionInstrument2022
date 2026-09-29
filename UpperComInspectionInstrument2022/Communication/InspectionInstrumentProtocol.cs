using System;

namespace UpperComInspectionInstrument2022.Communication
{
    /// <summary>
    /// 当前巡检仪硬件和最新版 Modbus 协议共同确定的通道边界。
    /// 协议为后续型号预留了 50 路温度和 10 路湿度测量地址，
    /// 当前设备实际安装 24 路温度接口和 9 路湿度接口，因此业务界面和采集请求只使用实际接口。读取15个温度通道：01 03 00 01 00 1E 94 02
    /// </summary>
    public static class InspectionInstrumentProtocol
    {
        /// <summary>当前设备实际可用的主温度接口数量。</summary>
        public const int PhysicalTemperatureChannelCount = 24;

        /// <summary>当前设备实际可用的湿度探头接口数量。</summary>
        public const int PhysicalHumidityChannelCount = 9;

        /// <summary>协议为以后扩展型号预留的温度测量通道容量。</summary>
        public const int ReservedTemperatureChannelCapacity = 50;

        /// <summary>协议为以后扩展型号预留的湿度测量通道容量。</summary>
        public const int ReservedHumidityChannelCapacity = 10;

        /// <summary>第一个主温度通道的保持寄存器地址。</summary>
        public const ushort TemperatureStartAddress = 0x0001;

        /// <summary>第一个湿度探头的湿度寄存器地址。</summary>
        public const ushort HumidityStartAddress = 0x0065;

        /// <summary>温度物理通道 1～24 的传感器类型配置起始地址。</summary>
        public const ushort SensorTypeStartAddress = 0x0146;

        /// <summary>物理通道 1～33 的使能配置起始地址；25～33 对应湿度接口 H1～H9。</summary>
        public const ushort ChannelEnableStartAddress = 0x015E;

        /// <summary>巡检仪剩余电池电量保持寄存器；协议值按 0～100 的百分比显示。</summary>
        public const ushort RemainingBatteryLevelAddress = 0x017F;

        /// <summary>设备采集运行状态：0 表示停止，1 表示正在采集。</summary>
        public const ushort AcquisitionRunningStateCoilAddress = 0x0000;

        public const ushort ConfigurationModeCoilAddress = 0x0001;
        public const ushort SaveConfigurationCoilAddress = 0x0005;

        /// <summary>等效于巡检仪面板“启动”按钮的一次性线圈命令。</summary>
        public const ushort StartAcquisitionCoilAddress = 0x0010;

        /// <summary>等效于巡检仪面板“停止”按钮的一次性线圈命令。</summary>
        public const ushort StopAcquisitionCoilAddress = 0x0011;

        /// <summary>读取运行状态及启动命令回显所使用的短超时。</summary>
        public const int RunStateResponseTimeoutMilliseconds = 800;

        /// <summary>
        /// 电量变化缓慢，状态读取只尝试一次短事务；失败不应打断实时测量。
        /// </summary>
        public const int BatteryStatusResponseTimeoutMilliseconds = 800;

        /// <summary>发送启动命令后等待设备恢复传感器扫描的最长时间。</summary>
        public const int StartConfirmationTimeoutMilliseconds = 5000;

        /// <summary>启动确认期间相邻状态检查之间的间隔。</summary>
        public const int StartConfirmationPollIntervalMilliseconds = 250;

        public const int ConfigurableSensorTypeChannelCount = 24;
        public const int ConfigurableEnableChannelCount = 33;

        /// <summary>
        /// 配置区相邻事务之间的安静间隔。底层每次请求已经独立清理迟到帧，
        /// 这里保留250 ms给设备处理，不再用1 s人为放慢整个配置页面。
        /// </summary>
        public const int ConfigurationRequestIntervalMilliseconds = 250;

        /// <summary>配置分段单次读取超时；由配置调度器负责只补读失败分段。</summary>
        public const int ConfigurationReadResponseTimeoutMilliseconds = 1200;

        /// <summary>
        /// 配置分段最多执行的读取轮次。配置响应不回显起始地址，必须为迟到测量帧污染和丢帧
        /// 留出补读机会；每轮仍只访问尚未完成的地址。
        /// </summary>
        public const int ConfigurationReadRoundCount = 4;

        /// <summary>
        /// 合法配置响应所需确认次数。业务值范围校验已经能排除测量浮点帧；只要求一次合法响应，
        /// 避免为了重复确认把配置请求量翻倍，并在设备仅偶发响应时误判整次读取失败。
        /// </summary>
        public const int ConfigurationReadConfirmationCount = 1;

        /// <summary>
        /// 超出业务范围的配置值必须连续出现两次才认定为设备中的稳定异常值；
        /// 单次非法值仍按迟到测量帧丢弃。
        /// </summary>
        public const int InvalidConfigurationReadConfirmationCount = 2;

        /// <summary>配置补读下一轮前给设备恢复处理能力的间隔。</summary>
        public const int ConfigurationReadRoundDelayMilliseconds = 600;

        /// <summary>
        /// 串口助手实机确认稳定返回的配置分段大小。
        /// </summary>
        public const int ConfigurationFallbackChunkRegisterCount = 4;

        /// <summary>配置控制线圈经常执行但不回显，缩短单次回显等待，避免维护窗口被等待时间耗尽。</summary>
        public const int ConfigurationControlResponseTimeoutMilliseconds = 750;

        /// <summary>功能码06单寄存器写入的短回显等待；最终成功以功能码03读回为准。</summary>
        public const int ConfigurationWriteResponseTimeoutMilliseconds = 900;

        /// <summary>写入配置寄存器后等待设备刷新内部配置的时间。</summary>
        public const int ConfigurationWriteSettleMilliseconds = 250;

        /// <summary>保存配置命令写入后，留给设备固化非易失配置的等待时间。</summary>
        public const int SaveConfigurationCommitDelayMilliseconds = 2000;

        /// <summary>一个 32 位温度值占用的 16 位寄存器数量。</summary>
        public const int TemperatureRegistersPerChannel = 2;

        /// <summary>一个湿度接口占用“湿度值、伴随温度”两个 16 位寄存器。</summary>
        public const int HumidityRegistersPerChannel = 2;

        /// <summary>
        /// Qt 旧程序和巡检仪固件长期使用的固定温度数据块长度。
        /// 即使当前硬件只有 24 个温度接口，协议响应区仍预留 50 路、共 100 个寄存器。
        /// </summary>
        public const ushort CompatibleTemperatureBlockRegisterCount =
            ReservedTemperatureChannelCapacity * TemperatureRegistersPerChannel;

        /// <summary>
        /// Qt 旧程序和巡检仪固件长期使用的固定湿度数据块长度。
        /// 当前硬件使用前 9 组，第 10 组保留，但兼容请求仍读取完整 20 个寄存器。
        /// </summary>
        public const ushort CompatibleHumidityBlockRegisterCount =
            ReservedHumidityChannelCapacity * HumidityRegistersPerChannel;

        /// <summary>
        /// 根据本轮实际温度测点数计算功能码 03 的寄存器数量。
        /// 例如 9 个温度通道对应 18 个寄存器，而不是读取全部 100 个预留寄存器。
        /// </summary>
        public static ushort GetTemperatureRegisterQuantity(int channelCount)
        {
            ValidateChannelCount(channelCount, PhysicalTemperatureChannelCount, "温度");
            return checked((ushort)(channelCount * TemperatureRegistersPerChannel));
        }

        /// <summary>根据本轮实际湿度测点数计算“湿度值、伴随温度”寄存器数量。</summary>
        public static ushort GetHumidityRegisterQuantity(int channelCount)
        {
            ValidateChannelCount(channelCount, PhysicalHumidityChannelCount, "湿度");
            return checked((ushort)(channelCount * HumidityRegistersPerChannel));
        }

        /// <summary>确保请求数量落在当前硬件实际接口范围内。</summary>
        private static void ValidateChannelCount(int channelCount, int maximum, string channelName)
        {
            if (channelCount < 1 || channelCount > maximum)
                throw new ArgumentOutOfRangeException(
                    nameof(channelCount),
                    channelCount,
                    $"{channelName}通道数量必须在 1～{maximum} 之间。");
        }
    }
}
