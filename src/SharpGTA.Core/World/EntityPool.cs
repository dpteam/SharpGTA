using System;

namespace SharpGTA.World
{
    /// <summary>
    /// Fixed-capacity entity pool with the original CPool handle encoding: the high bits
    /// are the slot index and the low byte is a 7-bit generation id plus a free flag.
    /// A handle whose generation no longer matches the slot resolves to null, so a stale
    /// reference can never silently address a recycled entity.
    /// </summary>
    public sealed class EntityPool<T> where T : class
    {
        private const int GenerationMask = 0x7F;
        private const byte FreeFlag = 0x80;

        private readonly T[] entries;
        private readonly byte[] flags;
        private int allocPtr;
        private int usedSpace;

        public EntityPool(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException("capacity");
            entries = new T[capacity];
            flags = new byte[capacity];
            for (int i = 0; i < capacity; i++)
                flags[i] = FreeFlag;
        }

        public int Capacity { get { return entries.Length; } }
        public int UsedSpace { get { return usedSpace; } }
        public int FreeSpace { get { return entries.Length - UsedSpace; } }

        /// <summary>
        /// Reserves the next free slot in the original wrap-around order, stores the
        /// entry and returns its handle, or -1 when the pool is full. Slot search starts
        /// after the current allocation cursor, exactly like CPool::New.
        /// </summary>
        public int New(T entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");
            if (usedSpace >= entries.Length)
                return -1;

            bool wrapped = false;
            do
            {
                if (++allocPtr >= entries.Length)
                {
                    allocPtr = 0;
                    if (wrapped)
                        return -1;
                    wrapped = true;
                }
            }
            while (!GetIsFree(allocPtr));

            SetIsFree(allocPtr, false);
            SetId(allocPtr, GetId(allocPtr) + 1);
            entries[allocPtr] = entry;
            usedSpace++;
            return flags[allocPtr] + (allocPtr << 8);
        }

        /// <summary>Resolves a handle; returns null for -1, foreign or stale handles.</summary>
        public T GetAt(int handle)
        {
            if (handle == -1)
                return null;
            int index = handle >> 8;
            if (index < 0 || index >= entries.Length)
                return null;
            return flags[index] == (byte)(handle & 0xFF) ? entries[index] : null;
        }

        /// <summary>Resolves a slot only while it is in use.</summary>
        public T GetSlot(int index)
        {
            if (index < 0 || index >= entries.Length)
                return null;
            return GetIsFree(index) ? null : entries[index];
        }

        /// <summary>The current handle of an in-use entry, or -1 when it is not in this pool.</summary>
        public int GetIndex(T entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");
            int index = Array.IndexOf(entries, entry);
            if (index < 0 || GetIsFree(index))
                return -1;
            return flags[index] + (index << 8);
        }

        /// <summary>Frees the slot addressed by the handle and rewinds the allocation cursor.</summary>
        public bool Delete(int handle)
        {
            int index = handle >> 8;
            if (index < 0 || index >= entries.Length)
                return false;
            if (flags[index] != (byte)(handle & 0xFF) || GetIsFree(index))
                return false;
            SetIsFree(index, true);
            entries[index] = null;
            usedSpace--;
            if (index < allocPtr)
                allocPtr = index;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = null;
                flags[i] = FreeFlag;
            }
            allocPtr = 0;
            usedSpace = 0;
        }

        private bool GetIsFree(int index)
        {
            return (flags[index] & FreeFlag) != 0;
        }

        private int GetId(int index)
        {
            return flags[index] & GenerationMask;
        }

        private void SetId(int index, int id)
        {
            flags[index] = (byte)((flags[index] & FreeFlag) | (id & GenerationMask));
        }

        private void SetIsFree(int index, bool isFree)
        {
            if (isFree)
                flags[index] |= FreeFlag;
            else
                flags[index] &= unchecked((byte)~FreeFlag);
        }

        private int GetNoOfUsedSpaces()
        {
            int used = 0;
            for (int i = 0; i < flags.Length; i++)
            {
                if ((flags[i] & FreeFlag) == 0)
                    used++;
            }
            return used;
        }

        /// <summary>Debug helper: the counter must always match a full scan.</summary>
        internal void VerifyUsedSpace()
        {
            if (GetNoOfUsedSpaces() != usedSpace)
                throw new InvalidOperationException("Entity pool used-space counter drifted.");
        }
    }
}