using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MapleOverlay.Manager;
using Newtonsoft.Json.Linq;

namespace MapleOverlay
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        private DispatcherTimer _trackingTimer;
        private IntPtr _mapleHandle;
        private MapleApiManager _apiManager = new MapleApiManager();
        private OcrManager _ocrManager;
        private ConfigManager _configManager;
        
        private bool _isSearchKeyDown = false;
        private bool _isExitKeyDown = false;

        private bool _isDragging = false;
        private System.Windows.Point _startPoint;
        private bool _isInfoPanelVisible = false;

        private Bitmap _frozenScreenBitmap;

        private TranslateTransform _infoPanelTransform = new TranslateTransform();
        private TranslateTransform _expPanelTransform = new TranslateTransform();
        private System.Windows.Point _panelDragStart;
        private bool _isPanelDragging = false;
        private FrameworkElement _draggedPanel = null;

        private enum CaptureMode { Item, ExpStart, ExpEnd }
        private CaptureMode _currentCaptureMode = CaptureMode.Item;

        private ExpManager _expManager = new ExpManager();

        public MainWindow()
        {
            InitializeComponent();
            _ocrManager = new OcrManager();
            _configManager = new ConfigManager();

            InfoPanel.RenderTransform = _infoPanelTransform;
            ExpPanel.RenderTransform = _expPanelTransform;

            UpdateKeyGuide();

            // --- 이벤트 핸들러 ---
            SearchInput.GotFocus += (s, e) => { if (SearchInput.Text == "직접 검색...") SearchInput.Text = ""; };
            SearchInput.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(SearchInput.Text)) SearchInput.Text = "직접 검색..."; };
            
            InputExpValue.GotFocus += (s, e) => { if (InputExpValue.Text == "경험치량") InputExpValue.Text = ""; };
            InputExpValue.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(InputExpValue.Text)) InputExpValue.Text = "경험치량"; };
            
            InputExpPercent.GotFocus += (s, e) => { if (InputExpPercent.Text == "%") InputExpPercent.Text = ""; };
            InputExpPercent.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(InputExpPercent.Text)) InputExpPercent.Text = "%"; };
        }

        private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement panel)
            {
                _isPanelDragging = true;
                _draggedPanel = panel;
                _panelDragStart = e.GetPosition(this);
                panel.CaptureMouse();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isPanelDragging && _draggedPanel != null)
            {
                var currentPos = e.GetPosition(this);
                var diff = currentPos - _panelDragStart;
                
                TranslateTransform transform = _draggedPanel == InfoPanel ? _infoPanelTransform : _expPanelTransform;
                
                transform.X += diff.X;
                transform.Y += diff.Y;
                
                _panelDragStart = currentPos;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_isPanelDragging && _draggedPanel != null)
            {
                _isPanelDragging = false;
                _draggedPanel.ReleaseMouseCapture();
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
            KeyGuideText.Text = $"메뉴: {GetKeyName(cfg.KeyCapture)} | 닫기: {GetKeyName(cfg.KeyClosePanel)} | 종료: {GetKeyName(cfg.KeyExit)}";
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

        private void TrackingTimer_Tick(object sender, EventArgs e)
        {
            var cfg = _configManager.Config;

            if ((GetAsyncKeyState(cfg.KeyExit) & 0x8000) != 0)
            {
                if (!_isExitKeyDown)
                {
                    _isExitKeyDown = true;
                    Application.Current.Shutdown();
                    return;
                }
            }
            else
            {
                _isExitKeyDown = false;
            }

            if ((GetAsyncKeyState(cfg.KeyClosePanel) & 0x8000) != 0)
            {
                CloseAllPanels();
                return;
            }

            if (CaptureCanvas.Visibility == Visibility.Visible) return;

            _mapleHandle = FindWindow("MapleStoryClass", "MapleStory");
            
            if (_mapleHandle != IntPtr.Zero)
            {
                if (GetWindowRect(_mapleHandle, out RECT rect))
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

            bool isAnyPanelVisible = _isInfoPanelVisible || ExpPanel.Visibility == Visibility.Visible || ModeSelectionPanel.Visibility == Visibility.Visible;
            
            if (isAnyPanelVisible)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int extendedStyle = GetWindowLong(hwnd, -20);
                if ((extendedStyle & 0x20) != 0)
                {
                    SetClickThrough(false);
                }
            }
            else
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int extendedStyle = GetWindowLong(hwnd, -20);
                if ((extendedStyle & 0x20) == 0)
                {
                    SetClickThrough(true);
                }
            }

            bool isCaptureKeyDown = (GetAsyncKeyState(cfg.KeyCapture) & 0x8000) != 0;
            if (isCaptureKeyDown && !_isSearchKeyDown)
            {
                _isSearchKeyDown = true;
                ToggleModeSelection();
            }
            else if (!isCaptureKeyDown)
            {
                _isSearchKeyDown = false;
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
                CloseAllPanels();
                ModeSelectionPanel.Visibility = Visibility.Visible;
            }
        }

        private void CloseAllPanels()
        {
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;
            ExpPanel.Visibility = Visibility.Hidden;
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            
            if (CaptureCanvas.Visibility == Visibility.Visible)
            {
                EndCaptureMode();
            }
            
            SetClickThrough(true);
        }

        private void ModeCapture_Click(object sender, RoutedEventArgs e)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            _currentCaptureMode = CaptureMode.Item;
            StartCaptureMode();
        }

        private void ModeExp_Click(object sender, RoutedEventArgs e)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            ExpPanel.Visibility = Visibility.Visible;
        }

        private void ModeManual_Click(object sender, RoutedEventArgs e)
        {
            ModeSelectionPanel.Visibility = Visibility.Collapsed;
            OpenManualSearch();
        }

        private void BtnStartExp_Click(object sender, RoutedEventArgs e)
        {
            ExpPanel.Visibility = Visibility.Hidden;
            _currentCaptureMode = CaptureMode.ExpStart;
            StartCaptureMode();
        }

        private void BtnUpdateExp_Click(object sender, RoutedEventArgs e)
        {
            ExpPanel.Visibility = Visibility.Hidden;
            _currentCaptureMode = CaptureMode.ExpEnd;
            StartCaptureMode();
        }

        private void CloseExpButton_Click(object sender, RoutedEventArgs e)
        {
            ExpPanel.Visibility = Visibility.Hidden;
        }

        private void CloseInfoPanel()
        {
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;
            SetClickThrough(true);
        }

        private void OpenManualSearch()
        {
            InfoPanel.Visibility = Visibility.Visible;
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseInfoPanel();
        }

        private async void StartCaptureMode()
        {
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;
            ExpPanel.Visibility = Visibility.Hidden;
            ModeSelectionPanel.Visibility = Visibility.Collapsed;

            await Task.Delay(100);

            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            
            if (_frozenScreenBitmap != null)
            {
                _frozenScreenBitmap.Dispose();
                _frozenScreenBitmap = null;
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

        private void CaptureCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(CaptureCanvas);
            
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
        }

        private void CaptureCanvas_MouseMove(object sender, MouseEventArgs e)
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

        private void CaptureCanvas_MouseUp(object sender, MouseButtonEventArgs e)
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
                GetCursorPos(out POINT pt);
                _mapleHandle = FindWindow("MapleStoryClass", "MapleStory");
                
                PerformOcrFromFrozenImage((int)x, (int)y, (int)w, (int)h);
            }
            else
            {
                if (_currentCaptureMode == CaptureMode.ExpStart || _currentCaptureMode == CaptureMode.ExpEnd)
                {
                    ExpPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private void PerformOcrFromFrozenImage(int x, int y, int width, int height)
        {
            if (_frozenScreenBitmap == null) return;

            try
            {
                System.Drawing.Rectangle cropRect = new System.Drawing.Rectangle(x, y, width, height);
                
                if (cropRect.Right > _frozenScreenBitmap.Width) cropRect.Width = _frozenScreenBitmap.Width - x;
                if (cropRect.Bottom > _frozenScreenBitmap.Height) cropRect.Height = _frozenScreenBitmap.Height - y;

                using (Bitmap cropped = _frozenScreenBitmap.Clone(cropRect, _frozenScreenBitmap.PixelFormat))
                {
                    string text = _ocrManager.RecognizeText(cropped);
                    
                    if (_currentCaptureMode == CaptureMode.Item)
                    {
                        ProcessItemOcr(text);
                    }
                    else if (_currentCaptureMode == CaptureMode.ExpStart)
                    {
                        ProcessExpOcr(text, true);
                    }
                    else if (_currentCaptureMode == CaptureMode.ExpEnd)
                    {
                        ProcessExpOcr(text, false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_currentCaptureMode == CaptureMode.Item)
                {
                    ItemDescText.Text = $"캡처 오류: {ex.Message}";
                    InfoPanel.Visibility = Visibility.Visible;
                    _isInfoPanelVisible = true;
                }
                else
                {
                    ExpPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private void ProcessItemOcr(string text)
        {
            InfoPanel.Visibility = Visibility.Visible;
            _isInfoPanelVisible = true;
            
            ItemNameText.Text = $"인식됨: [{text}]";
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
                ItemNameText.Text = "인식 실패";
                ItemDescText.Text = "글자를 읽지 못했습니다. 아래 검색창을 이용하세요.";
            }
        }

        private void ProcessExpOcr(string text, bool isStart)
        {
            ExpPanel.Visibility = Visibility.Visible;
            TxtDebugOcr.Text = string.IsNullOrWhiteSpace(text) ? "(인식 실패)" : text;

            if (string.IsNullOrWhiteSpace(text)) return;

            long expValue = ParseExpValue(text);
            double expPercent = ParseExpPercent(text);

            if (expValue < 0) return;

            UpdateExpPanel(expValue, expPercent, isStart);
        }

        // [추가] 수동 시작 버튼 핸들러
        private void BtnManualStart_Click(object sender, RoutedEventArgs e)
        {
            long expValue = ParseExpValue(InputExpValue.Text);
            double expPercent = ParseExpPercent(InputExpPercent.Text);

            if (expValue < 0) return;

            UpdateExpPanel(expValue, expPercent, true);
        }

        // [추가] 수동 갱신 버튼 핸들러
        private void BtnManualUpdate_Click(object sender, RoutedEventArgs e)
        {
            long expValue = ParseExpValue(InputExpValue.Text);
            double expPercent = ParseExpPercent(InputExpPercent.Text);

            if (expValue < 0) return;

            UpdateExpPanel(expValue, expPercent, false);
        }

        private void UpdateExpPanel(long expValue, double expPercent, bool isStart)
        {
            if (isStart)
            {
                _expManager.Start(expValue, expPercent);
                TxtStartExp.Text = $"{expValue:N0} ({expPercent:F2}%)";
                TxtCurrentExp.Text = "-";
                TxtStartTime.Text =_expManager.StartTime.ToString(@"hh\:mm\:ss");
                TxtElapsedTime.Text = "00:00:00";
                TxtGainedExp.Text = "0 (0.00%)";
                TxtExpPerHour.Text = "0 / hr";
            }
            else
            {
                _expManager.Update(expValue, expPercent);
                TxtCurrentExp.Text = $"{expValue:N0} ({expPercent:F2}%)";
                
                var stats = _expManager.GetStats();
                TxtElapsedTime.Text = stats.Elapsed.ToString(@"hh\:mm\:ss");
                TxtGainedExp.Text = $"{stats.GainedExp:N0} ({stats.GainedPercent:F2}%)";
                TxtExpPerHour.Text = $"{stats.ExpPerHour:N0} / hr";
            }
        }

        private long ParseExpValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            try
            {
                // [수정] 정규식으로 "숫자 + 괄호" 패턴 찾기
                // 예: 12345[12.34%] 또는 12345(12.34%)
                // 앞뒤에 붙은 쓰레기 값(1 등)을 무시하기 위해 패턴 매칭 사용
                var match = Regex.Match(text, @"(\d+)[\(\[]");
                if (match.Success)
                {
                    string numStr = match.Groups[1].Value;
                    if (long.TryParse(numStr, out long result))
                    {
                        return result;
                    }
                }
                
                // 패턴 매칭 실패 시 기존 방식 시도 (숫자만 추출)
                // 하지만 이 경우 앞에 붙은 '1'도 포함될 위험이 있음.
                // 일단 패턴 매칭 실패하면 실패로 처리하는 게 안전할 수 있음.
                // 여기서는 기존 로직 유지하되, 패턴 매칭을 우선함.
                
                string numPart = text.Split('(', '[')[0];
                string cleanNum = Regex.Replace(numPart, @"[^0-9]", "");
                if (long.TryParse(cleanNum, out long expValue))
                {
                    return expValue;
                }
            }
            catch { }
            return -1;
        }

        private double ParseExpPercent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0.0;
            try
            {
                // [수정] 정규식으로 괄호 안의 숫자+점+숫자 패턴 찾기
                var match = Regex.Match(text, @"[\(\[]\s*([\d\.]+)\s*%?[\)\]]");
                if (match.Success)
                {
                    string percentStr = match.Groups[1].Value;
                    if (double.TryParse(percentStr, out double result))
                    {
                        return result;
                    }
                }
            }
            catch { }
            return 0.0;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string text = SearchInput.Text;
            if (text != "직접 검색..." && !string.IsNullOrWhiteSpace(text))
            {
                SearchItem(text);
            }
        }

        private void SearchInput_KeyDown(object sender, KeyEventArgs e)
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

                ItemNameText.Text = $"검색 중: {cleanName}";
                
                var results = await _apiManager.SearchItemAsync(cleanName);
                
                if (results != null && results.Count > 0)
                {
                    var bestMatch = results.Cast<JToken>()
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
                            UpdateItemUI(detail, kmsName, kmsDesc);
                        }
                        else
                        {
                            UpdateItemUI(bestMatch, kmsName, kmsDesc);
                        }
                    }
                    else
                    {
                        UpdateItemUI(bestMatch, kmsName, kmsDesc);
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

        private void UpdateItemUI(JToken item, string forcedName = null, string forcedDesc = null)
        {
            string name = forcedName;
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

            string desc = forcedDesc;
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

        public class ExpManager
        {
            public long StartExp { get; private set; }
            public double StartPercent { get; private set; }
            public DateTime StartTime { get; private set; }

            public long CurrentExp { get; private set; }
            public double CurrentPercent { get; private set; }
            public DateTime LastUpdateTime { get; private set; }

            public void Start(long exp, double percent)
            {
                StartExp = exp;
                StartPercent = percent;
                StartTime = DateTime.Now;
                
                CurrentExp = exp;
                CurrentPercent = percent;
                LastUpdateTime = DateTime.Now;
            }

            public void Update(long exp, double percent)
            {
                CurrentExp = exp;
                CurrentPercent = percent;
                LastUpdateTime = DateTime.Now;
            }

            public (TimeSpan Elapsed, long GainedExp, double GainedPercent, long ExpPerHour) GetStats()
            {
                var elapsed = LastUpdateTime - StartTime;
                long gainedExp = CurrentExp - StartExp;
                double gainedPercent = CurrentPercent - StartPercent;

                if (gainedExp < 0) gainedExp = 0; 
                if (gainedPercent < 0) gainedPercent = 0;

                long expPerHour = 0;
                if (elapsed.TotalHours > 0)
                {
                    expPerHour = (long)(gainedExp / elapsed.TotalHours);
                }

                return (elapsed, gainedExp, gainedPercent, expPerHour);
            }
        }
    }
}