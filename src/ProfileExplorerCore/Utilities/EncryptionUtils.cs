﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Security.Cryptography;

namespace ProfileExplorer.Core;

public static class EncryptionUtils {
  public const int BlockBitSize = 128;
  public const int KeyBitSize = 256;

  public static byte[] Encrypt(byte[] data, byte[] key) {
    using var aes = new AesManaged {
      KeySize = KeyBitSize,
      BlockSize = BlockBitSize,
      Mode = CipherMode.CBC,
      Padding = PaddingMode.PKCS7
    };

    // Use random IV.
    aes.GenerateIV();
    byte[] iv = aes.IV;

    using var encrypter = aes.CreateEncryptor(key, iv);
    using var cipherStream = new MemoryStream();

    using (var cryptoStream = new CryptoStream(cipherStream, encrypter, CryptoStreamMode.Write)) {
      using var binaryWriter = new BinaryWriter(cryptoStream);
      binaryWriter.Write(data);
    }

    byte[] cipherText = cipherStream.ToArray();

    using var encryptedStream = new MemoryStream();

    using (var binaryWriter = new BinaryWriter(encryptedStream)) {
      binaryWriter.Write(iv);
      binaryWriter.Write(cipherText);
    }

    return encryptedStream.ToArray();
  }

  public static byte[] Decrypt(byte[] data, byte[] key) {
    using var aes = new AesManaged {
      KeySize = KeyBitSize,
      BlockSize = BlockBitSize,
      Mode = CipherMode.CBC,
      Padding = PaddingMode.PKCS7
    };

    // Get IV from front of message.
    int ivLength = BlockBitSize / 8;
    byte[] iv = new byte[ivLength];
    Array.Copy(data, 0, iv, 0, ivLength);

    using var decrypter = aes.CreateDecryptor(key, iv);
    using var plainTextStream = new MemoryStream();

    using (var decrypterStream = new CryptoStream(plainTextStream, decrypter, CryptoStreamMode.Write)) {
      using var binaryWriter = new BinaryWriter(decrypterStream);

      // Decrypt cipher text from message.
      binaryWriter.Write(data, ivLength, data.Length - ivLength);
    }

    return plainTextStream.ToArray();
  }

  public static byte[] CreateNewKey() {
    byte[] key = new byte[KeyBitSize / 8];
    var random = new RNGCryptoServiceProvider();
    random.GetNonZeroBytes(key);
    return key;
  }
}