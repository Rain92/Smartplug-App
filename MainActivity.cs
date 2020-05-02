using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Net.Wifi;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using AlertDialog = Android.App.AlertDialog;
using Debug = System.Diagnostics.Debug;
using Android.Util;
using System.Diagnostics;
using Timer = System.Timers.Timer;
using MikePhil.Charting.Charts;
using MikePhil.Charting.Components;
using Android.Graphics;
using MikePhil.Charting.Data;

namespace SmartPlugAndroid
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        const string AKK = "Success!";
        const string DEFAULTPASSWORD = "password";

        Esp32Commuicator communicator = new Esp32Commuicator();

        ControlSettings settings;

        bool countdownTimerRunning = false;
        DateTime countdownTimerEndTime;
        Timer countdownTimer;

        bool powerMonitorActive = false;
        Timer powerMonitorUpdateTimer;
        PowerUsageHistory powerHistory;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            settings = ControlSettings.Constructor();

            FloatingActionButton fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
            fab.Click += FabOnClickAsync;

            FindViewById<ToggleButton>(Resource.Id.toggleButtonOff).Click += ToggleButtonPressed;
            FindViewById<ToggleButton>(Resource.Id.toggleButtonOn).Click += ToggleButtonPressed;
            FindViewById<ToggleButton>(Resource.Id.toggleButtonTime).Click += ToggleButtonPressed;

            FindViewById<Button>(Resource.Id.buttonSipleTimer).Click += SimpleTimerButtonPressed;

            FindViewById<ImageButton>(Resource.Id.imageButtonEditName).Click += (e, o) => RenameDevice();
            FindViewById<Spinner>(Resource.Id.spinnerDevice).ItemSelected += SpinnerDevice_SelectedItem;


            Handler uihandler = new Handler(Looper.MainLooper);
            communicator.FeedbackCallback += (string msg) =>
            {
                uihandler.Post(() => ShowToast(msg));
            };
            communicator.OnNewDeviceDiscovered += (string name) =>
            {
                uihandler.Post(() => OnDeviceDiscovered(name));
            };

            communicator.SendDiscoveryCommand();

            countdownTimer = new Timer(1000);
            countdownTimer.Elapsed += (o, e) => uihandler.Post(() => UpdateCountdown());
            countdownTimer.AutoReset = true;

            powerMonitorUpdateTimer = new Timer(2000);
            powerMonitorUpdateTimer.Elapsed += (o, e) => uihandler.Post(() => UpdatePowerMonitor());
            powerMonitorUpdateTimer.AutoReset = true;

            ShowPowerMonitor(powerMonitorActive);
            InitPowerMonitorChart();
        }

        private void SpinnerDevice_SelectedItem(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            var name = FindViewById<Spinner>(Resource.Id.spinnerDevice).GetItemAtPosition(e.Position).ToString();
            Debug.WriteLine("Selected item! " + name);

            if (communicator.ActiveDevice.Item1 != name)
            {
                communicator.SetActiveDevice(name);
                RecieveSettings();
                RecieveCountdownTime();
            }
        }

        public void ShowToast(string msg)
        {
            var toast = Toast.MakeText(this, msg, ToastLength.Short);
            ViewGroup group = (ViewGroup)toast.View;
            TextView messageTextView = (TextView)group.GetChildAt(0);
            messageTextView.SetTextSize(ComplexUnitType.Sp, 14);
            toast.Show();
        }

        private void RenameDevice()
        {
            if (communicator.ActiveDevice.Item1 == null)
                return;

            ShowInputDialog("Rename Device", "Enter new device Name", communicator.ActiveDevice.Item1, Android.Text.InputTypes.TextFlagNoSuggestions, (name) =>
             {
                 if (name != "")
                 {
                     try
                     {
                         settings.NetIdStr = name;
                     }
                     catch (ArgumentException)
                     {
                         ShowToast("Invalid name");
                         return;
                     }
                     SendSettings();
                     communicator.ClearDevices();
                     communicator.SendDiscoveryCommand();

                     ShowToast($"Renamed device to {name}");
                 }
                 else
                 {
                     ShowToast("Invalid name");
                 }
             });
        }

        private void UpdateSpinnerEntries()
        {
            Spinner spinner = FindViewById<Spinner>(Resource.Id.spinnerDevice);

            var items = communicator.RegisteredDevices.Keys.ToList();
            var adapter = new ArrayAdapter(this, Android.Resource.Layout.SimpleSpinnerItem, items);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinner.Adapter = adapter;
            int i = items.FindIndex((s) => s == communicator.ActiveDevice.Item1);
            if (i > 0)
                spinner.SetSelection(i);
        }

        void OnDeviceDiscovered(string name)
        {
            if (communicator.ActiveDevice.Item1 == null)
            {
                communicator.SetActiveDevice(name);
                RecieveSettings();
                RecieveCountdownTime();
            }
            UpdateSpinnerEntries();
        }

        void SendSettings()
        {
            communicator.SendCommandData("setsettings", settings.ToByteArray());
        }

        void RecieveSettings()
        {
            try
            {
                var r = communicator.SendCommand("getsettings");
                settings = ControlSettings.FromByteArray(r);
                SetToggleButtonState(settings.Mode);
                UpdateTableView();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Couldn't sync settings: " + e.ToString());
            }
        }

        void RecieveMode()
        {
            try
            {
                int i = Int32.Parse(communicator.SendCommandParsed("mode"));
                settings.Mode = (Mode)i;
                SetToggleButtonState(settings.Mode);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Couldn't sync mode: " + e.ToString());
            }
        }

        void SimpleTimerButtonPressed(object sender, EventArgs eventArgs)
        {
            if (!countdownTimerRunning)
            {
                ShowInputDialog("Countdown timer", "Turning off device after set minutes:", "10", Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal, (input) =>
                {
                    int minutes = 0;
                    if (int.TryParse(input, out minutes) && minutes > 0)
                    {
                        if (SendCountdownTime(minutes * 60))
                        {
                            RecieveCountdownTime();
                            ShowToast($"Turning off device in {minutes} minutes");
                        }
                    }
                    else
                    {
                        ShowToast("Invalid time");
                    }
                });
            }
            else
            {
                SendCountdownTime(0);
                RecieveCountdownTime();
            }
        }

        bool SendCountdownTime(int seconds)
        {
            var data = new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>("time", (seconds).ToString()) };
            var res = communicator.SendCommandDataParsed("setturnoffcountdown", data);
            return res == AKK;
        }
        void RecieveCountdownTime()
        {
            var res = communicator.SendCommandParsed("getturnoffcountdown");
            int seconds = 0;
            if (res != "0" && int.TryParse(res, out seconds))
                StartCountdown(seconds + 1);
            else
                EndCountdown();
        }

        void StartCountdown(int seconds)
        {
            countdownTimerRunning = true;
            countdownTimer.Start();
            countdownTimerEndTime = DateTime.Now.AddSeconds(seconds);
            FindViewById<Button>(Resource.Id.buttonSipleTimer).SetText("Stop", TextView.BufferType.Normal);
        }

        void UpdateCountdown()
        {
            if (!countdownTimerRunning)
                return;

            if (countdownTimerEndTime > DateTime.Now)
            {
                var rem = countdownTimerEndTime - DateTime.Now;
                FindViewById<TextView>(Resource.Id.textViewSimpleTimerInfo).SetText($"Turning off after {rem.Minutes}:{rem.Seconds.ToString("D2")}", TextView.BufferType.Normal);
            }
            else
            {
                EndCountdown();
                System.Threading.Thread.Sleep(1100);
                RecieveMode();
            }
        }

        void EndCountdown()
        {
            countdownTimerRunning = false;
            countdownTimer.Stop();
            FindViewById<Button>(Resource.Id.buttonSipleTimer).SetText("Start", TextView.BufferType.Normal);
            FindViewById<TextView>(Resource.Id.textViewSimpleTimerInfo).SetText("Turn off after countdown", TextView.BufferType.Normal);
        }

        void SetToggleButtonState(Mode mode)
        {
            FindViewById<ToggleButton>(Resource.Id.toggleButtonOff).Checked = false;
            FindViewById<ToggleButton>(Resource.Id.toggleButtonOn).Checked = false;
            FindViewById<ToggleButton>(Resource.Id.toggleButtonTime).Checked = false;

            switch (mode)
            {
                case Mode.Off:
                    FindViewById<ToggleButton>(Resource.Id.toggleButtonOff).Checked = true;
                    FindViewById<LinearLayout>(Resource.Id.linearLayoutSimpleTimer).Visibility = ViewStates.Invisible;
                    FindViewById<ScrollView>(Resource.Id.scrollViewTimeTable).Visibility = ViewStates.Gone;
                    break;
                case Mode.On:
                    FindViewById<ToggleButton>(Resource.Id.toggleButtonOn).Checked = true;
                    FindViewById<LinearLayout>(Resource.Id.linearLayoutSimpleTimer).Visibility = ViewStates.Visible;
                    FindViewById<ScrollView>(Resource.Id.scrollViewTimeTable).Visibility = ViewStates.Gone;
                    break;
                case Mode.Timer:
                    FindViewById<ToggleButton>(Resource.Id.toggleButtonTime).Checked = true;
                    FindViewById<LinearLayout>(Resource.Id.linearLayoutSimpleTimer).Visibility = ViewStates.Invisible;
                    FindViewById<ScrollView>(Resource.Id.scrollViewTimeTable).Visibility = ViewStates.Visible;
                    break;
            }
        }

        private void ToggleButtonPressed(object sender, EventArgs eventArgs)
        {
            var target = sender as ToggleButton;
            var command = target.TextOn.ToLower();

            if (command != "on")
                EndCountdown();

            string s = communicator.SendCommandParsed(command);

            if (s == AKK)
            {
                RecieveMode();
                if (command == "on")
                    RecieveCountdownTime();
            }
            else
            {
                target.Toggle();
            }
        }

        public void ShowInputDialog(string title, string message, string defaultInput, Android.Text.InputTypes inputTypes, Action<string> acceptAction)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(this);
            builder.SetTitle(title);
            builder.SetMessage(message);

            var inputText = new EditText(this);
            inputText.SetRawInputType(inputTypes);
            inputText.Text = defaultInput;
            inputText.TextAlignment = TextAlignment.Center;
            builder.SetView(inputText);

            builder.SetPositiveButton("OK", delegate { acceptAction(inputText.Text); });
            builder.SetNegativeButton("Cancel", delegate { });
            Dialog dialog = builder.Create();
            dialog.Show();
        }


        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }
        private string GetCurrentSSID()
        {
            string ssid = "";
            WifiManager wifiManager = (WifiManager)(Application.Context.GetSystemService(Android.Content.Context.WifiService));
            WifiInfo info = wifiManager.ConnectionInfo;
            int networkId = info.NetworkId;

            IList<WifiConfiguration> netConfList = wifiManager.ConfiguredNetworks;

            foreach (WifiConfiguration wificonf in netConfList)
            {
                if (wificonf.NetworkId == networkId)
                {
                    ssid = wificonf.Ssid.Replace("\"", "");
                    break;
                }
            }
            return ssid;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_smartconfig)
            {
                WifiManager wifiManager = (WifiManager)(ApplicationContext.GetSystemService(Android.Content.Context.WifiService));
                if (wifiManager == null)
                    return false;
                var ssid = GetCurrentSSID();
                var bssid = wifiManager.ConnectionInfo.BSSID;

                ShowInputDialog("Send Wifi Config", $"SSID: {ssid}\nEnter password:", DEFAULTPASSWORD, Android.Text.InputTypes.TextVariationVisiblePassword, (password) =>
                  {
                      communicator.SendSmartConfig(ssid, bssid, password);
                  });
            }
            if (id == Resource.Id.action_discover)
            {
                communicator.SendDiscoveryCommand();
                return true;
            }
            if (id == Resource.Id.action_powermonitor)
            {
                powerMonitorActive = !powerMonitorActive;
                ShowPowerMonitor(powerMonitorActive);

                item.SetTitle(powerMonitorActive ? Resource.String.action_powermonitor_hide : Resource.String.action_powermonitor_show);

                return true;
            }

            return true;
        }

        private Task<Time?> ShowTimePickerDialog(string message = "", int initialHour = 12, int initialMinute = 0)
        {
            var tcs = new TaskCompletionSource<Time?>();

            TimePickerDialog dialog = new TimePickerDialog
                (this, AlertDialog.ThemeHoloLight,
                (s, e) => { tcs.TrySetResult(new Time { Hour = (byte)e.HourOfDay, Minute = (byte)e.Minute }); },
                initialHour, initialMinute, true);
            if (message != "")
                dialog.SetMessage(message);
            dialog.CancelEvent += (s, e) => { tcs.TrySetResult(null); };
            dialog.DismissEvent += (s, e) => { tcs.TrySetResult(null); };
            dialog.Show();

            return tcs.Task;
        }
        private async Task<TimeInterval> PickTimeInterval(DayOfWeek day)
        {
            TimeInterval interval = new TimeInterval();

            var from = await ShowTimePickerDialog("Select start time.");
            if (!from.HasValue)
                return interval;
            var to = await ShowTimePickerDialog("Select end time.", from.Value.Hour, from.Value.Minute);
            if (!to.HasValue)
                return interval;
            if (CheckValidInterval(from.Value, to.Value))
            {
                interval.From = from.Value;
                interval.To = to.Value;
                interval.Weekday = day;
                interval.Active = true;
            }

            return interval;
        }

        private bool CheckValidInterval(Time from, Time to)
        {
            return from < to;
        }

        private async void EditInterval(TimeInterval interval, bool editFrom)
        {
            for (int i = 0; i < settings.TimeIntervals.Length; i++)
            {
                if (settings.TimeIntervals[i].Equals(interval))
                {
                    Time time = editFrom ? interval.From : interval.To;

                    var newtime = await ShowTimePickerDialog(
                        "Edit " + (editFrom ? "start" : "end") + " time.", time.Hour, time.Minute);
                    if (!newtime.HasValue)
                        return;

                    TimeInterval newinterval = new TimeInterval()
                    {
                        Active = true,
                        From = editFrom ? newtime.Value : interval.From,
                        To = !editFrom ? newtime.Value : interval.To,
                        Weekday = interval.Weekday
                    };

                    if (CheckValidInterval(newinterval.From, newinterval.To))
                    {
                        RemoveInterval(interval);
                        AddInterval(newinterval);
                    }
                    else
                    {
                        ShowToast($"Invalid timespan!");
                    }
                }
            }
        }
        bool IntervalsOverlap(TimeInterval i1, TimeInterval i2)
        {
            return (i1.From >= i2.From && i1.From <= i2.To) ||
                   (i1.To >= i2.From && i1.To <= i2.To);
        }

        private async void AddIntervalDialog(DayOfWeek day)
        {
            TimeInterval interval = await PickTimeInterval(day);
            if (!interval.Active)
            {
                ShowToast($"Invalid timespan!");
            }
            else
            {
                AddInterval(interval);
            }
        }
        private void AddInterval(TimeInterval interval)
        {
            for (int i = 0; i < settings.TimeIntervals.Length; i++)
            {
                if (settings.TimeIntervals[i].Active)
                {
                    // merge
                    if (settings.TimeIntervals[i].Weekday == interval.Weekday && IntervalsOverlap(settings.TimeIntervals[i], interval))
                    {
                        settings.TimeIntervals[i].From = settings.TimeIntervals[i].From < interval.From ?
                                                         settings.TimeIntervals[i].From : interval.From;
                        settings.TimeIntervals[i].To = settings.TimeIntervals[i].To > interval.To ?
                                                       settings.TimeIntervals[i].To : interval.To;
                        SendSettings();
                        RecieveSettings();
                        ShowToast($"Saved merged time: From {settings.TimeIntervals[i].From} to {settings.TimeIntervals[i].To}");
                        return;
                    }
                    // insert
                    else if (settings.TimeIntervals[i].Weekday > interval.Weekday ||
                           (settings.TimeIntervals[i].Weekday == interval.Weekday &&
                            settings.TimeIntervals[i].From > interval.From))
                    {
                        for (int i2 = settings.TimeIntervals.Length - 2; i2 >= i; i2--)
                        {
                            settings.TimeIntervals[i2 + 1] = settings.TimeIntervals[i2];
                        }
                        settings.TimeIntervals[i] = interval;
                        SendSettings();
                        RecieveSettings();
                        ShowToast($"Saved new time: From {interval.From} to {interval.To}");
                        return;
                    }
                }
                else // append
                {
                    settings.TimeIntervals[i] = interval;
                    SendSettings();
                    RecieveSettings();
                    ShowToast($"Saved new time: From {interval.From} to {interval.To}");
                    return;
                }
            }
        }
        private async void RemoveInterval(TimeInterval interval)
        {
            for (int i = 0; i < settings.TimeIntervals.Length; i++)
            {
                if (settings.TimeIntervals[i].Equals(interval))
                {
                    settings.TimeIntervals[i].Active = false;

                    for (; i < settings.TimeIntervals.Length - 1; i++)
                    {
                        settings.TimeIntervals[i] = settings.TimeIntervals[i + 1];
                    }
                    SendSettings();
                    RecieveSettings();
                }
            }
        }

        void InitPowerMonitorChart()
        {
            var chart = FindViewById<LineChart>(Resource.Id.lineChart);

            chart.SetViewPortOffsets(0, 0, 0, 0);
            chart.SetBackgroundColor(Color.White);

            chart.Description.Enabled = false;

            chart.SetDrawGridBackground(false);

            var x = chart.XAxis;
            x.Enabled = false;

            var y = chart.AxisLeft;
            //y.setTypeface(tfLight);
            y.SetLabelCount(8, false);
            y.TextColor = Color.Black;
            y.SetPosition(YAxis.YAxisLabelPosition.InsideChart);
            y.SetDrawGridLines(false);
            y.AxisLineColor = Color.Black;

            chart.AxisRight.Enabled = false;
            chart.Legend.Enabled = false;
        }

        void UpdatePowerMonitorChart()
        {
            var chart = FindViewById<LineChart>(Resource.Id.lineChart);


            Entry[] values = new Entry[powerHistory.values.Length];

            for (int i = 0; i < values.Count(); i++)
                values[i] = new Entry(i, powerHistory.values[i]);

            LineDataSet set1;
            if (chart.Data != null &&
                    chart.Data.DataSetCount > 0)
            {
                set1 = (LineDataSet)chart.Data.GetDataSetByIndex(0);
                set1.Values = values;
                chart.Data.NotifyDataChanged();
                chart.NotifyDataSetChanged();
            }
            else
            {
                // create a dataset and give it a type
                set1 = new LineDataSet(values, "Power Usage");

                set1.SetMode(LineDataSet.Mode.CubicBezier);
                set1.CubicIntensity = 0.2f;
                set1.SetDrawFilled(true);
                set1.SetDrawCircles(false);
                set1.LineWidth = 1.6f;
                //set1.CircleRadius = 4f;
                //set1.SetCircleColor(Color.White);
                set1.HighLightColor = Color.Rgb(244, 117, 117);
                set1.Color = Color.ParseColor("#68B9C0");
                set1.FillColor = Color.ParseColor("#68B9C0");
                set1.FillAlpha = 100;
                set1.SetDrawHorizontalHighlightIndicator(false);
                set1.SetDrawVerticalHighlightIndicator(false);
                //    set1.FillFormatter = new IFillFormatter() {
                //    public float getFillLinePosition(ILineDataSet dataSet, LineDataProvider dataProvider)
                //    {
                //        return chart.getAxisLeft().getAxisMinimum();
                //    }
                //};

                // create a data object with the data sets
                LineData data = new LineData(set1);
                //data.setValueTypeface(tfLight);
                data.SetValueTextSize(12f);
                data.SetDrawValues(false);

                // set data
                chart.Data = data;
            }
            chart.SetViewPortOffsets(0, 0, 0, 0);
            chart.Invalidate();
        }

        void UpdatePowerMonitor()
        {
            if (!powerMonitorActive || communicator.ActiveDevice.Item1 == null)
                return;
            var r = communicator.SendCommand("powerhistory");
            powerHistory = PowerUsageHistory.FromByteArray(r);
            FindViewById<TextView>(Resource.Id.textViewCurrentPower).SetText($"Current Power Draw: {powerHistory.values[0]} Watt", TextView.BufferType.Normal);
            UpdatePowerMonitorChart();
        }



        void ShowPowerMonitor(bool show)
        {
            if (show)
            {
                FindViewById<LinearLayout>(Resource.Id.linearLayoutPowerMonitor).Visibility = ViewStates.Visible;
                FindViewById<ScrollView>(Resource.Id.scrollViewTimeTable).Visibility = ViewStates.Gone;
                UpdatePowerMonitor();
                powerMonitorUpdateTimer.Start();
                FindViewById<LineChart>(Resource.Id.lineChart).AnimateXY(1000, 1000);
            }
            else
            {
                FindViewById<LinearLayout>(Resource.Id.linearLayoutPowerMonitor).Visibility = ViewStates.Gone;
                powerMonitorUpdateTimer.Stop();
            }
        }

        private void UpdateTableView()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Debug.WriteLine("Updating Table");
            var table = FindViewById<TableLayout>(Resource.Id.tableLayoutTimes);

            if (table.ChildCount > 2)
                table.RemoveViews(2, table.ChildCount - 2);

            foreach (var weekday in (DayOfWeek[])Enum.GetValues(typeof(DayOfWeek)))
            {
                var header = LayoutInflater.From(this).Inflate(Resource.Layout.table_header_row, null) as TableRow;
                (header.GetChildAt(0) as TextView).Text = weekday.ToString();
                (header.GetChildAt(1) as ImageButton).Click += (s, e) => { AddIntervalDialog(weekday); };
                table.AddView(header);

                foreach (TimeInterval interval in settings.TimeIntervals)
                {
                    if (interval.Active && interval.Weekday == weekday)
                    {
                        var content = LayoutInflater.From(this).Inflate(Resource.Layout.table_content_row, null) as TableRow;
                        (content.GetChildAt(0) as TextView).Text = interval.From.ToString();
                        (content.GetChildAt(0) as TextView).Click += (s, e) => { EditInterval(interval, true); };
                        (content.GetChildAt(1) as TextView).Text = interval.To.ToString();
                        (content.GetChildAt(1) as TextView).Click += (s, e) => { EditInterval(interval, false); };
                        (content.GetChildAt(2) as ImageButton).Click += (s, e) => { RemoveInterval(interval); };

                        table.AddView(content);
                    }
                }


                var separator = LayoutInflater.From(this).Inflate(Resource.Layout.table_content_separator, null);
                table.AddView(separator);
            }
        }

        private void Test()
        {
            ControlSettings s = ControlSettings.Constructor();

            for (byte i = 0; i < 32; i++)
            {
                s.TimeIntervals[i].Active = true;
                s.TimeIntervals[i].From.Hour = i;
                s.TimeIntervals[i].From.Minute = i;
                s.TimeIntervals[i].To.Hour = i;
                s.TimeIntervals[i].To.Minute = i;
            }

            communicator.SendCommandData("setsettings", s.ToByteArray());
            var r = communicator.SendCommand("getsettings");
            ControlSettings s2 = ControlSettings.FromByteArray(r);

            string str = "";
            for (byte i = 0; i < 32; i++)
            {
                str += s2.TimeIntervals[i].From.Hour + "\n";
                str += s2.TimeIntervals[i].From.Minute + "\n";
                str += s2.TimeIntervals[i].To.Hour + "\n";
                str += s2.TimeIntervals[i].To.Minute + "\n";
            }

            Debug.WriteLine(str);

            Debug.Assert(s.Mode == s2.Mode);
            Debug.Assert(Enumerable.SequenceEqual(s.TimeIntervals, s2.TimeIntervals));

            Debug.WriteLine(s2.TimeIntervals[5].To.Hour);
        }
        private async void FabOnClickAsync(object sender, EventArgs eventArgs)
        {
            //Test();
            communicator.SendDiscoveryCommand();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}
