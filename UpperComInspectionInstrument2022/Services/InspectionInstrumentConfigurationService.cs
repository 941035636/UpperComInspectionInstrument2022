using DocumentFormat.OpenXml.Office2010.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UpperComInspectionInstrument2022.Communication;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>巡检仪物理通道配置快照。</summary>
    public sealed class InspectionInstrumentConfiguration
    {
        public ushort[] TemperatureSensorTypes { get; init; } = Array.Empty<ushort>();
        public bool[] ChannelEnabled { get; init; } = Array.Empty<bool>();

        /// <summary>温度传感器类型区在无法从零值判断时采用的默认字节序。</summary>
        public bool SensorTypeRegistersUseLowByteFirst { get; init; }

        /// <summary>通道使能区在无法从零值判断时采用的默认字节序。</summary>
        public bool EnableRegistersUseLowByteFirst { get; init; }

        /// <summary>每个温度传感器类型寄存器实际检测到或继承的字节序。</summary>
        public bool[] SensorTypeRegisterLowByteFirst { get; init; } = Array.Empty<bool>();

        /// <summary>每个通道使能寄存器实际检测到或继承的字节序。</summary>
        public bool[] EnableRegisterLowByteFirst { get; init; } = Array.Empty<bool>();

        /// <summary>当前设备实际可用的配置寄存器分段大小。</summary>
        public int ConfigurationChunkRegisterCount { get; init; } =
            InspectionInstrumentProtocol.ConfigurationFallbackChunkRegisterCount;

        /// <summary>控制线圈未回显但配置数据事务成功时，保留给操作人员的风险提示。</summary>
        public string CommunicationWarning { get; set; } = string.Empty;

        /// <summary>保存配置后是否已经通过线圈 0x0000 确认设备恢复传感器扫描。</summary>
        public bool DeviceRunningConfirmed { get; set; }

        /// <summary>本次保存是否因直接写入失败而使用过配置模式兼容路径。</summary>
        public bool UsedCompatibilityConfigurationMode { get; set; }

        /// <summary>设备读取到的温度类型配置原始寄存器值，用于识别并安全修复稳定异常值。</summary>
        public ushort[] SensorTypeRegisterRawValues { get; init; } = Array.Empty<ushort>();

        /// <summary>设备读取到的通道使能配置原始寄存器值。</summary>
        public ushort[] EnableRegisterRawValues { get; init; } = Array.Empty<ushort>();

        /// <summary>温度类型寄存器中超出0～13范围且重复确认的通道。</summary>
        public bool[] InvalidSensorTypeRegisters { get; init; } = Array.Empty<bool>();

        /// <summary>使能寄存器中不是0/1且重复确认的通道。</summary>
        public bool[] InvalidEnableRegisters { get; init; } = Array.Empty<bool>();

        public bool HasInvalidRegisters =>
            InvalidSensorTypeRegisters.Any(value => value) || InvalidEnableRegisters.Any(value => value);

        /// <summary>
        /// 同一配置区域内的非零寄存器理应使用一致的读回字节序。
        /// 少数寄存器与区域主字节序相反时，通常表示旧版上位机曾把读回字节序错误地用于功能码06写入：
        /// 界面虽然能把 0x0001 和 0x0100 都解码成逻辑值1，但巡检仪内部可能实际保存成256。
        /// </summary>
        public bool HasNonCanonicalRegisterOrder =>
            TemperatureSensorTypes.Select((value, index) => new { value, index })
                .Any(item => item.value != 0 &&
                             SensorTypeRegisterLowByteFirst[item.index] != SensorTypeRegistersUseLowByteFirst) ||
            ChannelEnabled.Select((value, index) => new { value, index })
                .Any(item => item.value &&
                             EnableRegisterLowByteFirst[item.index] != EnableRegistersUseLowByteFirst);

        /// <summary>是否存在必须通过重新写入消除的非法值或可疑旧字节序值。</summary>
        public bool HasRegistersNeedingRepair => HasInvalidRegisters || HasNonCanonicalRegisterOrder;

        /// <summary>通道使能区是否存在需要重新写入修复的非法值或可疑旧字节序值。</summary>
        public bool HasEnableRegistersNeedingRepair =>
            InvalidEnableRegisters.Any(value => value) ||
            ChannelEnabled.Select((value, index) => new { value, index })
                .Any(item => item.value &&
                             EnableRegisterLowByteFirst[item.index] != EnableRegistersUseLowByteFirst);
    }

    /// <summary>开始测量前读取的33路通道使能状态，不访问温度传感器类型区。</summary>
    public sealed class InspectionInstrumentEnableState
    {
        public bool[] ChannelEnabled { get; init; } = Array.Empty<bool>();
        public ushort[] RawRegisters { get; init; } = Array.Empty<ushort>();
        public bool[] InvalidRegisters { get; init; } = Array.Empty<bool>();
        public bool[] RegisterLowByteFirst { get; init; } = Array.Empty<bool>();
        public string CommunicationWarning { get; init; } = string.Empty;
    }

    /// <summary>
    /// 通道配置写入事务的可判定失败。写前通信核对失败且尚未发出任何配置写命令时，
    /// <see cref="CanRetryWithCurrentSnapshot"/> 为 true，界面可以保留用户编辑和读取快照直接重试。
    /// </summary>
    public sealed class InspectionInstrumentConfigurationWriteException : InvalidOperationException
    {
        public InspectionInstrumentConfigurationWriteException(
            string message,
            bool canRetryWithCurrentSnapshot,
            bool deviceMayHaveChanged,
            Exception innerException)
            : base(message, innerException)
        {
            CanRetryWithCurrentSnapshot = canRetryWithCurrentSnapshot;
            DeviceMayHaveChanged = deviceMayHaveChanged;
        }

        public bool CanRetryWithCurrentSnapshot { get; }
        public bool DeviceMayHaveChanged { get; }
    }

    /// <summary>
    /// 按最新版协议读取和写入传感器类型、通道使能配置。
    /// 读取使用可断点补读的功能码03小分段；写入默认使用功能码06单寄存器并逐项读回，
    /// 只有固件拒绝直接写入时才进入配置模式兼容路径，避免无条件停止设备扫描。
    /// </summary>
    public sealed class InspectionInstrumentConfigurationService
    {
        private readonly ModbusRtuClient _client;

        public InspectionInstrumentConfigurationService(ModbusRtuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public InspectionInstrumentConfiguration Read(
            byte slaveAddress,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunkRegisterCount = InspectionInstrumentProtocol.ConfigurationFallbackChunkRegisterCount;
            // 实机串口助手证明配置保持寄存器可直接用功能码03读取。
            // 读取本身不切换配置模式，避免无关的功能码05命令清空或扰乱正在返回的读响应。
            ushort[] rawSensorTypes = ReadConfigurationRegisters(
                slaveAddress,
                InspectionInstrumentProtocol.SensorTypeStartAddress,
                InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount,
                "读取温度通道传感器类型",
                chunkRegisterCount,
                13,
                cancellationToken);

            WaitForDeviceConfigurationCycle(cancellationToken);
            ushort[] rawEnableRegisters = ReadConfigurationRegisters(
                slaveAddress,
                InspectionInstrumentProtocol.ChannelEnableStartAddress,
                InspectionInstrumentProtocol.ConfigurableEnableChannelCount,
                "读取通道使能状态",
                chunkRegisterCount,
                1,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            DecodedConfigurationBlock sensorTypeBlock = DecodeConfigurationBlock(
                rawSensorTypes,
                13,
                "温度传感器类型");
            DecodedConfigurationBlock enableBlock = DecodeConfigurationBlock(
                rawEnableRegisters,
                1,
                "通道使能");
            return new InspectionInstrumentConfiguration
            {
                TemperatureSensorTypes = sensorTypeBlock.Values,
                ChannelEnabled = enableBlock.Values.Select(value => value == 1).ToArray(),
                SensorTypeRegistersUseLowByteFirst = sensorTypeBlock.DefaultLowByteFirst,
                EnableRegistersUseLowByteFirst = enableBlock.DefaultLowByteFirst,
                SensorTypeRegisterLowByteFirst = sensorTypeBlock.LowByteFirstByRegister,
                EnableRegisterLowByteFirst = enableBlock.LowByteFirstByRegister,
                SensorTypeRegisterRawValues = rawSensorTypes,
                EnableRegisterRawValues = rawEnableRegisters,
                InvalidSensorTypeRegisters = sensorTypeBlock.InvalidRegisters,
                InvalidEnableRegisters = enableBlock.InvalidRegisters,
                ConfigurationChunkRegisterCount = chunkRegisterCount,
                CommunicationWarning = BuildInvalidConfigurationWarning(
                    sensorTypeBlock.InvalidRegisters,
                    rawSensorTypes,
                    enableBlock.InvalidRegisters,
                    rawEnableRegisters)
            };
        }

        /// <summary>
        /// 只读取33路通道使能，用于开始实时测量前核对下位机本机界面是否覆盖了上位机配置。
        /// </summary>
        public InspectionInstrumentEnableState ReadChannelEnabled(
            byte slaveAddress,
            CancellationToken cancellationToken = default)
        {
            ushort[] rawEnableRegisters = ReadConfigurationRegisters(
                slaveAddress,
                InspectionInstrumentProtocol.ChannelEnableStartAddress,
                InspectionInstrumentProtocol.ConfigurableEnableChannelCount,
                "检查通道使能状态",
                InspectionInstrumentProtocol.ConfigurationFallbackChunkRegisterCount,
                1,
                cancellationToken);
            DecodedConfigurationBlock enableBlock = DecodeConfigurationBlock(
                rawEnableRegisters,
                1,
                "通道使能");
            return new InspectionInstrumentEnableState
            {
                ChannelEnabled = enableBlock.Values.Select(value => value == 1).ToArray(),
                RawRegisters = rawEnableRegisters,
                InvalidRegisters = enableBlock.InvalidRegisters,
                RegisterLowByteFirst = enableBlock.LowByteFirstByRegister,
                CommunicationWarning = BuildInvalidConfigurationWarning(
                    Array.Empty<bool>(),
                    Array.Empty<ushort>(),
                    enableBlock.InvalidRegisters,
                    rawEnableRegisters)
            };
        }

        /// <summary>
        /// 只读取任务绑定实际需要维护的33路通道使能，并转换成配置事务快照。
        /// 当前设备只支持在本机统一设置传感器类型，上位机不再访问或修改逐通道类型寄存器。
        /// </summary>
        public InspectionInstrumentConfiguration ReadChannelConfiguration(
            byte slaveAddress,
            CancellationToken cancellationToken = default)
        {
            InspectionInstrumentEnableState state = ReadChannelEnabled(slaveAddress, cancellationToken);
            int sensorCount = InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount;
            return new InspectionInstrumentConfiguration
            {
                TemperatureSensorTypes = new ushort[sensorCount],
                ChannelEnabled = state.ChannelEnabled,
                SensorTypeRegisterLowByteFirst = new bool[sensorCount],
                EnableRegisterLowByteFirst = state.RegisterLowByteFirst,
                SensorTypeRegisterRawValues = new ushort[sensorCount],
                EnableRegisterRawValues = state.RawRegisters,
                InvalidSensorTypeRegisters = new bool[sensorCount],
                InvalidEnableRegisters = state.InvalidRegisters,
                EnableRegistersUseLowByteFirst = GetDefaultOrder(state.RegisterLowByteFirst),
                ConfigurationChunkRegisterCount = InspectionInstrumentProtocol.ConfigurationFallbackChunkRegisterCount,
                CommunicationWarning = state.CommunicationWarning
            };
        }

        public InspectionInstrumentConfiguration WriteAndSave(
            byte slaveAddress,
            InspectionInstrumentConfiguration current,
            IReadOnlyList<ushort> sensorTypes,
            IReadOnlyList<bool> channelEnabled)
        {
            return WriteAndSaveCore(slaveAddress, current, sensorTypes, channelEnabled, true);
        }

        /// <summary>
        /// 只写入并核对33路通道使能。设备传感器类型由下位机统一设置，本方法不会访问
        /// 0x0146～0x015D，也不会因该区域存在旧值而尝试修复。
        /// </summary>
        public InspectionInstrumentConfiguration WriteChannelEnabledAndSave(
            byte slaveAddress,
            InspectionInstrumentConfiguration current,
            IReadOnlyList<bool> channelEnabled)
        {
            return WriteAndSaveCore(
                slaveAddress,
                current,
                current.TemperatureSensorTypes,
                channelEnabled,
                false);
        }

        private InspectionInstrumentConfiguration WriteAndSaveCore(
            byte slaveAddress,
            InspectionInstrumentConfiguration current,
            IReadOnlyList<ushort> sensorTypes,
            IReadOnlyList<bool> channelEnabled,
            bool updateSensorTypeRegisters)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (sensorTypes.Count != InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount)
                throw new ArgumentException("温度传感器类型必须包含 24 个通道。", nameof(sensorTypes));
            if (channelEnabled.Count != InspectionInstrumentProtocol.ConfigurableEnableChannelCount)
                throw new ArgumentException("通道使能必须包含 33 个通道。", nameof(channelEnabled));
            if (sensorTypes.Any(value => value > 13))
                throw new ArgumentException("传感器类型代码必须在 0～13 之间。", nameof(sensorTypes));
            ValidateSnapshot(current);

            ushort[] requestedEnable = channelEnabled.Select(value => value ? (ushort)1 : (ushort)0).ToArray();
            ushort[] currentEnable = current.ChannelEnabled.Select(value => value ? (ushort)1 : (ushort)0).ToArray();
            bool[] sensorRegistersToRepair = updateSensorTypeRegisters
                ? current.InvalidSensorTypeRegisters
                    .Select((invalid, index) =>
                        invalid ||
                        (current.TemperatureSensorTypes[index] != 0 &&
                         current.SensorTypeRegisterLowByteFirst[index] != current.SensorTypeRegistersUseLowByteFirst))
                    .ToArray()
                : new bool[InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount];
            bool[] enableRegistersToRepair = current.InvalidEnableRegisters
                .Select((invalid, index) =>
                    invalid ||
                    (current.ChannelEnabled[index] &&
                     current.EnableRegisterLowByteFirst[index] != current.EnableRegistersUseLowByteFirst))
                .ToArray();
            bool hasChanges = (updateSensorTypeRegisters &&
                               !current.TemperatureSensorTypes.SequenceEqual(sensorTypes)) ||
                              !currentEnable.SequenceEqual(requestedEnable) ||
                              (updateSensorTypeRegisters && sensorRegistersToRepair.Any(value => value)) ||
                              enableRegistersToRepair.Any(value => value);
            if (!hasChanges) return current;

            bool[] sensorOrders = current.SensorTypeRegisterLowByteFirst.ToArray();
            bool[] enableOrders = current.EnableRegisterLowByteFirst.ToArray();
            bool configurationModeEntered = false;
            List<string> warnings = new();
            InspectionInstrumentConfiguration? savedConfiguration = null;
            Exception? operationFailure = null;
            bool writePhaseStarted = false;
            string restartError = string.Empty;
            try
            {
                // 在第一次写入前核对所有待变更地址，避免用户读取快照后又在设备面板修改配置时，
                // 上位机用过期快照覆盖设备端的新设置。预检必须全部通过后才允许开始写入。
                if (updateSensorTypeRegisters)
                {
                    ValidateChangedRegistersAgainstSnapshot(
                        slaveAddress,
                        InspectionInstrumentProtocol.SensorTypeStartAddress,
                        current.TemperatureSensorTypes,
                        current.SensorTypeRegisterRawValues,
                        sensorRegistersToRepair,
                        sensorTypes,
                        sensorOrders,
                        13,
                        "温度传感器类型");
                }
                ValidateChangedRegistersAgainstSnapshot(
                    slaveAddress,
                    InspectionInstrumentProtocol.ChannelEnableStartAddress,
                    currentEnable,
                    current.EnableRegisterRawValues,
                    enableRegistersToRepair,
                    requestedEnable,
                    enableOrders,
                    1,
                    "通道使能");

                // 协议仅声明配置寄存器可读写，并未要求写入前必须进入配置模式。
                // 默认使用功能码06逐项写入并读回；只有收到明确 Modbus 异常响应时才降级进入配置模式。
                // 超时、丢帧或读回失败属于“结果不确定”，此时切换模式只会扩大设备停止扫描的风险。
                writePhaseStarted = true;
                if (updateSensorTypeRegisters)
                {
                    WriteChangedRegistersAndVerify(
                        slaveAddress,
                        InspectionInstrumentProtocol.SensorTypeStartAddress,
                        current.TemperatureSensorTypes,
                        sensorTypes,
                        sensorRegistersToRepair,
                        sensorOrders,
                        current.SensorTypeRegistersUseLowByteFirst,
                        13,
                        "温度传感器类型",
                        ref configurationModeEntered,
                        warnings);
                }
                WriteChangedRegistersAndVerify(
                    slaveAddress,
                    InspectionInstrumentProtocol.ChannelEnableStartAddress,
                    currentEnable,
                    requestedEnable,
                    enableRegistersToRepair,
                    enableOrders,
                    current.EnableRegistersUseLowByteFirst,
                    1,
                    "通道使能",
                    ref configurationModeEntered,
                    warnings);

                // 保存是触发命令，不进行盲目重发。配置寄存器已经逐项读回一致；
                // 即使设备不返回功能码05回显，也只记录持久化风险，不重复触发保存。
                WaitForDeviceConfigurationCycle();
                ModbusResponse saveResponse = _client.WriteSingleCoil(
                    slaveAddress,
                    InspectionInstrumentProtocol.SaveConfigurationCoilAddress,
                    true,
                    InspectionInstrumentProtocol.ConfigurationControlResponseTimeoutMilliseconds);
                if (!saveResponse.Success)
                    warnings.Add("保存命令未回显；当前运行配置已读回一致，但断电保持需在设备重启后核对");
                Thread.Sleep(InspectionInstrumentProtocol.SaveConfigurationCommitDelayMilliseconds);

                savedConfiguration = new InspectionInstrumentConfiguration
                {
                    TemperatureSensorTypes = updateSensorTypeRegisters
                        ? sensorTypes.ToArray()
                        : current.TemperatureSensorTypes.ToArray(),
                    ChannelEnabled = channelEnabled.ToArray(),
                    SensorTypeRegistersUseLowByteFirst = GetDefaultOrder(sensorOrders),
                    EnableRegistersUseLowByteFirst = GetDefaultOrder(enableOrders),
                    SensorTypeRegisterLowByteFirst = sensorOrders,
                    EnableRegisterLowByteFirst = enableOrders,
                    SensorTypeRegisterRawValues = updateSensorTypeRegisters
                        ? sensorTypes.Select((value, index) =>
                                EncodeConfigurationRegister(value, sensorOrders[index]))
                            .ToArray()
                        : current.SensorTypeRegisterRawValues.ToArray(),
                    EnableRegisterRawValues = requestedEnable
                        .Select((value, index) => EncodeConfigurationRegister(value, enableOrders[index]))
                        .ToArray(),
                    InvalidSensorTypeRegisters = updateSensorTypeRegisters
                        ? new bool[sensorTypes.Count]
                        : current.InvalidSensorTypeRegisters.ToArray(),
                    InvalidEnableRegisters = new bool[channelEnabled.Count],
                    ConfigurationChunkRegisterCount = current.ConfigurationChunkRegisterCount,
                    UsedCompatibilityConfigurationMode = configurationModeEntered
                };
            }
            catch (Exception ex)
            {
                operationFailure = ex;
            }
            finally
            {
                if (configurationModeEntered)
                {
                    WaitForDeviceConfigurationCycle();
                    ModbusResponse exitResponse = _client.WriteSingleCoil(
                        slaveAddress,
                        InspectionInstrumentProtocol.ConfigurationModeCoilAddress,
                        false,
                        InspectionInstrumentProtocol.ConfigurationControlResponseTimeoutMilliseconds);
                    if (!exitResponse.Success && savedConfiguration != null)
                        warnings.Add("兼容配置模式退出命令未回显");
                }

                // 无论保存成功还是中途失败，都尽力恢复设备扫描。配置结果与运行恢复分阶段处理；
                // 失败路径也不能把巡检仪留在停止状态，工作台仍会再次执行运行门禁。
                bool deviceRunningConfirmed = false;
                try
                {
                    WaitForDeviceConfigurationCycle();
                    new InspectionInstrumentRunStateService(_client).EnsureStarted(slaveAddress);
                    deviceRunningConfirmed = true;
                }
                catch (Exception ex)
                {
                    restartError = ex.Message;
                }

                if (savedConfiguration != null)
                {
                    savedConfiguration.DeviceRunningConfirmed = deviceRunningConfirmed;
                    if (!deviceRunningConfirmed)
                        warnings.Add($"配置已保存，但设备未确认恢复采集：{restartError}");
                    savedConfiguration.CommunicationWarning = string.Join("；", warnings);
                }
            }

            if (operationFailure != null)
            {
                string recoveryText = string.IsNullOrWhiteSpace(restartError)
                    ? "失败后已确认设备恢复采集。"
                    : $"失败后设备也未确认恢复采集：{restartError}";
                bool canRetryWithCurrentSnapshot = !writePhaseStarted &&
                                                   operationFailure is ConfigurationPreflightCommunicationException;
                bool deviceMayHaveChanged = writePhaseStarted ||
                                            operationFailure is ConfigurationSnapshotChangedException;
                throw new InspectionInstrumentConfigurationWriteException(
                    $"{operationFailure.Message}；{recoveryText}",
                    canRetryWithCurrentSnapshot,
                    deviceMayHaveChanged,
                    operationFailure);
            }

            return savedConfiguration ??
                throw new InvalidOperationException("配置事务未生成有效结果，请重新读取设备配置后再试。");
        }

        /// <summary>
        /// 写入前核对全部待变更地址。设备值等于原快照表示可以继续，等于目标值表示设备端已完成同样修改；
        /// 若三者均不一致，则判定读取快照已经过期并在任何写入发生前终止事务。
        /// </summary>
        private void ValidateChangedRegistersAgainstSnapshot(
            byte slaveAddress,
            ushort startAddress,
            IReadOnlyList<ushort> snapshot,
            IReadOnlyList<ushort> snapshotRawValues,
            IReadOnlyList<bool> invalidSnapshotRegisters,
            IReadOnlyList<ushort> requested,
            bool[] lowByteFirstByRegister,
            ushort maximumValue,
            string valueName)
        {
            for (int index = 0; index < requested.Count; index++)
            {
                bool forceRepair = invalidSnapshotRegisters[index];
                if (snapshot[index] == requested[index] && !forceRepair) continue;
                ushort address = checked((ushort)(startAddress + index));

                if (forceRepair)
                {
                    if (!TryReadRawRegisterWithRetries(slaveAddress, address, out ushort actualRawValue, out string rawError))
                        throw new ConfigurationPreflightCommunicationException(
                            $"写入前无法核对{valueName}异常地址 0x{address:X4}：{rawError}。本次尚未修改设备。");
                    if (actualRawValue == snapshotRawValues[index]) continue;

                    bool order = lowByteFirstByRegister[index];
                    if (!TryDecodeConfigurationValue(actualRawValue, maximumValue, ref order, out ushort repairedActualValue) ||
                        repairedActualValue != requested[index])
                        throw new ConfigurationSnapshotChangedException(
                            $"{valueName}异常地址 0x{address:X4} 已在设备端变化：读取快照原始值为 " +
                            $"0x{snapshotRawValues[index]:X4}，设备当前为 0x{actualRawValue:X4}。请重新读取后再修复。");
                    lowByteFirstByRegister[index] = order;
                    continue;
                }

                if (!TryReadLogicalValueWithRetries(
                        slaveAddress,
                        address,
                        maximumValue,
                        ref lowByteFirstByRegister[index],
                        valueName,
                        out ushort actualValue,
                        out string error))
                    throw new ConfigurationPreflightCommunicationException(
                        $"写入前无法核对{valueName}地址 0x{address:X4}：{error}。为避免部分写入，本次尚未修改设备。");

                if (actualValue != snapshot[index] && actualValue != requested[index])
                    throw new ConfigurationSnapshotChangedException(
                        $"{valueName}地址 0x{address:X4} 已在设备端变化：读取快照为 {snapshot[index]}，" +
                        $"设备当前为 {actualValue}，待写入为 {requested[index]}。请重新读取配置后再修改。");
            }
        }

        /// <summary>
        /// 只处理与读取快照不同的地址。每个地址使用功能码06写一次并用功能码03读回；
        /// 写回显不是成功依据，读回业务值一致才算成功。直接写失败时才进入配置模式兼容旧固件。
        /// </summary>
        private void WriteChangedRegistersAndVerify(
            byte slaveAddress,
            ushort startAddress,
            IReadOnlyList<ushort> current,
            IReadOnlyList<ushort> requested,
            IReadOnlyList<bool> forceWriteRegisters,
            bool[] lowByteFirstByRegister,
            bool expectedReadLowByteFirst,
            ushort maximumValue,
            string valueName,
            ref bool configurationModeEntered,
            List<string> warnings)
        {
            if (lowByteFirstByRegister.Length != requested.Count || current.Count != requested.Count)
                throw new InvalidOperationException("配置快照与待写入数据数量不一致，已停止写入。");

            for (int index = 0; index < requested.Count; index++)
            {
                if (current[index] == requested[index] && !forceWriteRegisters[index]) continue;
                ushort address = checked((ushort)(startAddress + index));
                if (TryWriteAndVerifyRegister(
                        slaveAddress,
                        address,
                        requested[index],
                        maximumValue,
                        forceWriteRegisters[index],
                        expectedReadLowByteFirst,
                        ref lowByteFirstByRegister[index],
                        valueName,
                        out string directError,
                        out bool directWriteExplicitlyRejected))
                    continue;

                if (!directWriteExplicitlyRejected)
                    throw new InvalidOperationException(
                        $"{valueName}地址 0x{address:X4} 的直接写入结果无法确认：{directError}。" +
                        "为保护设备，程序未进入配置模式；请重新读取配置确认实际状态。");

                if (!configurationModeEntered)
                {
                    ModbusResponse enterResponse = _client.WriteSingleCoil(
                        slaveAddress,
                        InspectionInstrumentProtocol.ConfigurationModeCoilAddress,
                        true,
                        InspectionInstrumentProtocol.ConfigurationControlResponseTimeoutMilliseconds);
                    configurationModeEntered = true;
                    if (!enterResponse.Success)
                        warnings.Add("设备拒绝直接写入后已发送兼容配置模式命令，但该命令未回显");
                    WaitForDeviceConfigurationCycle();
                }

                if (!TryWriteAndVerifyRegister(
                        slaveAddress,
                        address,
                        requested[index],
                        maximumValue,
                        forceWriteRegisters[index],
                        expectedReadLowByteFirst,
                        ref lowByteFirstByRegister[index],
                        valueName,
                        out string compatibilityError,
                        out _))
                    throw new InvalidOperationException(
                        $"{valueName}地址 0x{address:X4} 写入失败。" +
                        $"直接写入：{directError}；兼容配置模式：{compatibilityError}");
            }
        }

        /// <summary>幂等地设置一个配置寄存器，并把最多两次写入都夹在实际读回检查之间。</summary>
        private bool TryWriteAndVerifyRegister(
            byte slaveAddress,
            ushort address,
            ushort expectedValue,
            ushort maximumValue,
            bool forceWrite,
            bool expectedReadLowByteFirst,
            ref bool lowByteFirst,
            string valueName,
            out string error,
            out bool explicitlyRejected)
        {
            error = string.Empty;
            explicitlyRejected = false;
            for (int writeAttempt = 1; writeAttempt <= 2; writeAttempt++)
            {
                if (!forceWrite && TryReadLogicalRegister(
                        slaveAddress,
                        address,
                        expectedValue,
                        maximumValue,
                        false,
                        expectedReadLowByteFirst,
                        ref lowByteFirst,
                        valueName,
                        out error))
                    return true;

                // 功能码06的寄存器值始终按标准 Modbus 大端顺序发送。
                // 读取响应中的兼容字节序只用于解码，不能反向套用到写帧。
                ushort encodedValue = EncodeConfigurationWriteValue(expectedValue);
                ModbusResponse writeResponse = _client.WriteSingleRegister(
                    slaveAddress,
                    address,
                    encodedValue,
                    InspectionInstrumentProtocol.ConfigurationWriteResponseTimeoutMilliseconds);
                if (IsExplicitModbusRejection(writeResponse))
                {
                    explicitlyRejected = true;
                    error = writeResponse.ErrorMessage ?? "设备返回 Modbus 异常响应";
                    return false;
                }
                Thread.Sleep(InspectionInstrumentProtocol.ConfigurationWriteSettleMilliseconds);

                for (int readAttempt = 1; readAttempt <= InspectionInstrumentProtocol.ConfigurationReadRoundCount; readAttempt++)
                {
                    if (TryReadLogicalRegister(
                            slaveAddress,
                            address,
                            expectedValue,
                            maximumValue,
                            true,
                            expectedReadLowByteFirst,
                            ref lowByteFirst,
                            valueName,
                            out error))
                        return true;
                    if (readAttempt < InspectionInstrumentProtocol.ConfigurationReadRoundCount)
                        Thread.Sleep(InspectionInstrumentProtocol.ConfigurationRequestIntervalMilliseconds);
                }

                if (!writeResponse.Success)
                    error = $"写命令未回显且读回未确认：{writeResponse.ErrorMessage}";
                if (writeAttempt < 2)
                    Thread.Sleep(InspectionInstrumentProtocol.ConfigurationReadRoundDelayMilliseconds);
            }
            return false;
        }

        private static bool IsExplicitModbusRejection(ModbusResponse response) =>
            !response.Success &&
            response.RawData is { Length: >= 3 } rawData &&
            (rawData[1] & 0x80) != 0;

        private bool TryReadLogicalRegister(
            byte slaveAddress,
            ushort address,
            ushort expectedValue,
            ushort maximumValue,
            bool requireExpectedOrder,
            bool expectedReadLowByteFirst,
            ref bool lowByteFirst,
            string valueName,
            out string error)
        {
            if (!TryReadLogicalValue(
                    slaveAddress,
                    address,
                    maximumValue,
                    ref lowByteFirst,
                    valueName,
                    out ushort actualValue,
                    out error))
                return false;

            if (actualValue == expectedValue &&
                (!requireExpectedOrder || expectedValue == 0 || lowByteFirst == expectedReadLowByteFirst))
            {
                error = string.Empty;
                return true;
            }
            error = actualValue != expectedValue
                ? $"读回值为 {actualValue}，期望 {expectedValue}"
                : $"读回逻辑值虽然为 {expectedValue}，但字节序仍与设备配置区主格式不一致，拒绝误判为成功";
            return false;
        }

        private bool TryReadLogicalValueWithRetries(
            byte slaveAddress,
            ushort address,
            ushort maximumValue,
            ref bool lowByteFirst,
            string valueName,
            out ushort value,
            out string error)
        {
            value = 0;
            error = string.Empty;
            for (int attempt = 1; attempt <= InspectionInstrumentProtocol.ConfigurationReadRoundCount; attempt++)
            {
                if (TryReadLogicalValue(
                        slaveAddress,
                        address,
                        maximumValue,
                        ref lowByteFirst,
                        valueName,
                        out value,
                        out error))
                    return true;
                if (attempt < InspectionInstrumentProtocol.ConfigurationReadRoundCount)
                    Thread.Sleep(InspectionInstrumentProtocol.ConfigurationReadRoundDelayMilliseconds);
            }
            return false;
        }

        private bool TryReadRawRegisterWithRetries(
            byte slaveAddress,
            ushort address,
            out ushort rawValue,
            out string error)
        {
            rawValue = 0;
            error = string.Empty;
            for (int attempt = 1; attempt <= InspectionInstrumentProtocol.ConfigurationReadRoundCount; attempt++)
            {
                ModbusResponse response = _client.ReadHoldingRegisters(
                    slaveAddress,
                    address,
                    1,
                    InspectionInstrumentProtocol.ConfigurationReadResponseTimeoutMilliseconds,
                    1);
                if (HasExpectedRegisters(response, 1))
                {
                    rawValue = response.Registers![0];
                    return true;
                }
                error = response.ErrorMessage ?? "设备未返回寄存器";
                if (attempt < InspectionInstrumentProtocol.ConfigurationReadRoundCount)
                    Thread.Sleep(InspectionInstrumentProtocol.ConfigurationReadRoundDelayMilliseconds);
            }
            return false;
        }

        private bool TryReadLogicalValue(
            byte slaveAddress,
            ushort address,
            ushort maximumValue,
            ref bool lowByteFirst,
            string valueName,
            out ushort actualValue,
            out string error)
        {
            actualValue = 0;
            ModbusResponse response = _client.ReadHoldingRegisters(
                slaveAddress,
                address,
                1,
                InspectionInstrumentProtocol.ConfigurationReadResponseTimeoutMilliseconds,
                1);
            if (!HasExpectedRegisters(response, 1))
            {
                error = response.ErrorMessage ?? "设备未返回寄存器";
                return false;
            }

            ushort rawValue = response.Registers![0];
            try
            {
                if (rawValue == 0)
                    actualValue = 0;
                else
                {
                    actualValue = DecodeConfigurationRegisterAutomatically(
                        rawValue,
                        maximumValue,
                        out bool detectedLowByteFirst,
                        $"{valueName}地址0x{address:X4}");
                    lowByteFirst = detectedLowByteFirst;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static void ValidateSnapshot(InspectionInstrumentConfiguration current)
        {
            if (current.TemperatureSensorTypes.Length != InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount ||
                current.ChannelEnabled.Length != InspectionInstrumentProtocol.ConfigurableEnableChannelCount ||
                current.SensorTypeRegisterLowByteFirst.Length != InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount ||
                current.EnableRegisterLowByteFirst.Length != InspectionInstrumentProtocol.ConfigurableEnableChannelCount ||
                current.SensorTypeRegisterRawValues.Length != InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount ||
                current.EnableRegisterRawValues.Length != InspectionInstrumentProtocol.ConfigurableEnableChannelCount ||
                current.InvalidSensorTypeRegisters.Length != InspectionInstrumentProtocol.ConfigurableSensorTypeChannelCount ||
                current.InvalidEnableRegisters.Length != InspectionInstrumentProtocol.ConfigurableEnableChannelCount)
                throw new InvalidOperationException("设备配置快照不完整，请重新读取设备配置后再保存。");
        }

        private static bool GetDefaultOrder(IReadOnlyList<bool> orders) =>
            orders.Count(value => value) >= orders.Count(value => !value);

        /// <summary>
        /// 按串口助手已验证的4寄存器分段读取。功能码03响应不包含起始地址，迟到的测量帧
        /// 在数据长度相同时可能混入当前事务，因此每个分段先校验配置业务值范围，合法后立即采用。
        /// 失败或未确认分段不会让整次读取立即作废，后续轮次只补读尚未完成的地址。
        /// </summary>
        private ushort[] ReadConfigurationRegisters(
            byte slaveAddress,
            ushort startAddress,
            int count,
            string operation,
            int chunkSize,
            ushort maximumValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ushort[] values = new ushort[count];
            chunkSize = NormalizeConfigurationChunkSize(chunkSize);
            List<ConfigurationReadBlock> blocks = new();
            for (int offset = 0; offset < count; offset += chunkSize)
            {
                int quantity = Math.Min(chunkSize, count - offset);
                blocks.Add(new ConfigurationReadBlock(
                    offset,
                    checked((ushort)(startAddress + offset)),
                    quantity));
            }

            for (int round = 1; round <= InspectionInstrumentProtocol.ConfigurationReadRoundCount; round++)
            {
                foreach (ConfigurationReadBlock block in blocks.Where(item => !item.Completed))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ModbusResponse chunkResponse = _client.ReadHoldingRegisters(
                        slaveAddress,
                        block.Address,
                        checked((ushort)block.Quantity),
                        InspectionInstrumentProtocol.ConfigurationReadResponseTimeoutMilliseconds,
                        1);
                    bool hasExpectedRegisters = HasExpectedRegisters(chunkResponse, block.Quantity);
                    string validationError = string.Empty;
                    bool hasValidConfigurationValues = hasExpectedRegisters &&
                        TryValidateConfigurationRegisters(
                            chunkResponse.Registers!,
                            maximumValue,
                            block.Offset,
                            operation,
                            out validationError);
                    if (hasValidConfigurationValues)
                    {
                        ushort[] candidate = chunkResponse.Registers!.ToArray();
                        if (block.CandidateRegisters != null && block.CandidateRegisters.SequenceEqual(candidate))
                            block.MatchingConfirmationCount++;
                        else
                        {
                            block.CandidateRegisters = candidate;
                            block.MatchingConfirmationCount = 1;
                        }

                        if (block.MatchingConfirmationCount >=
                            InspectionInstrumentProtocol.ConfigurationReadConfirmationCount)
                        {
                            Array.Copy(candidate, 0, values, block.Offset, block.Quantity);
                            block.Completed = true;
                            block.LastError = string.Empty;
                        }
                        else
                        {
                            block.LastError =
                                $"已收到合法响应，等待第 {InspectionInstrumentProtocol.ConfigurationReadConfirmationCount} 次一致确认";
                        }
                    }
                    else if (hasExpectedRegisters)
                    {
                        ushort[] invalidCandidate = chunkResponse.Registers!.ToArray();
                        if (block.CandidateRegisters != null && block.CandidateRegisters.SequenceEqual(invalidCandidate))
                            block.MatchingConfirmationCount++;
                        else
                        {
                            block.CandidateRegisters = invalidCandidate;
                            block.MatchingConfirmationCount = 1;
                        }

                        if (block.MatchingConfirmationCount >=
                            InspectionInstrumentProtocol.InvalidConfigurationReadConfirmationCount)
                        {
                            Array.Copy(invalidCandidate, 0, values, block.Offset, block.Quantity);
                            block.Completed = true;
                            block.AcceptedStableInvalidValues = true;
                            block.LastError = validationError;
                        }
                        else
                        {
                            block.LastError =
                                $"收到疑似迟到测量帧或设备异常配置值，等待重复确认：{validationError}";
                        }
                    }
                    else
                    {
                        block.LastError = chunkResponse.ErrorMessage ?? "设备未确认请求";
                    }
                    WaitForDeviceConfigurationCycle(cancellationToken);
                }
                if (blocks.All(item => item.Completed)) return values;
                if (round < InspectionInstrumentProtocol.ConfigurationReadRoundCount)
                    WaitForCancellationOrDelay(
                        cancellationToken,
                        InspectionInstrumentProtocol.ConfigurationReadRoundDelayMilliseconds);
            }

            // 某些固件在扫描传感器期间会偶发忽略特定4寄存器分段，但同一地址的单寄存器请求仍可响应。
            // 这里只对已经完成多轮补读仍失败的分段做有界降级，最多补读4个地址，避免退化为全区逐项轮询。
            foreach (ConfigurationReadBlock block in blocks.Where(item => !item.Completed))
            {
                if (TryReadConfigurationBlockRegisterByRegister(
                        slaveAddress,
                        block,
                        maximumValue,
                        operation,
                        values,
                        cancellationToken,
                        out string fallbackError))
                {
                    block.Completed = true;
                    block.LastError = string.Empty;
                }
                else
                {
                    block.LastError = $"分段补读失败，单寄存器降级仍未确认：{fallbackError}";
                }
            }
            if (blocks.All(item => item.Completed)) return values;

            string failedRanges = string.Join("；", blocks
                .Where(item => !item.Completed)
                .Select(item =>
                    $"0x{item.Address:X4}～0x{item.Address + item.Quantity - 1:X4}：{item.LastError}"));
            throw new InvalidOperationException(
                $"{operation}未完成。成功分段 {blocks.Count(item => item.Completed)}/{blocks.Count}；" +
                $"仅以下分段在 {InspectionInstrumentProtocol.ConfigurationReadRoundCount} 轮补读后仍失败：{failedRanges}");
        }

        /// <summary>
        /// 仅对一个持续失败的小分段逐寄存器补读。合法值一次即可确认；非法值必须连续两次相同，
        /// 才作为设备中的稳定异常配置交给上层标记和修复。
        /// </summary>
        private bool TryReadConfigurationBlockRegisterByRegister(
            byte slaveAddress,
            ConfigurationReadBlock block,
            ushort maximumValue,
            string operation,
            ushort[] destination,
            CancellationToken cancellationToken,
            out string error)
        {
            ushort[] recovered = new ushort[block.Quantity];
            List<string> failures = new();
            bool acceptedStableInvalidValues = false;
            for (int index = 0; index < block.Quantity; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort address = checked((ushort)(block.Address + index));
                ushort? invalidCandidate = null;
                bool completed = false;
                string lastError = "设备未确认请求";
                for (int attempt = 1; attempt <= InspectionInstrumentProtocol.ConfigurationReadRoundCount; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ModbusResponse response = _client.ReadHoldingRegisters(
                        slaveAddress,
                        address,
                        1,
                        InspectionInstrumentProtocol.ConfigurationReadResponseTimeoutMilliseconds,
                        1);
                    if (HasExpectedRegisters(response, 1))
                    {
                        ushort rawValue = response.Registers![0];
                        if (TryValidateConfigurationRegisters(
                                response.Registers,
                                maximumValue,
                                block.Offset + index,
                                operation,
                                out string validationError))
                        {
                            recovered[index] = rawValue;
                            completed = true;
                            break;
                        }

                        lastError = validationError;
                        if (invalidCandidate == rawValue)
                        {
                            recovered[index] = rawValue;
                            acceptedStableInvalidValues = true;
                            completed = true;
                            break;
                        }
                        invalidCandidate = rawValue;
                    }
                    else
                    {
                        lastError = response.ErrorMessage ?? "设备未确认请求";
                    }

                    if (attempt < InspectionInstrumentProtocol.ConfigurationReadRoundCount)
                        WaitForCancellationOrDelay(
                            cancellationToken,
                            InspectionInstrumentProtocol.ConfigurationReadRoundDelayMilliseconds);
                }

                if (!completed)
                    failures.Add($"0x{address:X4}：{lastError}");
                WaitForDeviceConfigurationCycle(cancellationToken);
            }

            if (failures.Count > 0)
            {
                error = string.Join("；", failures);
                return false;
            }

            Array.Copy(recovered, 0, destination, block.Offset, block.Quantity);
            block.AcceptedStableInvalidValues = acceptedStableInvalidValues;
            error = string.Empty;
            return true;
        }

        private static int NormalizeConfigurationChunkSize(int chunkSize) =>
            InspectionInstrumentProtocol.ConfigurationFallbackChunkRegisterCount;

        private static bool HasExpectedRegisters(ModbusResponse response, int expectedCount) =>
            response.Success && response.Registers?.Length == expectedCount;

        /// <summary>
        /// 配置寄存器只允许业务定义范围内的值，并兼容标准/低字节在前两种表示。
        /// 例如 0xCDCC 是 123.4 浮点测量值的片段，不可能是0/1使能值，必须丢弃而不能完成分段。
        /// </summary>
        private static bool TryValidateConfigurationRegisters(
            IReadOnlyList<ushort> rawValues,
            ushort maximumValue,
            int blockOffset,
            string operation,
            out string error)
        {
            for (int index = 0; index < rawValues.Count; index++)
            {
                ushort rawValue = rawValues[index];
                if (rawValue == 0) continue;
                try
                {
                    DecodeConfigurationRegisterAutomatically(
                        rawValue,
                        maximumValue,
                        out _,
                        $"{operation}通道{blockOffset + index + 1}");
                }
                catch (InvalidOperationException ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private static bool TryDecodeConfigurationValue(
            ushort rawValue,
            ushort maximumValue,
            ref bool lowByteFirst,
            out ushort value)
        {
            value = 0;
            if (rawValue == 0) return true;
            try
            {
                value = DecodeConfigurationRegisterAutomatically(
                    rawValue,
                    maximumValue,
                    out bool detectedLowByteFirst);
                lowByteFirst = detectedLowByteFirst;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void WaitForDeviceConfigurationCycle() =>
            Thread.Sleep(InspectionInstrumentProtocol.ConfigurationRequestIntervalMilliseconds);

        /// <summary>读取配置时使用可取消等待；写入事务继续使用不可中断等待。</summary>
        private static void WaitForDeviceConfigurationCycle(CancellationToken cancellationToken) =>
            WaitForCancellationOrDelay(
                cancellationToken,
                InspectionInstrumentProtocol.ConfigurationRequestIntervalMilliseconds);

        /// <summary>在设备恢复间隔内响应窗口关闭请求，避免继续执行后续分段和重试。</summary>
        private static void WaitForCancellationOrDelay(CancellationToken cancellationToken, int milliseconds)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                Thread.Sleep(milliseconds);
                return;
            }

            if (cancellationToken.WaitHandle.WaitOne(milliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 逐寄存器识别配置值字节序。现场设备同一区域可能同时出现 00 01 和 01 00，
        /// 因此不能再假设整个配置块使用统一格式。非零值保留各自格式；零值无法判断，
        /// 继承该区多数格式，数量相同时按当前固件默认使用低字节在前。
        /// </summary>
        private static DecodedConfigurationBlock DecodeConfigurationBlock(
            IReadOnlyList<ushort> rawValues,
            ushort maximum,
            string blockName)
        {
            ushort[] values = new ushort[rawValues.Count];
            bool?[] detectedOrders = new bool?[rawValues.Count];
            bool[] invalidRegisters = new bool[rawValues.Count];
            int standardCount = 0;
            int lowByteFirstCount = 0;
            for (int index = 0; index < rawValues.Count; index++)
            {
                ushort rawValue = rawValues[index];
                if (rawValue == 0) continue;
                bool inheritedOrder = false;
                if (!TryDecodeConfigurationValue(rawValue, maximum, ref inheritedOrder, out values[index]))
                {
                    invalidRegisters[index] = true;
                    continue;
                }
                bool useLowByteFirst = inheritedOrder;
                detectedOrders[index] = useLowByteFirst;
                if (useLowByteFirst) lowByteFirstCount++;
                else standardCount++;
            }

            bool defaultLowByteFirst = lowByteFirstCount >= standardCount;
            bool[] resolvedOrders = detectedOrders
                .Select(order => order ?? defaultLowByteFirst)
                .ToArray();
            return new DecodedConfigurationBlock(values, resolvedOrders, defaultLowByteFirst, invalidRegisters);
        }

        private static string BuildInvalidConfigurationWarning(
            IReadOnlyList<bool> invalidSensorTypes,
            IReadOnlyList<ushort> rawSensorTypes,
            IReadOnlyList<bool> invalidEnables,
            IReadOnlyList<ushort> rawEnables)
        {
            List<string> items = new();
            items.AddRange(invalidSensorTypes
                .Select((invalid, index) => new { invalid, index })
                .Where(item => item.invalid)
                .Select(item => $"CH{item.index + 1}传感器类型=0x{rawSensorTypes[item.index]:X4}"));
            items.AddRange(invalidEnables
                .Select((invalid, index) => new { invalid, index })
                .Where(item => item.invalid)
                .Select(item => $"通道{item.index + 1}使能=0x{rawEnables[item.index]:X4}"));
            return items.Count == 0
                ? string.Empty
                : $"设备存在稳定异常配置：{string.Join("、", items)}。界面已使用安全默认值，点击写入并保存可修复异常寄存器";
        }

        /// <summary>按单个配置值自动判断标准字节序或设备低字节在前格式。</summary>
        public static ushort DecodeConfigurationRegisterAutomatically(
            ushort wireValue,
            ushort maximum,
            out bool useLowByteFirst,
            string valueName = "配置寄存器")
        {
            bool standardValid = wireValue <= maximum;
            ushort swapped = SwapRegisterBytes(wireValue);
            bool lowByteFirstValid = swapped <= maximum;
            if (standardValid == lowByteFirstValid)
                throw new InvalidOperationException(
                    $"{valueName}返回无法识别的配置值 0x{wireValue:X4}，为保护设备已停止配置操作。");
            useLowByteFirst = lowByteFirstValid;

            return useLowByteFirst ? swapped : wireValue;
        }

        /// <summary>按配置区检测到的字节序，把 Modbus 客户端解析出的寄存器还原为业务值。</summary>
        public static ushort DecodeConfigurationRegister(ushort wireValue, bool useLowByteFirst) =>
            useLowByteFirst ? SwapRegisterBytes(wireValue) : wireValue;

        /// <summary>按配置区检测到的字节序，把业务值转换为设备实际接收的寄存器值。</summary>
        public static ushort EncodeConfigurationRegister(ushort value, bool useLowByteFirst) =>
            useLowByteFirst ? SwapRegisterBytes(value) : value;

        /// <summary>
        /// 生成功能码06的写入值。Modbus请求中的16位寄存器始终采用标准大端顺序；
        /// 设备读取响应的兼容字节序只用于解码，绝不能影响写帧。
        /// </summary>
        public static ushort EncodeConfigurationWriteValue(ushort value) => value;

        private static ushort SwapRegisterBytes(ushort value) =>
            (ushort)((value << 8) | (value >> 8));

        /// <summary>写入前读取没有取得完整响应；确认尚未发出配置写命令，可保留快照重试。</summary>
        private sealed class ConfigurationPreflightCommunicationException : InvalidOperationException
        {
            public ConfigurationPreflightCommunicationException(string message) : base(message) { }
        }

        /// <summary>写入前发现设备值已偏离读取快照；即使尚未写入，也必须重新读取配置。</summary>
        private sealed class ConfigurationSnapshotChangedException : InvalidOperationException
        {
            public ConfigurationSnapshotChangedException(string message) : base(message) { }
        }

        private sealed record DecodedConfigurationBlock(
            ushort[] Values,
            bool[] LowByteFirstByRegister,
            bool DefaultLowByteFirst,
            bool[] InvalidRegisters);

        /// <summary>配置读取分段状态；失败轮次只补读未完成分段。</summary>
        private sealed class ConfigurationReadBlock
        {
            public ConfigurationReadBlock(int offset, ushort address, int quantity)
            {
                Offset = offset;
                Address = address;
                Quantity = quantity;
            }

            public int Offset { get; }
            public ushort Address { get; }
            public int Quantity { get; }
            public bool Completed { get; set; }
            public ushort[]? CandidateRegisters { get; set; }
            public int MatchingConfirmationCount { get; set; }
            public bool AcceptedStableInvalidValues { get; set; }
            public string LastError { get; set; } = string.Empty;
        }

    }
}
