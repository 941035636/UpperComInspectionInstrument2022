
///打开 COM
///关闭 COM
///设置 115200 / 8N1
///构造 Modbus RTU 请求
///CRC16
///发送
///接收
///原始报文返回

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace UpperComInspectionInstrument2022.Communication
{
    /// <summary>
    /// 面向巡检仪的 Modbus RTU 主站客户端。
    /// 该类串行化所有串口读写，负责组帧、完整读取、协议字段检查和 CRC16 校验。
    /// </summary>
    public class ModbusRtuClient : IDisposable
    {
        private SerialPort? _serialPort;
        private readonly object _ioLock = new object();
        private const int ReadAttemptCount = 3;
        private const int ReadResponseTimeoutMilliseconds = 1500;
        private const int ReadRetryDelayMilliseconds = 150;
        private const int InputQuietMilliseconds = 30;

        /// <summary>串口对象存在且操作系统报告端口已经打开。</summary>
        public bool IsOpen
        {
            get
            {
                return _serialPort != null && _serialPort.IsOpen;
            }
        }

        /// <summary>当前已打开的串口名；未连接时为空。</summary>
        public string? PortName
        {
            get
            {
                return _serialPort?.PortName;
            }
        }

        /// <summary>当前串口波特率；未连接时为 0。</summary>
        public int BaudRate
        {
            get
            {
                return _serialPort?.BaudRate ?? 0;
            }
        }

        /// <summary>最近一次发送帧的十六进制文本，用于现场排错。</summary>
        public string LastRequestHex { get; private set; } = string.Empty;
        /// <summary>最近一次接收帧的十六进制文本，用于现场排错。</summary>
        public string LastResponseHex { get; private set; } = string.Empty;

        /// <summary>
        /// 打开指定串口，并按巡检仪协议固定使用 8 位数据位、无校验、1 位停止位。
        /// 如果此前已经打开其他端口，会先安全关闭旧端口。
        /// </summary>
        public void Open(
            string portName,
            int baudRate = 115200)
        {
            lock (_ioLock)
            {
                CloseCore();

                SerialPort port = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    DtrEnable = false,
                    RtsEnable = false,
                    // 新版巡检仪在连续读取温度区后，可能需要较长时间准备下一帧湿度区。
                    // 不能用 1.5 秒把“尚未收到完整帧”直接判定为设备断线。
                    ReadTimeout = 4000,
                    WriteTimeout = 1000,
                    Encoding = System.Text.Encoding.ASCII
                };

                try
                {
                    port.Open();
                    _serialPort = port;
                }
                catch
                {
                    port.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// 线程安全地关闭并释放当前串口。
        /// </summary>
        public void Close()
        {
            lock (_ioLock)
            {
                CloseCore();
            }
        }

        /// <summary>执行实际关闭操作；调用者必须已经持有 <see cref="_ioLock"/>。</summary>
        private void CloseCore()
        {
            SerialPort? port = _serialPort;
            _serialPort = null;
            if (port == null) return;

            try
            {
                if (port.IsOpen) port.Close();
            }
            finally
            {
                port.Dispose();
            }
        }
    //    public ushort[] ReadHoldingRegisters(
    //byte slaveAddress,
    //ushort startAddress,
    //ushort quantity)
    //    { }
        /// <summary>
        /// 使用功能码 03 读取一段连续保持寄存器。
        /// 公共入口通过锁保证一次请求完整结束后才允许下一次请求进入。
        /// </summary>
        public ModbusResponse ReadHoldingRegisters(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity)
        {
            return ReadHoldingRegisters(
                slaveAddress,
                startAddress,
                quantity,
                ReadResponseTimeoutMilliseconds,
                ReadAttemptCount);
        }

        /// <summary>
        /// 使用功能码 03 读取保持寄存器，并允许上层事务按设备能力控制单次超时和尝试次数。
        /// 配置服务使用单次短请求自行安排分段补读；实时采集继续使用默认三次有界重试。
        /// </summary>
        public ModbusResponse ReadHoldingRegisters(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity,
            int responseTimeoutMilliseconds,
            int attemptCount)
        {
            if (responseTimeoutMilliseconds < 100)
                return new ModbusResponse { Success = false, ErrorMessage = "寄存器读取响应等待时间不能小于100 ms" };
            if (attemptCount < 1 || attemptCount > 5)
                return new ModbusResponse { Success = false, ErrorMessage = "寄存器读取重试次数必须在1～5之间" };

            lock (_ioLock)
            {
                ModbusResponse response = new ModbusResponse
                {
                    Success = false,
                    ErrorMessage = "尚未执行读取"
                };
                for (int attempt = 1; attempt <= attemptCount; attempt++)
                {
                    response = ReadHoldingRegistersCore(
                        slaveAddress,
                        startAddress,
                        quantity,
                        responseTimeoutMilliseconds);
                    if (response.Success || !IsTransientReadFailure(response)) return response;
                    if (attempt < attemptCount) Thread.Sleep(ReadRetryDelayMilliseconds);
                }
                response.ErrorMessage =
                    $"连续 {attemptCount} 次请求均未收到当前事务的完整响应。{response.ErrorMessage}";
                return response;
            }
        }

        /// <summary>
        /// 使用功能码 01 读取线圈状态。运行状态检查使用短超时和有限重试，
        /// 不能因为测量保持寄存器仍保留旧值就把停止扫描的设备误判为正在采集。
        /// </summary>
        public ModbusResponse ReadCoils(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity,
            int responseTimeoutMilliseconds = ReadResponseTimeoutMilliseconds,
            int attemptCount = ReadAttemptCount)
        {
            if (responseTimeoutMilliseconds < 100)
                return new ModbusResponse { Success = false, ErrorMessage = "线圈读取响应等待时间不能小于100 ms" };
            if (attemptCount < 1 || attemptCount > 5)
                return new ModbusResponse { Success = false, ErrorMessage = "线圈读取重试次数必须在1～5之间" };

            lock (_ioLock)
            {
                ModbusResponse response = new ModbusResponse
                {
                    Success = false,
                    ErrorMessage = "尚未执行线圈读取"
                };
                for (int attempt = 1; attempt <= attemptCount; attempt++)
                {
                    response = ReadCoilsCore(
                        slaveAddress,
                        startAddress,
                        quantity,
                        responseTimeoutMilliseconds);
                    if (response.Success || !IsTransientReadFailure(response)) return response;
                    if (attempt < attemptCount) Thread.Sleep(ReadRetryDelayMilliseconds);
                }
                response.ErrorMessage =
                    $"连续 {attemptCount} 次请求均未收到当前线圈事务的完整响应。{response.ErrorMessage}";
                return response;
            }
        }

        private static bool IsTransientReadFailure(ModbusResponse response)
        {
            string error = response.ErrorMessage ?? string.Empty;
            return error.Contains("超时", StringComparison.OrdinalIgnoreCase) ||
                   error.Contains("CRC", StringComparison.OrdinalIgnoreCase) ||
                   error.Contains("完整", StringComparison.OrdinalIgnoreCase) ||
                   error.Contains("数据长度", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>使用功能码 05 写单个线圈，并校验设备回显帧。</summary>
        public ModbusResponse WriteSingleCoil(byte slaveAddress, ushort address, bool value)
        {
            return WriteSingleCoil(slaveAddress, address, value, 4000);
        }

        /// <summary>使用功能码 05 写单个线圈，并允许维护命令使用较短的回显等待时间。</summary>
        public ModbusResponse WriteSingleCoil(
            byte slaveAddress,
            ushort address,
            bool value,
            int responseTimeoutMilliseconds)
        {
            if (responseTimeoutMilliseconds < 100)
                return new ModbusResponse { Success = false, ErrorMessage = "写入响应等待时间不能小于100 ms" };
            ushort encoded = value ? (ushort)0xFF00 : (ushort)0x0000;
            byte[] request = BuildFixedWriteRequest(slaveAddress, 0x05, address, encoded);
            lock (_ioLock) return ExecuteWriteRequest(request, responseTimeoutMilliseconds);
        }

        /// <summary>使用功能码 06 写单个保持寄存器，并校验设备回显帧。</summary>
        public ModbusResponse WriteSingleRegister(byte slaveAddress, ushort address, ushort value)
        {
            return WriteSingleRegister(slaveAddress, address, value, 4000);
        }

        /// <summary>使用功能码 06 写单个保持寄存器，并允许配置事务使用短回显等待。</summary>
        public ModbusResponse WriteSingleRegister(
            byte slaveAddress,
            ushort address,
            ushort value,
            int responseTimeoutMilliseconds)
        {
            if (responseTimeoutMilliseconds < 100)
                return new ModbusResponse { Success = false, ErrorMessage = "写入响应等待时间不能小于100 ms" };
            byte[] request = BuildFixedWriteRequest(slaveAddress, 0x06, address, value);
            lock (_ioLock) return ExecuteWriteRequest(request, responseTimeoutMilliseconds);
        }

        /// <summary>使用功能码 10 批量写入连续保持寄存器。</summary>
        public ModbusResponse WriteMultipleRegisters(byte slaveAddress, ushort startAddress, IReadOnlyList<ushort> values)
        {
            if (values == null || values.Count is < 1 or > 123)
                return new ModbusResponse { Success = false, ErrorMessage = "写入寄存器数量必须在 1～123 之间" };

            byte[] request = new byte[9 + values.Count * 2];
            request[0] = slaveAddress;
            request[1] = 0x10;
            request[2] = (byte)(startAddress >> 8);
            request[3] = (byte)startAddress;
            request[4] = (byte)(values.Count >> 8);
            request[5] = (byte)values.Count;
            request[6] = (byte)(values.Count * 2);
            for (int index = 0; index < values.Count; index++)
            {
                request[7 + index * 2] = (byte)(values[index] >> 8);
                request[8 + index * 2] = (byte)values[index];
            }
            AppendCrc(request);
            lock (_ioLock) return ExecuteWriteRequest(request);
        }

        /// <summary>
        /// 执行一次写事务。设备配置只允许在采集停止时调用，因此写入前清除迟到的读响应，
        /// 再按从站、功能码和 CRC 寻找标准写回显，避免把历史功能码 03 帧误判为写成功。
        /// </summary>
        private ModbusResponse ExecuteWriteRequest(byte[] request, int responseTimeoutMilliseconds = 4000)
        {
            if (!IsOpen) return new ModbusResponse { Success = false, ErrorMessage = "串口尚未打开" };
            string requestText = BytesToHex(request);
            LastRequestHex = requestText;
            LastResponseHex = string.Empty;
            try
            {
                SerialPort port = _serialPort ?? throw new InvalidOperationException("串口尚未打开");
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                port.Write(request, 0, request.Length);
                byte[] response = ReadWriteResponseFrame(port, request[0], request[1], responseTimeoutMilliseconds);
                LastResponseHex = BytesToHex(response);
                if ((response[1] & 0x80) != 0)
                    return new ModbusResponse { Success = false, RawData = response, ErrorMessage = $"Modbus异常响应，异常码：0x{response[2]:X2}" };
                for (int index = 0; index < 6; index++)
                {
                    if (response[index] != request[index])
                        return new ModbusResponse { Success = false, RawData = response, ErrorMessage = "设备写入回显与请求不一致" };
                }
                return new ModbusResponse { Success = true, RawData = response };
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
            {
                return new ModbusResponse { Success = false, ErrorMessage = $"写入失败：{ex.Message}。请求帧：{requestText}" };
            }
        }

        private byte[] ReadWriteResponseFrame(SerialPort port, byte slaveAddress, byte function, int timeoutMilliseconds)
        {
            List<byte> buffer = new();
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                int available = port.BytesToRead;
                if (available <= 0)
                {
                    Thread.Sleep(5);
                    continue;
                }
                byte[] chunk = new byte[Math.Min(available, 128)];
                int read = port.Read(chunk, 0, chunk.Length);
                for (int index = 0; index < read; index++) buffer.Add(chunk[index]);
                for (int start = 0; start <= buffer.Count - 5; start++)
                {
                    if (buffer[start] != slaveAddress) continue;
                    byte responseFunction = buffer[start + 1];
                    int length = responseFunction == function ? 8 : (responseFunction & 0x80) != 0 ? 5 : 0;
                    if (length == 0 || buffer.Count - start < length) continue;
                    byte[] candidate = buffer.GetRange(start, length).ToArray();
                    if (CheckCrc(candidate)) return candidate;
                }
                if (buffer.Count > 256) buffer.RemoveRange(0, buffer.Count - 32);
            }
            throw new TimeoutException("未收到完整的写入响应");
        }

        private byte[] BuildFixedWriteRequest(byte slaveAddress, byte function, ushort address, ushort value)
        {
            byte[] request = new byte[8];
            request[0] = slaveAddress;
            request[1] = function;
            request[2] = (byte)(address >> 8);
            request[3] = (byte)address;
            request[4] = (byte)(value >> 8);
            request[5] = (byte)value;
            AppendCrc(request);
            return request;
        }

        private void AppendCrc(byte[] frame)
        {
            ushort crc = CalculateCrc(frame, 0, frame.Length - 2);
            frame[^2] = (byte)crc;
            frame[^1] = (byte)(crc >> 8);
        }

        /// <summary>
        /// 完成一次“发送请求—读取响应—校验—解析寄存器”的完整事务。
        /// 任何可预期的通信失败都转换为 <see cref="ModbusResponse"/>，避免设备循环因偶发超时崩溃。
        /// </summary>
        private ModbusResponse ReadHoldingRegistersCore(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity,
            int responseTimeoutMilliseconds)
        {
            if (!IsOpen)
            {
                return new ModbusResponse
                {
                    Success = false,
                    ErrorMessage = "串口尚未打开"
                };
            }

            if (quantity < 1 || quantity > 125)
            {
                return new ModbusResponse
                {
                    Success = false,
                    ErrorMessage = "读取寄存器数量必须在 1~125 之间"
                };
            }

            byte[] request = BuildReadRequest(
                slaveAddress,
                startAddress,
                quantity);

            string txText = BytesToHex(request);
            LastRequestHex = txText;
            LastResponseHex = string.Empty;

            try
            {
                SerialPort port = _serialPort ?? throw new InvalidOperationException("串口尚未打开");
                // Modbus RTU 响应不回显起始地址，绝不能把上一请求的迟到帧留给下一请求。
                // 每次发送前等待线路短暂静默并清除旧输入，随后使用本次事务自己的局部缓冲。
                DrainInputUntilQuiet(port, InputQuietMilliseconds);

                port.Write(
                    request,
                    0,
                    request.Length);

                // 不按“读到的前三个字节”盲目决定后续长度。
                // 设备偶发延迟或残帧时，前三个字节可能不是当前响应的帧头，
                // 直接 ReadExact 会把错位字节当成 byteCount，最终表现为 CRC 失败。
                // 这里在接收缓冲中寻找符合从站、功能码、期望长度且 CRC 正确的完整帧。
                byte[] response = ReadResponseFrame(
                    port,
                    slaveAddress,
                    quantity,
                    responseTimeoutMilliseconds);
                LastResponseHex = BytesToHex(response);

                byte responseSlave = response[0];
                byte responseFunction = response[1];
                byte byteCount = response[2];

                // 校验顺序从报文完整性到业务字段，便于给用户最准确的故障提示。
                if (!CheckCrc(response))
                {
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage =
                            "CRC 校验失败"
                    };
                }

                // 从站地址检查
                if (responseSlave != slaveAddress)
                {
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage =
                            $"从站地址错误，期望 {slaveAddress}，实际 {responseSlave}"
                    };
                }

                // 异常响应
                if ((responseFunction & 0x80) != 0)
                {
                    byte exceptionCode = response[2];

                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage =
                            $"Modbus异常响应，异常码：0x{exceptionCode:X2}"
                    };
                }
                if (responseFunction != 0x03)
                {
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage =
                            $"功能码错误：0x{responseFunction:X2}"
                    };
                }

                if (byteCount != quantity * 2)
                {
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage =
                            $"数据长度错误，期望 {quantity * 2}，实际 {byteCount}"
                    };
                }

                ushort[] registers =
                    new ushort[quantity];

                for (int i = 0; i < quantity; i++)
                {
                    int index = 3 + i * 2;

                    registers[i] =
                        (ushort)(
                            (response[index] << 8)
                            |
                            response[index + 1]);
                }

                return new ModbusResponse
                {
                    Success = true,
                    RawData = response,
                    Registers = registers
                };
            }
            catch (TimeoutException)
            {
                return new ModbusResponse
                {
                    Success = false,
                    ErrorMessage =
                        $"读取超时，没有收到完整的 Modbus 响应。请求帧：{txText}"
                };
            }
            catch (Exception ex)
            {
                return new ModbusResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>执行一次功能码 01 事务并把响应位域展开为布尔数组。</summary>
        private ModbusResponse ReadCoilsCore(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity,
            int responseTimeoutMilliseconds)
        {
            if (!IsOpen)
                return new ModbusResponse { Success = false, ErrorMessage = "串口尚未打开" };
            if (quantity < 1 || quantity > 2000)
                return new ModbusResponse { Success = false, ErrorMessage = "读取线圈数量必须在 1～2000 之间" };

            byte[] request = BuildBitReadRequest(slaveAddress, startAddress, quantity);
            string txText = BytesToHex(request);
            LastRequestHex = txText;
            LastResponseHex = string.Empty;
            try
            {
                SerialPort port = _serialPort ?? throw new InvalidOperationException("串口尚未打开");
                DrainInputUntilQuiet(port, InputQuietMilliseconds);
                port.Write(request, 0, request.Length);
                int expectedByteCount = (quantity + 7) / 8;
                byte[] response = ReadBitResponseFrame(
                    port,
                    slaveAddress,
                    expectedByteCount,
                    responseTimeoutMilliseconds);
                LastResponseHex = BytesToHex(response);

                if ((response[1] & 0x80) != 0)
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage = $"Modbus异常响应，异常码：0x{response[2]:X2}"
                    };
                if (response[1] != 0x01)
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage = $"功能码错误：0x{response[1]:X2}"
                    };
                if (response[2] != expectedByteCount)
                    return new ModbusResponse
                    {
                        Success = false,
                        RawData = response,
                        ErrorMessage = $"数据长度错误，期望 {expectedByteCount}，实际 {response[2]}"
                    };

                bool[] coils = new bool[quantity];
                for (int index = 0; index < quantity; index++)
                    coils[index] = (response[3 + index / 8] & (1 << (index % 8))) != 0;
                return new ModbusResponse { Success = true, RawData = response, Coils = coils };
            }
            catch (TimeoutException ex)
            {
                return new ModbusResponse
                {
                    Success = false,
                    ErrorMessage = $"读取线圈超时：{ex.Message}。请求帧：{txText}"
                };
            }
            catch (Exception ex)
            {
                return new ModbusResponse { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 从串口缓冲区中重组一帧 Modbus 响应。
        /// 设备偶发发送延迟、残帧或前一帧未完整到达时，不能只看当前读取到的前三个字节；
        /// 必须按本次请求的期望数据长度和 CRC 重新寻找有效帧。
        /// </summary>
        private byte[] ReadResponseFrame(
            SerialPort port,
            byte slaveAddress,
            ushort quantity,
            int timeoutMilliseconds)
        {
            int expectedByteCount = quantity * 2;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            // 响应不包含请求起始地址，因此缓冲区只能属于当前请求，不能跨请求复用。
            List<byte> buffer = new List<byte>();
            bool sawCandidateWithBadCrc = false;

            while (DateTime.UtcNow < deadline)
            {
                if (TryExtractReadResponse(buffer, slaveAddress, expectedByteCount, ref sawCandidateWithBadCrc, out byte[] frame))
                    return frame;

                int available = port.BytesToRead;
                if (available > 0)
                {
                    byte[] chunk = new byte[Math.Min(available, 256)];
                    int count = port.Read(chunk, 0, chunk.Length);
                    for (int i = 0; i < count; i++) buffer.Add(chunk[i]);

                    if (buffer.Count > 512)
                        buffer.RemoveRange(0, buffer.Count - 64);
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            if (sawCandidateWithBadCrc)
                throw new InvalidDataException(
                    $"CRC 校验失败，未找到完整有效的 Modbus 响应；本次接收：{BytesToHex(buffer.ToArray())}");

            throw new TimeoutException(
                $"未收到完整的 Modbus 响应；本次接收：{(buffer.Count == 0 ? "无" : BytesToHex(buffer.ToArray()))}");
        }

        /// <summary>在当前事务的局部缓冲中寻找功能码 01 的完整有效响应。</summary>
        private byte[] ReadBitResponseFrame(
            SerialPort port,
            byte slaveAddress,
            int expectedByteCount,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            List<byte> buffer = new List<byte>();
            bool sawCandidateWithBadCrc = false;
            while (DateTime.UtcNow < deadline)
            {
                if (TryExtractBitReadResponse(
                        buffer,
                        slaveAddress,
                        expectedByteCount,
                        ref sawCandidateWithBadCrc,
                        out byte[] frame))
                    return frame;

                int available = port.BytesToRead;
                if (available > 0)
                {
                    byte[] chunk = new byte[Math.Min(available, 256)];
                    int count = port.Read(chunk, 0, chunk.Length);
                    for (int index = 0; index < count; index++) buffer.Add(chunk[index]);
                    if (buffer.Count > 512) buffer.RemoveRange(0, buffer.Count - 64);
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            if (sawCandidateWithBadCrc)
                throw new InvalidDataException(
                    $"CRC 校验失败，未找到完整有效的线圈响应；本次接收：{BytesToHex(buffer.ToArray())}");
            throw new TimeoutException(
                $"未收到完整的线圈响应；本次接收：{(buffer.Count == 0 ? "无" : BytesToHex(buffer.ToArray()))}");
        }

        /// <summary>从当前事务缓冲中提取功能码 01 响应或 Modbus 异常帧。</summary>
        private static bool TryExtractBitReadResponse(
            List<byte> buffer,
            byte slaveAddress,
            int expectedByteCount,
            ref bool sawCandidateWithBadCrc,
            out byte[] response)
        {
            response = Array.Empty<byte>();
            while (true)
            {
                bool removedCandidate = false;
                for (int start = 0; start <= buffer.Count - 5; start++)
                {
                    if (buffer[start] != slaveAddress) continue;
                    byte function = buffer[start + 1];
                    int frameLength;
                    if (function == 0x01 && buffer[start + 2] == expectedByteCount)
                        frameLength = expectedByteCount + 5;
                    else if (function == 0x81)
                        frameLength = 5;
                    else
                        continue;
                    if (buffer.Count - start < frameLength) continue;
                    byte[] candidate = buffer.GetRange(start, frameLength).ToArray();
                    if (CheckCrc(candidate))
                    {
                        buffer.RemoveRange(0, start + frameLength);
                        response = candidate;
                        return true;
                    }
                    sawCandidateWithBadCrc = true;
                    buffer.RemoveAt(start);
                    removedCandidate = true;
                    break;
                }
                if (!removedCandidate) return false;
            }
        }

        /// <summary>从当前事务缓冲中查找符合从站、功能码、长度和CRC的读响应。</summary>
        private static bool TryExtractReadResponse(
            List<byte> buffer,
            byte slaveAddress,
            int expectedByteCount,
            ref bool sawCandidateWithBadCrc,
            out byte[] response)
        {
            response = Array.Empty<byte>();
            while (true)
            {
                bool removedCandidate = false;
                for (int start = 0; start <= buffer.Count - 5; start++)
                {
                    if (buffer[start] != slaveAddress) continue;
                    byte function = buffer[start + 1];
                    int frameLength;
                    if (function == 0x03 && buffer[start + 2] == expectedByteCount)
                        frameLength = expectedByteCount + 5;
                    else if ((function & 0x80) != 0)
                        frameLength = 5;
                    else
                        continue;
                    if (buffer.Count - start < frameLength) continue;
                    byte[] candidate = buffer.GetRange(start, frameLength).ToArray();
                    if (CheckCrc(candidate))
                    {
                        buffer.RemoveRange(0, start + frameLength);
                        response = candidate;
                        return true;
                    }
                    sawCandidateWithBadCrc = true;
                    buffer.RemoveAt(start);
                    removedCandidate = true;
                    break;
                }
                if (!removedCandidate) return false;
            }
        }

        /// <summary>重试新事务前排空迟到响应，并确保线路至少保持指定静默时间。</summary>
        private static void DrainInputUntilQuiet(SerialPort port, int quietMilliseconds)
        {
            DateTime quietSince = DateTime.UtcNow;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(quietMilliseconds * 4, 120));
            while (DateTime.UtcNow < deadline)
            {
                if (port.BytesToRead > 0)
                {
                    port.DiscardInBuffer();
                    quietSince = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - quietSince).TotalMilliseconds >= quietMilliseconds)
                {
                    return;
                }
                Thread.Sleep(2);
            }
            port.DiscardInBuffer();
        }

        /// <summary>
        /// 构造固定 8 字节的功能码 03 请求帧，并在末尾写入低字节在前的 Modbus CRC。
        /// </summary>
        private byte[] BuildReadRequest(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity)
        {
            byte[] frame = new byte[8];

            frame[0] = slaveAddress;

            // 功能码03
            frame[1] = 0x03;

            // 起始地址
            frame[2] = (byte)(startAddress >> 8);
            frame[3] = (byte)(startAddress & 0xFF);

            // 数量
            frame[4] = (byte)(quantity >> 8);
            frame[5] = (byte)(quantity & 0xFF);

            ushort crc =
                CalculateCrc(frame, 0, 6);

            // Modbus CRC低字节在前
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);

            return frame;
        }

        /// <summary>构造固定 8 字节的功能码 01 请求帧。</summary>
        private byte[] BuildBitReadRequest(byte slaveAddress, ushort startAddress, ushort quantity)
        {
            byte[] frame = new byte[8];
            frame[0] = slaveAddress;
            frame[1] = 0x01;
            frame[2] = (byte)(startAddress >> 8);
            frame[3] = (byte)startAddress;
            frame[4] = (byte)(quantity >> 8);
            frame[5] = (byte)quantity;
            ushort crc = CalculateCrc(frame, 0, 6);
            frame[6] = (byte)crc;
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        /// <summary>
        /// 循环读取直到获得指定数量字节，因为 SerialPort.Read 一次不保证返回完整帧。
        /// </summary>
        private static byte[] ReadExact(SerialPort port, int length)
        {
            byte[] buffer = new byte[length];

            int offset = 0;

            while (offset < length)
            {
                int count =
                    port.Read(
                        buffer,
                        offset,
                        length - offset);

                if (count <= 0)
                {
                    throw new TimeoutException();
                }

                offset += count;
            }

            return buffer;
        }

        /// <summary>
        /// 按 Modbus 多项式 0xA001 计算 CRC16。
        /// </summary>
        private static ushort CalculateCrc(
            byte[] data,
            int offset,
            int length)
        {
            ushort crc = 0xFFFF;

            for (int i = offset;
                 i < offset + length;
                 i++)
            {
                crc ^= data[i];

                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }

        /// <summary>
        /// 将帧末尾收到的 CRC 与对前面所有字节重新计算的 CRC 比较。
        /// </summary>
        private static bool CheckCrc(byte[] frame)
        {
            if (frame == null || frame.Length < 4)
            {
                return false;
            }

            int lengthWithoutCrc =
                frame.Length - 2;


            ushort calculated =
                CalculateCrc(
                    frame,
                    0,
                    lengthWithoutCrc);

            ushort received =
                (ushort)(
                    frame[lengthWithoutCrc]
                    |
                    (frame[lengthWithoutCrc + 1] << 8));

            return calculated == received;
        }

        /// <summary>
        /// 把字节数组格式化为“01 03 02 ...”，用于日志和错误对话框。 
        /// </summary>
        public static string BytesToHex(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            List<string> list =
                new List<string>();

            foreach (byte b in data)
            {
                list.Add(b.ToString("X2"));
            }

            return string.Join(" ", list);
        }

      



        /// <summary>实现 <see cref="IDisposable"/>；释放客户端等价于关闭串口。</summary>
        public void Dispose()
        {
            Close();
        }
    }
}
