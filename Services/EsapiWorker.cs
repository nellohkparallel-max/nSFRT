using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SFRThelper.Services
{
    /// <summary>
    /// Dedicated STA marshal for all VMS.TPS COM calls.
    /// Plugin mode posts work onto Eclipse's dispatcher via a BlockingCollection + TCS.
    /// A dedicated STA pump is available for standalone when ESAPI is created on that thread.
    /// UI / Task.Run threads never touch VMS objects.
    /// </summary>
    public sealed class EsapiWorker : IEsapiWorker
    {
        private readonly Dispatcher _dispatcher;
        private readonly BlockingCollection<WorkItem> _queue = new BlockingCollection<WorkItem>();
        private readonly Thread _pumpThread;
        private volatile bool _draining;

        private sealed class WorkItem
        {
            public Action Action;
            public Func<object> Func;
            public TaskCompletionSource<object> Tcs;
        }

        public EsapiWorker(Dispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");
            _dispatcher = dispatcher;
        }

        /// <summary>Starts a dedicated STA thread that owns ESAPI for standalone executables.</summary>
        public EsapiWorker()
        {
            var ready = new ManualResetEventSlim(false);
            Dispatcher captured = null;
            _pumpThread = new Thread(() =>
            {
                captured = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.IsBackground = true;
            _pumpThread.Name = "nSFRT-ESAPI";
            _pumpThread.Start();
            ready.Wait();
            _dispatcher = captured;
        }

        public bool CheckAccess()
        {
            return _dispatcher.CheckAccess();
        }

        public void Invoke(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }
            Enqueue(action, null).Task.GetAwaiter().GetResult();
        }

        public T Invoke<T>(Func<T> func)
        {
            if (func == null)
                throw new ArgumentNullException("func");
            if (_dispatcher.CheckAccess())
                return func();
            object boxed = Enqueue(null, () => func()).Task.GetAwaiter().GetResult();
            return (T)boxed;
        }

        public Task InvokeAsync(Action action, CancellationToken token)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            if (token.IsCancellationRequested)
                return Task.FromCanceled(token);
            if (_dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Enqueue(action, null, token).Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken token)
        {
            if (func == null)
                throw new ArgumentNullException("func");
            if (token.IsCancellationRequested)
                return Task.FromCanceled<T>(token);
            if (_dispatcher.CheckAccess())
                return Task.FromResult(func());
            return Enqueue(null, () => func(), token).Task.ContinueWith(t => (T)t.Result, token);
        }

        public void BeginShutdown()
        {
            try { _queue.CompleteAdding(); }
            catch { }
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                return;
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }

        private TaskCompletionSource<object> Enqueue(Action action, Func<object> func, CancellationToken token = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<object>();
            if (token.CanBeCanceled)
            {
                token.Register(delegate { tcs.TrySetCanceled(); });
            }
            _queue.Add(new WorkItem { Action = action, Func = func, Tcs = tcs });
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Drain));
            return tcs;
        }

        private void Drain()
        {
            if (_draining)
                return;
            _draining = true;
            try
            {
                WorkItem item;
                while (_queue.TryTake(out item))
                {
                    try
                    {
                        if (item.Func != null)
                            item.Tcs.TrySetResult(item.Func());
                        else
                        {
                            if (item.Action != null)
                                item.Action();
                            item.Tcs.TrySetResult(null);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        item.Tcs.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        item.Tcs.TrySetException(ex);
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }
    }
}
