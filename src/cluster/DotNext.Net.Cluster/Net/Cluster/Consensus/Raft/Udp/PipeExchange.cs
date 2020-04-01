using System;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DotNext.Net.Cluster.Consensus.Raft.Udp
{
    internal abstract class PipeExchange : IExchange
    {
        private readonly Pipe pipe;

        private protected PipeExchange(PipeOptions? options = null)
            => pipe = new Pipe(options ?? PipeOptions.Default);
        
        private protected void ReusePipe() => pipe.Reset();
        
        private protected PipeWriter Writer => pipe.Writer;

        private protected PipeReader Reader => pipe.Reader;

        public abstract ValueTask<bool> ProcessInbountMessageAsync(PacketHeaders headers, ReadOnlyMemory<byte> payload, EndPoint endpoint, CancellationToken token);

        public abstract ValueTask<(PacketHeaders Headers, int BytesWritten, bool)> CreateOutboundMessageAsync(Memory<byte> payload, CancellationToken token);
    
        void IExchange.OnException(Exception e) => pipe.Writer.Complete(e);

        void IExchange.OnCanceled(CancellationToken token) => pipe.Writer.Complete(new OperationCanceledException(token));
    }
}