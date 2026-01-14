using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace MapleOverlay
{
    public partial class App : Application
    {
        public App()
        {
            // DLL 로딩 경로를 'lib' 폴더로 지정
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            // 찾으려는 어셈블리 이름 (예: "Newtonsoft.Json, Version=...")
            var assemblyName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(assemblyName)) return null;

            // 1. 실행 파일 위치 기준 'lib' 폴더 경로
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string libPath = Path.Combine(basePath, "lib");
            string dllPath = Path.Combine(libPath, assemblyName + ".dll");

            // 2. 파일이 존재하면 로드
            if (File.Exists(dllPath))
            {
                try
                {
                    return Assembly.LoadFrom(dllPath);
                }
                catch (Exception ex)
                {
                    // 로딩 실패 시 로그 남기기 (필요 시)
                    System.Diagnostics.Debug.WriteLine($"Failed to load assembly from {dllPath}: {ex.Message}");
                }
            }

            return null;
        }
    }
}