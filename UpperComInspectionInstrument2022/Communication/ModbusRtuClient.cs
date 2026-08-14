
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
    public class ModbusRtuClient : IDisposable
    {
        private SerialPort? _serialPort;

        public bool IsOpen
        {
            get
            {
                return _serialPort != null && _serialPort.IsOpen;
            }
        }

        public string PortName
        {
            get
            {
                return _serialPort?.PortName;
            }
        }

        public int BaudRate
        {
            get
            {
                return _serialPort?.BaudRate ?? 0;
            }
        }

        /// <summary>
        /// 打开串口
        /// </summary>
        public void Open(
            string portName,
            int baudRate = 115200)
        {
            Close();

            _serialPort = new SerialPort();

            _serialPort.PortName = portName;
            _serialPort.BaudRate = baudRate;

            // 8N1
            _serialPort.DataBits = 8;
            _serialPort.Parity = Parity.None;
            _serialPort.StopBits = StopBits.One;

            _serialPort.ReadTimeout = 1000;
            _serialPort.WriteTimeout = 1000;

            // Modbus RTU 不使用普通文本编码
            _serialPort.Encoding = System.Text.Encoding.ASCII;

            _serialPort.Open();
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                }
                catch
                {
                }

                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        /// <summary>
        /// 功能码03：读取保持寄存器
        /// </summary>
        public ModbusResponse ReadHoldingRegisters(
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

            try
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // 发送
                _serialPort.Write(
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

                byte[] header = ReadExact(3);

                byte responseSlave = header[0];
                byte responseFunction = header[1];
                byte byteCount = header[2];

                int remainingLength = byteCount + 2;

                byte[] remaining = ReadExact(remainingLength);

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

                // CRC检查
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
                        "读取超时，没有收到完整的 Modbus 响应"
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
        /// 构造功能码03请求
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
        /// 精确读取指定数量字节
        /// </summary>
        private byte[] ReadExact(int length)
        {
            byte[] buffer = new byte[length];

            int offset = 0;

            while (offset < length)
            {
                int count =
                    _serialPort.Read(
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
        /// Modbus CRC16
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
        /// CRC校验
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
        /// 十六进制显示
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

        public void Dispose()
        {
            Close();
        }
    }
}
