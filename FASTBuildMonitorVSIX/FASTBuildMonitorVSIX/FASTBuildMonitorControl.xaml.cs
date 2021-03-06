﻿//#define ENABLE_RENDERING_STATS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Globalization;
using System.Windows.Media.Animation;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using System.ComponentModel.Design;
using Microsoft.Win32;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;

namespace FASTBuildMonitorVSIX
{
    class GifImage : Image
    {
        private bool _isInitialized;
        private GifBitmapDecoder _gifDecoder;
        private Int32Animation _animation;

        public int FrameIndex
        {
            get { return (int)GetValue(FrameIndexProperty); }
            set { SetValue(FrameIndexProperty, value); }
        }

        private void Initialize()
        {
            _gifDecoder = FASTBuildMonitorControl.GetGifBitmapDecoder(GifSource);
            _animation = new Int32Animation(0, _gifDecoder.Frames.Count - 1, new Duration(new TimeSpan(0, 0, 0, _gifDecoder.Frames.Count / 10, (int)((_gifDecoder.Frames.Count / 10.0 - _gifDecoder.Frames.Count / 10) * 1000))));
            _animation.RepeatBehavior = RepeatBehavior.Forever;
            this.Source = _gifDecoder.Frames[0];

            _isInitialized = true;
        }

        static GifImage()
        {
            VisibilityProperty.OverrideMetadata(typeof(GifImage),
                new FrameworkPropertyMetadata(VisibilityPropertyChanged));
        }

        private static void VisibilityPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if ((Visibility)e.NewValue == Visibility.Visible)
            {
                ((GifImage)sender).StartAnimation();
            }
            else
            {
                ((GifImage)sender).StopAnimation();
            }
        }

        public static readonly DependencyProperty FrameIndexProperty =
            DependencyProperty.Register("FrameIndex", typeof(int), typeof(GifImage), new UIPropertyMetadata(0, new PropertyChangedCallback(ChangingFrameIndex)));

        static void ChangingFrameIndex(DependencyObject obj, DependencyPropertyChangedEventArgs ev)
        {
            var gifImage = obj as GifImage;
            gifImage.Source = gifImage._gifDecoder.Frames[(int)ev.NewValue];
        }

        /// <summary>
        /// Defines whether the animation starts on it's own
        /// </summary>
        public bool AutoStart
        {
            get { return (bool)GetValue(AutoStartProperty); }
            set { SetValue(AutoStartProperty, value); }
        }

        public static readonly DependencyProperty AutoStartProperty =
            DependencyProperty.Register("AutoStart", typeof(bool), typeof(GifImage), new UIPropertyMetadata(false, AutoStartPropertyChanged));

        private static void AutoStartPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                (sender as GifImage).StartAnimation();
        }

        public string GifSource
        {
            get { return (string)GetValue(GifSourceProperty); }
            set { SetValue(GifSourceProperty, value); }
        }

        public static readonly DependencyProperty GifSourceProperty =
            DependencyProperty.Register("GifSource", typeof(string), typeof(GifImage), new UIPropertyMetadata(string.Empty, GifSourcePropertyChanged));

        private static void GifSourcePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            (sender as GifImage).Initialize();
        }

        /// <summary>
        /// Starts the animation
        /// </summary>
        public void StartAnimation()
        {
            if (!_isInitialized)
                this.Initialize();

            BeginAnimation(FrameIndexProperty, _animation);
        }

        /// <summary>
        /// Stops the animation
        /// </summary>
        public void StopAnimation()
        {
            BeginAnimation(FrameIndexProperty, null);
        }
    }


    /// <summary>
    /// Interaction logic for MyControl.xaml
    /// </summary>
    /// 
    public partial class FASTBuildMonitorControl : UserControl
    {
        private DispatcherTimer _timer;

        private static List<Rectangle> _bars = new List<Rectangle>();

        private static FASTBuildMonitorControl _StaticWindow = null;

        public FASTBuildMonitorControl()
        {
            // WPF init flow
            InitializeComponent();

            // Our internal init
            InitializeInternalState();

            _StaticWindow = this;
        }

        private void InitializeInternalState()
        {
            // Font text
            if (_glyphTypeface == null)
            {
                Typeface typeface = new Typeface(new FontFamily("Segoe UI"),
                                                FontStyles.Normal,
                                                FontWeights.Normal,
                                                FontStretches.Normal);

                if (!typeface.TryGetGlyphTypeface(out _glyphTypeface))
                {
                    throw new InvalidOperationException("No glyphtypeface found");
                }
            }

            // Time bar display
            _timeBar = new TimeBar(TimeBarCanvas);

            //events
            this.Loaded += FASTBuildMonitorControl_Loaded;

            EventsScrollViewer.PreviewMouseWheel += MainWindow_MouseWheel;
            EventsScrollViewer.MouseWheel += MainWindow_MouseWheel;
            MouseWheel += MainWindow_MouseWheel;
            EventsCanvas.MouseWheel += MainWindow_MouseWheel;

            EventsScrollViewer.PreviewMouseLeftButtonDown += EventsScrollViewer_MouseDown;
            EventsScrollViewer.MouseDown += EventsScrollViewer_MouseDown;
            MouseDown += EventsScrollViewer_MouseDown;
            EventsCanvas.MouseDown += EventsScrollViewer_MouseDown;

            EventsScrollViewer.PreviewMouseLeftButtonUp += EventsScrollViewer_MouseUp;
            EventsScrollViewer.MouseUp += EventsScrollViewer_MouseUp;
            MouseUp += EventsScrollViewer_MouseUp;
            EventsCanvas.MouseUp += EventsScrollViewer_MouseUp;

            EventsScrollViewer.PreviewMouseDoubleClick += EventsScrollViewer_MouseDoubleClick;
            EventsScrollViewer.MouseDoubleClick += EventsScrollViewer_MouseDoubleClick;

            OutputTextBox.PreviewMouseDoubleClick += OutputTextBox_PreviewMouseDoubleClick;
            OutputTextBox.MouseDoubleClick += OutputTextBox_PreviewMouseDoubleClick;
            OutputTextBox.PreviewKeyDown += OutputTextBox_KeyDown;
            OutputTextBox.KeyDown += OutputTextBox_KeyDown;
            OutputTextBox.LayoutUpdated += OutputTextBox_LayoutUpdated;

            OutputWindowComboBox.SelectionChanged += OutputWindowComboBox_SelectionChanged;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                //update timer
                _timer = new DispatcherTimer();
                _timer.Tick += HandleTick;
                _timer.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * 16);
                _timer.Start();
            }));
        }


        /* Output Window ESC */
        private void OutputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Key == Key.Space)
            {
                if (_StaticWindow.OutputWindowComboBox.SelectedIndex != 0)
                {
                    _StaticWindow.OutputWindowComboBox.SelectedIndex = 0;
                }
            }
        }


        /* Output Window double click */

        private void OutputTextBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {

            if (e.ChangedButton == MouseButton.Left)
            {
                TextBox tb = sender as TextBox;
                String doubleClickedWord = tb.SelectedText;


                if (tb.SelectionStart >= 0 && tb.SelectionLength > 0)
                {
                    try
                    {
                        string text = tb.Text;
                        int startLineIndex = text.LastIndexOf(Environment.NewLine, tb.SelectionStart) + Environment.NewLine.Length;
                        int endLineIndex = tb.Text.IndexOf(Environment.NewLine, tb.SelectionStart);

                        string selectedLineText = tb.Text.Substring(startLineIndex, endLineIndex - startLineIndex);
                        Console.WriteLine("SelectedLine {0}", selectedLineText);

                        int startParenthesisIndex = selectedLineText.IndexOf('(');
                        int endParenthesisIndex = selectedLineText.IndexOf(')');

                        if (startParenthesisIndex > 0 && endParenthesisIndex > 0)
                        {
                            string filePath = selectedLineText.Substring(0, startParenthesisIndex);
                            string lineString = selectedLineText.Substring(startParenthesisIndex + 1, endParenthesisIndex - startParenthesisIndex - 1);

                            Int32 lineNumber = Int32.Parse(lineString);

                            Console.WriteLine("File({0}) Line({1})", filePath, lineNumber);

                            Microsoft.VisualStudio.Shell.VsShellUtilities.OpenDocument(FASTBuildMonitorVSIX.FASTBuildMonitorPackage._instance, filePath);

                            DTE2 _dte = (DTE2)FASTBuildMonitorPackage._instance._dte;

                            Console.WriteLine("Window: {0}", _dte.ActiveWindow.Caption);

                            EnvDTE.TextSelection sel = _dte.ActiveDocument.Selection as EnvDTE.TextSelection;

                            sel.StartOfDocument(false);
                            sel.EndOfDocument(true);
                            sel.GotoLine(lineNumber);

                            try
                            {
                                sel.ActivePoint.TryToShow(vsPaneShowHow.vsPaneShowCentered, null);
                            }
                            catch (System.Exception ex)
                            {
                                Console.WriteLine("Exception! " + ex.ToString());
                            }

                        }
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine("Exception! " + ex.ToString());
                    }
                }
            }
        }

        /* Output Window Filtering & Combo box management */

        public class OutputFilterItem
        {
            public OutputFilterItem(string name)
            {
                _name = name;
            }

            public OutputFilterItem(BuildEvent buildEvent)
            {
                _buildEvent = buildEvent;
            }

            private BuildEvent _internalBuildEvent = null;

            public BuildEvent _buildEvent
            {
                get { return _internalBuildEvent; }

                private set { _internalBuildEvent = value; }
            }

            public string _internalName = "";
            public string _name
            {
                get
                {
                    string result;

                    if (_buildEvent != null)
                    {
                        result = _buildEvent._name;
                    }
                    else
                    {
                        // fallback
                        result = _internalName;
                    }

                    const int charactersToDisplay = 50;

                    if (result.Length > charactersToDisplay)
                    {
                        result = result.Substring(result.IndexOf('\\', result.Length - charactersToDisplay));
                    }

                    return result;
                }

                set
                {
                    _internalName = value;
                }
            }
        }

        static public ObservableCollection<OutputFilterItem> _outputComboBoxFilters;

        void ResetOutputWindowCombox()
        {
            if (_outputComboBoxFilters != null)
            {
                _outputComboBoxFilters.Clear();
            }
            else
            {
                _outputComboBoxFilters = new ObservableCollection<OutputFilterItem>();
            }

            _outputComboBoxFilters.Add(new OutputFilterItem("ALL"));

            OutputWindowComboBox.ItemsSource = _outputComboBoxFilters;

            OutputWindowComboBox.SelectedIndex = 0;
        }


        void AddOutputWindowFilterItem(BuildEvent buildEvent)
        {
            _outputComboBoxFilters.Add(new OutputFilterItem(buildEvent));

            RefreshOutputTextBox();
        }

        private void OutputWindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshOutputTextBox();
        }

        void RefreshOutputTextBox()
        {
            OutputTextBox.Clear();


            if (OutputWindowComboBox.SelectedIndex >= 0)
            {
                OutputFilterItem selectedFilter = _outputComboBoxFilters[OutputWindowComboBox.SelectedIndex];

                foreach (OutputFilterItem filter in _outputComboBoxFilters)
                {
                    if (filter._buildEvent != null && (selectedFilter._buildEvent == null || filter._buildEvent == selectedFilter._buildEvent))
                    {
                        OutputTextBox.AppendText(filter._buildEvent._outputMessages);
                    }
                }
            }

            // Since we changed the text inside the text box we now require a layout update to refresh
            // the internal state of the UIControl
            _outputTextBoxPendingLayoutUpdate = true;

            _StaticWindow.OutputTextBox.UpdateLayout();
        }

        void ChangeOutputWindowComboBoxSelection(BuildEvent buildEvent)
        {
            int index = 0;

            foreach (OutputFilterItem filter in _outputComboBoxFilters)
            {
                if (filter._buildEvent == buildEvent)
                {
                    OutputWindowComboBox.SelectedIndex = index;
                    break;
                }
                index++;
            }
        }



        /* Tab Control management */

        public enum eTABs
        {
            TAB_TimeLine = 0,
            TAB_OUTPUT
        }

        private void MyTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (e.Source is TabControl)
            {
                TabControl tabControl = e.Source as TabControl;

                if (tabControl.SelectedIndex == (int)eTABs.TAB_OUTPUT)
                {
                    _StaticWindow.OutputTextBox.UpdateLayout();

                    _outputTextBoxPendingLayoutUpdate = true;
                }
            }
        }

        private void FASTBuildMonitorControl_Loaded(object sender, RoutedEventArgs e)
        {
            Image image = new Image();
            image.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.TimeLineTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "View Events TimeLine";
            TabItemTimeBar.Header = image;

            image = new Image();
            image.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.TextOutputTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "View Output Window";
            TabItemOutput.Header = image;
        }

        static public bool _outputTextBoxPendingLayoutUpdate = false;
        private void OutputTextBox_LayoutUpdated(object sender, EventArgs e)
        {
            _outputTextBoxPendingLayoutUpdate = false;
        }

        /* Double-click handling */
        public class HitTestResult
        {
            public HitTestResult(BuildHost host, CPUCore core, BuildEvent ev)
            {
                _host = host;
                _core = core;
                _event = ev;
            }

            public BuildHost _host;
            public CPUCore _core;
            public BuildEvent _event;
        }

        private void EventsScrollViewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_StaticWindow.MyTabControl.SelectedIndex == (int)eTABs.TAB_TimeLine)
                {
                    Point mousePosition = e.GetPosition(EventsScrollViewer);

                    mousePosition.X += EventsScrollViewer.HorizontalOffset;
                    mousePosition.Y += EventsScrollViewer.VerticalOffset;

                    HitTestResult result = HitTest(mousePosition);

                    if (result != null)
                    {
                        Console.WriteLine("\n\nHost: " + result._host._name);
                        Console.WriteLine("core: " + result._core._coreIndex);
                        Console.WriteLine("event name: " + result._event._name);

                        string filename = result._event._name.Substring(1, result._event._name.Length - 2);

                        result._event.HandleDoubleClickEvent();

                        e.Handled = true;
                    }
                }
            }
        }

        /* Mouse Pan handling */
        private static bool _isPanning = false;
        private static Point _panReferencePosition;

        private void EventsScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_StaticWindow.MyTabControl.SelectedIndex == (int)eTABs.TAB_TimeLine)
                {
                    Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                    Point mousePosition = e.GetPosition(EventsScrollViewer);

                    if (viewPort.Contains(mousePosition))
                    {
                        _panReferencePosition = mousePosition;

                        StartPanning();

                        e.Handled = true;
                    }
                }
            }
        }

        private void EventsScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;

            if (e.ChangedButton == MouseButton.Left && _isPanning)
            {
                Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                Point mousePosition = e.GetPosition(EventsScrollViewer);

                if (viewPort.Contains(mousePosition))
                {
                    StopPanning();

                    e.Handled = true;
                }
            }
        }

        private void StartPanning()
        {
            this.Cursor = Cursors.SizeAll;
            _isPanning = true;
        }

        private void StopPanning()
        {
            this.Cursor = Cursors.Arrow;
            _isPanning = false;
        }

        private void UpdateMousePanning()
        {
            if (_isPanning)
            {
                Point currentMousePosition = Mouse.GetPosition(EventsScrollViewer);

                Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                if (viewPort.Contains(currentMousePosition))
                {
                    Vector posDelta = (currentMousePosition - _panReferencePosition) * -1.0f;

                    _panReferencePosition = currentMousePosition;

                    double newVerticalOffset = EventsScrollViewer.VerticalOffset + posDelta.Y;
                    newVerticalOffset = Math.Min(newVerticalOffset, EventsCanvas.Height - EventsScrollViewer.ViewportHeight);
                    newVerticalOffset = Math.Max(0.0f, newVerticalOffset);

                    double newHorizontaOffset = EventsScrollViewer.HorizontalOffset + posDelta.X;
                    newHorizontaOffset = Math.Min(newHorizontaOffset, EventsCanvas.Width - EventsScrollViewer.ViewportWidth);
                    newHorizontaOffset = Math.Max(0.0f, newHorizontaOffset);


                    //Console.WriteLine("Mouse (X: {0}, Y: {1})", currentMousePosition.X, currentMousePosition.Y);
                    //Console.WriteLine("Pan (X: {0}, Y: {1})", newHorizontaOffset, newVerticalOffset);

                    EventsScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);
                    TimeBarScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);

                    EventsScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                    CoresScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
                else
                {
                    StopPanning();
                }
            }
        }

        /* Mouse Scrolling handling */
        private static Boolean _autoScrolling = true;

        private void ScrollViewer_ScrollChanged(Object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentWidthChange == 0)
            {
                if (EventsScrollViewer.HorizontalOffset == EventsScrollViewer.ScrollableWidth)
                {
                    _autoScrolling = true;
                }
                else
                {
                    _autoScrolling = false;
                }
            }

            if (_autoScrolling && e.ExtentWidthChange != 0)
            {
                EventsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);

                TimeBarScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);
            }

            if (e.VerticalChange != 0)
            {
                //Console.WriteLine("Scroll V Offset: (cores: {0} - events: {1})", CoresScrollViewer.VerticalOffset, EventsScrollViewer.VerticalOffset);

                _StaticWindow.CoresScrollViewer.ScrollToVerticalOffset(EventsScrollViewer.VerticalOffset);

                UpdateViewport();
            }

            if (e.HorizontalChange != 0)
            {
                _StaticWindow.TimeBarScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.HorizontalOffset);

                UpdateViewport();
            }
        }

        /* Mouse Zoom handling */
        static private double _zoomFactor = 1.0f;
        static private double _zoomFactorOld = 0.1f;

        void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            //handle the case where we can receive many events between 2 frames
            if (_zoomFactorOld == _zoomFactor)
            {
                _zoomFactorOld = _zoomFactor;
            }

            double zoomMultiplier = 1.0f;

            if (_zoomFactor > 3.0f)
            {
                if (_zoomFactor < 7.0f)
                {
                    zoomMultiplier = 3.0f;
                }
                else
                {
                    zoomMultiplier = 6.0f;
                }
            }
            else if (_zoomFactor < 0.5f)
            {
                if (_zoomFactor > 0.1f)
                {
                    zoomMultiplier = 0.3f;
                }
                else
                {
                    zoomMultiplier = 0.05f;
                }
            }


            //Accumulate some value
            _zoomFactor += zoomMultiplier * (double)e.Delta / 1000.0f;
            _zoomFactor = Math.Min(_zoomFactor, 30.0f);
            _zoomFactor = Math.Max(_zoomFactor, 0.05f);

            //Console.WriteLine("Zoom: {0} (multiplier: {1})", _zoomFactor, zoomMultiplier);

            //disable auto-scrolling when we zoom
            _autoScrolling = false;

            e.Handled = true;
        }

        private void UpdateZoomTargetPosition()
        {
            if (_zoomFactorOld != _zoomFactor)
            {
                Point mouseScreenPosition = Mouse.GetPosition(_StaticWindow.EventsCanvas);

                //Find out the time position the mouse (canvas relative) was at pre-zoom
                double mouseTimePosition = mouseScreenPosition.X / (_zoomFactorOld * pix_per_second);

                double screenSpaceMousePositionX = mouseScreenPosition.X - EventsScrollViewer.HorizontalOffset;

                //Determine the new canvas relative mouse position post-zoom
                double newMouseCanvasPosition = mouseTimePosition * _zoomFactor * pix_per_second;

                double newHorizontalScrollOffset = Math.Max(0.0f, newMouseCanvasPosition - screenSpaceMousePositionX);

                _StaticWindow.EventsScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);
                _StaticWindow.TimeBarScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);

                _zoomFactorOld = _zoomFactor;
            }
        }


        /* Input File IO feature */
        private FileStream _fileStream = null;
        private Int64 _fileStreamPosition = 0;
        private List<byte> _fileBuffer = new System.Collections.Generic.List<byte>();

        private bool CanRead()
        {
            return _fileStream != null && _fileStream.CanRead;
        }

        private bool HasFileContentChanged()
        {
            bool bFileChanged = false;

            if (_fileStream.Length < _fileStreamPosition)
            {
                // detect if the current file has been overwritten with less data
                bFileChanged = true;
            }
            else if (_fileBuffer.Count > 0)
            {
                // detect if the current file has been overwritten with different data

                int numBytesToCompare = Math.Min(_fileBuffer.Count, 256);

                byte[] buffer = new byte[numBytesToCompare];

                _fileStream.Seek(0, SeekOrigin.Begin);

                int numBytesRead = _fileStream.Read(buffer, 0, numBytesToCompare);
                Debug.Assert(numBytesRead == numBytesToCompare, "Could not read the expected amount of data from the log file...!");

                for (int i = 0; i < numBytesToCompare; ++i)
                {
                    if (buffer[i] != _fileBuffer[i])
                    {
                        bFileChanged = true;
                        break;
                    }
                }
            }

            return bFileChanged;
        }

        private bool BuildRestarted()
        {
            return CanRead() && HasFileContentChanged();
        }

        private void ResetState()
        {
            _fileStreamPosition = 0;
            _fileStream.Seek(0, SeekOrigin.Begin);

            _fileBuffer.Clear();

            _buildRunningState = eBuildRunningState.Ready;
            _buildStatus = eBuildStatus.AllClear;

            _buildStartTimeMS = 0;
            _latestTimeStampMS = 0;

            _hosts.Clear();
            _localHost = null;

            _lastProcessedPosition = 0;
            _bPreparingBuildsteps = false;

            _StaticWindow.EventsCanvas.Children.Clear();
            _StaticWindow.CoresCanvas.Children.Clear();

            // Start by adding a local host
            _localHost = new BuildHost(_cLocalHostName);
            _hosts.Add(_cLocalHostName, _localHost);

            // Always add the prepare build steps event first
            BuildEvent buildEvent = new BuildEvent(_cPrepareBuildStepsText, 0);
            _localHost.OnStartEvent(buildEvent);
            _bPreparingBuildsteps = true;

            // Reset the Output window text
            OutputTextBox.Text = "";

            // Change back the tabcontrol to the TimeLine automatically
            _StaticWindow.MyTabControl.SelectedIndex = (int)eTABs.TAB_TimeLine;

            ResetOutputWindowCombox();

            // progress status
            UpdateBuildProgress(0.0f);
            StatusBarProgressBar.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF06B025"));


            // reset to autoscrolling ON
            _autoScrolling = true;

            // reset our zoom levels
            _zoomFactor = 1.0f;
            _zoomFactorOld = 0.1f;

            // target pid
            _targetPID = 0;
            _lastTargetPIDCheckTimeMS = 0;
        }

        /* Build State Management */
        public enum eBuildRunningState
        {
            Ready = 0,
            Running,
        }

        private static eBuildRunningState _buildRunningState;

        public void UpdateStatusBar()
        {
            switch (_buildRunningState)
            {
                case eBuildRunningState.Ready:
                    StatusBarBuildStatus.Text = "Ready";
                    break;
                case eBuildRunningState.Running:
                    StatusBarBuildStatus.Text = "Running";
                    break;
            }

            int numCores = 0;
            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host._name.Contains(_cLocalHostName))
                {
                    numCores += host._cores.Count;
                }
                else
                {
                    numCores += host._cores.Count - 1;
                }
            }

            StatusBarDetails.Text = string.Format("{0} Agents - {1} Cores", _hosts.Count, numCores);
        }

        public enum eBuildStatus
        {
            AllClear = 0,
            HasWarnings,
            HasErrors,
        }
        private static eBuildStatus _buildStatus;

        public void UpdateBuildStatus(BuildEventState jobResult)
        {
            eBuildStatus newBuildStatus = _buildStatus;

            switch (jobResult)
            {
                case BuildEventState.FAILED:
                    newBuildStatus = eBuildStatus.HasErrors;
                    break;

                case BuildEventState.TIMEOUT:
                case BuildEventState.SUCCEEDED_WITH_WARNINGS:
                    if ((int)_buildStatus < (int)eBuildStatus.HasWarnings)
                    {
                        newBuildStatus = eBuildStatus.HasWarnings;
                    }
                    break;
            }

            if (_buildStatus != newBuildStatus)
            {
                switch (newBuildStatus)
                {
                    case eBuildStatus.HasErrors:
                        StatusBarProgressBar.Foreground = Brushes.Red;
                        break;
                    case eBuildStatus.HasWarnings:
                        StatusBarProgressBar.Foreground = Brushes.Yellow;
                        break;
                }

                _buildStatus = newBuildStatus;
            }
        }

        static private float _currentProgressPCT = 0.0f;
        ToolTip _statusBarProgressToolTip = new ToolTip();

        public void UpdateBuildProgress(float progressPCT)
        {
            _currentProgressPCT = progressPCT;

            StatusBarBuildTime.Text = string.Format("Duration: {0}", GetTimeFormattedString2(GetCurrentBuildTimeMS()));

            StatusBarProgressBar.Value = _currentProgressPCT;


            StatusBarProgressBar.ToolTip = _statusBarProgressToolTip;

            _statusBarProgressToolTip.Content = string.Format("{0:0.00}%", _currentProgressPCT);
        }


        /* Target Process ID monitoring */
        private static int _targetPID = 0;

        private static bool IsTargetProcessRunning(int pid)
        {
            bool bIsRunning = false;

            System.Diagnostics.Process[] processlist = System.Diagnostics.Process.GetProcesses();
            foreach (System.Diagnostics.Process proc in processlist)
            {
                if (proc.Id == pid)
                {
                    bIsRunning = true;
                    break;
                }
            }

            return bIsRunning;
        }

        private static Int64 _lastTargetPIDCheckTimeMS = 0;
        const Int64 cTargetPIDCheckPeriodMS = 1 * 1000;

        private static bool PollIsTargetProcessRunning()
        {
            // assume the process is running
            bool bIsRunning = true;

            if (_targetPID != 0 && _buildRunningState == eBuildRunningState.Running)
            {
                Int64 currentTimeMS = GetCurrentSystemTimeMS();

                if ((currentTimeMS - _lastTargetPIDCheckTimeMS) > cTargetPIDCheckPeriodMS)
                {
                    bIsRunning = IsTargetProcessRunning(_targetPID);

                    _lastTargetPIDCheckTimeMS = currentTimeMS;
                }
            }

            return bIsRunning;
        }

        /* Time management */
        private static Int64 _buildStartTimeMS = 0;
        private static Int64 _latestTimeStampMS = 0;

        private static Int64 ConvertFileTimeToMS(Int64 fileTime)
        {
            // FileTime: Contains a 64-bit value representing the number of 100-nanosecond intervals since January 1, 1601 (UTC).
            return fileTime / (10 * 1000);
        }

        const double cTimeStepMS = 500.0f;

        private static Int64 GetCurrentSystemTimeMS()
        {
            Int64 currentTimeMS = DateTime.Now.ToFileTime() / (10 * 1000);

            return currentTimeMS;
        }

        private static Int64 GetCurrentBuildTimeMS(bool bUseTimeStep = false)
        {
            Int64 elapsedBuildTime = -_buildStartTimeMS;

            if (_buildRunningState == eBuildRunningState.Running)
            {
                Int64 currentTimeMS = GetCurrentSystemTimeMS();

                elapsedBuildTime += currentTimeMS;

                if (bUseTimeStep)
                {
                    elapsedBuildTime = (Int64)(Math.Truncate(elapsedBuildTime / cTimeStepMS) * cTimeStepMS);
                }
            }
            else
            {
                elapsedBuildTime += _latestTimeStampMS;
            }

            return elapsedBuildTime;
        }

        private static Int64 RegisterNewTimeStamp(Int64 fileTime)
        {
            _latestTimeStampMS = ConvertFileTimeToMS(fileTime);

            return _latestTimeStampMS;
        }

        public class CPUCore : Canvas
        {
            public BuildHost _parent;
            public int _coreIndex = 0;
            public BuildEvent _activeEvent = null;
            public List<BuildEvent> _completedEvents = new List<BuildEvent>();

            public double _x = 0.0f;
            public double _y = 0.0f;

            //WPF stuff
            public TextBlock _textBlock = new TextBlock();
            public static Image _sLODImage = null;
            public ToolTip _toolTip = new ToolTip();
            public Line _lineSeparator = new Line();

            //LOD handling
            public List<Rectangle> _usedLODBlocks = new List<Rectangle>();
            public bool _isLODBlockActive = false;
            public Rect _currentLODRect = new Rect();

            public CPUCore(BuildHost parent, int coreIndex)
            {
                _parent = parent;

                _coreIndex = coreIndex;

                _textBlock.Text = string.Format("{0} (Core # {1})", parent._name, _coreIndex);

                _StaticWindow.CoresCanvas.Children.Add(_textBlock);

                _StaticWindow.EventsCanvas.Children.Add(this);


                this.Height = pix_height;

                if (_sLODImage == null)
                {

                    _sLODImage = new Image();
                    _sLODImage.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.LODBlock);
                }

                this.ToolTip = _toolTip;
            }

            public bool ScheduleEvent(BuildEvent ev)
            {
                bool bOK = _activeEvent == null;

                if (bOK)
                {
                    _activeEvent = ev;

                    _activeEvent.Start(this);
                }

                return bOK;
            }

            public bool UnScheduleEvent(Int64 timeCompleted, string eventName, BuildEventState jobResult, string outputMessages, bool bForce = false)
            {
                bool bOK = (_activeEvent != null && (_activeEvent._name == eventName || bForce));

                if (bOK)
                {
                    if (!bForce && outputMessages.Length > 0)
                    {
                        _activeEvent.SetOutputMessages(outputMessages);
                    }

                    _activeEvent.Stop(timeCompleted, jobResult);

                    _completedEvents.Add(_activeEvent);

                    _activeEvent = null;
                }

                return bOK;
            }

            protected override void OnRender(DrawingContext dc)
            {
                foreach (BuildEvent ev in _completedEvents)
                {
                    ev.OnRender(dc);
                }

                // we need to close the currently active LOD block before rendering the active event
                if (_isLODBlockActive)
                {
                    _isLODBlockActive = false;

                    VisualBrush brush = new VisualBrush();
                    brush.Visual = _sLODImage;
                    brush.Stretch = Stretch.None;
                    brush.TileMode = TileMode.Tile;
                    brush.AlignmentY = AlignmentY.Top;
                    brush.AlignmentX = AlignmentX.Left;
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    brush.Viewport = new Rect(0, 0, 40, 20);

                    dc.DrawRectangle(brush, new Pen(Brushes.Black, 1), _currentLODRect);
                }

                if (_activeEvent != null)
                {
                    _activeEvent.OnRender(dc);
                }
            }

            public HitTestResult HitTest(Point localMousePosition)
            {
                HitTestResult result = null;

                foreach (BuildEvent ev in _completedEvents)
                {
                    result = ev.HitTest(localMousePosition);

                    if (result != null)
                    {
                        break;
                    }
                }

                return result;
            }

            public void RenderUpdate(ref double X, ref double Y)
            {
                // WPF Layout update
                Canvas.SetLeft(_textBlock, X);
                Canvas.SetTop(_textBlock, Y + 2);

                if (_x != X)
                {
                    Canvas.SetLeft(this, X);
                    _x = X;
                }

                if (_y != Y)
                {
                    Canvas.SetTop(this, Y);
                    _y = Y;
                }

                double relX = 0.0f;

                foreach (BuildEvent ev in _completedEvents)
                {
                    ev.RenderUpdate(ref relX, 0);
                }

                if (_activeEvent != null)
                {
                    _activeEvent.RenderUpdate(ref relX, 0);
                }


                X = this.Width = X + relX + 40.0f;

                Y += 25;
            }
        }


        public class BuildHost
        {
            public string _name;
            public List<CPUCore> _cores = new List<CPUCore>();
            public bool bLocalHost = false;

            //WPF stuff
            public Line _lineSeparator = new Line();

            public BuildHost(string name)
            {
                _name = name;

                bLocalHost = name.Contains(_cLocalHostName);

                // Add line separator
                _StaticWindow.CoresCanvas.Children.Add(_lineSeparator);

                _lineSeparator.Stroke = new SolidColorBrush(Colors.LightGray);
                _lineSeparator.StrokeThickness = 1;
                DoubleCollection dashes = new DoubleCollection();
                dashes.Add(2);
                dashes.Add(2);
                _lineSeparator.StrokeDashArray = dashes;

                _lineSeparator.X1 = 10;
                _lineSeparator.X2 = 300;
            }

            public void OnStartEvent(BuildEvent newEvent)
            {
                bool bAssigned = false;
                for (int i = 0; i < _cores.Count; ++i)
                {
                    if (_cores[i].ScheduleEvent(newEvent))
                    {
                        //Console.WriteLine("START {0} (Core {1}) [{2}]", _name, i, newEvent._name);
                        bAssigned = true;
                        break;
                    }
                }

                // we discovered a new core
                if (!bAssigned)
                {
                    CPUCore core = new CPUCore(this, _cores.Count);

                    core.ScheduleEvent(newEvent);

                    //Console.WriteLine("START {0} (Core {1}) [{2}]", _name, _cores.Count, newEvent._name);

                    _cores.Add(core);
                }
            }

            public void OnCompleteEvent(Int64 timeCompleted, string eventName, BuildEventState jobResult, string outputMessages)
            {
                for (int i = 0; i < _cores.Count; ++i)
                {
                    if (_cores[i].UnScheduleEvent(timeCompleted, eventName, jobResult, outputMessages))
                    {
                        break;
                    }
                }
            }

            public HitTestResult HitTest(Point mousePosition)
            {
                HitTestResult result = null;

                foreach (CPUCore core in _cores)
                {
                    double x = Canvas.GetLeft(core);
                    double y = Canvas.GetTop(core);

                    Rect rect = new Rect(x, y, core.Width, core.Height);

                    if (rect.Contains(mousePosition))
                    {
                        Point localMousePosition = new Point(mousePosition.X - x, mousePosition.Y - y);
                        result = core.HitTest(localMousePosition);

                        break;
                    }
                }

                return result;
            }

            public void RenderUpdate(double X, ref double Y)
            {
                double maxX = 0.0f;

                //update all cores
                foreach (CPUCore core in _cores)
                {
                    double localX = X;

                    core.RenderUpdate(ref localX, ref Y);

                    maxX = Math.Max(maxX, localX);
                }

                //adjust the dynamic line separator
                _lineSeparator.Y1 = _lineSeparator.Y2 = Y + 10;

                Y += 20;

                UpdateEventsCanvasMaxSize(X, Y);
            }
        }

        public enum BuildEventState
        {
            UNKOWN = 0,
            IN_PROGRESS,
            FAILED,
            SUCCEEDED,
            SUCCEEDED_WITH_WARNINGS,
            TIMEOUT
        }

        const double pix_space_between_events = 2;
        const double pix_per_second = 20.0f;
        const double pix_height = 20;
        const double pix_LOD_Threshold = 2.0f;

        const double toolTip_TimeThreshold = 1.0f; //in Seconds


        public static BitmapImage GetBitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();

            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }

            return bitmapImage;
        }

        public static GifBitmapDecoder GetGifBitmapDecoder(string gifResourceName)
        {
            GifBitmapDecoder bitMapDecoder = null;

            object obj = FASTBuildMonitorVSIX.Resources.Images.ResourceManager.GetObject(gifResourceName, FASTBuildMonitorVSIX.Resources.Images.Culture);

            if (obj != null)
            {
                System.Drawing.Bitmap bitmapObject = obj as System.Drawing.Bitmap;

                MemoryStream memory = new MemoryStream();
                bitmapObject.Save(memory, System.Drawing.Imaging.ImageFormat.Gif);
                memory.Position = 0;
                bitMapDecoder = new GifBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            }

            return bitMapDecoder;
        }

        public class BuildEvent
        {
            // Attributes
            public CPUCore _core = null;

            public Int64 _timeStarted = 0;    // in ms
            public Int64 _timeFinished = 0;    // in ms

            public string _name;
            public string _fileName; // extracted from the full name

            public BuildEventState _state;

            public string _outputMessages;

            public string _toolTipText;

            // WPF rendering stuff
            public ImageBrush _brush = null;

            // Coordinates
            public Rect _bordersRect;
            public Rect _progressRect;

            // LOD/Culling
            public bool _isInLowLOD = false;
            public bool _isDirty = false;

            // Static Members
            public static bool _sbInitialized = false;
            public static ImageBrush _sSuccessCodeBrush = new ImageBrush();
            public static ImageBrush _sSuccessNonCodeBrush = new ImageBrush();
            public static ImageBrush _sFailedBrush = new ImageBrush();
            public static ImageBrush _sTimeoutBrush = new ImageBrush();
            public static ImageBrush _sRunningBrush = new ImageBrush();

            // Constants
            private const int _cTextLabeloffset_X = 4;
            private const int _cTextLabeloffset_Y = 4;
            private const double _cMinTextLabelWidthThreshold = 50.0f; // The minimum element width to be eligible for text display
            private const double _cMinDotDotDotWidthThreshold = 20.0f; // The minimum element width to be eligible for a "..." display

            public static void StaticInitialize()
            {
                _sSuccessCodeBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_code);
                _sSuccessNonCodeBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_noncode);
                _sFailedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Failed);
                _sTimeoutBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Timeout);
                _sRunningBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Running);

                _sbInitialized = true;
            }

            public BuildEvent(string name, Int64 timeStarted)
            {
                // Lazy initialize static resources
                if (!_sbInitialized)
                {
                    StaticInitialize();
                }

                _name = name;

                _toolTipText = _name.Replace("\"", "");

                _fileName = System.IO.Path.GetFileName(_name.Replace("\"", ""));

                _timeStarted = timeStarted;

                _state = BuildEventState.IN_PROGRESS;
            }

            public void SetOutputMessages(string outputMessages)
            {
                char[] newLineSymbol = new char[1];
                newLineSymbol[0] = (char)12;

                // Todo: Remove this crap!
                _outputMessages = outputMessages.Replace(new string(newLineSymbol), Environment.NewLine);
            }

            public void Start(CPUCore core)
            {
                _core = core;

                _brush = _sRunningBrush;

                _toolTipText = "BUILDING: " + _name.Replace("\"", "");
            }

            public void Stop(Int64 timeFinished, BuildEventState jobResult)
            {
                _timeFinished = timeFinished;

                double totalTimeSeconds = (_timeFinished - _timeStarted) / 1000.0f;

                Debug.Assert(totalTimeSeconds >= 0.0f);

                _toolTipText = string.Format("{0}", _name.Replace("\"", "")) + "\nStatus: ";

                _state = jobResult;

                switch (_state)
                {
                    case BuildEventState.SUCCEEDED:
                        if (_name.Contains(".cpp") || _name.Contains(".c"))
                        {
                            _brush = _sSuccessCodeBrush;
                        }
                        else
                        {
                            _brush = _sSuccessNonCodeBrush;
                        }
                        _toolTipText += "Success";

                        break;
                    case BuildEventState.FAILED:

                        _brush = _sFailedBrush;
                        _toolTipText += "Errors";

                        break;
                    case BuildEventState.TIMEOUT:

                        _brush = _sTimeoutBrush;
                        _toolTipText += "Timeout";

                        break;
                    default:
                        break;
                }

                _toolTipText += "\nDuration: " + GetTimeFormattedString(_timeFinished - _timeStarted);
                _toolTipText += "\nStart Time: " + GetTimeFormattedString(_timeStarted);
                _toolTipText += "\nEnd Time: " + GetTimeFormattedString(_timeFinished);

                if (null != _outputMessages && _outputMessages.Length > 0)
                {
                    // show only an extract of the errors so we don't flood the visual
                    int textLength = Math.Min(_outputMessages.Length, 100);

                    _toolTipText += "\n" + _outputMessages.Substring(0, textLength);
                    _toolTipText += "... [Double-Click on the event to see more details]";

                    _outputMessages = string.Format("[Output {0}]: {1}", _name.Replace("\"", ""), Environment.NewLine) + _outputMessages;

                    _StaticWindow.AddOutputWindowFilterItem(this);
                }
            }

            public bool JumpToEventLineInOutputBox()
            {
                bool bSuccess = false;

                int index = _StaticWindow.OutputTextBox.Text.IndexOf(_name.Replace("\"", ""));

                int lineNumber = _StaticWindow.OutputTextBox.Text.Substring(0, index).Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;

                _StaticWindow.OutputTextBox.ScrollToLine(lineNumber - 1);

                int position = _StaticWindow.OutputTextBox.GetCharacterIndexFromLineIndex(lineNumber - 1);
                if (position >= 0)
                {
                    int lineEnd = _StaticWindow.OutputTextBox.Text.IndexOf(Environment.NewLine, position);
                    if (lineEnd < 0)
                    {
                        lineEnd = _StaticWindow.OutputTextBox.Text.Length;
                    }

                    _StaticWindow.OutputTextBox.Select(position, lineEnd - position);
                }

                return bSuccess;
            }

            public bool HandleDoubleClickEvent()
            {
                bool bHandled = true;

                if (_state != BuildEventState.IN_PROGRESS && _outputMessages != null && _outputMessages.Length > 0)
                {
                    // Switch to the Output Window Tab item
                    _StaticWindow.MyTabControl.SelectedIndex = (int)eTABs.TAB_OUTPUT;

                    _StaticWindow.ChangeOutputWindowComboBoxSelection(this);
                }

                return bHandled;
            }

            public HitTestResult HitTest(Point localMousePosition)
            {
                HitTestResult result = null;

                if (_bordersRect.Contains(localMousePosition))
                {
                    result = new HitTestResult(this._core._parent, this._core, this);
                }

                return result;
            }

            public void RenderUpdate(ref double X, double Y)
            {
                long duration = 0;

                bool bIsCompleted = false;

                double OriginalWidthInPixels = 0.0f;
                double AdjustedWidthInPixels = 0.0f;

                double borderRectWidth = 0.0f;

                if (_state == BuildEventState.IN_PROGRESS)
                {
                    // Event is in progress
                    duration = (long)Math.Max(0.0f, GetCurrentBuildTimeMS(true) - _timeStarted);

                    Point textSize = ComputeTextSize(_fileName);

                    OriginalWidthInPixels = AdjustedWidthInPixels = _zoomFactor * pix_per_second * (double)duration / (double)1000;

                    borderRectWidth = OriginalWidthInPixels + pix_per_second * cTimeStepMS / 1000.0f;

                    borderRectWidth = Math.Max(Math.Min(_cMinTextLabelWidthThreshold * 2, textSize.X), borderRectWidth);
                }
                else
                {
                    // Event is completed
                    bIsCompleted = true;
                    duration = _timeFinished - _timeStarted;

                    // Handle the zoom factor
                    OriginalWidthInPixels = _zoomFactor * pix_per_second * (double)duration / (double)1000;

                    // Try to compensate for the pixels lost with the spacing introduced between events
                    AdjustedWidthInPixels = Math.Max(0.0f, OriginalWidthInPixels - pix_space_between_events);

                    borderRectWidth = AdjustedWidthInPixels;
                }

                // Adjust the start time position if possible
                double desiredX = _zoomFactor * pix_per_second * (double)_timeStarted / (double)1000;
                if (desiredX > X)
                {
                    X = desiredX;
                }

                // Are we a Low LOD candidate?
                bool isInLowLOD = (AdjustedWidthInPixels <= pix_LOD_Threshold) && bIsCompleted;

                // Update the element size and figure out of anything changed since the last update
                Rect newBorderRect = new Rect(X, Y, borderRectWidth, pix_height);
                Rect newProgressRect = new Rect(X, Y, AdjustedWidthInPixels, pix_height);

                _isDirty = !_bordersRect.Equals(newBorderRect) || !_progressRect.Equals(newProgressRect) || isInLowLOD != _isInLowLOD;

                _isInLowLOD = isInLowLOD;
                _bordersRect = newBorderRect;
                _progressRect = newProgressRect;

                // Update our horizontal position on the time-line
                X = X + OriginalWidthInPixels;

                // Make sure we update our Canvas boundaries
                UpdateEventsCanvasMaxSize(X, Y);

                // Detect the mouse cursor is over our element and display a Tooltip
                Point mousePosition = Mouse.GetPosition(_core);
                if (_bordersRect.Contains(mousePosition))
                {
                    _core._toolTip.Content = _toolTipText;
                }
            }

            public bool IsObjectVisibleInternal(Rect localRect)
            {
                Rect absoluteRect = new Rect(_core._x + localRect.X, _core._y + localRect.Y, localRect.Width, localRect.Height);

                return IsObjectVisible(absoluteRect);
            }

            public void OnRender(DrawingContext dc)
            {
                if (_isInLowLOD)
                {
                    if (_core._isLODBlockActive)
                    {
                        _core._currentLODRect.Width = Math.Max(_bordersRect.X + _bordersRect.Width - _core._currentLODRect.X, 0.0f);
                    }
                    else
                    {
                        _core._currentLODRect.X = _bordersRect.X;
                        _core._currentLODRect.Y = _bordersRect.Y;
                        _core._currentLODRect.Width = 0.0f;
                        _core._currentLODRect.Height = _bordersRect.Height;

                        _core._isLODBlockActive = true;
                    }
                }
                else
                {
                    if (_core._isLODBlockActive)
                    {
                        VisualBrush brush = new VisualBrush();
                        brush.Visual = CPUCore._sLODImage;
                        brush.Stretch = Stretch.None;
                        brush.TileMode = TileMode.Tile;
                        brush.AlignmentY = AlignmentY.Top;
                        brush.AlignmentX = AlignmentX.Left;
                        brush.ViewportUnits = BrushMappingMode.Absolute;
                        brush.Viewport = new Rect(0, 0, 40, 6);

                        if (IsObjectVisibleInternal(_core._currentLODRect))
                        {
#if ENABLE_RENDERING_STATS
                        _StaticWindow._numShapesDrawn++;
#endif
                            dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), _core._currentLODRect);
                        }

                        _core._isLODBlockActive = false;
                    }

                    if (IsObjectVisibleInternal(_bordersRect))
                    {
#if ENABLE_RENDERING_STATS
                    _StaticWindow._numShapesDrawn++;
#endif
                        dc.DrawImage(_brush.ImageSource, _progressRect);

                        SolidColorBrush colorBrush = Brushes.Black;

                        if (_state == BuildEventState.IN_PROGRESS)
                        {
                            // Draw an open rectangle
                            Point P0 = new Point(_bordersRect.X, _bordersRect.Y);
                            Point P1 = new Point(_bordersRect.X + _bordersRect.Width, _bordersRect.Y);
                            Point P2 = new Point(_bordersRect.X + _bordersRect.Width, _bordersRect.Y + _bordersRect.Height);
                            Point P3 = new Point(_bordersRect.X, _bordersRect.Y + _bordersRect.Height);

                            Pen pen = new Pen(Brushes.Gray, 1);

                            dc.DrawLine(pen, P0, P1);
                            dc.DrawLine(pen, P0, P3);
                            dc.DrawLine(pen, P3, P2);
                        }
                        else
                        {
                            if (_state == BuildEventState.FAILED)
                            {
                                //colorBrush = Brushes.WhiteSmoke;
                            }

                            dc.DrawRectangle(new VisualBrush(), new Pen(Brushes.Gray, 1), _bordersRect);
                        }

                        string textToDisplay = null;

                        if (_bordersRect.Width > _cMinTextLabelWidthThreshold)
                        {
                            textToDisplay = _fileName;
                        }
                        //else if (_bordersRect.Width > _cMinDotDotDotWidthThreshold)
                        //{
                        //    textToDisplay = "...";
                        //}

                        if (textToDisplay != null)
                        {
#if ENABLE_RENDERING_STATS
                        _StaticWindow._numTextElementsDrawn++;
#endif
                            double allowedTextWidth = Math.Max(0.0f, _bordersRect.Width - 2 * _cTextLabeloffset_X);

                            DrawText(dc, textToDisplay, _bordersRect.X + _cTextLabeloffset_X, _bordersRect.Y + _cTextLabeloffset_Y, allowedTextWidth, true, colorBrush);
                        }
                    }
                }
            }
        }


        /* Commands parsing feature */
        private BuildEventState TranslateBuildEventState(string eventString)
        {
            BuildEventState output = BuildEventState.UNKOWN;

            switch (eventString)
            {
                case "ERROR":
                    output = BuildEventState.FAILED;
                    break;
                case "SUCCESS":
                    output = BuildEventState.SUCCEEDED;
                    break;
                case "TIMEOUT":
                    output = BuildEventState.TIMEOUT;
                    break;
            }

            return output;
        }


        private enum BuildEventCommand
        {
            UNKNOWN = -1,
            START_BUILD,
            STOP_BUILD,
            START_JOB,
            FINISH_JOB,
            PROGRESS_STATUS
        }

        private BuildEventCommand TranslateBuildEventCommand(string commandString)
        {
            BuildEventCommand output = BuildEventCommand.UNKNOWN;

            switch (commandString)
            {
                case "START_BUILD":
                    output = BuildEventCommand.START_BUILD;
                    break;
                case "STOP_BUILD":
                    output = BuildEventCommand.STOP_BUILD;
                    break;
                case "START_JOB":
                    output = BuildEventCommand.START_JOB;
                    break;
                case "FINISH_JOB":
                    output = BuildEventCommand.FINISH_JOB;
                    break;
                case "PROGRESS_STATUS":
                    output = BuildEventCommand.PROGRESS_STATUS;
                    break;
            }

            return output;
        }


        const string _cLocalHostName = "local";
        const string _cPrepareBuildStepsText = "Preparing Build Steps";
        bool _bPreparingBuildsteps = false;
        Hashtable _hosts = new Hashtable();
        BuildHost _localHost = null;


        public static class CommandArgumentIndex
        {
            // Global arguments (apply to all commands)
            public const int TIME_STAMP = 0;
            public const int COMMAND_TYPE = 1;

            public const int START_BUILD_PID = 2;

            public const int START_JOB_HOST_NAME = 2;
            public const int START_JOB_EVENT_NAME = 3;

            public const int FINISH_JOB_RESULT = 2;
            public const int FINISH_JOB_HOST_NAME = 3;
            public const int FINISH_JOB_EVENT_NAME = 4;
            public const int FINISH_JOB_OUTPUT_MESSAGES = 5;

            public const int PROGRESS_STATUS_PROGRESS_PCT = 2;
        }


        private int _lastProcessedPosition = 0;

        private void ProcessInputFileStream()
        {
            if (_fileStream == null)
            {
                string path = System.Environment.GetEnvironmentVariable("TEMP") + @"\FastBuild\FastBuildLog.log";

                if (!Directory.Exists(System.IO.Path.GetDirectoryName(path)))
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                }

                try
                {
                    _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    ResetState();
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine("Exception! " + ex.ToString());
                    // the log file does not exist, bail out...
                    return;
                }
            }

            // The file has been emptied so we must reset our state and start over
            if (BuildRestarted())
            {
                ResetState();

                return;
            }

            // Read all the new data and append it to our _fileBuffer
            int numBytesToRead = (int)(_fileStream.Length - _fileStreamPosition);

            if (numBytesToRead > 0)
            {
                byte[] buffer = new byte[numBytesToRead];

                _fileStream.Seek(_fileStreamPosition, SeekOrigin.Begin);

                int numBytesRead = _fileStream.Read(buffer, 0, numBytesToRead);

                Debug.Assert(numBytesRead == numBytesToRead, "Could not read the expected amount of data from the log file...!");

                _fileStreamPosition += numBytesRead;

                _fileBuffer.AddRange(buffer);

                //Scan the current buffer looking for the last line position
                int newPayloadStart = _lastProcessedPosition;
                int newPayLoadSize = -1;
                for (int i = _fileBuffer.Count - 1; i > _lastProcessedPosition; --i)
                {
                    if (_fileBuffer[i] == '\n')
                    {
                        newPayLoadSize = i - newPayloadStart;
                        break;
                    }
                }

                if (newPayLoadSize > 0)
                {
                    string newEventsRaw = System.Text.Encoding.Default.GetString(_fileBuffer.GetRange(_lastProcessedPosition, newPayLoadSize).ToArray());
                    string[] newEvents = newEventsRaw.Split(new char[] { '\n' });

                    foreach (string eventString in newEvents)
                    {
                        string[] tokens = Regex.Matches(eventString, @"[\""].+?[\""]|[^ ]+")
                                         .Cast<Match>()
                                         .Select(m => m.Value)
                                         .ToList().ToArray();

                        // TODO More error handling...
                        if (tokens.Length >= 2)
                        {
                            // let's get the command timestamp and update our internal time reference
                            Int64 eventFileTime = Int64.Parse(tokens[CommandArgumentIndex.TIME_STAMP]);
                            Int64 eventLocalTimeMS = RegisterNewTimeStamp(eventFileTime);

                            // parse the command
                            string commandString = tokens[CommandArgumentIndex.COMMAND_TYPE];
                            BuildEventCommand command = TranslateBuildEventCommand(commandString);

                            switch (command)
                            {
                                case BuildEventCommand.START_BUILD:
                                    if (_buildRunningState == eBuildRunningState.Ready)
                                    {
                                        ExecuteCommandStartBuild(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.STOP_BUILD:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandStopBuild(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.START_JOB:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandStartJob(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.FINISH_JOB:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandFinishJob(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.PROGRESS_STATUS:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandProgressStatus(tokens);
                                    }
                                    break;
                                default:
                                    // Skipping unknown commands
                                    break;
                            }
                        }
                    }

                    _lastProcessedPosition += newPayLoadSize;
                }
            }
            else if (_buildRunningState == eBuildRunningState.Running && PollIsTargetProcessRunning() == false)
            {
                _latestTimeStampMS = GetCurrentSystemTimeMS();

                ExecuteCommandStopBuild(null, _latestTimeStampMS);
            }
        }

        // Commands handling
        private void ExecuteCommandStartBuild(string[] tokens, Int64 eventLocalTimeMS)
        {
            int targetPID = int.Parse(tokens[CommandArgumentIndex.START_BUILD_PID]);

            // remember our valid targetPID
            _targetPID = targetPID;

            // Record the start time
            _buildStartTimeMS = eventLocalTimeMS;

            _buildRunningState = eBuildRunningState.Running;

            // start the gif "building" animation
            StatusBarRunningGif.StartAnimation();

            ToolTip newToolTip = new ToolTip();
            StatusBarRunningGif.ToolTip = newToolTip;
            newToolTip.Content = "Build in Progress...";
        }

        private void ExecuteCommandStopBuild(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            if (_bPreparingBuildsteps)
            {
                _localHost.OnCompleteEvent(timeStamp, _cPrepareBuildStepsText, BuildEventState.SUCCEEDED, "");
            }

            // Stop all the active events currently running
            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;
                foreach (CPUCore core in host._cores)
                {
                    core.UnScheduleEvent(timeStamp, _cPrepareBuildStepsText, BuildEventState.TIMEOUT, "", true);
                }
            }

            _bPreparingBuildsteps = false;

            _buildRunningState = eBuildRunningState.Ready;

            StatusBarRunningGif.StopAnimation();
            StatusBarRunningGif.ToolTip = null;

            UpdateBuildProgress(100.0f);
        }

        private void ExecuteCommandStartJob(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            string hostName = tokens[CommandArgumentIndex.START_JOB_HOST_NAME];
            string eventName = tokens[CommandArgumentIndex.START_JOB_EVENT_NAME];

            if (_bPreparingBuildsteps)
            {
                _localHost.OnCompleteEvent(timeStamp, _cPrepareBuildStepsText, BuildEventState.SUCCEEDED, "");
            }

            BuildEvent newEvent = new BuildEvent(eventName, timeStamp);

            BuildHost host = null;
            if (_hosts.ContainsKey(hostName))
            {
                host = _hosts[hostName] as BuildHost;
            }
            else
            {
                // discovered a new host!
                host = new BuildHost(hostName);
                _hosts.Add(hostName, host);
            }

            host.OnStartEvent(newEvent);
        }

        private void ExecuteCommandFinishJob(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            string jobResultString = tokens[CommandArgumentIndex.FINISH_JOB_RESULT];
            string hostName = tokens[CommandArgumentIndex.FINISH_JOB_HOST_NAME];
            string eventName = tokens[CommandArgumentIndex.FINISH_JOB_EVENT_NAME];

            string eventOutputMessages = "";

            // Optional parameters
            if (tokens.Length > CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES)
            {
                eventOutputMessages = tokens[CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES].Substring(1, tokens[CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES].Length - 2);
            }

            BuildEventState jobResult = TranslateBuildEventState(jobResultString);

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;
                host.OnCompleteEvent(timeStamp, eventName, jobResult, eventOutputMessages);
            }

            UpdateBuildStatus(jobResult);
        }

        private void ExecuteCommandProgressStatus(string[] tokens)
        {
            float progressPCT = float.Parse(tokens[CommandArgumentIndex.PROGRESS_STATUS_PROGRESS_PCT]);

            // Update the build status after each job's result
            UpdateBuildProgress(progressPCT);
        }


        private static bool IsObjectVisible(Rect objectRect)
        {
            // Todo: activate clipping optimization
            //return true;

            // make the viewport 10% larger
            const double halfIncPct = 10.0f / (100.0f * 2.0f);

            double x = Math.Max(0.0f, _viewport.X - _viewport.Width * halfIncPct);
            double y = Math.Max(0.0f, _viewport.Y - _viewport.Height * halfIncPct);
            double w = _viewport.Width * (1.0 + halfIncPct);
            double h = _viewport.Height * (1.0 + halfIncPct);

            Rect largerViewport = new Rect(x, y, w, h);

            return largerViewport.IntersectsWith(objectRect) || largerViewport.Contains(objectRect);
        }

        private static Rect _viewport = new Rect();

        private static double _maxX = 0.0f;
        private static double _maxY = 0.0f;

        private static void UpdateEventsCanvasMaxSize(double X, double Y)
        {
            _maxX = X > _maxX ? X : _maxX;
            _maxY = Y > _maxY ? Y : _maxY;
        }

#if ENABLE_RENDERING_STATS
    private int _numShapesDrawn = 0;            // (stats) number of shapes (ex: Rectangle) drawn on each frame
    private int _numTextElementsDrawn = 0;      // (stats) number of text elements drawn on each frame
#endif

        private void UpdateViewport()
        {
            Rect newViewport = new Rect(_StaticWindow.EventsScrollViewer.HorizontalOffset, _StaticWindow.EventsScrollViewer.VerticalOffset,
                                _StaticWindow.EventsScrollViewer.ViewportWidth, _StaticWindow.EventsScrollViewer.ViewportHeight);


            if (!_viewport.Equals(newViewport))
            {
                foreach (DictionaryEntry entry in _hosts)
                {
                    BuildHost host = entry.Value as BuildHost;
                    foreach (CPUCore core in host._cores)
                    {
                        core.InvalidateVisual();
                    }
                }

                _viewport = newViewport;
            }
        }

        // Text rendering stuff
        private static GlyphTypeface _glyphTypeface = null;

        private const double _cFontSize = 12.0f;

        private static Point ComputeTextSize(string text)
        {
            Point result = new Point();

            for (int charIndex = 0; charIndex < text.Length; charIndex++)
            {
                ushort glyphIndex = _glyphTypeface.CharacterToGlyphMap[text[charIndex]];

                double width = _glyphTypeface.AdvanceWidths[glyphIndex] * _cFontSize;

                result.Y = Math.Max(_glyphTypeface.AdvanceHeights[glyphIndex] * _cFontSize, result.Y);

                result.X += width;
            }

            return result;
        }

        private static void DrawText(DrawingContext dc, string text, double x, double y, double maxWidth, bool bEnableDotDotDot, SolidColorBrush colorBrush)
        {
            ushort[] glyphIndexes = null;
            double[] advanceWidths = null;

            ushort[] tempGlyphIndexes = new ushort[text.Length];
            double[] tempAdvanceWidths = new double[text.Length];

            double totalTextWidth = 0;
            double maxHeight = 0.0f;

            bool needDoTDotDot = false;
            double desiredTextWidth = maxWidth;
            int charIndex = 0;

            // Build the text info and measure the final text width
            for (; charIndex < text.Length; charIndex++)
            {
                ushort glyphIndex = _glyphTypeface.CharacterToGlyphMap[text[charIndex]];
                tempGlyphIndexes[charIndex] = glyphIndex;

                double width = _glyphTypeface.AdvanceWidths[glyphIndex] * _cFontSize;
                tempAdvanceWidths[charIndex] = width;

                maxHeight = Math.Max(_glyphTypeface.AdvanceHeights[glyphIndex] * _cFontSize, maxHeight);

                totalTextWidth += width;

                if (totalTextWidth > desiredTextWidth)
                {
                    //we need to clip the text since it doesn't fit the allowed width
                    //do a second measurement pass
                    needDoTDotDot = true;
                    break;
                }
            }

            if (bEnableDotDotDot && needDoTDotDot)
            {
                ushort suffixGlyphIndex = _glyphTypeface.CharacterToGlyphMap['.'];
                double suffixWidth = _glyphTypeface.AdvanceWidths[suffixGlyphIndex] * _cFontSize;

                desiredTextWidth -= suffixWidth * 3;

                for (; charIndex > 0; charIndex--)
                {
                    double removedCharacterWidth = tempAdvanceWidths[charIndex];

                    totalTextWidth -= removedCharacterWidth;

                    if (totalTextWidth <= desiredTextWidth)
                    {
                        charIndex--;
                        break;
                    }
                }

                int finalNumCharacters = charIndex + 1 + 3;

                glyphIndexes = new ushort[finalNumCharacters];
                advanceWidths = new double[finalNumCharacters];

                Array.Copy(tempGlyphIndexes, glyphIndexes, charIndex + 1);
                Array.Copy(tempAdvanceWidths, advanceWidths, charIndex + 1);

                for (int i = charIndex + 1; i < finalNumCharacters; ++i)
                {
                    glyphIndexes[i] = suffixGlyphIndex;
                    advanceWidths[i] = suffixWidth;
                }
            }
            else
            {
                glyphIndexes = tempGlyphIndexes;
                advanceWidths = tempAdvanceWidths;
            }

            double roundedX = Math.Round(x);
            double roundedY = Math.Round(y + maxHeight);

            GlyphRun gr = new GlyphRun(
                _glyphTypeface,
                0,       // Bi-directional nesting level
                false,   // isSideways
                _cFontSize,      // pt size
                glyphIndexes,   // glyphIndices
                new Point(roundedX, roundedY),           // baselineOrigin
                advanceWidths,  // advanceWidths
                null,    // glyphOffsets
                null,    // characters
                null,    // deviceFontName
                null,    // clusterMap
                null,    // caretStops
                null);   // xmlLanguage

            dc.DrawGlyphRun(colorBrush, gr);
        }

        HitTestResult HitTest(Point mousePosition)
        {
            HitTestResult result = null;

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                result = host.HitTest(mousePosition);

                if (result != null)
                {
                    break;
                }
            }

            return result;
        }


        private void RenderUpdate()
        {
            // Handling Mouse panning
            UpdateMousePanning();

            // Resolve ViewPort center/size in case of zoom in/out event
            UpdateZoomTargetPosition();

            // Update the viewport and decide if we have to redraw the UI
            UpdateViewport();

            _maxX = 0.0f;
            _maxY = 0.0f;

            _timeBar.RenderUpdate(10, 0, _zoomFactor);

            double X = 10;
            double Y = 10;

            // Always draw the local host first
            if (_localHost != null)
            {
                _localHost.RenderUpdate(X, ref Y);
            }

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host != _localHost)
                {
                    host.RenderUpdate(X, ref Y);
                }
            }

            //Console.WriteLine("Scroll V Offset: (cores: {0} - events: {1})", CoresScrollViewer.ScrollableHeight, EventsScrollViewer.ScrollableHeight);

            EventsCanvas.Width = TimeBarCanvas.Width = _maxX + _viewport.Width * 0.25f;
            EventsCanvas.Height = CoresCanvas.Height = _maxY;

#if ENABLE_RENDERING_STATS
        Console.WriteLine("Render Stats (Shapes: {0} - Text: {1})", _numShapesDrawn, _numTextElementsDrawn);
        _numShapesDrawn = 0;
        _numTextElementsDrawn = 0;
#endif
        }


        private static string GetTimeFormattedString(Int64 timeMS)
        {
            Int64 remainingTimeSeconds = timeMS / 1000;

            int hours = (int)(remainingTimeSeconds / (60 * 60));
            remainingTimeSeconds -= hours * 60 * 60;

            int minutes = (int)(remainingTimeSeconds / (60));
            remainingTimeSeconds -= minutes * 60;

            string formattedText;

            if (hours > 0)
            {
                formattedText = string.Format("{0}:{1:00}:{2:00}", hours, minutes, remainingTimeSeconds);
            }
            else
            {
                formattedText = string.Format("{0}:{1:00}", minutes, remainingTimeSeconds);
            }

            return formattedText;
        }

        private static string GetTimeFormattedString2(Int64 timeMS)
        {
            Int64 remainingTimeSeconds = timeMS / 1000;

            int hours = (int)(remainingTimeSeconds / (60 * 60));
            remainingTimeSeconds -= hours * 60 * 60;

            int minutes = (int)(remainingTimeSeconds / (60));
            remainingTimeSeconds -= minutes * 60;

            string formattedText;

            if (hours > 0)
            {
                formattedText = string.Format("{0}h {1}m {2}s", hours, minutes, remainingTimeSeconds);
            }
            else
            {
                formattedText = string.Format("{0}m {1}s", minutes, remainingTimeSeconds);
            }

            return formattedText;
        }


        TimeBar _timeBar = null;

        private class TimeBar : Canvas
        {
            public TimeBar(Canvas parentCanvas)
            {
                _parentCanvas = parentCanvas;

                this.Width = _parentCanvas.Width;
                this.Height = _parentCanvas.Height;

                _parentCanvas.Children.Add(this);
            }

            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 1), _geometry);

                _textTags.ForEach(tag => DrawText(dc, tag._text, tag._x, tag._y, 100, false, Brushes.Black));
            }

            void UpdateGeometry(double X, double Y, double zoomFactor)
            {
                // Clear old geometry
                _geometry.Clear();

                _textTags.Clear();

                // Open a StreamGeometryContext that can be used to describe this StreamGeometry 
                // object's contents.
                using (StreamGeometryContext ctx = _geometry.Open())
                {
                    Int64 totalTimeMS = 0;

                    Int64 numSteps = GetCurrentBuildTimeMS() / (_bigTimeUnit * 1000);
                    Int64 remainder = GetCurrentBuildTimeMS() % (_bigTimeUnit * 1000);

                    numSteps += remainder > 0 ? 2 : 1;

                    Int64 timeLimitMS = numSteps * _bigTimeUnit * 1000;

                    while (totalTimeMS <= timeLimitMS)
                    {
                        bool bDrawBigMarker = totalTimeMS % (_bigTimeUnit * 1000) == 0;

                        double x = X + zoomFactor * pix_per_second * totalTimeMS / 1000.0f;

                        //if (x >= _savedTimebarViewPort.X && x <= _savedTimebarViewPort.Y)
                        {
                            double height = bDrawBigMarker ? 5.0f : 2.0f;

                            ctx.BeginFigure(new Point(x, Y), true /* is filled */, false /* is closed */);

                            // Draw a line to the next specified point.
                            ctx.LineTo(new Point(x, Y + height), true /* is stroked */, false /* is smooth join */);

                            if (bDrawBigMarker)
                            {
                                string formattedText = GetTimeFormattedString(totalTimeMS);

                                Point textSize = ComputeTextSize(formattedText);

                                double horizontalCorrection = textSize.X / 2.0f;

                                TextTag newTag = new TextTag(formattedText, x - horizontalCorrection, Y + height + 2);

                                _textTags.Add(newTag);
                            }
                        }

                        totalTimeMS += _smallTimeUnit * 1000;
                    }
                }
            }

            bool UpdateTimeUnits()
            {
                bool bNeedsToUpdateGeometry = false;

                const double pixChunkSize = 100.0f;

                double timePerChunk = pixChunkSize / (_zoomFactor * pix_per_second);

                int newBigTimeUnit = 0;
                int newSmallTimeUnit = 0;

                if (timePerChunk > 30.0f)
                {
                    newBigTimeUnit = 60;
                    newSmallTimeUnit = 10;
                }
                else if (timePerChunk > 10.0f)
                {
                    newBigTimeUnit = 30;
                    newSmallTimeUnit = 6;
                }
                else if (timePerChunk > 5.0f)
                {
                    newBigTimeUnit = 10;
                    newSmallTimeUnit = 2;
                }
                else
                {
                    newBigTimeUnit = 5;
                    newSmallTimeUnit = 1;
                }

                Point newTimebarViewPort = new Point(_StaticWindow.EventsScrollViewer.HorizontalOffset, _StaticWindow.EventsScrollViewer.HorizontalOffset + _StaticWindow.EventsScrollViewer.ViewportWidth);

                if (_zoomFactor != _savedZoomFactor || GetCurrentBuildTimeMS() != _savedBuildTime || newTimebarViewPort != _savedTimebarViewPort)
                {
                    _bigTimeUnit = newBigTimeUnit;
                    _smallTimeUnit = newSmallTimeUnit;

                    _savedZoomFactor = _zoomFactor;

                    _savedBuildTime = GetCurrentBuildTimeMS();

                    _savedTimebarViewPort = newTimebarViewPort;

                    this.InvalidateVisual();

                    bNeedsToUpdateGeometry = true;
                }

                return bNeedsToUpdateGeometry;
            }

            public void RenderUpdate(double X, double Y, double zoomFactor)
            {
                if (UpdateTimeUnits())
                {
                    this.InvalidateVisual();

                    UpdateGeometry(X, Y, zoomFactor);
                }
            }

            private class TextTag
            {
                public TextTag(string text, double x, double y)
                {
                    _text = text;
                    _x = x;
                    _y = y;
                }

                public string _text;
                public double _x;
                public double _y;
            }

            List<TextTag> _textTags = new List<TextTag>();

            StreamGeometry _geometry = new StreamGeometry();

            int _bigTimeUnit = 0;
            int _smallTimeUnit = 0;

            double _savedZoomFactor = 0.0f;
            double _savedBuildTime = 0.0f;
            Point _savedTimebarViewPort = new Point();

            Canvas _parentCanvas = null;
        }


        private void HandleTick(object sender, EventArgs e)
        {
            try
            {
                ProcessInputFileStream();

                RenderUpdate();

                UpdateStatusBar();
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("Exception detected... Restarting! details: " + ex.ToString()) ;
                ResetState();
            }
        }
    }
}
