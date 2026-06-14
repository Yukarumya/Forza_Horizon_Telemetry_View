using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Security.Cryptography;



namespace ForzaHorizon5Telemetry {
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>

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

            [DllImport("KERNEL32.DLL", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool WritePrivateProfileString(
                string lpAppName,
                string lpKeyName,
                string lpString,
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

            public void WriteValueString(string section, string key, string value, string fileName) {
                WritePrivateProfileString(section, key, value, fileName);
            }
        }

        enum GameVariant {
            Horizon,
            Motorsport
        }

        class DataOffsets {
            public int CarClass { get; set; }
            public int PerformanceIndex { get; set; }
            public int DriveType { get; set; }

            public int SpeedFloat { get; set; }
            public int Power { get; set; }
            public int Torque { get; set; }

            public int MaxRpm { get; set; }
            public int MinRpm { get; set; }
            public int CurtRpm { get; set; }

            public int Boost { get; set; }

            public int SlipFL { get; set; }
            public int SlipFR { get; set; }
            public int SlipRL { get; set; }
            public int SlipRR { get; set; }

            public int SuspFL { get; set; }
            public int SuspFR { get; set; }
            public int SuspRL { get; set; }
            public int SuspRR { get; set; }

            public int Accel { get; set; }
            public int FBrake { get; set; }
            public int Clutch { get; set; }
            public int HBrake { get; set; }
            public int Gear { get; set; }
            public int Steer { get; set; }

            public static DataOffsets ForHorizonDefaults() {
                return new DataOffsets {
                    CarClass = 0xD8,
                    PerformanceIndex = 0xDC,
                    DriveType = 0xE0,

                    SpeedFloat = 0x100,
                    Power = 0x104,
                    Torque = 0x108,

                    MaxRpm = 0x08,
                    MinRpm = 0x0C,
                    CurtRpm = 0x10,

                    Boost = 0x11C,

                    SlipFL = 0x54,
                    SlipFR = 0x58,
                    SlipRL = 0x5C,
                    SlipRR = 0x60,

                    SuspFL = 0x44,
                    SuspFR = 0x48,
                    SuspRL = 0x4C,
                    SuspRR = 0x50,

                    Accel = 0x13B,
                    FBrake = 0x13C,
                    Clutch = 0x13D,
                    HBrake = 0x13E,
                    Gear = 0x13F,
                    Steer = 0x140
                };
            }

            public static DataOffsets ForMotorsportDefaults() {
                // Forza Motorsport のパケット順に基づくバイトオフセット（公式フォーマットから算出）
                return new DataOffsets {
                    // car info
                    CarClass = 216,           // S32 CarClass (decimal 216 = 0xD8)
                    PerformanceIndex = 220,  // S32 CarPerformanceIndex (0xDC)
                    DriveType = 224,         // S32 DrivetrainType (0xE0)

                    // speed/power/torque
                    SpeedFloat = 244,        // F32 Speed
                    Power = 248,             // F32 Power
                    Torque = 252,            // F32 Torque

                    // rpm
                    MaxRpm = 8,              // F32 EngineMaxRpm
                    MinRpm = 12,             // F32 EngineIdleRpm
                    CurtRpm = 16,            // F32 CurrentEngineRpm

                    // boost
                    Boost = 272,             // F32 Boost

                    // slips (TireSlipRatio)
                    SlipFL = 84,             // F32 TireSlipRatioFrontLeft
                    SlipFR = 88,             // F32 TireSlipRatioFrontRight
                    SlipRL = 92,             // F32 TireSlipRatioRearLeft
                    SlipRR = 96,             // F32 TireSlipRatioRearRight

                    // suspensions (normalized)
                    SuspFL = 68,             // F32 NormalizedSuspensionTravelFrontLeft
                    SuspFR = 72,
                    SuspRL = 76,
                    SuspRR = 80,

                    // inputs (Motorsport offsets)
                    Accel = 303,             // U8 Accel
                    FBrake = 304,            // U8 Brake
                    Clutch = 305,            // U8 Clutch
                    HBrake = 306,            // U8 HandBrake
                    Gear = 307,              // U8 Gear
                    Steer = 308              // S8 Steer
                };
            }

            public void OverrideFromConfig(ReadINI ini, string iniPath) {
                // セクション名: offsets
                foreach (var prop in typeof(DataOffsets).GetProperties()) {
                    string key = prop.Name;
                    string val = ini.GetValueString("offsets", key, iniPath);
                    if (!string.IsNullOrWhiteSpace(val) && val != "-1") {
                        try {
                            int num = ParseOffset(val);
                            prop.SetValue(this, num);
                        }
                        catch {
                            // 無効な値は無視
                        }
                    }
                }
            }

            static int ParseOffset(string s) {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                    return Convert.ToInt32(s.Substring(2), 16);
                }
                return int.Parse(s);
            }
        }

        int UDP_PORT = 62400;
        int Serial_PORT = 0;
        int UDP_sendPORT = 0;
        string UDP_sendIP = "192.168.0.5";
        SolidColorBrush centerBrushColor = new SolidColorBrush(), mainBrushColor = new SolidColorBrush(), rpmBrushColor = new SolidColorBrush(), slipBrushColor = new SolidColorBrush();
        bool overspeed = false;
        int speed = 0, maxRpm = 1, curtRpm = 1, minRpm = 1, slip = 0, performanceindex = 0, drivetype = 0, carclass = 0;
        float slipfl, slipfr, sliprl, sliprr, suspfl, suspfr, susprr, susprl, speedFloat, power, torque, boost;
        float revLimit = .82f, shitfChange = .76f;
        float rateSpeed = 0;
        int rateRPM = 0;
        System.Diagnostics.Stopwatch rateStopwach = new System.Diagnostics.Stopwatch();
        int rateReload = 200;

        GameVariant gameVariant = GameVariant.Horizon;
        DataOffsets offsets = DataOffsets.ForHorizonDefaults();

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

        private void button_Motorsportsmode(object sender, RoutedEventArgs e) {
            if (gameVariant == GameVariant.Motorsport)
            {
                gameVariant = GameVariant.Horizon;
                offsets = DataOffsets.ForHorizonDefaults();
                Motorsport_mode.Content = "FH";
            }
            else
            { 
                gameVariant = GameVariant.Motorsport;
                offsets = DataOffsets.ForMotorsportDefaults();
                Motorsport_mode.Content = "FM";
            }
            try
            {
                var ini = new ReadINI();
                ini.WriteValueString("general", "Game", (gameVariant == GameVariant.Motorsport) ? "Motorsport" : "Horizon", "./config.ini");
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("config.ini に書き込めませんでした: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        byte accel = 0, fbrake = 0, hbrake = 0, clutch = 0, gear = 0;
        sbyte steer = 0;
        bool isRace = false, isSlip = false;

        public MainWindow() {
            centerBrushColor.Color = Color.FromArgb(0x4C, 0x00, 0x7A, 0xFF);//4C007AFF, CCFFFFFF
            mainBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            rpmBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            slipBrushColor.Color = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            InitializeComponent();

            MouseLeftButtonDown += new MouseButtonEventHandler(Window1_MouseLeftButtonDown);
            if (System.IO.File.Exists("./config.ini"))
                set_config("./config.ini");
            ListenMessage();
        }


        public async void ListenMessage() {
            var remote = new UdpClient(UDP_sendIP, UDP_sendPORT);
            var client = new UdpClient(UDP_PORT);

            byte[] data;
            byte[] carCahnge = new byte[3];

            while (true) {
                var result = await client.ReceiveAsync();
                data = result.Buffer;

                if (UDP_sendPORT != 0)
                    await remote.SendAsync(data, data.Length);

                // 受信データ長が最小限必要な長さに満たない場合はスキップ
                if (data == null || data.Length < 4) continue;

                isRace = (BitConverter.ToBoolean(data, 0x00));
                if (!isRace) {
                    rateStopwach.Stop();
                    continue;
                }
                rateStopwach.Start();

                // 安全にオフセットへアクセスするヘルパー
                Func<int, bool> HasOffset = (off) => (data != null && data.Length > off);

                try {
                    if (!HasOffset(offsets.CarClass)) { continue; }

                    if ((carCahnge[0] != data[offsets.CarClass]) || (carCahnge[1] != data[offsets.PerformanceIndex]) || (carCahnge[2] != data[offsets.DriveType])) {
                        if (HasOffset(offsets.CarClass + 3)) // Int32 は 4 バイト
                            carclass = BitConverter.ToInt32(data, offsets.CarClass);
                        if (HasOffset(offsets.PerformanceIndex + 3))
                            performanceindex = BitConverter.ToInt32(data, offsets.PerformanceIndex);
                        if (HasOffset(offsets.DriveType + 3))
                            drivetype = BitConverter.ToInt32(data, offsets.DriveType);
                        OnRecieve_info();
                    }

                    if (HasOffset(offsets.SpeedFloat + 3))
                        speedFloat = (float)(BitConverter.ToSingle(data, offsets.SpeedFloat) * 3.6);
                    if (HasOffset(offsets.Power + 3))
                        power = (float)Math.Round(BitConverter.ToSingle(data, offsets.Power) / 735.5, 1, MidpointRounding.AwayFromZero);
                    if (HasOffset(offsets.Torque + 3))
                        torque = (float)Math.Round(BitConverter.ToSingle(data, offsets.Torque) / 9.806652, 1, MidpointRounding.AwayFromZero);
                    if (HasOffset(offsets.MaxRpm + 3))
                        maxRpm = (int)(BitConverter.ToSingle(data, offsets.MaxRpm)) + 1;
                    if (HasOffset(offsets.MinRpm + 3))
                        minRpm = (int)(BitConverter.ToSingle(data, offsets.MinRpm));
                    if (HasOffset(offsets.CurtRpm + 3))
                        curtRpm = (int)(BitConverter.ToSingle(data, offsets.CurtRpm));
                    if (HasOffset(offsets.Boost + 3))
                        boost = (float)(Math.Round(BitConverter.ToSingle(data, offsets.Boost) / 14.5, 2, MidpointRounding.AwayFromZero));

                    if (HasOffset(offsets.SlipFL + 3)) slipfl = Math.Abs(BitConverter.ToSingle(data, offsets.SlipFL));
                    if (HasOffset(offsets.SlipFR + 3)) slipfr = Math.Abs(BitConverter.ToSingle(data, offsets.SlipFR));
                    if (HasOffset(offsets.SlipRL + 3)) sliprl = Math.Abs(BitConverter.ToSingle(data, offsets.SlipRL));
                    if (HasOffset(offsets.SlipRR + 3)) sliprr = Math.Abs(BitConverter.ToSingle(data, offsets.SlipRR));
                    if (HasOffset(offsets.SuspFL + 3)) suspfl = Math.Abs(BitConverter.ToSingle(data, offsets.SuspFL));
                    if (HasOffset(offsets.SuspFR + 3)) suspfr = Math.Abs(BitConverter.ToSingle(data, offsets.SuspFR));
                    if (HasOffset(offsets.SuspRL + 3)) susprl = Math.Abs(BitConverter.ToSingle(data, offsets.SuspRL));
                    if (HasOffset(offsets.SuspRR + 3)) susprr = Math.Abs(BitConverter.ToSingle(data, offsets.SuspRR));

                    if (slipfl > 1.0 || slipfr > 1.0 || sliprl > 1.0 || sliprr > 1.0)
                        isSlip = true;
                    else
                        isSlip = false;
                    slip = (int)((slipfl + slipfr + sliprl + sliprr) * 256 / 4);

                    if (HasOffset(offsets.Accel)) accel = data[offsets.Accel];
                    if (HasOffset(offsets.FBrake)) fbrake = data[offsets.FBrake];
                    if (HasOffset(offsets.Clutch)) clutch = data[offsets.Clutch];
                    if (HasOffset(offsets.HBrake)) hbrake = data[offsets.HBrake];
                    if (HasOffset(offsets.Gear)) gear = data[offsets.Gear];
                    if (HasOffset(offsets.Steer)) steer = (sbyte)data[offsets.Steer];

                    //RawData.Text = BitConverter.ToString(data);

                    OnRecieve();
                }
                catch {
                    // 受信データに想定外の長さ・値が来た場合はスキップして続行
                    continue;
                }
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


            //textSpeed.Text = speedFloat.ToString().PadLeft(3, '0');

            textCurtRpm.Text = curtRpm.ToString().PadLeft((maxRpm == 0) ? 1 : ((byte)Math.Log10(maxRpm) + 1), '0');
            textMaxtRpm.Text = maxRpm.ToString();
            textPower.Text = power.ToString("F1") + " PS";
            textTorque.Text = torque.ToString("F1") + " kgf/m";
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



            textSpeed.Text = Math.Floor(speedFloat).ToString().PadLeft(3, '0');
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

            //サスペンション
            textSuspFL.Text = (Math.Round((suspfl * 100), 1, MidpointRounding.AwayFromZero)).ToString("F1") + "%";
            textSuspRL.Text = (Math.Round((susprl * 100), 1, MidpointRounding.AwayFromZero)).ToString("F1") + "%";
            textSuspFR.Text = (Math.Round((suspfr * 100), 1, MidpointRounding.AwayFromZero)).ToString("F1") + "%";
            textSuspRR.Text = (Math.Round((susprr * 100), 1, MidpointRounding.AwayFromZero)).ToString("F1") + "%";
            recSFL.Height = suspfl * 33;
            recSRL.Height = susprl * 33;
            recSFR.Height = suspfr * 33;
            recSRR.Height = susprr * 33;

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


            try
            {
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
                if (value != "-1")
                {
                    revLimit = float.Parse(value);
                    sliderRevLimit.Value = float.Parse(value);
                }

                value = program.GetValueString("driving_support", "ShitfChange", iniPath);
                if (value != "-1")
                {
                    shitfChange = float.Parse(value);
                    sliderShiftChangeLimit.Value = float.Parse(value);
                }

                value = program.GetValueString("exterior", "TopMost", iniPath);
                if (value != "True" && value != "true")
                {
                    this.windowMain.Topmost = false;
                    this.chkFront.IsChecked = false;
                }

                value = program.GetValueString("exterior", "ChangeGearColorByRev", iniPath);
                if (value != "True" && value != "true")
                {
                    this.overspeed = false;
                    this.chkRedGear.IsChecked = false;
                }

                value = program.GetValueString("exterior", "BackgroundOpacity", iniPath);
                if (value != "-1")
                {
                    sliderOpacity.Value = int.Parse(value);
                    windowMain.Background.Opacity = float.Parse(value) / 100.0;
                }

                // ゲーム種別の読み取り (general セクション)
                value = program.GetValueString("general", "Game", iniPath);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (value.Equals("Motorsport", StringComparison.OrdinalIgnoreCase))
                    {
                        gameVariant = GameVariant.Motorsport;
                        Motorsport_mode.Content = "FM";
                    }
                    else
                    {
                        gameVariant = GameVariant.Horizon;
                        Motorsport_mode.Content = "FH";
                    }

                    // デフォルトオフセットの選択
                    offsets = (gameVariant == GameVariant.Motorsport) ? DataOffsets.ForMotorsportDefaults() : DataOffsets.ForHorizonDefaults();

                    // config.ini の offsets セクションで個別上書き可能
                    offsets.OverrideFromConfig(program, iniPath);

                }
            }
            catch
            {
                System.Windows.Forms.MessageBox.Show(
                    "Illegal argument was specified in config.ini.\n" +
                    "Check the value of config.ini",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Windows.Application.Current.Shutdown();
            }
        }
    }


}
