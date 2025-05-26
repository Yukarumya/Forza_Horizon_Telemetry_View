using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;




namespace ForzaHorizon5Telemetry {
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>



    //class dataConfig {
    //        public int RevLimitLevel { get; set; }
    //        public int RevLimitLevel { get; set; }
    //        public int BackgroundOpacity { get; set; }
    //        public bool RedGear { get; set; }
    //        public bool Topmost { get; set; }
    //    }

    public partial class MainWindow : Window {
        class ReadINI {
            [DllImport("KERNEL32.DLL")]
            public static extern uint GetPrivateProfileString(
            string lpAppName,
            string lpKeyName,
            string lpDefault,
            StringBuilder lpReturnedString,
            uint nSize,
            string lpFileName);

            [DllImport("KERNEL32.DLL")]
            public static extern uint GetPrivateProfileInt(
                string lpAppName,
                string lpKeyName,
                int nDefault,
                string lpFileName);

            public string GetValueString(string section, string key, string fileName) {
                var sb = new StringBuilder(1024);
                GetPrivateProfileString(section, key, "", sb, Convert.ToUInt32(sb.Capacity), fileName);
                return sb.ToString();
            }

            public int GetValueInt(string section, string key, string fileName) {
                var sb = new StringBuilder(1024);
                return (int)GetPrivateProfileInt(section, key, 0, fileName);
            }
        }

        int UDP_PORT = 62400;
        int Serial_PORT = 0;
        int UDP_sendPORT = 0;
        string UDP_sendIP = "192.168.0.5";
        SolidColorBrush centerBrushColor = new SolidColorBrush(), mainBrushColor = new SolidColorBrush(), rpmBrushColor = new SolidColorBrush(), slipBrushColor = new SolidColorBrush();
        bool overspeed = false;
        int speed = 0, maxRpm = 1, curtRpm = 1, minRpm = 1, slip = 0, performanceindex = 0, drivetype = 0, carclass = 0;
        float slipfl, slipfr, sliprl, sliprr, speedFloat, boost;
        float revLimit = .82f, shitfChange = .76f;
        float rateSpeed = 0;
        int rateRPM = 0;
        System.Diagnostics.Stopwatch rateStopwach = new System.Diagnostics.Stopwatch();
        int rateReload = 200;

        private void LeftBottom_Click(object sender, RoutedEventArgs e) {
            windowMain.Top = SystemParameters.WorkArea.Height - windowMain.Height + 40;
            windowMain.Left = SystemParameters.WorkArea.Width - windowMain.Width + 40;
        }

        private void button_sizeDown(object sender, RoutedEventArgs e) {
            if (sliderWindowSize.Value > .5)
                sliderWindowSize.Value = sliderWindowSize.Value - .1;
        }

        private void button_sizeUp(object sender, RoutedEventArgs e) {
            if (sliderWindowSize.Value < 1.0)
                sliderWindowSize.Value = sliderWindowSize.Value + .1;
        }

        byte accel = 0, fbrake = 0, hbrake = 0, clutch = 0, gear = 0;
        sbyte steer = 0;
        bool isRace = false, isSlip = false;

        public MainWindow() {
            centerBrushColor.Color = Color.FromArgb(0x4C, 0x00, 0x7A, 0xFF);//4C007AFF, CCFFFFFF
            mainBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            rpmBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            slipBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            //System.Threading.Thread.Sleep(5000);
            InitializeComponent();

            // var configs = File.ReadAllText("./config.json");


            // dataConfig jsonData = new dataConfig();
            // jsonData = JsonSerializer.Deserialize<dataConfig>(configs);

            // revLimit = jsonData.RevLimitLevel;
            // windowMain.Background.Opacity = jsonData.BackgroundOpacity / 100;
            // chkFront.IsChecked = jsonData.Topmost;
            // chkRedGear.IsChecked = jsonData.RedGear;


            //  var ms = new MemoryStream(Encoding.UTF8.GetBytes((configs)));



            MouseLeftButtonDown += new MouseButtonEventHandler(Window1_MouseLeftButtonDown);
            if (System.IO.File.Exists("./config.ini"))
                set_config("./config.ini");
            ListenMessage();
            //SendMessage();
        }


        public async void ListenMessage() {
            // 接続ソケットの準備
            var remote = new UdpClient(UDP_sendIP, UDP_sendPORT);
            var client = new UdpClient(UDP_PORT);


            // 受信したデータを変換
            byte[] data;
            byte[] carCahnge = new byte[3];


            while (true) {
                // データ受信待機
                var result = await client.ReceiveAsync();
                data = result.Buffer;

                if (UDP_sendPORT != 0)
                    await remote.SendAsync(data, data.Length);

                isRace = (BitConverter.ToBoolean(data, 0x00));
                if (!isRace) {
                    //textPause.Visibility = Visibility.Visible;
                    rateStopwach.Stop();
                    continue;
                }
                rateStopwach.Start();
                //textPause.Visibility = Visibility.Hidden;
                
                if ((carCahnge[0] != data[0xD8]) || (carCahnge[1] != data[0xDC]) || (carCahnge[2] != data[0xE0])) {
                    carclass = BitConverter.ToInt32(data, 0xD8);
                    performanceindex = BitConverter.ToInt32(data, 0xDC);
                    drivetype = BitConverter.ToInt32(data, 0xE0);
                    OnRecieve_info();
                }

                speedFloat = (float)(BitConverter.ToSingle(data, 0x100) * 3.6);
                speed = (int)speedFloat;
                maxRpm = (int)(BitConverter.ToSingle(data, 0x08)) + 1;
                minRpm = (int)(BitConverter.ToSingle(data, 0x0C));
                curtRpm = (int)(BitConverter.ToSingle(data, 0x10));
                boost = (float)(Math.Round(BitConverter.ToSingle(data, 0x11C) / 14.5, 2, MidpointRounding.AwayFromZero));
                slipfl = Math.Abs(BitConverter.ToSingle(data, 0x54));
                slipfr = Math.Abs(BitConverter.ToSingle(data, 0x58));
                sliprl = Math.Abs(BitConverter.ToSingle(data, 0x5C));
                sliprr = Math.Abs(BitConverter.ToSingle(data, 0x60));
                if (slipfl > 1.0 || slipfr > 1.0 || sliprl > 1.0 || sliprr > 1.0)
                    isSlip = true;
                else
                    isSlip = false;
                slip = (int)((slipfl + slipfr + sliprl + sliprr) * 256 / 4);
                accel = data[0x13B];
                fbrake = data[0x13C];
                clutch = data[0x13D];
                hbrake = data[0x13E];
                gear = data[0x13F];
                steer = (sbyte)data[0x140];
                //RawData.Text = BitConverter.ToString(data);

                OnRecieve();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            System.Windows.Application.Current.Shutdown();
        }

        private void sliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            windowMain.Background.Opacity = sliderOpacity.Value / 100;
        }
        private void sliderWindowSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            windowMain.VisualTransform = new ScaleTransform(sliderWindowSize.Value, sliderWindowSize.Value);
        }

        private void OnRecieve_info() {
            textInfo.Text = "";
            switch (drivetype) {
                case 0:
                    textInfo.Text += "FDW";
                    break;
                case 1:
                    textInfo.Text += "RWD";
                    break;
                default:
                    textInfo.Text += "AWD";
                    break;
            }
            textInfo.Text += ", perf : " + performanceindex.ToString() + ", class : ";
            switch (carclass) {
                case 0:
                    textInfo.Text += "D";
                    break;
                case 1:
                    textInfo.Text += "C";
                    break;
                case 2:
                    textInfo.Text += "B";
                    break;
                case 3:
                    textInfo.Text += "A";
                    break;
                case 4:
                    textInfo.Text += "S1";
                    break;
                case 5:
                    textInfo.Text += "S2";
                    break;
                default:
                    textInfo.Text += "X";
                    break;
            }

        }

        private void OnRecieve() {

            switch (gear) {
                case 0:
                    textGear.Text = "R";
                    break;
                case 11:
                    textGear.Text = "N";
                    break;
                default:
                    textGear.Text = gear.ToString();
                    break;
            }


            textSpeed.Text = speed.ToString().PadLeft(3, '0');

            textCurtRpm.Text = curtRpm.ToString().PadLeft((maxRpm == 0) ? 1 : ((byte)Math.Log10(maxRpm) + 1), '0');
            textMaxtRpm.Text = maxRpm.ToString();
            //ブースト(ターボ)計
            textboost.Text = boost.ToString("F2");

            if (boost <= -0.7)
            {
                boostneedle.Angle = -216;
            }
            else if (boost >= 2)
            {
                boostneedle.Angle = 36;
            }else if (boost <= 0)
            {
                boostneedle.Angle = (boost * 154.2) - 108;
            }else
            {
                boostneedle.Angle = (boost * 72) - 108;  
            }
            {

            }


            if (maxRpm != 0)
            {
                var rpm = (curtRpm * 256) / maxRpm;
                recRpm.Width = rpm;
                recRmpMin.Width = (minRpm * 256) / maxRpm;
                if (maxRpm * revLimit <= curtRpm)
                {
                    rpmBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0x00, 0x00);
                    recRpm.Fill = rpmBrushColor;
                    if (overspeed)
                        textRpm.Foreground = Brushes.Red;
                }
                else if (maxRpm * shitfChange <= curtRpm)
                {
                    rpmBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0x8C, 0x00);
                    recRpm.Fill = rpmBrushColor;
                    if (overspeed)
                        textRpm.Foreground = Brushes.DarkOrange;
                }
                else
                {
                    rpmBrushColor.Color = Color.FromArgb(0xCC, (byte)rpm, (byte)(256 - rpm), (byte)(256 - rpm));
                    recRpm.Fill = rpmBrushColor;
                    textRpm.Foreground = Brushes.White;
                }
            }


            if (rateStopwach.Elapsed.TotalMilliseconds >= rateReload) {
                int tmpRPM;
                float tmpSpeed;
                string tmps;

                tmpRPM = curtRpm - rateRPM;
                if (tmpRPM < 0) {
                    tmpRPM *= -1;
                    if (tmpRPM >= 10000)
                        tmpRPM = 9999;
                    tmps = "▼" + String.Format("{0:D3}", tmpRPM);
                    textRateRPM.Foreground = Brushes.OrangeRed;

                }
                else if (tmpRPM > 0) {
                    if (tmpRPM >= 10000)
                        tmpRPM = 9999;
                    tmps = "▲" + String.Format("{0:D3}", tmpRPM);
                    textRateRPM.Foreground = Brushes.Aqua;
                }
                else {
                    tmps = "▲▼ 0";
                    textRateRPM.Foreground = Brushes.White;
                }
                textRateRPM.Text = tmps;


                tmps = "";
                tmpSpeed = (speedFloat - rateSpeed) * 10000;
                tmpRPM = (int)(Math.Ceiling(tmpSpeed) / 10);
                if (tmpRPM < 0) {
                    tmpRPM *= -1;
                    if (tmpRPM >= 10000)
                        tmpRPM = 9999;
                    tmps = "▼" + String.Format("{0:D4}", tmpRPM);
                    textRateSpeed.Foreground = Brushes.OrangeRed;

                }
                else if (tmpRPM > 0) {
                    if (tmpRPM >= 10000)
                        tmpRPM = 9999;
                    tmps = "▲" + String.Format("{0:D4}", tmpRPM);
                    textRateSpeed.Foreground = Brushes.Aqua;
                }
                else {
                    tmps = "▲▼ 00";
                    textRateSpeed.Foreground = Brushes.White;
                }
                textRateSpeed.Text = tmps;

                rateStopwach.Restart();
            }
            rateRPM = curtRpm;
            rateSpeed = speedFloat;



            textSpeed.Text = speed.ToString().PadLeft(3, '0');
            if (maxRpm != 0) {
                var rpm = (curtRpm * 256) / maxRpm;
                recRpm.Width = rpm;
                recRmpMin.Width = (minRpm * 256) / maxRpm;
                if (maxRpm * revLimit <= curtRpm) {
                    rpmBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0x00, 0x00);
                    recRpm.Fill = rpmBrushColor;
                    textRpm.Foreground = Brushes.Red;
                    textGear.Foreground = Brushes.Red;
                }
                else if (maxRpm * shitfChange <= curtRpm) {
                    rpmBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0x8C, 0x00);
                    recRpm.Fill = rpmBrushColor;
                    textRpm.Foreground = Brushes.DarkOrange;
                    textGear.Foreground = Brushes.DarkOrange;
                }
                else {
                    rpmBrushColor.Color = Color.FromArgb(0xCC, (byte)rpm, (byte)(256 - rpm), (byte)(256 - rpm));
                    recRpm.Fill = rpmBrushColor;
                    textRpm.Foreground = Brushes.White;
                    textGear.Foreground = Brushes.White;
                }
            }

            if (slip <= 1)
                textSlip.Text = "Flying!!!";
            else
                textSlip.Text = "TireSlip";
            slip = (int)(slip * 0.5);
            if (slip >= 256)
                slip = 255;
            recSlip.Width = slip;
            slipBrushColor.Color = Color.FromArgb(0xCC, (byte)slip, (byte)(256 - slip), (byte)(256 - slip));
            recSlip.Fill = slipBrushColor;
            if (isSlip)
                textSlip.Foreground = Brushes.Red;
            else
                textSlip.Foreground = Brushes.White;
            //タイヤゲージ
            textTireFL.Text = (Math.Round((slipfl * 100), 2, MidpointRounding.AwayFromZero)).ToString("F2") + "%";
            textTireRL.Text = (Math.Round((sliprl * 100), 2, MidpointRounding.AwayFromZero)).ToString("F2") + "%";
            textTireFR.Text = (Math.Round((slipfr * 100), 2, MidpointRounding.AwayFromZero)).ToString("F2") + "%";
            textTireRR.Text = (Math.Round((sliprr * 100), 2, MidpointRounding.AwayFromZero)).ToString("F2") + "%";
            if (slipfl >= 1)
            {
                recFL.Height = 36;
            }
            else
            {
                recFL.Height = slipfl * 36;
            }
            if (sliprl >= 1)
            {
                recRL.Height = 36;
            }
            else
            {
                recRL.Height = sliprl * 36;
            }
            if (slipfr >= 1)
            {
                recFR.Height = 36;
            }
            else
            {
                recFR.Height = slipfr * 36;
            }
            if (sliprr >= 1)
            {
                recRR.Height = 36;
            }
            else
            {
                recRR.Height = sliprr * 36;
            }
            recAccel.Width = accel;
            recFootBrake.Width = fbrake;
            recClutch.Width = clutch;
            recHandBrake.Width = hbrake;
            recSteer.Width = steer + 128;
            if (steer == 0)
                recSteer.Fill = centerBrushColor;
            else
                recSteer.Fill = mainBrushColor;

        }
        private void chkRedGear_click(object sender, RoutedEventArgs e) {
            if (this.chkRedGear.IsChecked == true)
                this.overspeed = true;
            else
                this.overspeed = false;
        }
        private void chkFromt_click(object sender, RoutedEventArgs e) {
            if (this.chkFront.IsChecked == true)
                this.windowMain.Topmost = true;
            else
                this.windowMain.Topmost = false;
        }

        void Window1_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            this.DragMove();
        }
        private void Slider_ValueChanged_shift(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (sliderRevLimit.Value < sliderShiftChangeLimit.Value) {
                sliderRevLimit.Value = (float)(sliderShiftChangeLimit.Value);
                revLimit = (float)(sliderShiftChangeLimit.Value);
            }
            shitfChange = (float)sliderShiftChangeLimit.Value;
        }


        private void Slider_ValueChanged_rev(object sender, RoutedPropertyChangedEventArgs<double> e) {
            try {
                if (sliderRevLimit.Value < sliderShiftChangeLimit.Value) {
                    sliderShiftChangeLimit.Value = (float)(sliderRevLimit.Value);
                    shitfChange = (float)(sliderRevLimit.Value);
                }
                revLimit = (float)sliderRevLimit.Value;
            }
            catch (NullReferenceException) {
            }
        }


        private void set_config(string iniPath) {
            var program = new ReadINI();


            try {
                string value = program.GetValueString("communication", "UDP_PORT", iniPath);
                if (value != "-1")
                    UDP_PORT = int.Parse(value);

                value = program.GetValueString("communication", "UDP_sendPORT", iniPath);
                if (value != "-1")
                    UDP_sendPORT = int.Parse(value);

                value = program.GetValueString("communication", "UDP_sendIP", iniPath);
                if (value != "-1")
                    UDP_sendIP = value;

                value = program.GetValueString("communication", "Serial_Port", iniPath);
                if (value != "-1")
                    Serial_PORT = int.Parse(value);




                value = program.GetValueString("driving_support", "Rate_Reload", iniPath);
                if (value != "-1")
                    rateReload = int.Parse(value);

                value = program.GetValueString("driving_support", "RevLimit", iniPath);
                if (value != "-1") {
                    revLimit = float.Parse(value);
                    sliderRevLimit.Value = float.Parse(value);
                }

                value = program.GetValueString("driving_support", "ShitfChange", iniPath);
                if (value != "-1") {
                    shitfChange = float.Parse(value);
                    sliderShiftChangeLimit.Value = float.Parse(value);
                }




                value = program.GetValueString("exterior", "TopMost", iniPath);
                if (value != "True" && value != "true") {
                    this.windowMain.Topmost = false;
                    this.chkFront.IsChecked = false;
                }

                value = program.GetValueString("exterior", "ChangeGearColorByRev", iniPath);
                if (value != "True" && value != "true") {
                    this.overspeed = false;
                    this.chkRedGear.IsChecked = false;
                }

                value = program.GetValueString("exterior", "BackgroundOpacity", iniPath);
                if (value != "-1") {
                    sliderOpacity.Value = int.Parse(value);
                    windowMain.Background.Opacity = float.Parse(value) / 100.0;
                }
            }
            catch {
                System.Windows.Forms.MessageBox.Show(
                    "Illegal argument was specified in config.ini.\n" +
                    "Check the value of config.ini",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Windows.Application.Current.Shutdown();
            }
        }
    }


}
