using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
        private bool _isManualSearchKeyDown = false;
        private bool _isExitKeyDown = false;

        private bool _isDragging = false;
        private System.Windows.Point _startPoint;
        private bool _isInfoPanelVisible = false;

        private Bitmap _frozenScreenBitmap;

        public MainWindow()
        {
            InitializeComponent();
            _ocrManager = new OcrManager();
            _configManager = new ConfigManager();

            UpdateKeyGuide();

            SearchInput.GotFocus += (s, e) => {
                if (SearchInput.Text == "직접 검색...") SearchInput.Text = "";
            };
            SearchInput.LostFocus += (s, e) => {
                if (string.IsNullOrWhiteSpace(SearchInput.Text)) SearchInput.Text = "직접 검색...";
            };
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
            // [수정] 닫기(ESC) 추가
            KeyGuideText.Text = $"캡처: {GetKeyName(cfg.KeyCapture)} | 검색: {GetKeyName(cfg.KeyManualSearch)} | 닫기: {GetKeyName(cfg.KeyClosePanel)} | 프로그램 종료: {GetKeyName(cfg.KeyExit)}";
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
                CloseInfoPanel();
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

            if (_isInfoPanelVisible)
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
                StartCaptureMode();
            }
            else if (!isCaptureKeyDown)
            {
                _isSearchKeyDown = false;
            }

            bool isManualSearchKeyDown = (GetAsyncKeyState(cfg.KeyManualSearch) & 0x8000) != 0;
            if (isManualSearchKeyDown && !_isManualSearchKeyDown)
            {
                _isManualSearchKeyDown = true;
                OpenManualSearch();
            }
            else if (!isManualSearchKeyDown)
            {
                _isManualSearchKeyDown = false;
            }
        }

        private void CloseInfoPanel()
        {
            InfoPanel.Visibility = Visibility.Hidden;
            _isInfoPanelVisible = false;
            
            if (CaptureCanvas.Visibility == Visibility.Visible)
            {
                EndCaptureMode();
            }
            
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
                _isInfoPanelVisible = true;
                
                GetCursorPos(out POINT pt);
                _mapleHandle = FindWindow("MapleStoryClass", "MapleStory");
                double offsetX = 0, offsetY = 0;
                
                if (_mapleHandle != IntPtr.Zero && GetWindowRect(_mapleHandle, out RECT rect))
                {
                    offsetX = rect.Left;
                    offsetY = rect.Top;
                }

                PerformOcrFromFrozenImage((int)x, (int)y, (int)w, (int)h);
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
                    try {
                        string debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_drag.png");
                        cropped.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);
                    } catch { }

                    string text = _ocrManager.RecognizeText(cropped);
                    
                    InfoPanel.Visibility = Visibility.Visible;
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
            }
            catch (Exception ex)
            {
                ItemDescText.Text = $"캡처 오류: {ex.Message}";
                InfoPanel.Visibility = Visibility.Visible;
            }
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
                string cleanName = System.Text.RegularExpressions.Regex.Replace(itemName, @"[^a-zA-Z0-9가-힣\s]", "").Trim();

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
    }
}