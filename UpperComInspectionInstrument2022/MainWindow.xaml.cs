//using System.IO.Ports;
//using System.Text;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
//using System.Windows.Shapes;
//using UpperComInspectionInstrument2022.Communication;

//namespace UpperComInspectionInstrument2022
//{
//    /// <summary>
//    /// Interaction logic for MainWindow.xaml
//    /// </summary>
//    public partial class MainWindow : Window
//    {
//        public MainWindow()
//        {
//            InitializeComponent();
//        }
//    }
//}

using UpperComInspectionInstrument2022.Communication;
using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;

namespace UpperComInspectionInstrument2022
{
    public partial class MainWindow : Window
    {
        private readonly ModbusRtuClient _modbusClient;

        public MainWindow()
        {
            InitializeComponent();

            _modbusClient =
                new ModbusRtuClient();

            LoadSerialPorts();

            Log("程序启动");
            Log("协议：Modbus RTU");
            Log("串口参数：115200 / 8N1");
            Log("功能码：03");
        }

        /// <summary>
        /// 获取电脑当前串口
        /// </summary>
        private void LoadSerialPorts()
        {
            PortComboBox.Items.Clear();

            string[] ports =
                SerialPort.GetPortNames();

            Array.Sort(ports);

            foreach (string port in ports)
            {
                PortComboBox.Items.Add(port);
            }

            if (PortComboBox.Items.Count > 0)
            {
                PortComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 打开串口
        /// </summary>
        private void OpenButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (PortComboBox.SelectedItem == null)
                {
                    MessageBox.Show(
                        "请选择串口。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string portName =
                    PortComboBox.SelectedItem.ToString();

                int baudRate =
                    int.Parse(
                        ((ComboBoxItem)
                        BaudRateComboBox.SelectedItem)
                        .Content.ToString());

                _modbusClient.Open(
                    portName,
                    baudRate);

                StatusTextBlock.Text =
                    $"状态：已打开 {portName}，{baudRate} 8N1";

                OpenButton.Content = "串口已打开";
                OpenButton.IsEnabled = false;

                Log(
                    $"串口打开成功：{portName}，{baudRate} 8N1");
            }
            catch (Exception ex)
            {
                Log("打开串口失败：" + ex.Message);

                MessageBox.Show(
                    ex.Message,
                    "打开串口失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 读取测量值1
        /// </summary>
        private void ReadButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (!_modbusClient.IsOpen)
                {
                    MessageBox.Show(
                        "请先打开串口。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                byte slaveAddress =
                    byte.Parse(
                        SlaveAddressTextBox.Text.Trim());

                ushort startAddress =
                    ParseAddress(
                        StartAddressTextBox.Text);

                ushort quantity =
                    ushort.Parse(
                        QuantityTextBox.Text.Trim());

                Log("");
                Log("========== 开始读取 ==========");

                Log(
                    $"从站地址：{slaveAddress}");

                Log(
                    $"起始寄存器：0x{startAddress:X4}");

                Log(
                    $"读取数量：{quantity}");

                // 构造请求
                byte[] requestPreview =
                    BuildPreviewFrame(
                        slaveAddress,
                        startAddress,
                        quantity);

                Log(
                    "TX → " +
                    ModbusRtuClient.BytesToHex(
                        requestPreview));

                ModbusResponse response =
                    _modbusClient.ReadHoldingRegisters(
                        slaveAddress,
                        startAddress,
                        quantity);

                if (response.RawData != null)
                {
                    Log(
                        "RX ← " +
                        ModbusRtuClient.BytesToHex(
                            response.RawData));
                }

                if (!response.Success)
                {
                    Log(
                        "读取失败：" +
                        response.ErrorMessage);

                    StatusTextBlock.Text =
                        "状态：通信失败";

                    return;
                }

                Log("读取成功");

                if (response.Registers != null)
                {
                    for (int i = 0;
                         i < response.Registers.Length;
                         i++)
                    {
                        Log(
                            $"寄存器[{i}] = " +
                            $"0x{response.Registers[i]:X4} " +
                            $"({response.Registers[i]})");
                    }
                }

                Log("========== 读取结束 ==========");

                StatusTextBlock.Text =
                    "状态：通信成功";
            }
            catch (Exception ex)
            {
                Log("读取异常：" + ex.Message);

                StatusTextBlock.Text =
                    "状态：读取异常";
            }
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _modbusClient.Close();

            OpenButton.Content = "打开串口";
            OpenButton.IsEnabled = true;

            StatusTextBlock.Text =
                "状态：串口已关闭";

            Log("串口已关闭");
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        /// <summary>
        /// 地址解析
        /// 支持：
        /// 0x0001
        /// 1
        /// </summary>
        private ushort ParseAddress(string text)
        {
            text = text.Trim();

            if (text.StartsWith("0x",
                StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToUInt16(
                    text.Substring(2),
                    16);
            }

            return ushort.Parse(text);
        }

        /// <summary>
        /// 仅用于在界面显示发送报文
        /// </summary>
        private byte[] BuildPreviewFrame(
            byte slaveAddress,
            ushort startAddress,
            ushort quantity)
        {
            byte[] frame = new byte[8];

            frame[0] = slaveAddress;
            frame[1] = 0x03;

            frame[2] =
                (byte)(startAddress >> 8);

            frame[3] =
                (byte)(startAddress & 0xFF);

            frame[4] =
                (byte)(quantity >> 8);

            frame[5] =
                (byte)(quantity & 0xFF);

            ushort crc =
                CalculateCrc(frame, 0, 6);

            frame[6] =
                (byte)(crc & 0xFF);

            frame[7] =
                (byte)(crc >> 8);

            return frame;
        }

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
                    if ((crc & 1) != 0)
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
        /// 日志
        /// </summary>
        private void Log(string message)
        {
            LogTextBox.AppendText(
                $"[{DateTime.Now:HH:mm:ss.fff}] " +
                message +
                Environment.NewLine);

            LogTextBox.ScrollToEnd();
        }

        protected override void OnClosed(
            EventArgs e)
        {
            _modbusClient.Dispose();

            base.OnClosed(e);
        }
    }
}