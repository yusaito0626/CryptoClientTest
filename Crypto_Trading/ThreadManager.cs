using Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Serialization;
using System.Runtime.Versioning;

namespace Crypto_Trading
{
    public class ThreadManager
    {
        public Dictionary<string, thread> threads;


        public Action<string, Enums.logType> _addLog;

        private ThreadManager()
        {
            this.threads = new Dictionary<string, thread>();
            //this._addLog = Console.WriteLine;
        }

        public void addThread(string name, Func<Task<(bool,double)>> action, Action onClosing = null,Action onError = null)
        {
            if(this.threads.ContainsKey(name))
            {
                this.addLog("The thread name already exists. name:" + name, Enums.logType.ERROR);
            }
            else
            {
                thread t = new thread(name, action, onClosing, onError);
                t.addLog = this.addLog;
                this.threads[name] = t;
                t.start();
                this.addLog("The thread started. name:" + name);
            }
        }

        public void addThread(string name, Func<Action,Action,CancellationToken,int,Task<bool>> loop, Action onClosing = null, Action onError = null,int spinningMax = 0)
        {
            if (this.threads.ContainsKey(name))
            {
                this.addLog("The thread name already exists. name:" + name, Enums.logType.ERROR);
            }
            else
            {
                thread t = new thread(name, loop, onClosing, onError,spinningMax);
                t.addLog = this.addLog;
                this.threads[name] = t;
                t.start();
                this.addLog("The thread started. name:" + name);
            }
        }

        public bool startThread(string name)
        {
            if (this.threads.ContainsKey(name))
            {
                if(this.threads[name].isRunning == false)
                {
                    this.threads[name].start();
                    return true;
                }
                else
                {
                    this.addLog("The thread is already running. name:" + name, Enums.logType.ERROR);
                    return false;
                }
            }
            else
            {
                this.addLog("The thread does not exist. name:" + name, Enums.logType.ERROR);
                return false;
            }
        }
        public bool stopThread(string name)
        {
            if (this.threads.ContainsKey(name))
            {
                this.threads[name].stop();
                return true;
            }
            else
            {
                this.addLog("The thread does not exist. name:" + name, Enums.logType.ERROR);
                return false;
            }
        }

        public bool disposeThread(string name)
        {
            if (this.threads.ContainsKey(name))
            {
                this.threads[name].stop();
                this.threads.Remove(name);
                return true;
            }
            else
            {
                this.addLog("The thread does not exist. name:" + name, Enums.logType.ERROR);
                return false;
            }
        }
        public void stopAllThreads()
        {
            foreach(var th in this.threads)
            {
                th.Value.stop();
            }
        }

        public bool detectStopped(ref List<string> stoppedTh)
        {
            bool output = true;
            foreach(var item in this.threads)
            {
                if(item.Value.isRunning == false)
                {
                    output = false;
                    stoppedTh.Add(item.Key);
                }
            }
            return output;
        }

        public void addLog(string line,logType logtype = logType.INFO)
        {
            this._addLog("[ThreadManager]" + line,logtype);
        }


        private static ThreadManager _instance;
        private static readonly object _lockObject = new object();

        public static ThreadManager GetInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = new ThreadManager();
                }
                return _instance;
            }
        }
    }

    public class thread
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int SetThreadDescription(IntPtr hThread, string lpThreadDescription);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentThread();
        [DllImport("libc", SetLastError = true)]
        static extern int prctl(int option, string name, ulong arg2, ulong arg3, ulong arg4);
        const int PR_SET_NAME = 15;

        public Thread threadObj;
        public bool isRunning;
        public string name;

        public double totalElapsedTime;
        public int count;

        public Func<Task<(bool,double)>> action;

        public CancellationTokenSource ct;
        public Func<Action,Action,CancellationToken,int,Task<bool>> loopFunc;
        public int spinnerMaxCount;


        public Action onClosing;
        public Action onError;

        public Action<string, logType> addLog;

        public thread(string name, Func<Task<(bool,double)>> action, Action onClosing = null, Action onError = null)
        {
            this.name = name;
            this.action = action;
            this.isRunning = false;
            onClosing ??= () => { };
            onError ??= () => { };
            this.onClosing = onClosing;
            this.onError = onError;
            this.totalElapsedTime = 0;
            this.count = 0;
            this.loopFunc = null;
        }
        public thread(string name, Func<Action, Action,CancellationToken,int, Task<bool>> _loop, Action onClosing = null,Action onError = null, int _spinnerMax = 0)
        {
            this.name = name;
            this.loopFunc = _loop;
            this.isRunning = false;
            onClosing ??= () => { };
            onError ??= () => { };
            this.onClosing = onClosing;
            this.onError = onError;
            this.action = null;
            this.spinnerMaxCount = _spinnerMax;
        }
        private void SetOsThreadName(string name)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetThreadDescription(GetCurrentThread(), name);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                prctl(PR_SET_NAME, name, 0, 0, 0);
            }
            else
            {
                
            }
        }
        public void start()
        {
            if(this.action != null)
            {
                this.threadObj = new Thread(() =>
                {
                    this.SetOsThreadName(this.name);
                    this.loop();
                });
                this.isRunning = true;
                this.threadObj.Start();
            }
            else if(this.loopFunc != null)
            {
                this.ct = new CancellationTokenSource();
                this.threadObj = new Thread(async () =>
                {
                    Stopwatch sw = new Stopwatch();
                    for(int i = 0; i < 3;++i)
                    {
                        sw.Start();
                        sw.Stop();
                        sw.Reset();
                    }
                    this.SetOsThreadName(this.name);
                    bool ret = false;
                    ret = await this.loopFunc(
                        ()=>sw.Start(),
                        ()=>
                        {
                            sw.Stop();
                            ++this.count;
                            this.totalElapsedTime += sw.Elapsed.TotalNanoseconds / 1000;
                            sw.Reset();
                        },
                        ct.Token,
                        this.spinnerMaxCount);
                    if(ret)
                    {
                        onClosing();
                        this.addLog("The thread has been successfully closed... name:" + this.name, logType.INFO);
                    }
                    else
                    {
                        onError();
                        this.addLog("The thread has been closed with an error. name:" + this.name, logType.WARNING);
                    }
                    this.isRunning = false;
                });
                this.isRunning = true;
                this.threadObj.Start();
            }
            else
            {
                this.addLog("Function is not set. thread name:" + this.name, logType.ERROR);
            }

        }

        private async void loop()
        {

            Stopwatch sw = new Stopwatch();
            double elapsedTime = 0;
            try
            {
                while (true)
                {
                    var ret = await this.action();
                    if (!ret.Item1)
                    {
                        //this.addLog("Thread is being closed by unexpected error. name:" + this.name, logType.ERROR);
                        this.isRunning=false;
                    }
                    if(ret.Item2 > 0)
                    {
                        this.totalElapsedTime += ret.Item2;
                        ++this.count;
                    }
                    if (!this.isRunning)
                    {
                        this.onClosing();
                        this.addLog("Thread closing. name:" + this.name,logType.INFO);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                sw.Stop();
                sw.Reset();
                this.addLog("An error thrown within the thread:" + this.name,logType.WARNING);
                this.addLog(e.Message, logType.WARNING);
                this.onError();
                if(e.StackTrace != null)
                {
                    this.addLog(e.StackTrace, logType.ERROR);
                }
                this.isRunning = false;
            }
           
        }
        public void stop()
        {
            if (isRunning)
            {
                isRunning = false;
            }
            if(ct != null)
            {
                ct.Cancel();
            }
        }
    }
}
