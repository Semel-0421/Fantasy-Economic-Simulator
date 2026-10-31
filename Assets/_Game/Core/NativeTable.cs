using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;

namespace FES.Core
{
    interface INativeColumn : IDisposable
    {
        int Length { get; }
        void RemoveAtSwapBack(int index);
        void Write(BinaryWriter writer);
        void Read(BinaryReader reader);
    }

    /// <summary>One growable column of a <see cref="NativeTable"/>. Owns its NativeList.</summary>
    public sealed class NativeColumn<T> : INativeColumn where T : unmanaged
    {
        NativeList<T> _list = new NativeList<T>(Allocator.Persistent);

        public int Length => _list.Length;

        public T this[int index]
        {
            get => _list[index];
            set => _list[index] = value;
        }

        public void Add(T value) => _list.Add(value);

        /// <summary>The column itself, for jobs that add or remove rows.</summary>
        public NativeList<T> List => _list;

        public void RemoveAtSwapBack(int index) => _list.RemoveAtSwapBack(index);

        /// <summary>View for jobs. Invalidated when the column grows.</summary>
        public NativeArray<T> AsArray() => _list.AsArray();

        public void Write(BinaryWriter writer) => NativeIO.WriteList(writer, _list);
        public void Read(BinaryReader reader) => NativeIO.ReadList(reader, _list);

        public void Dispose()
        {
            if (_list.IsCreated) _list.Dispose();
        }
    }

    /// <summary>
    /// Base for growable SoA tables (one row per entity). Declare columns with <see cref="Column{T}"/>;
    /// saving, loading and disposal cover every column automatically, so a new column cannot be forgotten.
    /// </summary>
    public abstract class NativeTable : IPersistentComponent, IDisposable
    {
        readonly List<INativeColumn> _columns = new List<INativeColumn>();

        public abstract string Key { get; }
        public abstract int Version { get; }

        public int Count => _columns.Count == 0 ? 0 : _columns[0].Length;

        protected NativeColumn<T> Column<T>() where T : unmanaged
        {
            var column = new NativeColumn<T>();
            _columns.Add(column);
            return column;
        }

        /// <summary>Removes a row by moving the last row into its place, so the last row's index changes.</summary>
        protected void RemoveRowSwapBack(int row)
        {
            foreach (var c in _columns) c.RemoveAtSwapBack(row);
        }

        public void Write(BinaryWriter writer)
        {
            foreach (var c in _columns) c.Write(writer);
        }

        public void Read(BinaryReader reader)
        {
            foreach (var c in _columns) c.Read(reader);
            OnRead();
        }

        /// <summary>Called after loading, for tables that keep a lookup over their rows.</summary>
        protected virtual void OnRead()
        {
        }

        public void Dispose()
        {
            foreach (var c in _columns) c.Dispose();
        }
    }
}
