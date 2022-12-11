using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;

namespace IRExplorerUI.Profile;

public class NamedPipeServer : IDisposable {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PipeMessageHeader {
        public int Kind;
        public int Size; // sizeof(PipeMessageHeader) + additional data

        public override string ToString() =>
            $"Message Kind: {Kind}, Size: {Size}";
    }

    private NamedPipeServerStream stream_;
    private BinaryReader reader_;
    private BinaryWriter writer_;
    private bool connected_;
    private static readonly int HeaderSize = Marshal.SizeOf<PipeMessageHeader>();

    public delegate void MessageReceivedDelegate(PipeMessageHeader messageHeader, byte[] messageBody);

    public NamedPipeServer(string pipeName) {
        stream_ = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                            PipeOptions.Asynchronous);
    }

    public void ReceiveMessages(MessageReceivedDelegate action, CancellationToken cancellationToken) {
        if (!Connect()) {
            throw new Exception("Failed to connect to pipe");
        }

        var buffer = new byte[HeaderSize];

        while (stream_.IsConnected && !cancellationToken.IsCancellationRequested) {
            var bytesRead = reader_.Read(buffer);

            if (bytesRead == 0) {
                break;
            }
            else if (bytesRead != HeaderSize) {
                throw new Exception($"Invalid message header, read {bytesRead} vs expected {HeaderSize}");
            }

            var header = MemoryMarshal.Cast<byte, PipeMessageHeader>(buffer)[0];
            byte[] bodyBuffer = null;

            if (header.Size > HeaderSize) {
                int messageSize = header.Size - HeaderSize;
                bodyBuffer = new byte[messageSize];
                bytesRead = reader_.Read(bodyBuffer);

                if (bytesRead != messageSize) {
                    throw new Exception($"Invalid message body, read {bytesRead} vs expected {messageSize}");
                }
            }

            action(header, bodyBuffer);
        }
    }

    public void SendMessage(int kind) {
        if (!Connect()) {
            throw new Exception("Failed to connect to pipe");
        }

        SendMessageHeader(kind, 0);
    }

    public void SendMessage<T>(int kind, T data) where T : struct {
        if (!Connect()) {
            throw new Exception("Failed to connect to pipe");
        }

        var dataSize = Marshal.SizeOf<T>();
        SendMessageHeader(kind, dataSize);

        if (dataSize > 0) {
            var bodySpan = MemoryMarshal.Cast<T, byte>(MemoryMarshal.CreateSpan(ref data, 1));
            writer_.Write(bodySpan);
        }
    }

    public void Flush() {
        writer_.Flush();
    }

    private void SendMessageHeader(int kind, int bodySize) {
        var header = new PipeMessageHeader { Kind = kind, Size = HeaderSize + bodySize };

        var headerSpan = MemoryMarshal.Cast<PipeMessageHeader, byte>(MemoryMarshal.CreateSpan(ref header, 1));
        writer_.Write(headerSpan);
    }

    private bool Connect() {
        if (connected_) {
            return true;
        }

        lock (this) {
            if (connected_) {
                return true;
            }

            stream_.WaitForConnection();
            reader_ = new BinaryReader(stream_);
            writer_ = new BinaryWriter(stream_);
            connected_ = true;
        }

        return connected_;
    }

    public void Dispose() {
        if (connected_) {
            reader_?.Dispose();
            writer_?.Dispose();

            if (stream_.IsConnected) {
                stream_.Disconnect();
            }
        }

        stream_.Dispose();
    }
}

public class ProfilerNamedPipeServer : IDisposable {
    enum MessageKind {
        StartSession,
        EndSession,
        FunctionCode,
        FunctionCallTarget,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct FunctionCodeMessage {
        public long FunctionId; // 0
        public long Address; // 8
        public int ReJITId; // 16
        public int ProcessId; // 20
        public int CodeSize; // 24
        // Code bytes start here at offset 28.
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct FunctionCallTargetMessage {
        public long FunctionId; // 0
        public long Address; // 8
        public int ReJITId; // 16
        public int ProcessId; // 20
        public int NameLength; // 20
        // UTF-8 name bytes start here at offset 28.
    }

    public delegate void FunctionCodeReceivedDelegate(long functionId, int rejitId, int processId, long address,
        int codeSize, byte[] codeBytes);

    public delegate void FunctionCallTargetsReceivedDelegate(long functionId, int rejitId, int processId, long address, string name);

    public const string PipeName = "IRXProfilerPipe";

    private NamedPipeServer instance_;

    public ProfilerNamedPipeServer() {
        instance_ = new NamedPipeServer(PipeName);
    }

    public event FunctionCodeReceivedDelegate FunctionCodeReceived;
    public event FunctionCallTargetsReceivedDelegate FunctionCallTargetsReceived;

    public bool Start(CancellationToken cancellationToken) {
        try {
            instance_.ReceiveMessages((header, body) => {
                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                switch ((MessageKind)header.Kind) {
                    case MessageKind.FunctionCode: {
                        var message = MemoryMarshal.Cast<byte, FunctionCodeMessage>(body)[0];
                        var code = body.AsSpan().Slice(28, message.CodeSize).ToArray();
                        FunctionCodeReceived?.Invoke(message.FunctionId, message.ReJITId, message.ProcessId,
                                                     message.Address, message.CodeSize, code);
                        break;
                    }
                    case MessageKind.FunctionCallTarget: {
                        var message = MemoryMarshal.Cast<byte, FunctionCallTargetMessage>(body)[0];
                        var nameBytes = body.AsSpan().Slice(28, message.NameLength).ToArray();
                        var name = Encoding.UTF8.GetString(nameBytes);
                        FunctionCallTargetsReceived?.Invoke(message.FunctionId, message.ReJITId, message.ProcessId,
                                                            message.Address, name);
                        break;
                    }
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) {
            Trace.WriteLine("Failed to receive messages: " + ex);
            return false;
        }
    }

    public void Stop() {
        instance_.Dispose();
        instance_ = null;
    }

    public void Dispose() {
        if (instance_ != null) {
            Stop();
        }
    }
}