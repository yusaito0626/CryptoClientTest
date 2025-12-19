using LockFreeStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

using Utils;

namespace LockFreeStack
{
    public class LockFreeStack<T> : IDisposable
    {
        Node<T> _pHead;
        LockFreePool<T> _UsedPool;
        int _pushes;
        int _pops;
        volatile int  _lock;
        public LockFreeStack(int PreservingSize = 10000)
        {
            this._pushes = 0;
            this._pops = 0;
            this._lock = 0;
            if (PreservingSize < 1)
            {
                PreservingSize = 10000;
            }
            this._UsedPool = new LockFreePool<T>(PreservingSize);
            this._pHead = this._UsedPool.pop();
        }
        public void Dispose()
        {
            if (this._pHead != null)
            {
                while (true)
                {
                    if (this._pHead.Next == null)
                    {
                        this._pHead.Dispose();
                        break;
                    }
                    else
                    {
                        Node<T> preHead = this._pHead;
                        this._pHead = preHead.Next;
                        preHead.Dispose();
                    }
                }
            }
            this._UsedPool.Dispose();
        }

        public void push(T val)
        {
            using (var func = new funcContainer(this.getLock))
            {
                Node<T> newNode;
                newNode = this._UsedPool.pop();
                newNode.value = val;
                newNode.Next = _pHead;
                _pHead = newNode;
                ++_pushes;
            }
        }
        public T pop()
        {
            T output = default!;
            using(var func = new funcContainer(this.getLock))
            {
                Node<T> ReturningNode;
                if (_pHead.Next != null)
                {
                    ReturningNode = this._pHead;
                    this._pHead = this._pHead.Next;
                    output = ReturningNode.value;
                    ReturningNode.Next = null;
                    ReturningNode.Back = null;
                    ReturningNode.value = default!;
                    this._UsedPool.push(ReturningNode);
                    ++_pops;
                }
            }
            return output;
        }
        Action getLock()
        {
            while (Interlocked.CompareExchange(ref this._lock, 1, 0) != 0)
            {

            }
            return () => { Volatile.Write(ref this._lock, 0); };
        }
        //public int Count() { return this._pushes - this._pops; }
        public int Count => this._pushes - this._pops;
    }
    internal class LockFreePool<T> : IDisposable
    {
        int _pushes = 0;
        int _pops = 0;
        Node<T>? _pHead;
        public LockFreePool(int PreservingSize = 10000)
        {
            this._pHead = null;
            if (PreservingSize < 1)
            {
                PreservingSize = 10000;
            }
            while (_pushes < PreservingSize)
            {
                Node<T> NewNode = new Node<T>();
                if (_pushes == 0)
                {
                    NewNode.Next = null;
                }
                else
                {
                    NewNode.Next = this._pHead;
                }
                _pHead = NewNode;
                ++_pushes;
            }
        }
        public void Dispose()
        {
            while (true)
            {
                if (this._pHead != null)
                {
                    this._pHead.Dispose();
                    break;
                }
                else
                {
                    Node<T> preHead = this._pHead;
                    _pHead = preHead.Next;
                    preHead.Dispose();
                }
            }
        }
        public void push(Node<T> NewNode)
        {
            Node<T> TempHead = this._pHead;
            NewNode.Next = TempHead;
            this._pHead = NewNode;
            ++(this._pushes);
        }
        public Node<T> pop()
        {
            Node<T> tempHead = _pHead;
            Node<T> ReturningNode;
            if (tempHead.Next == null)
            {
                ReturningNode = new Node<T>();
            }
            else
            {
                ReturningNode = tempHead.Next;
                tempHead.Next = ReturningNode.Next;
            }
            ReturningNode.Next = null;
            ReturningNode.Back = null;
            ReturningNode.value = default!;
            ++(this._pops);
            return ReturningNode;
        }

        //public int size() { return _pushes - _pops; }
        public int size => this._pushes - this._pops;
    }
    internal class Node<T> : IDisposable
    {
        public T value;
        public Node<T>? Next;
        public Node<T>? Back;

        public Node()
        {
            Next = null;
            Back = null;
            value = default!;
        }

        public void Dispose()
        {
            Next = null;
            Back = null;
            value = default!;
        }
    }
}

namespace LockFreeQueue
{
    public class MIMOQueue<T> : IDisposable
    {
        Node<T> _pHead;
        Node<T> _pHeadTemp;
        Node<T> _pTail;
        LockFreePool<T> _NodePool;
        int _Enqueues;
        int _Dequeues;

        volatile int _EnqueueLock;
        volatile int _DequeueLock;

        public MIMOQueue(int PoolSize = 20000)
        {
            this._Enqueues = 0;
            this._Dequeues = 0;
            this._EnqueueLock = 0;
            this._DequeueLock = 0;
            if (PoolSize < 2)
            {
                PoolSize = 20000;
            }
            this._NodePool = new LockFreePool<T>(PoolSize);
            this._pHead = this._NodePool.pop();
            this._pTail = this._pHead;
            this._pHeadTemp = this._NodePool.pop();
        }
        public void Dispose()
        {
            this._NodePool.Dispose();
            while (true)
            {
                if (this._pTail.Back == null)
                {
                    break;
                }
                else
                {
                    Node<T> nd;
                    nd = this._pTail;
                    this._pTail = nd.Back;
                    nd.value = default!;
                    this._pTail.Next = null;
                    nd.Dispose();
                }
            }
            this._pHead.Dispose();
            this._pTail.Dispose();
        }

        public void Enqueue(T val)
        {
            using(var func = new funcContainer(this.getEnqueueLock))
            {
                Node<T> newNode;
                newNode = _NodePool.pop();
                newNode.value = val;
                Node<T> OldHead = this._pHead;
                newNode.Next = OldHead;
                this._pHead = newNode;
                OldHead.Back = newNode;
                this._pHeadTemp.value = newNode.value;
                this._pHeadTemp.Next = newNode.Next;
                this._pHeadTemp.Back = newNode.Back;
                ++(this._Enqueues);
            }
        }
        public T Dequeue()
        {
            T val = default!;
            using (var func = new funcContainer(this.getDequeueLock))
            {
                Node<T> preTail = this._pTail;
                if (preTail.Back == null)
                {
                    return default!;
                }
                val = preTail.Back.value;
                this._pTail = this._pTail.Back;
                this._pTail.Next = null;
                this._pTail.value = default!;
                preTail.Back = null;
                this._NodePool.push(preTail);
                ++(this._Dequeues);
            }
                
            return val;
        }
        public T Peek()
        {
            using (var func = new funcContainer(this.getDequeueLock))
            {
                if (this._pTail.Back == null)
                {
                    return default!;
                }
                else
                {
                    return this._pTail.Back.value;
                }
            }
        }
        //public int Count() { return this._Enqueues - this._Dequeues; }
        public int Count => this._Enqueues - this._Dequeues;
        Action getEnqueueLock()
        {
            while (Interlocked.CompareExchange(ref this._EnqueueLock, 1, 0) != 0)
            {

            }
            return () => { Volatile.Write(ref this._EnqueueLock, 0); };
        }
        Action getDequeueLock()
        {
            while (Interlocked.CompareExchange(ref this._DequeueLock, 1, 0) != 0)
            {

            }
            return () => { Volatile.Write(ref this._DequeueLock, 0); };
        }
    }
    public class MISOQueue<T> : IDisposable
    {
        Node<T> _pHead;
        Node<T> _pHeadTemp;
        Node<T> _pTail;
        LockFreePool<T> _NodePool;
        int _Enqueues;
        int _Dequeues;

        volatile int _EnqueueLock;

        public MISOQueue(int PoolSize = 20000)
        {
            this._Enqueues = 0;
            this._Dequeues = 0;
            this._EnqueueLock = 0;
            if (PoolSize < 2)
            {
                PoolSize = 20000;
            }
            this._NodePool = new LockFreePool<T>(PoolSize);
            this._pHead = this._NodePool.pop();
            this._pTail = this._pHead;
            this._pHeadTemp = this._NodePool.pop();
        }
        public void Dispose()
        {
            this._NodePool.Dispose();
            while (true)
            {
                if (this._pTail.Back == null)
                {
                    break;
                }
                else
                {
                    Node<T> nd;
                    nd = this._pTail;
                    this._pTail = nd.Back;
                    nd.value = default!;
                    this._pTail.Next = null;
                    nd.Dispose();
                }
            }
            this._pHead.Dispose();
            this._pTail.Dispose();
        }

        public void Enqueue(T val)
        {
            using (var func = new funcContainer(this.getEnqueueLock))
            {
                Node<T> newNode;
                newNode = _NodePool.pop();
                newNode.value = val;
                Node<T> OldHead = this._pHead;
                newNode.Next = OldHead;
                this._pHead = newNode;
                OldHead.Back = newNode;
                this._pHeadTemp.value = newNode.value;
                this._pHeadTemp.Next = newNode.Next;
                this._pHeadTemp.Back = newNode.Back;
                ++(this._Enqueues);
            }
        }
        public T Dequeue()
        {
            T val = default!;
            Node<T> preTail = this._pTail;
            if (preTail.Back == null)
            {
                return default!;
            }
            val = preTail.Back.value;
            this._pTail = this._pTail.Back;
            this._pTail.Next = null;
            this._pTail.value = default!;
            preTail.Back = null;
            this._NodePool.push(preTail);
            ++(this._Dequeues);

            return val;
        }
        public T Peak()
        {
            if (this._pTail.Back == null)
            {
                return default!;
            }
            else
            {
                return this._pTail.Back.value;
            }
        }
        //public int Count() { return this._Enqueues - this._Dequeues; }
        //public int Count { get { return this._Enqueues - this._Dequeues; } }
        public int Count => this._Enqueues - this._Dequeues;
        Action getEnqueueLock()
        {
            while (Interlocked.CompareExchange(ref this._EnqueueLock, 1, 0) != 0)
            {

            }
            return () => { Volatile.Write(ref this._EnqueueLock, 0); };
        }
    }
    public class SIMOQueue<T> : IDisposable
    {
        Node<T> _pHead;
        Node<T> _pHeadTemp;
        Node<T> _pTail;
        LockFreePool<T> _NodePool;
        int _Enqueues;
        int _Dequeues;

        volatile int _DequeueLock;

        public SIMOQueue(int PoolSize = 20000)
        {
            this._Enqueues = 0;
            this._Dequeues = 0;
            this._DequeueLock = 0;
            if (PoolSize < 2)
            {
                PoolSize = 20000;
            }
            this._NodePool = new LockFreePool<T>(PoolSize);
            this._pHead = this._NodePool.pop();
            this._pTail = this._pHead;
            this._pHeadTemp = this._NodePool.pop();
        }
        public void Dispose()
        {
            this._NodePool.Dispose();
            while (true)
            {
                if (this._pTail.Back == null)
                {
                    break;
                }
                else
                {
                    Node<T> nd;
                    nd = this._pTail;
                    this._pTail = nd.Back;
                    nd.value = default!;
                    this._pTail.Next = null;
                    nd.Dispose();
                }
            }
            this._pHead.Dispose();
            this._pTail.Dispose();
        }

        public void Enqueue(T val)
        {
            Node<T> newNode;
            newNode = _NodePool.pop();
            newNode.value = val;
            Node<T> OldHead = this._pHead;
            newNode.Next = OldHead;
            this._pHead = newNode;
            OldHead.Back = newNode;
            this._pHeadTemp.value = newNode.value;
            this._pHeadTemp.Next = newNode.Next;
            this._pHeadTemp.Back = newNode.Back;
            ++(this._Enqueues);
        }
        public T Dequeue()
        {
            T val = default!;
            using (var func = new funcContainer(this.getDequeueLock))
            {
                Node<T> preTail = this._pTail;
                if (preTail.Back == null)
                {
                    return default!;
                }
                val = preTail.Back.value;
                this._pTail = this._pTail.Back;
                this._pTail.Next = null;
                this._pTail.value = default!;
                preTail.Back = null;
                this._NodePool.push(preTail);
                ++(this._Dequeues);
            }

            return val;
        }
        public T Peak()
        {
            using (var func = new funcContainer(this.getDequeueLock))
            {
                if (this._pTail.Back == null)
                {
                    return default!;
                }
                else
                {
                    return this._pTail.Back.value;
                }
            }
        }
        //public int Count() { return this._Enqueues - this._Dequeues; }
        public int Count => this._Enqueues - this._Dequeues;
        Action getDequeueLock()
        {
            while (Interlocked.CompareExchange(ref this._DequeueLock, 1, 0) != 0)
            {

            }
            return () => { Volatile.Write(ref this._DequeueLock, 0); };
        }
    }
    public class SISOQueue<T> : IDisposable
    {
        Node<T> _pHead;
        Node<T> _pHeadTemp;
        Node<T> _pTail;
        LockFreePool<T> _NodePool;
        int _Enqueues;
        int _Dequeues;

        public SISOQueue(int PoolSize = 20000)
        {
            this._Enqueues = 0;
            this._Dequeues = 0;
            if (PoolSize < 2)
            {
                PoolSize = 20000;
            }
            this._NodePool = new LockFreePool<T>(PoolSize);
            this._pHead = this._NodePool.pop();
            this._pTail = this._pHead;
            this._pHeadTemp = this._NodePool.pop();
        }
        public void Dispose()
        {
            this._NodePool.Dispose();
            while(true)
            {
                if (this._pTail.Back == null)
                {
                    break;
                }
                else
                {
                    Node<T> nd;
                    nd = this._pTail;
                    this._pTail = nd.Back;
                    nd.value = default!;
                    this._pTail.Next = null;
                    nd.Dispose();
                }
            }
            this._pHead.Dispose();
            this._pTail.Dispose();
        }

        public void Enqueue(T val)
        {
            Node<T> newNode;
            newNode = _NodePool.pop();
            newNode.value = val;
            Node<T> OldHead = this._pHead;
            newNode.Next = OldHead;
            this._pHead = newNode;
            OldHead.Back = newNode;
            this._pHeadTemp.value = newNode.value;
            this._pHeadTemp.Next = newNode.Next;
            this._pHeadTemp.Back = newNode.Back;
            ++(this._Enqueues);
        }
        public T Dequeue()
        {
            T val;
            Node<T> preTail = this._pTail;
            if (preTail.Back == null)
            {
                return default!;
            }
            val = preTail.Back.value;
            this._pTail = this._pTail.Back;
            this._pTail.Next = null;
            this._pTail.value = default!;
            preTail.Back = null;
            this._NodePool.push(preTail);
            ++(this._Dequeues);
            return val;
        }
        public T Peek()
        {
            if (this._pTail.Back == null)
            {
                return default!;
            }
            else
            {
                return this._pTail.Back.value;
            }
        }

        //public int Count() { return this._Enqueues - this._Dequeues; }
        public int Count => this._Enqueues - this._Dequeues;
    }
    internal class LockFreePool<T> : IDisposable
    {
        int _pushes = 0;
        int _pops = 0;
        Node<T>? _pHead;
        public LockFreePool(int PreservingSize = 10000)
        {
            this._pHead = null;
            if (PreservingSize < 1)
            {
                PreservingSize = 10000;
            }
            while (_pushes < PreservingSize)
            {
                Node<T> NewNode = new Node<T>();
                if (_pushes == 0)
                {
                    NewNode.Next = null;
                }
                else
                {
                    NewNode.Next = this._pHead;
                }
                _pHead = NewNode;
                ++_pushes;
            }
        }
        public void Dispose()
        {
            while (true)
            {
                if (this._pHead != null)
                {
                    this._pHead.Dispose();
                    break;
                }
                else
                {
                    Node<T> preHead = this._pHead;
                    _pHead = preHead.Next;
                    preHead.Dispose();
                }
            }
        }
        public void push(Node<T> NewNode)
        {
            Node<T> TempHead = this._pHead;
            NewNode.Next = TempHead;
            this._pHead = NewNode;
            ++(this._pushes);
        }
        public Node<T> pop()
        {
            Node<T> tempHead = _pHead;
            Node<T> ReturningNode;
            if (tempHead.Next == null)
            {
                ReturningNode = new Node<T>();
            }
            else
            {
                ReturningNode = tempHead.Next;
                tempHead.Next = ReturningNode.Next;
            }
            ReturningNode.Next = null;
            ReturningNode.Back = null;
            ReturningNode.value = default!;
            ++(this._pops);
            return ReturningNode;
        }

        //public int size() { return _pushes - _pops; }
        public int size => this._pushes - this._pops;
    }
    internal class Node<T> : IDisposable
    {
        public T value;
        public Node<T>? Next;
        public Node<T>? Back;

        public Node()
        {
            Next = null;
            Back = null;
            value = default!;
        }

        public void Dispose()
        {
            Next = null;
            Back = null;
            value = default!;
        }
    }
}