using System;

namespace UpperComInspectionInstrument2022.Models
{
    /// <summary>
    /// 通道类型
    /// </summary>
    public enum ChannelType
    {
        /// <summary>温度量，单位通常为 ℃。</summary>
        Temperature,
        /// <summary>相对湿度量，单位通常为 %RH。</summary>
        Humidity
    }

    /// <summary>
    /// 同为温度单位的数据可能来自主温度通道，也可能是湿度探头的伴随温度。
    /// 校准矩阵、通道修正和结果计算必须按角色区分，不能只按单位区分。
    /// </summary>
    public enum ChannelRole
    {
        /// <summary>参与温度布点和温度结果计算的主温度通道。</summary>
        PrimaryTemperature,
        /// <summary>参与湿度布点和湿度结果计算的相对湿度通道。</summary>
        Humidity,
        /// <summary>湿度探头内部用于补偿的伴随温度，不计入主温度测点。</summary>
        HumidityProbeTemperature
    }

    /// <summary>
    /// 数据状态
    /// 注意：这里描述的是“通信数据是否可用”
    /// 不代表校准是否合格。
    /// </summary>
    public enum DataStatus
    {
        /// <summary>值可用于显示和后续计算。</summary>
        Valid,
        /// <summary>值未通过一般有效性检查。</summary>
        Invalid,
        /// <summary>寄存器或字节数据无法按协议解析。</summary>
        ParseError,
        /// <summary>设备返回断线、超量程等约定的特殊值。</summary>
        DeviceSpecialValue
    }

    /// <summary>
    /// 巡检仪单个通道数据
    /// </summary>
    public class InspectionChannelData
    {
        /// <summary>
        /// 通道号
        /// </summary>
        public int Channel { get; set; }

        /// <summary>
        /// 通道类型
        /// </summary>
        public ChannelType Type { get; set; }

        /// <summary>该通道在校准业务中的角色。</summary>
        public ChannelRole Role { get; set; } = ChannelRole.PrimaryTemperature;

        /// <summary>
        /// 测量值
        /// </summary>
        public double Value { get; set; }

        /// <summary>应用证书修正前的巡检仪原始测量值。</summary>
        public double RawValue { get; set; }

        /// <summary>本任务标准器快照中配置的通道修正值。</summary>
        public double CorrectionValue { get; set; }

        /// <summary>当前值是否已经叠加标准器证书中的通道修正值。</summary>
        public bool HasAppliedCorrection { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// 第一个16位寄存器地址
        /// </summary>
        public ushort RegisterAddress1 { get; set; }

        /// <summary>
        /// 第二个16位寄存器地址
        /// </summary>
        public ushort RegisterAddress2 { get; set; }


        /// <summary>
        /// 第一个寄存器原始值
        /// </summary>
        public ushort Register1 { get; set; }

        /// <summary>
        /// 第二个寄存器原始值
        /// </summary>
        public ushort Register2 { get; set; }

        /// <summary>
        /// 原始4字节数据
        /// </summary>
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 原始HEX字符串
        /// 例如：42 F6 CC CD
        /// </summary>
        public string RawHex { get; set; } = string.Empty;

        /// <summary>
        /// 数据状态
        /// </summary>
        public DataStatus DataStatus { get; set; }

        /// <summary>
        /// 状态文字
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 本次采集时间
        /// </summary>
        public DateTime Timestamp { get; set; }




        /// <summary>
        /// 所属采集批次
        /// </summary>
        public long AcquisitionId { get; set; }

        /// <summary>
        /// 是否可以进入后续业务计算
        ///
        /// 注意：
        /// true 不代表校准合格。
        /// 只代表通信层面数据有效。
        /// </summary>
        public bool IsValid { get; set; }
    }
}
