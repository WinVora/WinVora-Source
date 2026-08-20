using System;
using System.Threading;

namespace WinVora
{
    internal enum UpdateOperationState { Idle, Discovering, Installing }
    internal enum StorageOperationState { Idle, Scanning, Deleting }
    internal enum UninstallOperationState { Idle, LoadingPrograms, Uninstalling }

    internal abstract class OperationController<TState> : IDisposable where TState : struct, Enum
    {
        private readonly object _sync = new();
        private CancellationTokenSource? _operationCancellation;
        private TState _state;

        protected OperationController(TState idleState) => IdleState = _state = idleState;

        protected TState IdleState { get; }
        public TState State { get { lock (_sync) return _state; } }
        public bool IsBusy { get { lock (_sync) return !Equals(_state, IdleState); } }
        public CancellationToken Token { get { lock (_sync) return _operationCancellation?.Token ?? CancellationToken.None; } }

        protected bool TryBegin(TState state, bool cancellable)
        {
            lock (_sync)
            {
                if (!Equals(_state, IdleState)) return false;
                _operationCancellation?.Dispose();
                _operationCancellation = cancellable ? new CancellationTokenSource() : null;
                _state = state;
                return true;
            }
        }

        protected void Complete(TState expectedState)
        {
            lock (_sync)
            {
                if (!Equals(_state, expectedState)) return;
                _operationCancellation?.Dispose();
                _operationCancellation = null;
                _state = IdleState;
            }
        }

        public void Cancel()
        {
            lock (_sync)
            {
                try { _operationCancellation?.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                try { _operationCancellation?.Cancel(); }
                catch (ObjectDisposedException) { }
                _operationCancellation?.Dispose();
                _operationCancellation = null;
                _state = IdleState;
            }
        }

        public void Dispose() => Reset();
    }

    internal sealed class UpdateOperationController : OperationController<UpdateOperationState>
    {
        public UpdateOperationController() : base(UpdateOperationState.Idle) { }
        public bool IsDiscovering => State == UpdateOperationState.Discovering;
        public bool IsInstalling => State == UpdateOperationState.Installing;
        public bool TryBeginDiscovery() => TryBegin(UpdateOperationState.Discovering, cancellable: false);
        public bool TryBeginInstall() => TryBegin(UpdateOperationState.Installing, cancellable: true);
        public void CompleteDiscovery() => Complete(UpdateOperationState.Discovering);
        public void CompleteInstall() => Complete(UpdateOperationState.Installing);
    }

    internal sealed class StorageOperationController : OperationController<StorageOperationState>
    {
        public StorageOperationController() : base(StorageOperationState.Idle) { }
        public bool IsScanning => State == StorageOperationState.Scanning;
        public bool IsDeleting => State == StorageOperationState.Deleting;
        public bool TryBeginScan() => TryBegin(StorageOperationState.Scanning, cancellable: true);
        public bool TryBeginDelete() => TryBegin(StorageOperationState.Deleting, cancellable: true);
        public void CompleteScan() => Complete(StorageOperationState.Scanning);
        public void CompleteDelete() => Complete(StorageOperationState.Deleting);
    }

    internal sealed class UninstallOperationController : OperationController<UninstallOperationState>
    {
        public UninstallOperationController() : base(UninstallOperationState.Idle) { }
        public bool IsLoading => State == UninstallOperationState.LoadingPrograms;
        public bool IsUninstalling => State == UninstallOperationState.Uninstalling;
        public bool TryBeginLoad() => TryBegin(UninstallOperationState.LoadingPrograms, cancellable: true);
        public bool TryBeginUninstall() => TryBegin(UninstallOperationState.Uninstalling, cancellable: true);
        public void CompleteLoad() => Complete(UninstallOperationState.LoadingPrograms);
        public void CompleteUninstall() => Complete(UninstallOperationState.Uninstalling);
    }
}
