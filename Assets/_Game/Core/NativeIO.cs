using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace FES.Core
{
    /// <summary>
    /// Raw little-endian (de)serialization of blittable native containers. Both shipping targets
    /// (Android ARM64, Windows x64) are little-endian, so saves are portable between them.
    /// </summary>
    public static class NativeIO
    {
        public static void WriteArray<T>(BinaryWriter writer, NativeArray<T> array) where T : unmanaged
        {
            writer.Write(array.Length);
            if (array.Length > 0)
                writer.Write(array.Reinterpret<byte>(UnsafeUtility.SizeOf<T>()).AsReadOnlySpan());
        }

        /// <summary>Reads into an existing array whose length must match the saved length.</summary>
        public static void ReadArray<T>(BinaryReader reader, NativeArray<T> array) where T : unmanaged
        {
            int length = reader.ReadInt32();
            if (length != array.Length)
                throw new InvalidDataException($"Saved length {length} does not match current length {array.Length}.");
            ReadBody(reader, array);
        }

        public static void WriteList<T>(BinaryWriter writer, NativeList<T> list) where T : unmanaged =>
            WriteArray(writer, list.AsArray());

        /// <summary>Replaces the list contents with the saved elements.</summary>
        public static void ReadList<T>(BinaryReader reader, NativeList<T> list) where T : unmanaged
        {
            int length = reader.ReadInt32();
            if (length < 0) throw new InvalidDataException("Negative list length.");
            list.ResizeUninitialized(length);
            ReadBody(reader, list.AsArray());
        }

        /// <summary>Disposes the field in place (so IsCreated turns false) if it is allocated.</summary>
        public static void DisposeIfCreated<T>(ref this NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
        }

        /// <summary>Disposes the field in place (so IsCreated turns false) if it is allocated.</summary>
        public static void DisposeIfCreated<T>(ref this NativeList<T> list) where T : unmanaged
        {
            if (list.IsCreated) list.Dispose();
        }

        static void ReadBody<T>(BinaryReader reader, NativeArray<T> array) where T : unmanaged
        {
            if (array.Length == 0) return;
            Span<byte> destination = array.Reinterpret<byte>(UnsafeUtility.SizeOf<T>()).AsSpan();
            while (destination.Length > 0)
            {
                int read = reader.Read(destination);
                if (read <= 0) throw new EndOfStreamException();
                destination = destination.Slice(read);
            }
        }
    }
}
