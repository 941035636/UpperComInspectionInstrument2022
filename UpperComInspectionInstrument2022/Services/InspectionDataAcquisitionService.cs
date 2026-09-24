using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UpperComInspectionInstrument2022.Communication;
using UpperComInspectionInstrument2022.Models;

namespace UpperComInspectionInstrument2022.Services
{
    /// <summary>
    /// 巡检仪实时数据采集服务。负责单一采集循环的生命周期，不负责页面显示。
    /// </summary>
    public class InspectionDataAcquisitionService
    {
        private const int RetryBaseDelayMilliseconds = 1000;
        private const int RetryMaximumDelayMilliseconds = 5000;
        private readonly IInspectionMeasurementReader _measurementReader;
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _intervalChangedSignal = new SemaphoreSlim(0, 1);
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private long _acquisitionId;
        private string _calibrationType = "温度";
        private int _temperatureChannelCount = InspectionInstrumentProtocol.PhysicalTemperatureChannelCount;
        private int _humidityChannelCount;
        private bool _isRunning;
        private int _intervalMilliseconds = 2000;
        private int _lastReadDurationMilliseconds;
        private int _consecutiveFailureCount;
        private int _nextRetryDelayMilliseconds;

        /// <summary>
        /// 连续通信失败达到该次数后，界面进入持续通信告警状态。
        /// 该阈值只改变告警级别，不停止采集、不关闭串口；设备恢复响应后计数自动清零。
        /// </summary>
        public int PersistentWarningFailureCount { get; } = 3;

        /// <summary>后台采集循环是否尚未完全退出。</summary>
        public bool IsRunning
        {
            get { lock (_stateLock) return _isRunning; }
        }

        /// <summary>当前实时完整轮询的等待周期，正式校准可在不重开串口的情况下临时调整。</summary>
        public int CurrentIntervalMilliseconds
        {
            get { lock (_stateLock) return _intervalMilliseconds; }
        }

        /// <summary>最近一次完整读取全部任务通道所用时间，用于拦截设备无法实现的正式采样间隔。</summary>
        public int LastReadDurationMilliseconds
        {
            get { lock (_stateLock) return _lastReadDurationMilliseconds; }
        }

        /// <summary>当前连续通信失败次数；任意一次完整读取成功后自动清零。</summary>
        public int ConsecutiveFailureCount
        {
            get { lock (_stateLock) return _consecutiveFailureCount; }
        }

        /// <summary>失败后下一次请求前的等待时间，单位为毫秒。</summary>
        public int NextRetryDelayMilliseconds
        {
            get { lock (_stateLock) return _nextRetryDelayMilliseconds; }
        }

        /// <summary>每完成一组全部通道读取后触发；事件在线程池线程上发出。</summary>
        public event Action<long, List<InspectionChannelData>>? DataAcquired;
        /// <summary>单次读取失败时触发；达到连续失败上限前，服务会按退避时间继续尝试。</summary>
        public event Action<Exception>? AcquisitionError;

        /// <summary>创建采集服务并注入负责设备协议解析的巡检仪服务。</summary>
        public InspectionDataAcquisitionService(IInspectionMeasurementReader measurementReader)
        {
            _measurementReader = measurementReader ?? throw new ArgumentNullException(nameof(measurementReader));
        }

        /// <summary>按纯温度模式启动采集，供旧调用代码兼容使用。</summary>
        public bool Start(byte slaveAddress, int intervalMilliseconds) => Start(
            slaveAddress,
            intervalMilliseconds,
            "温度",
            InspectionInstrumentProtocol.PhysicalTemperatureChannelCount,
            0);

        /// <summary>
        /// 按当前设备全部实际接口启动采集，保留给旧调用代码使用。
        /// 工作台应使用带点数的重载，避免读取任务没有使用的预留通道。
        /// </summary>
        public bool Start(byte slaveAddress, int intervalMilliseconds, string calibrationType)
        {
            bool hasTemperature = calibrationType.Contains("温度", StringComparison.Ordinal);
            bool hasHumidity = calibrationType.Contains("湿度", StringComparison.Ordinal);
            return Start(
                slaveAddress,
                intervalMilliseconds,
                calibrationType,
                hasTemperature ? InspectionInstrumentProtocol.PhysicalTemperatureChannelCount : 0,
                hasHumidity ? InspectionInstrumentProtocol.PhysicalHumidityChannelCount : 0);
        }

        /// <summary>
        /// 启动唯一后台采集循环。旧循环尚未退出时返回 false，避免两个循环交替访问同一串口。
        /// 温湿度点数决定本轮功能码 03 实际读取的寄存器数量，而不是只控制页面显示列。
        /// 最小轮询周期限制为 200 ms。
        /// </summary>
        public bool Start(
            byte slaveAddress,
            int intervalMilliseconds,
            string calibrationType,
            int temperatureChannelCount,
            int humidityChannelCount)
        {
            string normalizedType = string.IsNullOrWhiteSpace(calibrationType) ? "温度" : calibrationType;
            bool hasTemperature = normalizedType.Contains("温度", StringComparison.Ordinal);
            bool hasHumidity = normalizedType.Contains("湿度", StringComparison.Ordinal);
            if (hasTemperature)
                InspectionInstrumentProtocol.GetTemperatureRegisterQuantity(temperatureChannelCount);
            else
                temperatureChannelCount = 0;
            if (hasHumidity)
                InspectionInstrumentProtocol.GetHumidityRegisterQuantity(humidityChannelCount);
            else
                humidityChannelCount = 0;

            if (intervalMilliseconds < 200) intervalMilliseconds = 200;
            CancellationTokenSource cts;
            lock (_stateLock)
            {
                if (_loopTask is { IsCompleted: false }) return false;
                cts = new CancellationTokenSource();
                _cts = cts;
                _calibrationType = normalizedType;
                _temperatureChannelCount = temperatureChannelCount;
                _humidityChannelCount = humidityChannelCount;
                _intervalMilliseconds = intervalMilliseconds;
                _lastReadDurationMilliseconds = 0;
                _consecutiveFailureCount = 0;
                _nextRetryDelayMilliseconds = 0;
                while (_intervalChangedSignal.Wait(0)) { }
                _isRunning = true;
                _loopTask = Task.Run(
                    () => AcquisitionLoop(slaveAddress, cts),
                    CancellationToken.None);
            }
            return true;
        }

        /// <summary>
        /// 动态调整运行中的轮询周期。周期缩短时会唤醒当前等待，使正式校准无需停止采集或重开串口即可加快刷新。
        /// </summary>
        public bool TryUpdateInterval(int intervalMilliseconds)
        {
            if (intervalMilliseconds < 200) intervalMilliseconds = 200;
            int previousInterval;
            lock (_stateLock)
            {
                if (!_isRunning || _loopTask is not { IsCompleted: false }) return false;
                previousInterval = _intervalMilliseconds;
                _intervalMilliseconds = intervalMilliseconds;
            }

            if (intervalMilliseconds < previousInterval)
            {
                try { _intervalChangedSignal.Release(); }
                catch (SemaphoreFullException) { }
            }
            return true;
        }

        /// <summary>
        /// 请求停止采集。该方法只发出取消信号；在当前串口读操作真正结束前，服务仍视为运行中。
        /// </summary>
        public void Stop()
        {
            RequestStop();
        }

        /// <summary>
        /// 请求停止并异步等待采集循环完全退出。界面“暂停”操作使用本方法，确保再次启动前旧请求已经结束。
        /// </summary>
        public async Task StopAsync()
        {
            Task? loopTask = RequestStop();
            if (loopTask == null) return;
            await loopTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 请求停止并在指定时间内同步等待退出，供应用关闭阶段使用。
        /// 返回 false 表示等待超时，调用方仍可继续执行兜底资源释放。
        /// </summary>
        public bool StopAndWait(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            Task? loopTask = RequestStop();
            if (loopTask == null) return true;
            return loopTask.Wait(timeout);
        }

        /// <summary>在锁内取得当前循环并发送取消信号，但不提前清除运行状态。</summary>
        private Task? RequestStop()
        {
            lock (_stateLock)
            {
                _cts?.Cancel();
                return _loopTask;
            }
        }

        /// <summary>
        /// 循环读取完整测量数据、分配采集序号并发布事件。
        /// 通信失败采用 1～5 秒线性退避，防止设备无响应时持续高频请求。
        /// </summary>
        private async Task AcquisitionLoop(byte slaveAddress, CancellationTokenSource owner)
        {
            CancellationToken token = owner.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    long acquisitionId = Interlocked.Increment(ref _acquisitionId);
                    int delayMilliseconds = CurrentIntervalMilliseconds;
                    bool readSucceeded = false;
                    long cycleStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        List<InspectionChannelData> data = _measurementReader.ReadMeasurements(
                            _calibrationType,
                            slaveAddress,
                            acquisitionId,
                            _temperatureChannelCount,
                            _humidityChannelCount);
                        int readDurationMilliseconds = Math.Max(1, (int)Math.Ceiling(Stopwatch.GetElapsedTime(cycleStarted).TotalMilliseconds));
                        lock (_stateLock)
                        {
                            _lastReadDurationMilliseconds = readDurationMilliseconds;
                            _consecutiveFailureCount = 0;
                            _nextRetryDelayMilliseconds = 0;
                        }
                        if (!token.IsCancellationRequested) DataAcquired?.Invoke(acquisitionId, data);
                        readSucceeded = true;
                    }
                    // 用户点击“暂停”时，当前同步串口读取不能被 CancellationToken 立即打断。
                    // 如果该读取随后以超时结束，这只是停止过程的正常收尾，不能让 StopAsync
                    // 以通信异常结束，更不能被页面误报为“实时记录未保存”。
                    catch (Exception) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        lock (_stateLock)
                        {
                            _consecutiveFailureCount++;
                            _nextRetryDelayMilliseconds = Math.Max(
                                _intervalMilliseconds,
                                Math.Min(RetryMaximumDelayMilliseconds, RetryBaseDelayMilliseconds * _consecutiveFailureCount));
                            delayMilliseconds = _nextRetryDelayMilliseconds;
                        }
                        // 偶发无响应只表示本轮没有取得新数据。保持串口和循环，按有上限的退避节拍继续请求；
                        // 正式校准样本只在 DataAcquired 事件中保存，因此失败期间不会用旧值补样本。
                        AcquisitionError?.Invoke(ex);
                    }
                    if (readSucceeded)
                        await WaitForNextCycleAsync(cycleStarted, token).ConfigureAwait(false);
                    else
                        await Task.Delay(delayMilliseconds, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_cts, owner))
                    {
                        _cts = null;
                        _isRunning = false;
                        _loopTask = null;
                        _nextRetryDelayMilliseconds = 0;
                    }
                }
                owner.Dispose();
            }
        }

        /// <summary>
        /// 等待下一轮读取；周期按相邻两轮完整读取的起始时刻计算，会扣除本轮读取和事件处理耗时。
        /// 若运行期间把轮询周期调短，则唤醒旧等待并按新周期重新计算剩余时间。
        /// 调长周期不会主动唤醒，避免正式校准结束时额外产生一次紧邻读取。
        /// </summary>
        private async Task WaitForNextCycleAsync(long cycleStarted, CancellationToken token)
        {
            while (true)
            {
                int elapsedMilliseconds = (int)Math.Ceiling(Stopwatch.GetElapsedTime(cycleStarted).TotalMilliseconds);
                int remainingMilliseconds = CurrentIntervalMilliseconds - elapsedMilliseconds;
                if (remainingMilliseconds <= 0) return;

                using CancellationTokenSource waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task delayTask = Task.Delay(remainingMilliseconds, waitCancellation.Token);
                Task intervalChangedTask = _intervalChangedSignal.WaitAsync(waitCancellation.Token);
                try
                {
                    Task completedTask = await Task.WhenAny(delayTask, intervalChangedTask).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (ReferenceEquals(completedTask, delayTask)) return;
                }
                finally
                {
                    waitCancellation.Cancel();
                }
            }
        }
    }
}
