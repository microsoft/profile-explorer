// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Client {
    public static class CompressionUtils {
        public static byte[] Compress(byte[] data, CompressionLevel level = CompressionLevel.Fastest) {
            using (var uncompressedStream = new MemoryStream(data)) {
                using (var compressedStream = new MemoryStream()) {
                    using (var compressorStream = new BrotliStream(compressedStream, level, true)) {
                        uncompressedStream.CopyTo(compressorStream);
                    }

                    return compressedStream.ToArray();
                }
            }
        }

        public static byte[] CompressString(string text, CompressionLevel level = CompressionLevel.Fastest) {
            return Compress(Encoding.UTF8.GetBytes(text), level);
        }

        public static byte[] Decompress(byte[] data) {
            byte[] decompressedBytes;
            var compressedStream = new MemoryStream(data);

            using (var decompressorStream = new BrotliStream(compressedStream, CompressionMode.Decompress)) {
                // The compression ratio is about 4x, preallocate this much.
                using (var decompressedStream = new MemoryStream(data.Length * 4)) {
                    decompressorStream.CopyTo(decompressedStream);
                    decompressedBytes = decompressedStream.ToArray();
                }
            }

            return decompressedBytes;
        }

        public static string DecompressString(byte[] data) {
            return Encoding.UTF8.GetString(Decompress(data));
        }

        public static byte[] CreateMD5(byte[] data) {
            using (var md5 = System.Security.Cryptography.MD5.Create()) {
                return md5.ComputeHash(data);
            }
        }

        public static string CreateMD5String(byte[] data) {
            var hash = CreateMD5(data);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
        }
    }
}
