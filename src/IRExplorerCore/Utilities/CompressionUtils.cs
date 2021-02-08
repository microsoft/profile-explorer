// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace IRExplorerCore {
    public static class CompressionUtils {
        public static byte[] Compress(byte[] data, CompressionLevel level = CompressionLevel.Fastest) {
            //? TODO: Mapping of compression level
            // https://paulcalvano.com/index.php/2018/07/25/brotli-compression-how-much-will-it-reduce-your-content/
            level = (CompressionLevel)3;
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
            var hash = CreateMD5(data);
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
    }

    public struct CompressedString : IEquatable<CompressedString> {
        private byte[] data_;
        private int hash_;

        public CompressedString(ReadOnlySpan<char> span) {
            data_ = CompressionUtils.Compress(Encoding.UTF8.GetBytes(span.ToArray()));
            hash_ = data_.GetHashCode();
        }

        public CompressedString(string value) {
            data_ = CompressionUtils.Compress(Encoding.UTF8.GetBytes(value));
            hash_ = data_.GetHashCode();
        }

        public byte[] GetUniqueId() {
            return CompressionUtils.CreateSHA256(data_);
        }

        public override bool Equals(object obj) {
            return obj is CompressedString other &&
                hash_ == other.hash_ &&
                data_.Equals(other.data_);
        }

        public override int GetHashCode() {
            return hash_;
        }

        public override string ToString() {
            return CompressionUtils.DecompressString(data_);
        }

        public static bool operator ==(CompressedString left, CompressedString right) {
            return left.Equals(right);
        }

        public static bool operator !=(CompressedString left, CompressedString right) {
            return !(left == right);
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
                var serializer = new BinaryFormatter();

                serializer.Serialize(stream, value_);
                var serializedData = stream.ToArray();
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
                var serializedData = CompressionUtils.Decompress(data_);
                using var stream = new MemoryStream(serializedData);
                var serializer = new BinaryFormatter();

                value_ = (T)serializer.Deserialize(stream);
                data_ = null;
                return value_;
            }
        }
    }
}
