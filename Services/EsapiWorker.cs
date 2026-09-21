using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SFRThelper.Services
{
    /// <summary>
    /// Dedicated STA dispatcher for all VMS.TPS calls. UI threads never touch ESAPI objects.
    /// </summary>
    public sealed class EsapiWorker : IEsapiWorker
    {
        private readonly Dispatcher _dispatcher;

        public EsapiWorker(Dispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");
            _dispatcher = dispatcher;
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
            _dispatcher.Invoke(action);
        }

        public T Invoke<T>(Func<T> func)
        {
            if (func == null)
                throw new ArgumentNullException("func");
            if (_dispatcher.CheckAccess())
                return func();
            return _dispatcher.Invoke(func);
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

            var tcs = new TaskCompletionSource<object>();
            DispatcherOperation op = _dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    action();
                    tcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));
            token.Register(delegate { op.Abort(); tcs.TrySetCanceled(); });
            return tcs.Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken token)
        {
            if (func == null)
                throw new ArgumentNullException("func");
            if (token.IsCancellationRequested)
                return Task.FromCanceled<T>(token);
            if (_dispatcher.CheckAccess())
                return Task.FromResult(func());

            var tcs = new TaskCompletionSource<T>();
            DispatcherOperation op = _dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    tcs.TrySetResult(func());
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));
            token.Register(delegate { op.Abort(); tcs.TrySetCanceled(); });
            return tcs.Task;
        }

        public void BeginShutdown()
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                return;
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }
    }
}
