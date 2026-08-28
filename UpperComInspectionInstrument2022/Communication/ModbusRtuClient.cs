
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
                    ReadTimeout = 1500,
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
            lock (_ioLock)
            {
                return ReadHoldingRegistersCore(slaveAddress, startAddress, quantity);
            }
        }

        /// <summary>
        /// 完成一次“发送请求—读取响应—校验—解析寄存器”的完整事务。
        /// 任何可预期的通信失败都转换为 <see cref="ModbusResponse"/>，避免设备循环因偶发超时崩溃。
        /// </summary>
        private ModbusResponse ReadHoldingRegistersCore(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity)
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
                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                // 每次请求前丢弃旧缓冲区残留，避免上一次不完整帧污染本次响应。
                port.Write(
                    request,
                    0,
                    request.Length);

                // Modbus RTU响应：
                //
                // 从站地址 1
                // 功能码   1
                // 字节数   1
                // 数据     N
                // CRC      2
                //
                // 因此先读取前3个字节

                byte[] header = ReadExact(port, 3);
                LastResponseHex = BytesToHex(header);

                byte responseSlave = header[0];
                byte responseFunction = header[1];
                byte byteCount = header[2];

                int remainingLength = byteCount + 2;

                byte[] remaining = ReadExact(port, remainingLength);
                LastResponseHex = BytesToHex(header) + " " + BytesToHex(remaining);

                byte[] response = new byte[
                    header.Length + remaining.Length];

                Buffer.BlockCopy(
                    header,
                    0,
                    response,
                    0,
                    header.Length);

                Buffer.BlockCopy(
                    remaining,
                    0,
                    response,
                    header.Length,
                    remaining.Length);

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
        private ushort CalculateCrc(
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
        private bool CheckCrc(byte[] frame)
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
