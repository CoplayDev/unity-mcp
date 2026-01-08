using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Validator.Services.Validation
{
    internal class FileSignatureUtilityService : IFileSignatureUtilityService
    {
        private class FileSignature
        {
            public byte[] SignatureBytes;
            public int Offset;

            public FileSignature(byte[] signatureBytes, int offset)
            {
                SignatureBytes = signatureBytes;
                Offset = offset;
            }
        }

        private static readonly Dictionary<FileSignature, ArchiveType> ArchiveSignatures = new Dictionary<FileSignature, ArchiveType>
        {
            { new FileSignature(new byte[] { 0x1f, 0x8b }, 0), ArchiveType.TarGz },
            { new FileSignature(new byte[] { 0x50, 0x4b, 0x03, 0x04 }, 0), ArchiveType.Zip },
            { new FileSignature(new byte[] { 0x50, 0x4b, 0x05, 0x06 }, 0), ArchiveType.Zip }, // Empty Zip Archive
            { new FileSignature(new byte[] { 0x50, 0x4b, 0x07, 0x08 }, 0), ArchiveType.Zip }, // Spanned Zip Archive

            { new FileSignature(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00 }, 0), ArchiveType.Rar }, // RaR v1.50+
            { new FileSignature(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00 }, 0), ArchiveType.Rar }, // RaR v5.00+
            { new FileSignature(new byte[] { 0x75, 0x73, 0x74, 0x61, 0x72, 0x00, 0x30, 0x30 }, 257), ArchiveType.Tar },
            { new FileSignature(new byte[] { 0x75, 0x73, 0x74, 0x61, 0x72, 0x20, 0x20, 0x00 }, 257), ArchiveType.Tar },
            { new FileSignature(new byte[] { 0x1f, 0x9d }, 0), ArchiveType.TarZip }, // TarZip LZW algorithm
            { new FileSignature(new byte[] { 0x1f, 0xa0 }, 0), ArchiveType.TarZip }, // TarZip LZH algorithm
            { new FileSignature(new byte[] { 0x42, 0x5a, 0x68 }, 0), ArchiveType.Bz2 },
            { new FileSignature(new byte[] { 0x4c, 0x5a, 0x49, 0x50 }, 0), ArchiveType.LZip },
            { new FileSignature(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }, 0), ArchiveType.SevenZip },
            { new FileSignature(new byte[] { 0x1f, 0x8b }, 0), ArchiveType.GZip },
            { new FileSignature(new byte[] { 0x52, 0x53, 0x56, 0x4b, 0x44, 0x41, 0x54, 0x41 }, 0), ArchiveType.QuickZip },
            { new FileSignature(new byte[] { 0xfd, 0x37, 0x7a, 0x58, 0x5a, 0x00 }, 0), ArchiveType.Xz },
            { new FileSignature(new byte[] { 0x4D, 0x53, 0x57, 0x49, 0x4D, 0x00, 0x00, 0x00, 0xD0, 0x00, 0x00, 0x00, 0x00 }, 0), ArchiveType.Wim }
        };

        public ArchiveType GetArchiveType(string filePath)
        {
            if (!File.Exists(filePath))
                return ArchiveType.None;

            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    foreach (var kvp in ArchiveSignatures)
                    {
                        var fileSignature = kvp.Key;
                        var archiveType = kvp.Value;

                        if (stream.Length < fileSignature.SignatureBytes.Length)
                            continue;

                        var bytes = new byte[fileSignature.SignatureBytes.Length];
                        stream.Seek(fileSignature.Offset, SeekOrigin.Begin);
                        stream.Read(bytes, 0, bytes.Length);

                        if (fileSignature.SignatureBytes.SequenceEqual(bytes.Take(fileSignature.SignatureBytes.Length)))
                            return archiveType;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                Debug.LogWarning($"File '{filePath}' exists, but could not be opened for reading. Please make sure the project path lengths are not too long for the Operating System");
            }

            return ArchiveType.None;
        }
    }
}