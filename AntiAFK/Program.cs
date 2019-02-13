using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

using AntiAFK.Utils;

namespace AntiAFK
{
    namespace Win32
    {
        using HWND = IntPtr;
        using BOOL = Int32;
        using UINT = UInt32;
        using LRESULT = Int64;
        using LPARAM = Int64;
        using WPARAM = UInt64;

        public class Imports
        {
            [DllImport("user32.dll", CharSet=CharSet.Auto)]
            public static extern int MessageBox(HWND hWnd, String text, 
                            String caption, uint type);

            // NOTE: ALT+TAB if fUnknown = true, else considered a normal swap
            [DllImport("user32.dll")]
            public static extern void SwitchToThisWindow(HWND hWnd, BOOL fUnknown);

            [DllImport("user32.dll")]
            public static extern HWND GetWindow(HWND hWnd, UINT uCmd);

            [DllImport("user32.dll",CharSet=CharSet.Auto)]
            public static extern int GetWindowText(HWND hWnd, StringBuilder titleTxt, int maxCount);

            // NOTE: the console window does not appear to be on the main thread of the application,
            // so this will not work by default, unless I attach the console thread to the main process (or vice versa)
            [DllImport("user32.dll")]
            public static extern HWND GetActiveWindow();

            [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
            public static extern HWND FindWindowByCaption(HWND zeroOnly, string lpWindowName);


            [DllImport("user32.dll")]
            public static extern UINT MapVirtualKeyA(UINT uCode, UINT uMapType);

            [DllImport("user32.dll")]
            public static extern HWND GetTopWindow(HWND parent);

            [DllImport("user32.dll")]
            public static extern LRESULT SendMessage(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
            [DllImport("user32.dll")]
            public static extern LRESULT SendNotifyMessageA(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
            [DllImport("user32.dll")]
            public static extern BOOL PostMessageA(HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam);
            [DllImport("user32.dll")]
            public static extern HWND SetFocus(HWND hWnd);

        }
    }

    namespace Utils
    {
        struct RandomRange
        {
            public RandomRange(int min, int max)
            {
                this.min = Math.Abs(min <= max ? min : max);
                this.max = Math.Abs(max >= min ? max : min);

                rand = new Random(DateTime.Now.Millisecond);
            }

            public int Next()
            {
                if (rand == null)
                {
                    rand = new Random(DateTime.Now.Millisecond);
                }

                return rand.Next(min, max);
            }

            private Random rand;
            public readonly int min;
            public readonly int max;
        }
    }

    class Program
    {
        enum ProcessStatus
        {
            Found, NotFound, Terminated
        }

        private const string _PROCESS_NAME = "World of Warcraft";
        private const string _APP_NAME = "AntiAFK";
        private static RandomRange _THREAD_DELAY_RANGE = new RandomRange(0, 600000);

        private static IntPtr _PROCESS_HWND = IntPtr.Zero;

        private static Mutex _MUT = new Mutex();
        private static Thread _THREAD = null;
        private static volatile ProcessStatus _STATUS = ProcessStatus.NotFound;

        private static uint [] _KEYS = {
                0x20,       // space
                0x33,       // 3
                0x51        // q
        };

        static void Main(string[] args)
        {
            Console.Title = _APP_NAME;
            _PROCESS_HWND = Win32.Imports.FindWindowByCaption(IntPtr.Zero, _PROCESS_NAME);

            _STATUS = _PROCESS_HWND != IntPtr.Zero ? ProcessStatus.Found : ProcessStatus.NotFound;
            if (_STATUS == ProcessStatus.Found)
            {
                Console.WriteLine("Process found. Now initializing...");

                // TODO: BACKGROUND THREAD FOR PROCESSING
                _THREAD = new Thread(BackProcess);
                _THREAD.IsBackground = true;
                _THREAD.Start(_THREAD_DELAY_RANGE);

                // INPUT THREAD
                while (true)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey();
                    _MUT.WaitOne();
                    {
                        switch(keyInfo.Key)
                        {
                            // TODO: make sure that the previous thread hasn't changed anything... appropriately
                            // ie. set to do something else, but then this thread cancels it
                            case ConsoleKey.Escape:
                                _STATUS = ProcessStatus.Terminated;
                                break;
                            default:
                                break;
                        }

                        // do not forget to release the mutex, if breaking out early!
                        if (_STATUS == ProcessStatus.Terminated)
                        {
                            _MUT.ReleaseMutex();
                            break;
                        }
                    }
                    _MUT.ReleaseMutex();
                }
            }
            else if (_STATUS == ProcessStatus.NotFound)
            {
                Console.WriteLine("Process not found. Hit any key to exit process.");
            }
        }

        static void BackProcess(object delay)
        {
            IntPtr consoleWindow = Win32.Imports.FindWindowByCaption(IntPtr.Zero, _APP_NAME);
            
            RandomRange keyIndexRange = new RandomRange(0, _KEYS.Length);
            RandomRange delayRange = delay.GetType() == typeof(RandomRange) ? (RandomRange)delay : new RandomRange(0, 0);

            const uint msg_KeyDown = 0x0100, msg_KeyUp = 0x0101;

            while (_STATUS == ProcessStatus.Found)
            {
                _PROCESS_HWND = Win32.Imports.FindWindowByCaption(IntPtr.Zero, _PROCESS_NAME);

                // TODO: user can add in new keys, or delete. and can set associated timers with each key.
                if (_PROCESS_HWND != IntPtr.Zero)
                {
                    Thread.Sleep(delayRange.Next());
                    {
                        // TODO: MAIN PROCESSING GOES HERE
                        int keyIndex = keyIndexRange.Next();
                        Console.WriteLine(keyIndex);

                        uint vKey = _KEYS[keyIndex];
                        uint sKey = Win32.Imports.MapVirtualKeyA(vKey, 0);

                        Win32.Imports.SendNotifyMessageA(_PROCESS_HWND, msg_KeyDown, vKey, 0x00000000 | (sKey << 16)
                                | 0x00000000 | 0x00000000 | 0x00000000);
                        Win32.Imports.SendNotifyMessageA(_PROCESS_HWND, msg_KeyUp, vKey, 0x00000000 | (sKey << 16)
                                | 0x00000000 | 0x00000000 | 0x00000000);
                    }
                }
                else
                {
                    _MUT.WaitOne();
                    _STATUS = ProcessStatus.Terminated;
                    _MUT.ReleaseMutex();
                }
            }

            Console.WriteLine("Now terminating.");
        }
    }
}
