using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace DotNext.Threading
{
    using Tasks;
    using Generic;

    /// <summary>
    /// Provides a framework for implementing asynchronous locks and related synchronizers that rely on first-in-first-out (FIFO) wait queues.
    /// </summary>
    /// <remarks>
    /// Derived synchronizers less efficient from memory utilization point of view in comparison with non-queued synchronizers.
    /// It provides the individual instance of <see cref="Task{TResult}"/> under contention for each waiter in the queue.
    /// </remarks>
    public abstract class QueuedSynchronizer: Disposable, ISynchronizer
    {
        private protected class WaitNode : Synchronizer.WaitNode
        {
            private WaitNode previous;
            private WaitNode next;

            internal WaitNode() => previous = next = null;

            internal WaitNode(WaitNode previous)
                : this()
            {
                previous.next = this;
                this.previous = previous;
            }

            internal void DetachNode()
            {
                if (!(previous is null))
                    previous.next = next;
                if (!(next is null))
                    next.previous = previous;
                next = previous = null;
            }

            internal WaitNode CleanupAndGotoNext()
            {
                var next = this.next;
                this.next = previous = null;
                return next;
            }

            internal WaitNode Previous => previous;

            internal WaitNode Next => next;

            internal bool IsRoot => previous is null && next is null;
        }

        private protected interface ILockManager<STATE, out N>
            where STATE: struct
            where N : WaitNode
        {
            bool CheckState(ref STATE state); //if true then Wait method can be completed synchronously; otherwise, false.

            N CreateNode(WaitNode tail);
        }

        private protected WaitNode head, tail;

        private protected QueuedSynchronizer()
        {
        }

        bool ISynchronizer.HasWaiters => !(head is null);

        [MethodImpl(MethodImplOptions.Synchronized)]
        private protected bool RemoveNode(WaitNode node)
        {
            var inList = ReferenceEquals(head, node) || !node.IsRoot;
            if (ReferenceEquals(head, node))
                head = node.Next;
            if (ReferenceEquals(tail, node))
                tail = node.Previous;
            node.DetachNode();
            return inList;
        }

        private async Task<bool> Wait(WaitNode node, TimeSpan timeout, CancellationToken token)
        {
            using (var tokenSource = token.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(token) : new CancellationTokenSource())
            {
                if (ReferenceEquals(node.Task, await Task.WhenAny(node.Task, Task.Delay(timeout, tokenSource.Token)).ConfigureAwait(false)))
                {
                    tokenSource.Cancel();   //ensure that Delay task is cancelled
                    return true;
                }
            }
            if (RemoveNode(node))
            {
                token.ThrowIfCancellationRequested();
                return false;
            }
            else
                return await node.Task.ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private protected Task<bool> Wait<STATE, M>(ref STATE state, TimeSpan timeout, CancellationToken token)
            where STATE : struct
            where M : struct, ILockManager<STATE, WaitNode>
        {
            ThrowIfDisposed();
            var manager = new M();
            if(timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            else if (token.IsCancellationRequested)
                return Task.FromCanceled<bool>(token);
            else if(manager.CheckState(ref state))
                return CompletedTask<bool, BooleanConst.True>.Task;
            else if (timeout == TimeSpan.Zero)   //if timeout is zero fail fast
                return CompletedTask<bool, BooleanConst.False>.Task;
            else if(head is null)
                head = tail = manager.CreateNode(null);
            else 
                tail = manager.CreateNode(tail);
            return timeout < TimeSpan.MaxValue || token.CanBeCanceled ? Wait(tail, timeout, token) : tail.Task;
        }

        /// <summary>
        /// Releases all resources associated with exclusive lock.
        /// </summary>
        /// <remarks>
        /// This method is not thread-safe and may not be used concurrently with other members of this instance.
        /// </remarks>
        /// <param name="disposing">Indicates whether the <see cref="Dispose(bool)"/> has been called directly or from finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (var current = head; !(current is null); current = current.CleanupAndGotoNext())
                    current.TrySetCanceled();
                head = tail = null;
            }
        }
    }
}