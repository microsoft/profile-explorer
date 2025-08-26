// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace ProfileExplorerCore.Profile.Utils;

public class NamedPipeServer : IDisposable {
  public delegate void MessageReceivedDelegate(PipeMessageHeader messageHeader, byte[] messageBody);
  private static readonly int HeaderSize = Marshal.SizeOf<PipeMessageHeader>();
  private NamedPipeServerStream pipeStream_;
  private BinaryReader reader_;
  private BinaryWriter writer_;
  private bool connected_;
  private bool connectionTimeout_;

  public NamedPipeServer(string pipeName) {
    // Allow non-admins to connect to the pipe.
    var securityIdentifier = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
    var pipeSecurity = new PipeSecurity();
    pipeSecurity.AddAccessRule(new PipeAccessRule(securityIdentifier,
                                                  PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                                                  AccessControlType.Allow));

    pipeStream_ = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut,
                                                  NamedPipeServerStream.MaxAllowedServerInstances,
                                                  PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                                                  0, 0, pipeSecurity);
  }

  public void Dispose() {
    if (connected_) {
      reader_?.Dispose();
      writer_?.Dispose();

      if (pipeStream_.IsConnected) {
        pipeStream_.Disconnect();
      }
    }

    pipeStream_.Dispose();
  }

  public void ReceiveMessages(MessageReceivedDelegate action, CancellationToken cancellationToken) {
    if (!Connect()) {
      throw new Exception("Failed to connect to pipe");
    }

    byte[] buffer = new byte[HeaderSize];

    while (pipeStream_.IsConnected && !cancellationToken.IsCancellationRequested) {
      int bytesRead = reader_.Read(buffer);

      if (bytesRead == 0) {
        break;
      }

      if (bytesRead != HeaderSize) {
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

    int dataSize = Marshal.SizeOf<T>();
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
    var header = new PipeMessageHeader {Kind = kind, Size = HeaderSize + bodySize};

    var headerSpan = MemoryMarshal.Cast<PipeMessageHeader, byte>(MemoryMarshal.CreateSpan(ref header, 1));
    writer_.Write(headerSpan);
  }

  private bool Connect(int timeoutMs = 10000) {
    if (connected_) {
      return true;
    }

    if (connectionTimeout_) {
      return false; // Failed to connect previously, don't try again.
    }

    lock (this) {
      if (connected_) {
        return true;
      }

      try {
        // Wait for a connection for a certain amount of time.
        var result = pipeStream_.BeginWaitForConnection(e => { }, null);

        if (result.AsyncWaitHandle.WaitOne(timeoutMs)) {
          Trace.WriteLine("Connected to pipe");
          pipeStream_.EndWaitForConnection(result);
          reader_ = new BinaryReader(pipeStream_);
          writer_ = new BinaryWriter(pipeStream_);
          connected_ = true;
        }
        else {
          Trace.WriteLine("Pipe connection timeout");
          connected_ = false;
          connectionTimeout_ = true;
        }
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to connect to pipe: {ex}");
        connected_ = false;
      }
    }

    return connected_;
  }

  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  public struct PipeMessageHeader {
    public int Kind;
    public int Size; // sizeof(PipeMessageHeader) + additional data

    public override string ToString() {
      return $"Message Kind: {Kind}, Size: {Size}";
    }
  }
}