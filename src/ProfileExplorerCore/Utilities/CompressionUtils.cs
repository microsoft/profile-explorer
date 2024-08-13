// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfileExplorer.Core;

public static class CompressionUtils {
  public static byte[] Compress(byte[] data, CompressionLevel level = CompressionLevel.Fastest) {
    //? TODO: Mapping of compression level
    // https://paulcalvano.com/index.php/2018/07/25/brotli-compression-how-much-will-it-reduce-your-content/
    //level = (CompressionLevel)3;
    using var uncompressedStream = new MemoryStream(data);
    using var compressedStream = new MemoryStream();
    using var compressorStream = new BrotliStream(compressedStream, level, true);
    compressorStream.Write(data, 0, data.Length);
    compressorStream.Close();
    return compressedStream.ToArray();
  }

  public static byte[] CompressString(string text, CompressionLevel level = CompressionLevel.Fastest) {
    return Compress(Encoding.UTF8.GetBytes(text), level);
  }

  public static byte[] Decompress(byte[] data) {
    var compressedStream = new MemoryStream(data);
    using var decompressorStream = new BrotliStream(compressedStream, CompressionMode.Decompress);

    // The compression ratio is about 5x, preallocate this much.
    using var decompressedStream = new MemoryStream(data.Length * 5);
    decompressorStream.CopyTo(decompressedStream);
    byte[] decompressedBytes = decompressedStream.ToArray();
    return decompressedBytes;
  }

  public static string DecompressString(byte[] data) {
    return Encoding.UTF8.GetString(Decompress(data));
  }

  public static byte[] CreateMD5(byte[] data) {
    using var md5 = MD5.Create();
    return md5.ComputeHash(data);
  }

  public static string CreateMD5String(byte[] data) {
    byte[] hash = CreateMD5(data);
    return BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal).
      ToLowerInvariant();
  }

  public static string CreateMD5String(string text) {
    return CreateMD5String(Encoding.UTF8.GetBytes(text));
  }

  public static byte[] CreateSHA256(byte[] data) {
    using var sha = SHA256.Create();
    return sha.ComputeHash(data);
  }

  public static byte[] CreateSHA256(string text) {
    return CreateSHA256(Encoding.UTF8.GetBytes(text));
  }

  public static byte[] CreateSHA256(List<byte[]> byteList) {
    int outputSize = 0;

    foreach (byte[] item in byteList) {
      outputSize += item.Length + 1;
    }

    byte[] output = new byte[outputSize];
    int position = 0;

    foreach (byte[] item in byteList) {
      Array.Copy(item, 0, output, position, item.Length);
      position += item.Length;
      output[position] = 0xFF; // Use a separator so that "ab cde" and "abc de" are distinct.
      position++;
    }

    using var sha = SHA256.Create();
    return sha.ComputeHash(output);
  }
}

public class CompressedString : IEquatable<CompressedString> {
  private byte[] data_;
  private int hash_;

  public CompressedString(ReadOnlySpan<char> span, CompressionLevel level = CompressionLevel.Fastest) {
    data_ = CompressionUtils.Compress(Encoding.UTF8.GetBytes(span.ToArray()), level);
    hash_ = 0;
  }

  public CompressedString(string value, CompressionLevel level = CompressionLevel.Fastest) {
    data_ = CompressionUtils.Compress(Encoding.UTF8.GetBytes(value), level);
    hash_ = data_.GetHashCode();
  }

  public int Size => data_.Length;
  public byte[] UniqueId => CompressionUtils.CreateSHA256(data_);

  public static bool operator ==(CompressedString left, CompressedString right) {
    return left.Equals(right);
  }

  public static bool operator !=(CompressedString left, CompressedString right) {
    return !(left == right);
  }

  public override bool Equals(object obj) {
    return obj is CompressedString other &&
           GetHashCode() == other.GetHashCode() &&
           data_.Equals(other.data_);
  }

  public override int GetHashCode() {
    if (hash_ == 0) {
      hash_ = data_.GetHashCode();
    }

    return hash_;
  }

  public override string ToString() {
    return CompressionUtils.DecompressString(data_);
  }

  public bool Equals(CompressedString other) {
    return Equals((object)other);
  }
}

public class CompressedObject<T> where T : class {
  private byte[] data_;
  private T value_;
  private object lockObject_;

  public CompressedObject(T value) {
    value_ = value;
    lockObject_ = new object();
  }

  //? TODO: public async Task CompressAsync

  public void Compress() {
    lock (lockObject_) {
      if (value_ == null) {
        return; // Compressed already.
      }

      using var stream = new MemoryStream();
      JsonSerializer.Serialize(stream, value_);
      byte[] serializedData = stream.ToArray();
      data_ = CompressionUtils.Compress(serializedData);
      value_ = null;
    }
  }

  public T GetValue() {
    lock (lockObject_) {
      if (value_ != null) {
        return value_;
      }

      // Decompress on-demand.
      byte[] serializedData = CompressionUtils.Decompress(data_);
      using var stream = new MemoryStream(serializedData);
      value_ = JsonSerializer.Deserialize<T>(stream);
      data_ = null;
      return value_;
    }
  }
}