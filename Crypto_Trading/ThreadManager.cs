using Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public void addThread(string name, Func<Task<bool>> action, Action onClosing = null)
        {
            if(this.threads.ContainsKey(name))
            {
                this.addLog("The thread name already exists. name:" + name, Enums.logType.ERROR);
            }
            else
            {
                thread t = new thread(name, action, onClosing);
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
        public Thread threadObj;
        public bool isRunning;
        public string name;

        public Func<Task<bool>> action;
        public Action onClosing;

        public Action<string, logType> addLog;

        public thread(string name, Func<Task<bool>> action, Action onClosing = null)
        {
            this.name = name;
            this.action = action;
            this.isRunning = false;
            onClosing ??= () => { };
            this.onClosing = onClosing;
        }
        public void start()
        {
            this.threadObj = new Thread(() =>
            {
                this.loop();
            });
            this.isRunning = true;
            this.threadObj.Start();
        }

        private async void loop()
        {
            try
            {
                while (true)
                {
                    if(!await this.action())
                    {
                        this.isRunning=false;
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
                this.addLog("An error thrown within the thread:" + this.name,logType.ERROR);
                this.addLog(e.Message, logType.ERROR);
                this.isRunning = false;
            }
           
        }
        public void stop()
        {
            if (isRunning)
            {
                isRunning = false;
            }
        }
    }
}
