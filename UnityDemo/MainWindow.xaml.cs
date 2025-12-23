using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using System.Timers;

namespace UnityDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool redraw);
        internal delegate int WindowEnumProc(IntPtr hwnd, IntPtr lparam);
        //改变指定窗口的位置和尺寸，基于左上角（屏幕/父窗口）（指定窗口的句柄，窗口左位置，窗口顶位置，窗口新宽度，窗口新高度，指定是否重画窗口）

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        internal static extern bool EnumChildWindows(IntPtr hwnd, WindowEnumProc func, IntPtr lParam);
        //枚举一个父窗口的所有子窗口（父窗口句柄，回调函数的地址，自定义的参数）

        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        //该函数将指定的消息发送到一个或多个窗口。此函数为指定的窗口调用窗口程序，直到窗口程序处理完消息再返回。（窗口句柄。窗口可以是任何类型的屏幕对象,用于区别其他消息的常量值,通常是一个与消息有关的常量值，也可能是窗口或控件的句柄,通常是一个指向内存中数据的指针）

        private Process process = new();
        private IntPtr unityHWND = IntPtr.Zero;
        private const int WM_ACTIVATE = 0x0006;
        private readonly IntPtr WA_ACTIVE = new IntPtr(1);
        private readonly IntPtr WA_INACTIVE = new IntPtr(0);

        private bool isU3DLoaded = false;
        private Point u3dLeftUpPos;

        private DispatcherTimer dispatcherTimer = new();

        // 创建服务器实例
        TcpServer server = new TcpServer();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            Closed += MainWindow_Closed;
        }

        /// <summary>
        /// 窗体加载事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUnity();

            // 订阅事件
            server.ServerStarted += (s, e) => Console.WriteLine("服务器已启动");
            server.ServerStopped += (s, e) => Console.WriteLine("服务器已停止");
            server.ClientConnected += (s, client) => Console.WriteLine("客户端已连接");
            server.ClientDisconnected += (s, client) => Console.WriteLine("客户端已断开");
            server.DataReceived += (s, data) =>
            {
                Console.WriteLine($"收到来自客户端的消息: {data.Message}");
                // 可以回复消息
                _ = server.SendToClientAsync(data.Client, "服务器已收到消息");
            };

            // 启动服务器
            server.Start("127.0.0.1", 8080);
        }

        /// <summary>
        /// 窗体大小改变事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ResizeU3D();
        }

        /// <summary>
        /// 窗体关闭触发事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                if (process == null)
                    return;

                process.CloseMainWindow();

                Thread.Sleep(1000);
                while (process.HasExited == false)
                {
                    process.Kill();
                    Thread.Sleep(100);
                }

                System.Environment.Exit(0);
            }
            catch (Exception)
            {
            }
        }

        #region Unity操作
        private void LoadUnity()
        {
            try
            {
                var panel = this.FindName("Panel1") as Border;

                IntPtr hwnd = ((HwndSource)PresentationSource.FromVisual(panel)).Handle;

                string? appStartupPath = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
                if (string.IsNullOrEmpty(appStartupPath))
                {
                    MessageBox.Show("读取程序路径失败");
                    return;
                }

                process.StartInfo.FileName = @$"{appStartupPath}\Game\My project.exe";
                process.StartInfo.Arguments = "-parentHWND " + hwnd.ToInt32() + " " + Environment.CommandLine;
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                process.WaitForInputIdle();
                isU3DLoaded = true;
                EnumChildWindows(hwnd, WindowEnum, IntPtr.Zero);

                dispatcherTimer.Tick += InitialResize;
                dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 200);
                dispatcherTimer.Start();
            }
            catch (Exception ex)
            {
                string error = ex.Message;
            }
        }
        private void InitialResize(object? sender, EventArgs e)
        {
            ResizeU3D();
            dispatcherTimer.Stop();
        }
        private int WindowEnum(IntPtr hwnd, IntPtr lparam)
        {
            unityHWND = hwnd;
            ActivateUnityWindow();
            return 0;
        }
        private void ActivateUnityWindow()
        {
            SendMessage(unityHWND, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
        }

        private void ResizeU3D()
        {
            if (isU3DLoaded)
            {
                var panel = this.FindName("Panel1") as Border;

                Window window = Window.GetWindow(this);
                u3dLeftUpPos = panel!.TransformToAncestor(window).Transform(new Point(0, 0));
                DPIUtils.Init(this);
                u3dLeftUpPos.X *= DPIUtils.DPIX;
                u3dLeftUpPos.Y *= DPIUtils.DPIY;
                MoveWindow(unityHWND, (int)u3dLeftUpPos.X, (int)u3dLeftUpPos.Y, (int)(panel.ActualWidth * DPIUtils.DPIX), (int)(panel.ActualHeight * DPIUtils.DPIY), true);
                ActivateUnityWindow();
            }
        }
        #endregion

        #region 窗体位置坐标变换
        public class DPIUtils
        {
            private static double _dpiX = 1.0;
            private static double _dpiY = 1.0;
            public static double DPIX
            {
                get
                {
                    return DPIUtils._dpiX;
                }
            }
            public static double DPIY
            {
                get
                {
                    return DPIUtils._dpiY;
                }
            }
            public static void Init(System.Windows.Media.Visual visual)
            {
                Matrix transformToDevice = System.Windows.PresentationSource.FromVisual(visual).CompositionTarget.TransformToDevice;
                DPIUtils._dpiX = transformToDevice.M11;
                DPIUtils._dpiY = transformToDevice.M22;
            }
            public static Point DivideByDPI(Point p)
            {
                return new Point(p.X / DPIUtils.DPIX, p.Y / DPIUtils.DPIY);
            }
            public static Rect DivideByDPI(Rect r)
            {
                return new Rect(r.Left / DPIUtils.DPIX, r.Top / DPIUtils.DPIY, r.Width, r.Height);
            }
        }
        #endregion

        // 重点：需要wpf程序触发事件
        private void Panel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ActivateUnityWindow();
        }


        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            _ = server.BroadcastAsync("5;");
        }
    }
}

