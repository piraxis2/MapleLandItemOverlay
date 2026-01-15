using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MapleOverlay.Manager;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Text;

namespace MapleOverlay
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out Point lpPoint);


        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X; public int Y; }

        private DispatcherTimer? _trackingTimer;
        private IntPtr _mapleHandle;
        private MapleApiManager _apiManager = new MapleApiManager();
        private OcrManager _ocrManager;
        private ConfigManager _configManager;
        
        private bool _isExitKeyDown;
        private bool _isCaptureKeyDown;
        private bool _isMenuKeyDown;
        private bool _isManualSearchKeyDown;

        private bool _isDragging;
        private System.Windows.Point _startPoint;
        private bool _isInfoPanelVisible;

        private Bitmap? _frozenScreenBitmap;

        private TranslateTransform _infoPanelTransform = new TranslateTransform();
        private TranslateTransform _expPanelTransform = new TranslateTransform();
        private TranslateTransform _realTimeExpPanelTransform = new TranslateTransform();
        private TranslateTransform _minimizedExpPanelTransform = new TranslateTransform();
        private TranslateTransform _minimizedRealTimePanelTransform = new TranslateTransform();
        private TranslateTransform _floatingControlPanelTransform = new TranslateTransform();
        private TranslateTransform _minimizedControlPanelTransform = new TranslateTransform();
        
        private System.Windows.Point _panelDragStart;
        private bool _isPanelDragging;
        private FrameworkElement? _draggedPanel;

        // 설정 임시 저장용 변수
        private int _tempKeyMenu;
        private int _tempKeyCapture;
        private int _tempKeyClose;
        private int _tempKeyManualSearch;

        private enum CaptureMode { Item, ExpUi }
        private CaptureMode _currentCaptureMode;

        private ExpManager _expManager = new ExpManager();
        private ExpManager _realTimeExpManager;

        // 실시간 트래커용 변수
        private DispatcherTimer _realTimeTrackingTimer;
        private System.Drawing.Rectangle _expSnapshotRect; // 공용 경험치 UI 영역
        private bool _isRealTimeTracking;
        private bool _isRealTimePaused;

        // 경험치 타이머용 변수
        private DispatcherTimer? _expTimer;
        private TimeSpan _expTimerRemaining;
        private bool _isExpTimerRunning;
        private DateTime _expTimerStartTime;

        public MainWindow()
        {
            InitializeComponent();
            
            try
            {
                this.Icon = new BitmapImage(new Uri("pack://application:,,,/유령.ico"));
            }
            catch { }

            _ocrManager = new OcrManager();
            _configManager = new ConfigManager();
            _realTimeExpManager = new ExpManager();

            InfoPanel.RenderTransform = _infoPanelTransform;
            ExpPanel.RenderTransform = _expPanelTransform;
            RealTimeExpPanel.RenderTransform = _realTimeExpPanelTransform;
            MinimizedExpPanel.RenderTransform = _minimizedExpPanelTransform;
            MinimizedRealTimePanel.RenderTransform = _minimizedRealTimePanelTransform;
            FloatingControlPanel.RenderTransform = _floatingControlPanelTransform;
            MinimizedControlPanel.RenderTransform = _minimizedControlPanelTransform;

            // 저장된 플로팅 패널 위치 복원
            _floatingControlPanelTransform.X = _configManager.Config.FloatingPanelX;
            _floatingControlPanelTransform.Y = _configManager.Config.FloatingPanelY;
            _minimizedControlPanelTransform.X = _configManager.Config.FloatingPanelX;
            _minimizedControlPanelTransform.Y = _configManager.Config.FloatingPanelY;

            UpdateKeyGuide();

            // 실시간 트래커 타이머 초기화
            _realTimeTrackingTimer = new DispatcherTimer();
            _realTimeTrackingTimer.Interval = TimeSpan.FromSeconds(1);
            _realTimeTrackingTimer.Tick += RealTimeTrackingTimer_Tick;

            // 경험치 타이머 초기화
            _expTimer = new DispatcherTimer();
            _expTimer.Interval = TimeSpan.FromSeconds(1);
            _expTimer.Tick += ExpTimer_Tick;

            // --- 이벤트 핸들러 ---
            SearchInput.GotFocus += (_, _) => { if (SearchInput.Text == "직접 검색...") SearchInput.Text = ""; };
            SearchInput.LostFocus += (_, _) => { if (string.IsNullOrWhiteSpace(SearchInput.Text)) SearchInput.Text = "직접 검색..."; };
        }

        private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement panel)
            {
                _isPanelDragging = true;
                _draggedPanel = panel;
                _panelDragStart = e.GetPosition(this);
                panel.CaptureMouse();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isPanelDragging && _draggedPanel != null)
            {
                var currentPos = e.GetPosition(this);
                var diff = currentPos - _panelDragStart;
                
                TranslateTransform? transform = null;
                if (_draggedPanel == InfoPanel) transform = _infoPanelTransform;
                else if (_draggedPanel == ExpPanel) transform = _expPanelTransform;
                else if (_draggedPanel == RealTimeExpPanel) transform = _realTimeExpPanelTransform;
                else if (_draggedPanel == MinimizedExpPanel) transform = _minimizedExpPanelTransform;
                else if (_draggedPanel == MinimizedRealTimePanel) transform = _minimizedRealTimePanelTransform;
                else if (_draggedPanel == FloatingControlPanel) transform = _floatingControlPanelTransform;
                else if (_draggedPanel == MinimizedControlPanel) transform = _minimizedControlPanelTransform;

                if (transform != null)
                {
                    transform.X += diff.X;
                    transform.Y += diff.Y;
                }
                
                _panelDragStart = currentPos;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_isPanelDragging)
            {
                // 플로팅 패널 드래그 종료 시 위치 저장
                if (_draggedPanel == FloatingControlPanel || _draggedPanel == MinimizedControlPanel)
                {
                    var transform = _draggedPanel == FloatingControlPanel ? _floatingControlPanelTransform : _minimizedControlPanelTransform;
                    _configManager.Config.FloatingPanelX = transform.X;
                    _configManager.Config.FloatingPanelY = transform.Y;
                    _configManager.SaveConfig();
                }

                _isPanelDragging = false;
                _draggedPanel?.ReleaseMouseCapture();
                _draggedPanel = null;
            }
        }

        private void UpdateKeyGuide()
        {
            string GetKeyName(int keyCode)
            {
                if (keyCode == 0xC0) return "`";
                if (keyCode == 0xDC) return "\\";
                if (keyCode >= 0x70 && keyCode <= 0x7B) return "F" + (keyCode - 0x6F);
                if (keyCode == 0x1B) return "ESC";
                return ((Key)KeyInterop.KeyFromVirtualKey(keyCode)).ToString();
            }

            var cfg = _configManager.Config;
            KeyGuideText.Text = $"메뉴: {GetKeyName(cfg.KeyMenu)} | 캡처: {GetKeyName(cfg.KeyCapture)} | 검색: {GetKeyName(cfg.KeyManualSearch)} | 닫기: {GetKeyName(cfg.KeyClosePanel)}";
            
            if (MenuKeyGuideText != null)
            {
                MenuKeyGuideText.Text = KeyGuideText.Text;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetClickThrough(true);

            _trackingTimer = new DispatcherTimer();
            _trackingTimer.Interval = TimeSpan.FromMilliseconds(50);
            _trackingTimer.Tick += TrackingTimer_Tick;
            _trackingTimer.Start();
        }

        private void SetClickThrough(bool enable)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, -20);
            
            if (enable)
                SetWindowLong(hwnd, -20, extendedStyle | 0x80000 | 0x20);
            else
                SetWindowLong(hwnd, -20, (extendedStyle | 0x80000) & ~0x20);
        }

        private void TrackingTimer_Tick(object? _, EventArgs __)
        {
            var cfg = _configManager.Config;

            // 메뉴 단축키 (기본 F12)
            if ((GetAsyncKeyState(cfg.KeyMenu) & 0x8000) != 0)
            {
                if (!_isMenuKeyDown)
                {
                    _isMenuKeyDown = true;
                    ToggleModeSelection();
                }
            }
            else
            {
                _isMenuKeyDown = false;
            }

            // 수동 검색 단축키 (기본 \)
            if ((GetAsyncKeyState(cfg.KeyManualSearch) & 0x8000) != 0)
            {
                if (!_isManualSearchKeyDown)
                {
                    _isManualSearchKeyDown = true;
                    ModeSelectionPanel.Visibility = Visibility.Collapsed;
                    InfoPanel.Visibility = Visibility.Visible;
                    OpenManualSearch();
                }
            }
            else
            {
                _isManualSearchKeyDown = false;
            }

            // 닫기 단축키 (기본 ESC)
            if ((GetAsyncKeyState(cfg.KeyClosePanel) & 0x8000) != 0)
            {
                CloseAllPanels();
                return;
            }

            if (CaptureCanvas.Visibility == Visibility.Visible) return;

            _mapleHandle = FindWindow("MapleStoryClass", "MapleStory");
            
            if (_mapleHandle != IntPtr.Zero)
            {
                if (GetWindowRect(_mapleHandle, out Rect rect))
                {
                    this.Left = rect.Left;
                    this.Top = rect.Top;
                    this.Width = rect.Right - rect.Left;
                    this.Height = rect.Bottom - rect.Top;
                }
            }
            else
            {
                this.Left = 0;
                this.Top = 0;
                this.Width = SystemParameters.PrimaryScreenWidth;
                this.Height = SystemParameters.PrimaryScreenHeight;
            }

            bool isAnyPanelVisible = _isInfoPanelVisible || ExpPanel.Visibility == Visibility.Visible || ModeSelectionPanel.Visibility == Visibility.Visible || RealTimeExpPanel.Visibility == Visibility.Visible || MinimizedExpPanel.Visibility == Visibility.Visible || MinimizedRealTimePanel.Visibility == Visibility.Visible || SettingsPanel.Visibility == Visibility.Visible;
            
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, -20);
            bool isClickThrough = (extendedStyle & 0x20) != 0;

            GetCursorPos(out Point cursor);
            
            // FloatingControlPanel 영역 확인
            var controlPanelPos = FloatingControlPanel.PointToScreen(new System.Windows.Point(0, 0));
            var controlPanelRect = new System.Windows.Rect(controlPanelPos.X, controlPanelPos.Y, FloatingControlPanel.ActualWidth, FloatingControlPanel.ActualHeight);
            bool isMouseOverControlPanel = controlPanelRect.Contains(new System.Windows.Point(cursor.X, cursor.Y)) && FloatingControlPanel.Visibility == Visibility.Visible;

            // MinimizedControlPanel 영역 확인
            var minControlPanelPos = MinimizedControlPanel.PointToScreen(new System.Windows.Point(0, 0));
            var minControlPanelRect = new System.Windows.Rect(minControlPanelPos.X, minControlPanelPos.Y, MinimizedControlPanel.ActualWidth, MinimizedControlPanel.ActualHeight);
            bool isMouseOverMinControlPanel = minControlPanelRect.Contains(new System.Windows.Point(cursor.X, cursor.Y)) && MinimizedControlPanel.Visibility == Visibility.Visible;

            bool shouldBeClickable = isAnyPanelVisible || isMouseOverControlPanel || isMouseOverMinControlPanel || _draggedPanel != null;

            if (shouldBeClickable && isClickThrough)
            {
                SetClickThrough(false);
            }
            else if (!shouldBeClickable && !isClickThrough)
            {
                SetClickThrough(true);
            }

            // 캡처 단축키 (기본 `)
            bool isCaptureKeyDown = (GetAsyncKeyState(cfg.KeyCapture) & 0x8000) != 0;
            if (isCaptureKeyDown && !_isCaptureKeyDown)
            {
                _isCaptureKeyDown = true;
                if (ModeSelectionPanel.Visibility == Visibility.Visible)
                {
                    ModeSelectionPanel.Visibility = Visibility.Collapsed;
                }
                _currentCaptureMode = CaptureMode.Item;
                StartCaptureMode();
            }
            else if (!isCaptureKeyDown)
            {
                _isCaptureKeyDown = false;
            }
        }

        private void ToggleModeSelection()
        {
            if (ModeSelectionPanel.Visibility == Visibility.Visible)
            {
                ModeSelectionPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ModeSelectionPanel.Visibility = Visibility.Visible;
                SettingsPanel.Visibility = Visibility.Collapsed; // 설정 패널이 열려있으면 닫음
            }
        }

        private void CloseAllPanels()
        {
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;
            ExpPanel.Visibility = Visibility.Hidden;
            RealTimeExpPanel.Visibility = Visibility.Hidden;
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            MinimizedExpPanel.Visibility = Visibility.Hidden;
            MinimizedRealTimePanel.Visibility = Visibility.Hidden;
            
            if (CaptureCanvas.Visibility == Visibility.Visible)
            {
                EndCaptureMode();
            }
            
            StopRealTimeTracking();
            StopExpTimer();
        }

        // --- 설정 관련 로직 ---

        private void BtnSettings_Click(object _, RoutedEventArgs __)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;

            // 현재 설정값 로드
            var cfg = _configManager.Config;
            _tempKeyMenu = cfg.KeyMenu;
            _tempKeyCapture = cfg.KeyCapture;
            _tempKeyClose = cfg.KeyClosePanel;
            _tempKeyManualSearch = cfg.KeyManualSearch;

            UpdateSettingsUi();
        }

        private void UpdateSettingsUi()
        {
            string GetKeyName(int keyCode)
            {
                if (keyCode == 0xC0) return "`";
                if (keyCode == 0xDC) return "\\";
                if (keyCode >= 0x70 && keyCode <= 0x7B) return "F" + (keyCode - 0x6F);
                if (keyCode == 0x1B) return "ESC";
                return ((Key)KeyInterop.KeyFromVirtualKey(keyCode)).ToString();
            }

            InputKeyMenu.Text = GetKeyName(_tempKeyMenu);
            InputKeyCapture.Text = GetKeyName(_tempKeyCapture);
            InputKeyClose.Text = GetKeyName(_tempKeyClose);
            InputKeyManualSearch.Text = GetKeyName(_tempKeyManualSearch);
        }

        private void InputKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            int vKey = KeyInterop.VirtualKeyFromKey(e.Key);
            
            // 특수 키 처리 (System Key 등)
            if (e.Key == Key.System) vKey = KeyInterop.VirtualKeyFromKey(e.SystemKey);

            if (sender is TextBox textBox)
            {
                string tag = textBox.Tag.ToString();
                if (tag == "Menu") _tempKeyMenu = vKey;
                else if (tag == "Capture") _tempKeyCapture = vKey;
                else if (tag == "Close") _tempKeyClose = vKey;
                else if (tag == "ManualSearch") _tempKeyManualSearch = vKey;

                UpdateSettingsUi();
            }
        }

        private void BtnSaveSettings_Click(object _, RoutedEventArgs __)
        {
            var cfg = _configManager.Config;
            cfg.KeyMenu = _tempKeyMenu;
            cfg.KeyCapture = _tempKeyCapture;
            cfg.KeyClosePanel = _tempKeyClose;
            cfg.KeyManualSearch = _tempKeyManualSearch;
            
            _configManager.SaveConfig();
            UpdateKeyGuide();
            
            SettingsPanel.Visibility = Visibility.Collapsed;
            ModeSelectionPanel.Visibility = Visibility.Visible;
        }

        private void BtnCancelSettings_Click(object _, RoutedEventArgs __)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ModeSelectionPanel.Visibility = Visibility.Visible;
        }

        private void BtnCloseMenu_Click(object _, RoutedEventArgs __)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
        }

        private void ModeCaptureExpUI_Click(object _, RoutedEventArgs __)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            _currentCaptureMode = CaptureMode.ExpUi;
            StartCaptureMode();
        }

        private void ModeExp_Click(object _, RoutedEventArgs __)
        {
            if (_expSnapshotRect.IsEmpty)
            {
                MessageBox.Show("먼저 '경험치 UI 캡처'를 실행해주세요.", "알림");
                return;
            }
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            ExpPanel.Visibility = Visibility.Visible;
            MinimizedExpPanel.Visibility = Visibility.Hidden;
        }

        private void ModeRealTimeExp_Click(object _, RoutedEventArgs __)
        {
            if (_expSnapshotRect.IsEmpty)
            {
                MessageBox.Show("먼저 '경험치 UI 캡처'를 실행해주세요.", "알림");
                return;
            }
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            RealTimeExpPanel.Visibility = Visibility.Visible;
            MinimizedRealTimePanel.Visibility = Visibility.Hidden;
            
            _isRealTimeTracking = true;
            _isRealTimePaused = false;
            BtnPauseResumeRealTime.Content = "일시정지";
            BtnPauseResumeRealTime.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444444"));
            _realTimeTrackingTimer.Start();
            
            PerformRealTimeUpdate(true);
        }

        private void ModeManual_Click(object _, RoutedEventArgs __)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Visible;
            OpenManualSearch();
        }

        private void BtnExitApp_Click(object _, RoutedEventArgs __)
        {
            Application.Current.Shutdown();
        }

        private void BtnUpdateExp_Click(object _, RoutedEventArgs __)
        {
            PerformExpUpdate();
        }

        private void PerformExpUpdate(bool isStart = false)
        {
            if (!_expSnapshotRect.IsEmpty)
            {
                try
                {
                    using (Bitmap bitmap = new Bitmap(_expSnapshotRect.Width, _expSnapshotRect.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.CopyFromScreen(_expSnapshotRect.Left, _expSnapshotRect.Top, 0, 0, bitmap.Size);
                        }
                        
                        string text = _ocrManager.RecognizeText(bitmap);
                        ProcessExpOcr(text, isStart);
                    }
                }
                catch
                {
                    // OCR 또는 화면 캡처 오류 시 사용자에게 알림
                    MessageBox.Show("경험치 UI를 읽는 중 오류가 발생했습니다.", "오류");
                }
            }
        }
        
        private void PerformRealTimeUpdate(bool isStart = false)
        {
            if (!_expSnapshotRect.IsEmpty)
            {
                try
                {
                    using (Bitmap bitmap = new Bitmap(_expSnapshotRect.Width, _expSnapshotRect.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.CopyFromScreen(_expSnapshotRect.Left, _expSnapshotRect.Top, 0, 0, bitmap.Size);
                        }
                        
                        string text = _ocrManager.RecognizeText(bitmap);
                        ProcessRealTimeExpOcr(text, isStart);
                    }
                }
                catch
                {
                    // 실시간 모드에서는 오류 메시지를 띄우지 않음 (사용자 경험 방해 최소화)
                }
            }
        }

        private void CloseExpButton_Click(object _, RoutedEventArgs __)
        {
            ExpPanel.Visibility = Visibility.Hidden;
            MinimizedExpPanel.Visibility = Visibility.Hidden;
            StopExpTimer();
        }

        private void CloseRealTimeExpButton_Click(object _, RoutedEventArgs __)
        {
            RealTimeExpPanel.Visibility = Visibility.Hidden;
            MinimizedRealTimePanel.Visibility = Visibility.Hidden;
            StopRealTimeTracking();
        }

        private void BtnStopRealTimeTracking_Click(object _, RoutedEventArgs __)
        {
            StopRealTimeTracking();
            RealTimeExpPanel.Visibility = Visibility.Hidden;
            MinimizedRealTimePanel.Visibility = Visibility.Hidden;
        }

        private void BtnPauseResumeRealTime_Click(object _, RoutedEventArgs __)
        {
            if (_isRealTimePaused)
            {
                _isRealTimePaused = false;
                _realTimeTrackingTimer.Start();
                BtnPauseResumeRealTime.Content = "일시정지";
                BtnPauseResumeRealTime.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444444"));
            }
            else
            {
                _isRealTimePaused = true;
                _realTimeTrackingTimer.Stop();
                BtnPauseResumeRealTime.Content = "재개";
                BtnPauseResumeRealTime.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#006600"));
            }
        }

        private void BtnRestartRealTime_Click(object _, RoutedEventArgs __)
        {
            _isRealTimePaused = false;
            BtnPauseResumeRealTime.Content = "일시정지";
            BtnPauseResumeRealTime.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444444"));
            
            if (!_realTimeTrackingTimer.IsEnabled)
            {
                _realTimeTrackingTimer.Start();
            }
            
            PerformRealTimeUpdate(true); // isStart = true로 호출하여 초기화
        }

        private void CloseInfoPanel()
        {
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;
            SetClickThrough(true);
        }

        private void OpenManualSearch()
        {
            _isInfoPanelVisible = true;
            
            ItemNameText.Text = "아이템 검색";
            ItemReqText.Visibility = Visibility.Collapsed;
            ReqSeparator.Visibility = Visibility.Collapsed;
            ItemStatsText.Text = "";
            ItemDescText.Text = "검색어를 입력하세요.";
            DescSeparator.Visibility = Visibility.Collapsed;
            
            SearchInput.Text = "";
            SearchInput.Focus();
            
            SetClickThrough(false);
        }

        private void CloseButton_Click(object _, RoutedEventArgs __)
        {
            CloseInfoPanel();
        }

        private async void StartCaptureMode()
        {
            // 캡처 중에는 메뉴와 정보 패널만 숨김
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;

            await Task.Delay(100);

            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            
            if (_frozenScreenBitmap != null)
            {
                _frozenScreenBitmap.Dispose();
            }

            _frozenScreenBitmap = new Bitmap(screenWidth, screenHeight);
            using (Graphics g = Graphics.FromImage(_frozenScreenBitmap))
            {
                g.CopyFromScreen(0, 0, 0, 0, _frozenScreenBitmap.Size);
            }

            var imageSource = BitmapToImageSource(_frozenScreenBitmap);
            CaptureCanvas.Background = new ImageBrush(imageSource);

            this.Left = 0;
            this.Top = 0;
            this.Width = screenWidth;
            this.Height = screenHeight;

            string modeText = "모드: -";
            if (_currentCaptureMode == CaptureMode.Item) modeText = "모드: 아이템 검색";
            else if (_currentCaptureMode == CaptureMode.ExpUi) modeText = "모드: 경험치 UI 캡처";
            
            CaptureModeText.Text = modeText;

            SetClickThrough(false);
            CaptureCanvas.Visibility = Visibility.Visible;
        }

        private void EndCaptureMode()
        {
            CaptureCanvas.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Collapsed;
            CaptureCanvas.Background = null; 
        }

        private ImageSource BitmapToImageSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private void CaptureCanvas_MouseDown(object _, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(CaptureCanvas);
            
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
        }

        private void CaptureCanvas_MouseMove(object _, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var currentPoint = e.GetPosition(CaptureCanvas);
            
            double x = Math.Min(currentPoint.X, _startPoint.X);
            double y = Math.Min(currentPoint.Y, _startPoint.Y);
            double w = Math.Abs(currentPoint.X - _startPoint.X);
            double h = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }

        private void CaptureCanvas_MouseUp(object _, MouseButtonEventArgs __)
        {
            if (!_isDragging) return;
            _isDragging = false;

            double x = Canvas.GetLeft(SelectionRect);
            double y = Canvas.GetTop(SelectionRect);
            double w = SelectionRect.Width;
            double h = SelectionRect.Height;

            EndCaptureMode();

            if (w > 10 && h > 10)
            {
                System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle((int)x, (int)y, (int)w, (int)h);
                
                if (_currentCaptureMode == CaptureMode.Item)
                {
                    PerformOcrFromFrozenImage(cropRect);
                }
                else if (_currentCaptureMode == CaptureMode.ExpUi)
                {
                    _expSnapshotRect = cropRect;
                    MessageBox.Show("경험치 UI 영역이 저장되었습니다.", "알림");
                }
            }
        }

        private void PerformOcrFromFrozenImage(System.Drawing.Rectangle cropRect)
        {
            if (_frozenScreenBitmap == null) return;

            try
            {
                if (cropRect.Right > _frozenScreenBitmap.Width) cropRect.Width = _frozenScreenBitmap.Width - cropRect.X;
                if (cropRect.Bottom > _frozenScreenBitmap.Height) cropRect.Height = _frozenScreenBitmap.Height - cropRect.Y;

                using (Bitmap cropped = _frozenScreenBitmap.Clone(cropRect, _frozenScreenBitmap.PixelFormat))
                {
                    string text = _ocrManager.RecognizeText(cropped);
                    ProcessItemOcr(text);
                }
            }
            catch (Exception ex)
            {
                ItemDescText.Text = $"캡처 오류: {ex.Message}";
                InfoPanel.Visibility = Visibility.Visible;
                _isInfoPanelVisible = true;
            }
        }

        private void RealTimeTrackingTimer_Tick(object? _, EventArgs __)
        {
            if (!_isRealTimeTracking || _expSnapshotRect.IsEmpty || _isRealTimePaused) return;

            PerformRealTimeUpdate();

            if (MinimizedRealTimePanel.Visibility == Visibility.Visible)
            {
                TxtMinimizedRealTimeStatus.Text = "실시간 추적 중";
            }
        }

        private void StopRealTimeTracking()
        {
            _isRealTimeTracking = false;
            _isRealTimePaused = false;
            _realTimeTrackingTimer.Stop();
        }

        private void ProcessItemOcr(string text)
        {
            InfoPanel.Visibility = Visibility.Visible;
            _isInfoPanelVisible = true;
            
            ItemNameText.Text = string.IsNullOrWhiteSpace(text) ? "인식 실패" : $"OCR: {text}";
            
            ItemReqText.Visibility = Visibility.Collapsed;
            ReqSeparator.Visibility = Visibility.Collapsed;
            ItemStatsText.Text = "";
            ItemDescText.Text = "API 검색 대기 중...";
            DescSeparator.Visibility = Visibility.Collapsed;
            
            SearchInput.Text = text ?? "";

            if (!string.IsNullOrWhiteSpace(text))
            {
                SearchItem(text);
            }
            else
            {
                ItemDescText.Text = "글자를 읽지 못했습니다. 아래 검색창을 이용하세요.";
            }
        }

        private void ProcessExpOcr(string text, bool isStart)
        {
            if (MinimizedExpPanel.Visibility != Visibility.Visible)
            {
                ExpPanel.Visibility = Visibility.Visible;
            }
            TxtDebugOcr.Text = string.IsNullOrWhiteSpace(text) ? "(인식 실패)" : text;

            if (string.IsNullOrWhiteSpace(text)) return;

            long expValue = ParseExpValue(text);
            
            if (expValue < 0) return;

            UpdateExpPanel(expValue, isStart);
        }

        private void ProcessRealTimeExpOcr(string text, bool isStart)
        {
            if (MinimizedRealTimePanel.Visibility != Visibility.Visible)
            {
                RealTimeExpPanel.Visibility = Visibility.Visible;
            }
            TxtRealTimeDebugOcr.Text = string.IsNullOrWhiteSpace(text) ? "(인식 실패)" : text;

            if (string.IsNullOrWhiteSpace(text)) return;

            long expValue = ParseExpValue(text);
            
            if (expValue < 0) return;

            UpdateRealTimeExpPanel(expValue, isStart);
        }

        private void UpdateExpPanel(long expValue, bool isStart)
        {
            if (isStart)
            {
                if (_isExpTimerRunning)
                {
                    _expManager.StartWithTime(expValue, _expTimerStartTime);
                }
                else
                {
                    _expManager.Start(expValue);
                }

                TxtStartExp.Text = $"{expValue:N0}";
                TxtCurrentExp.Text = "-";
                TxtStartTime.Text =_expManager.StartTime.ToString(@"hh\:mm\:ss");
                TxtElapsedTime.Text = "00:00:00";
                TxtGainedExp.Text = "0";
                TxtExpPerHour.Text = "0 / hr";
            }
            else
            {
                _expManager.Update(expValue);
                TxtCurrentExp.Text = $"{expValue:N0}";
                
                var stats = _expManager.GetStats();
                TxtElapsedTime.Text = stats.Elapsed.ToString(@"hh\:mm\:ss");
                TxtGainedExp.Text = $"{stats.GainedExp:N0}";
                TxtExpPerHour.Text = $"{stats.ExpPerHour:N0} / hr";
            }
        }

        private void UpdateRealTimeExpPanel(long expValue, bool isStart)
        {
            if (isStart)
            {
                _realTimeExpManager.Start(expValue);
                TxtRealTimeStartExp.Text = $"{expValue:N0}";
                TxtRealTimeCurrentExp.Text = "-";
                TxtRealTimeGainedExp.Text = "0";
                TxtRealTimeElapsedTime.Text = "00:00:00";
                TxtRealTimeExpPerHour.Text = "0 / hr";
            }
            else
            {
                _realTimeExpManager.Update(expValue);
                TxtRealTimeCurrentExp.Text = $"{expValue:N0}";
                
                var stats = _realTimeExpManager.GetStats();
                TxtRealTimeElapsedTime.Text = stats.Elapsed.ToString(@"hh\:mm\:ss");
                TxtRealTimeGainedExp.Text = $"{stats.GainedExp:N0}";
                TxtRealTimeExpPerHour.Text = $"{stats.ExpPerHour:N0} / hr";
            }
        }

        private long ParseExpValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            try
            {
                var matchBracket = Regex.Match(text, @"([\d,]+)\s*[\[\(]");
                if (matchBracket.Success)
                {
                    string clean = matchBracket.Groups[1].Value.Replace(",", "");
                    if (long.TryParse(clean, out long result)) return result;
                }

                var matchExp = Regex.Match(text, @"EXP\.?\s*([\d,]+)", RegexOptions.IgnoreCase);
                if (matchExp.Success)
                {
                    string clean = matchExp.Groups[1].Value.Replace(",", "");
                    if (long.TryParse(clean, out long result)) return result;
                }

                var matchNum = Regex.Match(text, @"[\d,]+");
                if (matchNum.Success)
                {
                    string clean = matchNum.Value.Replace(",", "");
                    if (long.TryParse(clean, out long result)) return result;
                }
            }
            catch
            {
                // 값 파싱 중 오류가 발생해도 프로그램을 중단하지 않음
            }
            return -1;
        }

        private void SearchButton_Click(object _, RoutedEventArgs __)
        {
            string text = SearchInput.Text;
            if (text != "직접 검색..." && !string.IsNullOrWhiteSpace(text))
            {
                SearchItem(text);
            }
        }

        private void SearchInput_KeyDown(object _, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string text = SearchInput.Text;
                if (text != "직접 검색..." && !string.IsNullOrWhiteSpace(text))
                {
                    SearchItem(text);
                }
            }
        }

        private async void SearchItem(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return;

            try
            {
                string cleanName = Regex.Replace(itemName, @"[^a-zA-Z0-9가-힣\s]", "").Trim();

                if (string.IsNullOrWhiteSpace(cleanName))
                {
                    ItemDescText.Text = "유효한 검색어가 아닙니다.";
                    return;
                }

                ItemDescText.Text = $"검색 중: {cleanName}";
                
                var results = await _apiManager.SearchItemAsync(cleanName);
                
                if (results != null && results.Count > 0)
                {
                    var bestMatch = results
                        .FirstOrDefault(item => item["name"]?.ToString() == cleanName);

                    if (bestMatch == null)
                    {
                        bestMatch = results[0];
                    }

                    string kmsName = bestMatch["name"]?.ToString() ?? cleanName;
                    string kmsDesc = bestMatch["description"]?.ToString() ?? "설명이 없습니다.";

                    int itemId = bestMatch["id"]?.ToObject<int>() ?? 0;
                    
                    if (itemId > 0)
                    {
                        ItemDescText.Text = $"상세 정보 로딩 중... (ID: {itemId})";
                        
                        var detail = await _apiManager.GetItemDetailAsync(itemId);
                        
                        if (detail != null)
                        {
                            UpdateItemUi(detail, kmsName, kmsDesc);
                        }
                        else
                        {
                            UpdateItemUi(bestMatch, kmsName, kmsDesc);
                        }
                    }
                    else
                    {
                        UpdateItemUi(bestMatch, kmsName, kmsDesc);
                    }
                }
                else
                {
                    ItemDescText.Text = $"'{cleanName}' 검색 결과 없음";
                }
            }
            catch (Exception ex)
            {
                ItemDescText.Text = $"오류: {ex.Message}";
            }
        }

        private void UpdateItemUi(JToken item, string? forcedName = null, string? forcedDesc = null)
        {
            string? name = forcedName;
            if (string.IsNullOrEmpty(name))
            {
                name = item["name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    name = item["description"]?["name"]?.ToString();
                }
            }
            ItemNameText.Text = name ?? "이름 없음";

            string reqInfo = ParseReqInfo(item);
            if (!string.IsNullOrEmpty(reqInfo))
            {
                ItemReqText.Text = reqInfo;
                ItemReqText.Visibility = Visibility.Visible;
                ReqSeparator.Visibility = Visibility.Visible;
            }
            else
            {
                ItemReqText.Visibility = Visibility.Collapsed;
                ReqSeparator.Visibility = Visibility.Collapsed;
            }

            string stats = ParseItemStats(item);
            if (!string.IsNullOrEmpty(stats))
            {
                ItemStatsText.Text = stats;
                ItemStatsText.Visibility = Visibility.Visible;
                DescSeparator.Visibility = Visibility.Visible;
            }
            else
            {
                ItemStatsText.Text = "옵션 정보 없음";
                ItemStatsText.Visibility = Visibility.Visible;
                DescSeparator.Visibility = Visibility.Collapsed;
            }

            // 상점가 정보 추가 (metaInfo.price 우선 확인)
            int? price = item["metaInfo"]?["price"]?.ToObject<int>();
            
            // metaInfo에 없으면 shop.price 확인 (구조가 다를 수 있으므로 주의)
            if (!price.HasValue)
            {
                price = item["shop"]?["price"]?.ToObject<int>();
            }

            if (price.HasValue && price > 0)
            {
                ItemPriceText.Text = $"상점가: {price:N0} 메소";
                ItemPriceText.Visibility = Visibility.Visible;
            }
            else
            {
                ItemPriceText.Visibility = Visibility.Collapsed;
            }

            string? desc = forcedDesc;
            if (string.IsNullOrEmpty(desc))
            {
                desc = item["description"]?.ToString();
                if (item["description"] is JObject descObj)
                {
                    desc = descObj["description"]?.ToString();
                }
            }
            
            ItemDescText.Text = desc ?? "설명이 없습니다.";
        }

        private string ParseReqInfo(JToken item)
        {
            var meta = item["metaInfo"];
            if (meta == null) return "";

            StringBuilder sb = new StringBuilder();
            
            AddReq(sb, meta, "reqLevel", "LEV");
            AddReq(sb, meta, "reqSTR", "STR");
            AddReq(sb, meta, "reqDEX", "DEX");
            AddReq(sb, meta, "reqINT", "INT");
            AddReq(sb, meta, "reqLUK", "LUK");
            AddReq(sb, meta, "reqPOP", "POP");

            return sb.ToString().Trim();
        }

        private void AddReq(StringBuilder sb, JToken meta, string key, string label)
        {
            var val = meta[key]?.ToString();
            if (!string.IsNullOrEmpty(val) && val != "0")
            {
                sb.AppendLine($"REQ {label} : {val}");
            }
        }

        private string ParseItemStats(JToken item)
        {
            var meta = item["metaInfo"];
            if (meta == null) return "";

            StringBuilder sb = new StringBuilder();

            AppendStat(sb, meta, "incSTR", "STR");
            AppendStat(sb, meta, "incDEX", "DEX");
            AppendStat(sb, meta, "incINT", "INT");
            AppendStat(sb, meta, "incLUK", "LUK");
            AppendStat(sb, meta, "incMHP", "MaxHP");
            AppendStat(sb, meta, "incMMP", "MaxMP");
            AppendStat(sb, meta, "incPAD", "공격력");
            AppendStat(sb, meta, "incMAD", "마력");
            AppendStat(sb, meta, "incPDD", "물리방어력");
            AppendStat(sb, meta, "incMDD", "마법방어력");
            AppendStat(sb, meta, "incACC", "명중률");
            AppendStat(sb, meta, "incEVA", "회피율");
            AppendStat(sb, meta, "incSpeed", "이동속도");
            AppendStat(sb, meta, "incJump", "점프력");

            return sb.ToString().Trim();
        }

        private void AppendStat(StringBuilder sb, JToken meta, string key, string label)
        {
            var val = meta[key]?.ToString();
            if (!string.IsNullOrEmpty(val) && val != "0")
            {
                sb.AppendLine($"{label} : +{val}");
            }
        }

        // --- 경험치 타이머 관련 로직 ---

        private void BtnToggleTimer_Click(object _, RoutedEventArgs __)
        {
            if (_isExpTimerRunning)
            {
                StopExpTimer();
            }
            else
            {
                if (int.TryParse(InputTimerMinutes.Text, out int minutes) && minutes > 0)
                {
                    StartExpTimer(minutes);
                }
                else
                {
                    TxtTimerCountdown.Text = "시간 오류";
                }
            }
        }

        private void StartExpTimer(int minutes)
        {
            if (_expSnapshotRect.IsEmpty)
            {
                MessageBox.Show("먼저 '경험치 UI 캡처'를 실행해주세요.", "알림");
                return;
            }

            _expTimerRemaining = TimeSpan.FromMinutes(minutes);
            TxtTimerCountdown.Text = _expTimerRemaining.ToString(@"mm\:ss");
            
            _isExpTimerRunning = true;
            _expTimerStartTime = DateTime.Now; // 타이머 시작 시간 기록

            BtnToggleTimer.Content = "타이머 중지";
            BtnToggleTimer.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#AA0000"));
            
            PerformExpUpdate(true); // isStart = true

            _expTimer?.Start();
        }

        private void StopExpTimer()
        {
            _isExpTimerRunning = false;
            _expTimer?.Stop();
            
            BtnToggleTimer.Content = "타이머 시작";
            BtnToggleTimer.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#006600"));
            TxtTimerCountdown.Text = "00:00";
        }

        private void ExpTimer_Tick(object? _, EventArgs __)
        {
            _expTimerRemaining = _expTimerRemaining.Add(TimeSpan.FromSeconds(-1));
            
            if (_expTimerRemaining.TotalSeconds <= 0)
            {
                PerformExpUpdate();
                StopExpTimer();

                if (MinimizedExpPanel.Visibility == Visibility.Visible)
                {
                    TxtMinimizedExpStatus.Text = "측정 완료";
                }
            }
            else
            {
                string remainingTime = _expTimerRemaining.ToString(@"mm\:ss");
                TxtTimerCountdown.Text = remainingTime;

                if (MinimizedExpPanel.Visibility == Visibility.Visible)
                {
                    TxtMinimizedExpStatus.Text = $"타이머: {remainingTime}";
                }
            }
        }

        // --- 최소화/복원 로직 ---

        private void BtnMinimizeExp_Click(object _, RoutedEventArgs __)
        {
            _minimizedExpPanelTransform.X = _expPanelTransform.X;
            _minimizedExpPanelTransform.Y = _expPanelTransform.Y;

            ExpPanel.Visibility = Visibility.Hidden;
            MinimizedExpPanel.Visibility = Visibility.Visible;
            TxtMinimizedExpStatus.Text = _isExpTimerRunning ? $"타이머: {_expTimerRemaining:mm\\:ss}" : "타이머 대기 중";
        }

        private void BtnRestoreExp_Click(object _, RoutedEventArgs __)
        {
            _expPanelTransform.X = _minimizedExpPanelTransform.X;
            _expPanelTransform.Y = _minimizedExpPanelTransform.Y;

            MinimizedExpPanel.Visibility = Visibility.Hidden;
            ExpPanel.Visibility = Visibility.Visible;
        }

        private void BtnMinimizeRealTime_Click(object _, RoutedEventArgs __)
        {
            _minimizedRealTimePanelTransform.X = _realTimeExpPanelTransform.X;
            _minimizedRealTimePanelTransform.Y = _realTimeExpPanelTransform.Y;

            RealTimeExpPanel.Visibility = Visibility.Hidden;
            MinimizedRealTimePanel.Visibility = Visibility.Visible;
            TxtMinimizedRealTimeStatus.Text = "실시간 추적 중";
        }

        private void BtnRestoreRealTime_Click(object _, RoutedEventArgs __)
        {
            _realTimeExpPanelTransform.X = _minimizedRealTimePanelTransform.X;
            _realTimeExpPanelTransform.Y = _minimizedRealTimePanelTransform.Y;

            MinimizedRealTimePanel.Visibility = Visibility.Hidden;
            RealTimeExpPanel.Visibility = Visibility.Visible;
        }

        // --- 플로팅 컨트롤 패널 로직 ---

        private void BtnOpenMenu_Click(object _, RoutedEventArgs __)
        {
            ToggleModeSelection();
        }

        private void BtnMinimizeControl_Click(object _, RoutedEventArgs __)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnRestoreControl_Click(object _, RoutedEventArgs __)
        {
            _floatingControlPanelTransform.X = _minimizedControlPanelTransform.X;
            _floatingControlPanelTransform.Y = _minimizedControlPanelTransform.Y;

            MinimizedControlPanel.Visibility = Visibility.Hidden;
            FloatingControlPanel.Visibility = Visibility.Visible;
        }

        public class ExpManager
        {
            public long StartExp { get; private set; }
            public DateTime StartTime { get; private set; }

            public long CurrentExp { get; private set; }
            public DateTime LastUpdateTime { get; private set; }

            public void Start(long exp)
            {
                StartExp = exp;
                StartTime = DateTime.Now;
                
                CurrentExp = exp;
                LastUpdateTime = DateTime.Now;
            }

            public void StartWithTime(long exp, DateTime startTime)
            {
                StartExp = exp;
                StartTime = startTime;
                
                CurrentExp = exp;
                LastUpdateTime = DateTime.Now;
            }

            public void Update(long exp)
            {
                CurrentExp = exp;
                LastUpdateTime = DateTime.Now;
            }

            public (TimeSpan Elapsed, long GainedExp, long ExpPerHour) GetStats()
            {
                var elapsed = LastUpdateTime - StartTime;
                long gainedExp = CurrentExp - StartExp;

                if (gainedExp < 0) gainedExp = 0; 

                long expPerHour = 0;
                if (elapsed.TotalHours > 0)
                {
                    expPerHour = (long)(gainedExp / elapsed.TotalHours);
                }

                return (elapsed, gainedExp, expPerHour);
            }
        }
    }
}