using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IOException = System.IO.IOException;
using static System.Buffers.Binary.BinaryPrimitives;
using Unsafe = System.Runtime.CompilerServices.Unsafe;

namespace DotNext.Net.Cluster.Consensus.Raft.Udp
{
    using static IO.Pipelines.PipeExtensions;
    using static IO.DataTransferObject;

    internal abstract class EntriesExchange : ClientExchange<Result<bool>>
    {
        /*
            Message flow:
            1.REQ(None) Announce number of entries, prevLogIndex, prevLogTerm etc.
            1.RES(Ack) Wait for command: NextEntry to start sending content, None to abort transmission

            2.REQ(StreamStart) with information about content-type and length of the record
            2.REP(Ack) Wait for command: NextEntry to start sending content, Continue to send next chunk, None to finalize transmission
        
            3.REQ(Fragment) with the chunk of record data
            3.REP(Ack) Wait for command: NextEntry to start sending content, Continue to send next chunk, None to finalize transmission

            4.REQ(StreamEnd) with the final chunk of record data
            4.REP(Ack) Wait for command: NextEntry to start sending content, None to finalize transmission
        */

        private protected enum TransferControl : byte
        {
            None = 0,       //should contain Result<bool>
            NextEntry = 1,  //ask for the next record with the specified index
            Continue = 2,    //ask for the next data chunk of the record
            Abort = 3   //remote peer disposes async enumerator
        }

        private protected readonly Pipe pipe;
        private readonly long term, prevLogIndex, prevLogTerm, commitIndex;

        internal EntriesExchange(long term, long prevLogIndex, long prevLogTerm, long commitIndex, PipeOptions? options = null)
        {
            pipe = new Pipe(options ?? PipeOptions.Default);
            this.term = term;
            this.prevLogIndex = prevLogIndex;
            this.prevLogTerm = prevLogTerm;
            this.commitIndex = commitIndex;
        }

        internal static int CreateResponse(in Result<bool> result, Span<byte> output)
        {
            output[0] = (byte)TransferControl.None;
            output = output.Slice(sizeof(TransferControl));
            return IExchange.WriteResult(in result, output) + sizeof(TransferControl);
        }

        internal static int CreateNextEntryResponse(Span<byte> output, int logEntryIndex)
        {
            output[0] = (byte)TransferControl.NextEntry;
            output = output.Slice(sizeof(TransferControl));

            WriteInt32LittleEndian(output, logEntryIndex);

            return sizeof(TransferControl) + sizeof(int);
        }

        internal static int CreateContinueResponse(Span<byte> output)
        {
            output[0] = (byte)TransferControl.Continue;

            return sizeof(TransferControl);
        }

        internal static int ParseLogEntryPrologue(ReadOnlySpan<byte> input, out long length, out long term, out DateTimeOffset timeStamp, out bool isSnapshot)
        {
            length = ReadInt64LittleEndian(input);
            input = input.Slice(sizeof(long));
            
            term = ReadInt64LittleEndian(input);
            input = input.Slice(sizeof(long));

            timeStamp = Span.Read<DateTimeOffset>(ref input);

            isSnapshot = ValueTypeExtensions.ToBoolean(input[0]);

            return sizeof(long) + sizeof(long) + Unsafe.SizeOf<DateTimeOffset>() + sizeof(byte);
        }

        internal static void ParseAnnouncement(ReadOnlySpan<byte> input, out long term, out long prevLogIndex, out long prevLogTerm, out long commitIndex, out int entriesCount)
        {
            term = ReadInt64LittleEndian(input);
            input = input.Slice(sizeof(long));

            prevLogIndex = ReadInt64LittleEndian(input);
            input = input.Slice(sizeof(long));

            prevLogTerm = ReadInt64LittleEndian(input);
            input = input.Slice(sizeof(long));

            commitIndex = ReadInt64LittleEndian(input);
            input = input.Slice(sizeof(long));

            entriesCount = ReadInt32LittleEndian(input);
        }

        private protected int WriteAnnouncement(Span<byte> output, int entriesCount)
        {
            WriteInt64LittleEndian(output, term);
            output = output.Slice(sizeof(long));

            WriteInt64LittleEndian(output, prevLogIndex);
            output = output.Slice(sizeof(long));

            WriteInt64LittleEndian(output, prevLogTerm);
            output = output.Slice(sizeof(long));

            WriteInt64LittleEndian(output, commitIndex);
            output = output.Slice(sizeof(long));

            WriteInt32LittleEndian(output, entriesCount);

            return sizeof(long) + sizeof(long) + sizeof(long) + sizeof(long) + sizeof(int);
        }

        private protected static Encoding Encoding => Encoding.UTF8;

        private protected sealed override void OnException(Exception e) => pipe.Writer.Complete(e);

        private protected sealed override void OnCanceled(CancellationToken token) => OnException(new OperationCanceledException(token));
    
        internal void AbortIO()
        {
            pipe.Writer.CancelPendingFlush();
            pipe.Reader.CancelPendingRead();
        }
    }

    internal sealed class EntriesExchange<TEntry, TList> : EntriesExchange
        where TEntry : IRaftLogEntry
        where TList : IReadOnlyList<TEntry>
    {
        private delegate ValueTask<FlushResult> LogEntryFragmentWriter(PipeWriter writer, ref TEntry entry, CancellationToken token);

        private static readonly LogEntryFragmentWriter[] fragmentWriters = 
        {
            WriteLogEntryLength,
            WriteLogEntryTerm,
            WriteLogEntryTimestamp, 
            WriteLogEntrySnapshotMarker,
            WriteLogEntryContent
        };

        
        private TList entries;
        
        private Task? writeSession;
        private int currentIndex;
        private bool streamStart;
        
        internal EntriesExchange(long term, in TList entries, long prevLogIndex, long prevLogTerm, long commitIndex, PipeOptions? options = null)
            : base(term, prevLogIndex, prevLogTerm, commitIndex, options)
        {
            this.entries = entries;
            currentIndex = -1;
        }


        public override async ValueTask<(PacketHeaders, int, bool)> CreateOutboundMessageAsync(Memory<byte> payload, CancellationToken token)
        {
            int count;
            FlowControl control;
            if(currentIndex >= 0)   //write portion of log entry
            {
                count = await pipe.Reader.CopyToAsync(payload, token).ConfigureAwait(false);
                if(count == payload.Length)
                    control = streamStart ? FlowControl.StreamStart : FlowControl.Fragment;
                else
                    control = FlowControl.StreamEnd;
            }
            else    //send announcement
            {
                count = WriteAnnouncement(payload.Span, entries.Count);
                control = FlowControl.None;
            }
            return (new PacketHeaders(MessageType.AppendEntries, control), count, true);
        }

        private void FinalizeTransmission(ReadOnlySpan<byte> input)
        {
            TrySetResult(IExchange.ReadResult(input));
            writeSession = null;
        }

        private static ValueTask<FlushResult> WriteLogEntryLength(PipeWriter writer, ref TEntry entry, CancellationToken token)
            => writer.WriteInt64Async(entry.Length.GetValueOrDefault(-1L), true, token);
        
        private static ValueTask<FlushResult> WriteLogEntryTerm(PipeWriter writer, ref TEntry entry, CancellationToken token)
            => writer.WriteInt64Async(entry.Term, true, token);
        
        private static ValueTask<FlushResult> WriteLogEntryTimestamp(PipeWriter writer, ref TEntry entry, CancellationToken token)
            => writer.WriteAsync(entry.Timestamp, token);
        
        private static ValueTask<FlushResult> WriteLogEntrySnapshotMarker(PipeWriter writer, ref TEntry entry, CancellationToken token)
            => writer.WriteAsync(entry.IsSnapshot.ToByte(), token);
        
        private static async ValueTask<FlushResult> WriteLogEntryContent(PipeWriter writer, TEntry entry, CancellationToken token)
        {
            var canceled = false;
            try
            {
                await entry.WriteToAsync(writer, token).ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
                canceled = true;
            }
            return new FlushResult(canceled, false);
        }

        private static ValueTask<FlushResult> WriteLogEntryContent(PipeWriter writer, ref TEntry entry, CancellationToken token)
            => WriteLogEntryContent(writer, entry, token);

        private async Task WriteEntryAsync(CancellationToken token)
        {
            var writer = pipe.Writer;
            var entry = entries[currentIndex];
            foreach(var serializer in fragmentWriters)
            {
                var flushResult = await serializer(writer, ref entry, token).ConfigureAwait(false);
                if(flushResult.IsCompleted)
                    return;
                if(flushResult.IsCanceled)
                    break;
            }
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        
        private async Task NextEntryAsync(ReadOnlyMemory<byte> input, CancellationToken token)
        {
            currentIndex = ReadInt32LittleEndian(input.Span);
            if(writeSession != null)
            {
                AbortIO();
                await writeSession.ConfigureAwait(false);
                await pipe.Reader.CompleteAsync();
                pipe.Reset();
            }
            this.writeSession = WriteEntryAsync(token);
        }

        public override async ValueTask<bool> ProcessInboundMessageAsync(PacketHeaders headers, ReadOnlyMemory<byte> payload, EndPoint endpoint, CancellationToken token)
        {
            var control = (TransferControl)payload.Span[0];
            payload = payload.Slice(sizeof(TransferControl));
            switch(control)
            {
                default:
                    return false;
                case TransferControl.None:
                    FinalizeTransmission(payload.Span);
                    return false;
                case TransferControl.NextEntry:
                    streamStart = true;
                    await NextEntryAsync(payload, token).ConfigureAwait(false);
                    return true;
                case TransferControl.Continue:
                    streamStart = false;
                    return true;
                case TransferControl.Abort:
                    TrySetException(new IOException(ExceptionMessages.AbortedByRemoteHost));
                    return false;
            }
        }
    }
}