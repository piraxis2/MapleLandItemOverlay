using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Tesseract;

namespace MapleOverlay.Manager
{
    public class OcrManager
    {
        private TesseractEngine _engine;
        
        private Dictionary<string, string> _typoDictionary = new Dictionary<string, string>
        {
            { "큼", "쿰" },
            { "자큼", "자쿰" },
            { "핑크빈", "핑크빈" },
            { "혼테일", "혼테일" }
        };

        public OcrManager(string language = "kor")
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string dataPath = Path.Combine(basePath, "tessdata");

                Log($"[Init] Base Path: {basePath}");
                Log($"[Init] Data Path: {dataPath}");

                if (!Directory.Exists(dataPath))
                {
                    Log("[Error] tessdata folder NOT found!");
                    string upPath = Path.Combine(basePath, "..", "tessdata");
                    if (Directory.Exists(upPath))
                    {
                        dataPath = upPath;
                        Log($"[Init] Found in parent: {dataPath}");
                    }
                }
                else
                {
                    Log("[Init] tessdata folder found.");
                }

                _engine = new TesseractEngine(dataPath, language, EngineMode.LstmOnly);
                Log("[Init] Engine initialized successfully.");
            }
            catch (Exception ex)
            {
                Log($"[Error] Engine Init Failed: {ex.Message}");
                Log($"[Error] StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Log($"[Error] InnerException: {ex.InnerException.Message}");
                }
            }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr_log.txt");
                File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
            }
            catch { }
        }

        // [추가] 숫자 인식 전용 메서드
        public string RecognizeNumbers(Bitmap bitmap)
        {
            if (_engine == null) return null;

            // 기존 whitelist 값 저장
            _engine.TryGetStringVariable("tessedit_char_whitelist", out string originalWhitelist);
            
            try
            {
                // 숫자 및 관련 기호만 인식하도록 설정
                _engine.SetVariable("tessedit_char_whitelist", "0123456789().[]%");
                Log("[Recognize] Set whitelist for numbers.");

                // 일반 인식 로직과 동일하게 수행
                return RecognizeInternal(bitmap, "Numbers");
            }
            finally
            {
                // whitelist 설정 원상복구
                _engine.SetVariable("tessedit_char_whitelist", originalWhitelist);
                Log("[Recognize] Reset whitelist.");
            }
        }

        public string RecognizeText(Bitmap bitmap)
        {
            return RecognizeInternal(bitmap, "Text");
        }

        private string RecognizeInternal(Bitmap bitmap, string mode)
        {
            if (_engine == null)
            {
                Log($"[{mode}] Engine is null. Cannot recognize.");
                return null;
            }

            try
            {
                using (var processedBitmap = PreprocessImage(bitmap))
                {
                    try {
                        string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"debug_processed_{mode}.png");
                        processedBitmap.Save(debugPath, System.Drawing.Imaging.ImageFormat.Png);
                    } catch { }

                    using (var pix = Pix.LoadFromMemory(BitmapToBytes(processedBitmap)))
                    {
                        using (var page = _engine.Process(pix, PageSegMode.SingleLine))
                        {
                            var text = page.GetText()?.Trim();
                            var confidence = page.GetMeanConfidence();
                            Log($"[{mode}] Recognized: '{text}' (Conf: {confidence})");
                            
                            // 숫자 모드가 아닐 때만 오타 보정
                            return mode == "Text" ? CorrectTypo(text) : text;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[{mode}] Recognition Failed: {ex.Message}");
                return null;
            }
        }

        private byte[] BitmapToBytes(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                return stream.ToArray();
            }
        }

        private string CorrectTypo(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            if (_typoDictionary.ContainsKey(text))
            {
                return _typoDictionary[text];
            }

            foreach (var entry in _typoDictionary)
            {
                if (text.Contains(entry.Key))
                {
                    text = text.Replace(entry.Key, entry.Value);
                }
            }

            return text;
        }

        public Bitmap PreprocessImage(Bitmap original)
        {
            int scale = 4;
            int newWidth = original.Width * scale;
            int newHeight = original.Height * scale;

            int padding = 20;
            Bitmap padded = new Bitmap(newWidth + (padding * 2), newHeight + (padding * 2));

            using (Graphics g = Graphics.FromImage(padded))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(original, padding, padding, newWidth, newHeight);
            }

            Bitmap sharpened = ApplySharpen(padded);
            padded.Dispose();

            BitmapData data = sharpened.LockBits(new Rectangle(0, 0, sharpened.Width, sharpened.Height), 
                                              ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(data.Stride) * sharpened.Height;
            byte[] rgbValues = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, rgbValues, 0, bytes);

            int blackPoint = 20; 
            int whitePoint = 160;

            for (int i = 0; i < rgbValues.Length; i += 4)
            {
                byte b = rgbValues[i];
                byte g = rgbValues[i + 1];
                byte r = rgbValues[i + 2];

                byte maxVal = Math.Max(r, Math.Max(g, b));
                int inverted = 255 - maxVal;

                int finalVal;
                if (inverted <= blackPoint)
                {
                    finalVal = 0;
                }
                else if (inverted >= whitePoint)
                {
                    finalVal = 255;
                }
                else
                {
                    finalVal = (inverted - blackPoint) * 255 / (whitePoint - blackPoint);
                }

                byte result = (byte)finalVal;

                rgbValues[i] = result;
                rgbValues[i + 1] = result;
                rgbValues[i + 2] = result;
                rgbValues[i + 3] = 255;
            }

            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, data.Scan0, bytes);
            sharpened.UnlockBits(data);

            return sharpened;
        }

        private Bitmap ApplySharpen(Bitmap image)
        {
            Bitmap sharpenImage = new Bitmap(image.Width, image.Height);

            int filterWidth = 3;
            int filterHeight = 3;
            int w = image.Width;
            int h = image.Height;

            double[,] filter = new double[,] {
                { 0, -1, 0 },
                { -1, 5, -1 },
                { 0, -1, 0 }
            };

            double factor = 1.0;
            double bias = 0.0;

            BitmapData srcData = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dstData = sharpenImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = srcData.Stride * h;
            byte[] resultBuffer = new byte[bytes];
            byte[] srcBuffer = new byte[bytes];

            System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcBuffer, 0, bytes);
            image.UnlockBits(srcData);

            int stride = srcData.Stride;

            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    if (x > 0 && x < w - 1 && y > 0 && y < h - 1)
                    {
                        double red = 0.0, green = 0.0, blue = 0.0;

                        for (int filterY = 0; filterY < filterHeight; filterY++)
                        {
                            for (int filterX = 0; filterX < filterWidth; filterX++)
                            {
                                int imageX = (x - filterWidth / 2 + filterX);
                                int imageY = (y - filterHeight / 2 + filterY);

                                int offset = imageY * stride + imageX * 4;
                                
                                red += srcBuffer[offset + 2] * filter[filterX, filterY];
                                green += srcBuffer[offset + 1] * filter[filterX, filterY];
                                blue += srcBuffer[offset] * filter[filterX, filterY];
                            }
                        }

                        int r = Math.Min(Math.Max((int)(factor * red + bias), 0), 255);
                        int g = Math.Min(Math.Max((int)(factor * green + bias), 0), 255);
                        int b = Math.Min(Math.Max((int)(factor * blue + bias), 0), 255);

                        int resOffset = y * stride + x * 4;
                        resultBuffer[resOffset + 2] = (byte)r;
                        resultBuffer[resOffset + 1] = (byte)g;
                        resultBuffer[resOffset] = (byte)b;
                        resultBuffer[resOffset + 3] = 255;
                    }
                    else
                    {
                        int offset = y * stride + x * 4;
                        resultBuffer[offset + 2] = srcBuffer[offset + 2];
                        resultBuffer[offset + 1] = srcBuffer[offset + 1];
                        resultBuffer[offset] = srcBuffer[offset];
                        resultBuffer[offset + 3] = 255;
                    }
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(resultBuffer, 0, dstData.Scan0, bytes);
            sharpenImage.UnlockBits(dstData);

            return sharpenImage;
        }
    }
}