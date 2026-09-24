using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 保存上位机最后一次成功写入并读回确认的巡检仪通道使能快照。
    /// 快照按串口和从站地址区分，使用本地 JSON，不依赖数据库。
    /// </summary>
    public sealed class InspectionInstrumentChannelProfileService
    {
        private readonly object _syncRoot = new();

        public static InspectionInstrumentChannelProfileService Default { get; } = new();

        public InspectionInstrumentChannelProfileService(string? storageDirectory = null)
        {
            StorageDirectory = string.IsNullOrWhiteSpace(storageDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UpperComInspectionInstrument2022")
                : Path.GetFullPath(storageDirectory);
            ProfileFilePath = Path.Combine(StorageDirectory, "instrument-channel-profiles.json");
        }

        public string StorageDirectory { get; }
        public string ProfileFilePath { get; }

        public bool TryLoad(
            string portName,
            byte slaveAddress,
            out bool[] expectedEnabled,
            out DateTime savedAt,
            out string error)
        {
            lock (_syncRoot)
            {
                try
                {
                    ChannelProfileStore store = ReadStore();
                    ChannelProfile? profile = store.Profiles.LastOrDefault(item =>
                        string.Equals(item.PortName, portName, StringComparison.OrdinalIgnoreCase) &&
                        item.SlaveAddress == slaveAddress);
                    if (profile?.ChannelEnabled?.Length !=
                        Communication.InspectionInstrumentProtocol.ConfigurableEnableChannelCount)
                    {
                        expectedEnabled = Array.Empty<bool>();
                        savedAt = default;
                        error = string.Empty;
                        return false;
                    }

                    expectedEnabled = profile.ChannelEnabled.ToArray();
                    savedAt = profile.SavedAt;
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    expectedEnabled = Array.Empty<bool>();
                    savedAt = default;
                    error = $"无法读取上次通道配置快照：{ex.Message}";
                    return false;
                }
            }
        }

        public bool TrySave(
            string portName,
            byte slaveAddress,
            IReadOnlyList<bool> channelEnabled,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                error = "串口名称为空，无法建立设备通道配置核对基准。";
                return false;
            }
            if (channelEnabled.Count !=
                Communication.InspectionInstrumentProtocol.ConfigurableEnableChannelCount)
            {
                error = "通道使能快照必须包含33个物理通道。";
                return false;
            }

            lock (_syncRoot)
            {
                string temporaryPath = ProfileFilePath + ".tmp";
                try
                {
                    ChannelProfileStore store = ReadStore();
                    store.Profiles.RemoveAll(item =>
                        string.Equals(item.PortName, portName, StringComparison.OrdinalIgnoreCase) &&
                        item.SlaveAddress == slaveAddress);
                    store.Profiles.Add(new ChannelProfile
                    {
                        PortName = portName,
                        SlaveAddress = slaveAddress,
                        ChannelEnabled = channelEnabled.ToArray(),
                        SavedAt = DateTime.Now
                    });
                    Directory.CreateDirectory(StorageDirectory);
                    File.WriteAllText(
                        temporaryPath,
                        JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(temporaryPath, ProfileFilePath, overwrite: true);
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    try
                    {
                        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                    }
                    catch
                    {
                        // 临时文件清理失败不能覆盖原始保存错误。
                    }
                    error = $"设备配置已写入，但无法保存本地通道快照：{ex.Message}";
                    return false;
                }
            }
        }

        private ChannelProfileStore ReadStore()
        {
            if (!File.Exists(ProfileFilePath)) return new ChannelProfileStore();
            string json = File.ReadAllText(ProfileFilePath);
            ChannelProfileStore store = JsonSerializer.Deserialize<ChannelProfileStore>(json) ?? new ChannelProfileStore();
            store.Profiles ??= new List<ChannelProfile>();
            return store;
        }

        private sealed class ChannelProfileStore
        {
            public List<ChannelProfile> Profiles { get; set; } = new();
        }

        private sealed class ChannelProfile
        {
            public string PortName { get; set; } = string.Empty;
            public byte SlaveAddress { get; set; }
            public bool[] ChannelEnabled { get; set; } = Array.Empty<bool>();
            public DateTime SavedAt { get; set; }
        }
    }
}
