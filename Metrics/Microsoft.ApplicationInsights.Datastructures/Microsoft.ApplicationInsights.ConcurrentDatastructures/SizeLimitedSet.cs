﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections;
using System.Threading;

namespace Microsoft.ApplicationInsights.ConcurrentDatastructures
{
    /// <summary>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SizeLimitedSet<T> : ICollection<T>, IReadOnlyCollection<T>
    {
        private readonly int _countLimit;
        private Data _data;

        /// <summary>
        /// </summary>
        /// <param name="countLimit"></param>
        public SizeLimitedSet(int countLimit)
        {
            if (countLimit < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(countLimit));
            }

            _countLimit = countLimit;
            _data = new Data();
        }

        /// <summary>
        /// </summary>
        public int CountLimit { get { return _countLimit; } }

        /// <summary>
        /// </summary>
        public int Count { get { return Math.Min(_data.Count, _countLimit); } }

        /// <summary>
        /// </summary>
        public bool IsReadOnly { get { return false; } }

        /// <summary>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool EnsureContains(T item)
        {
            bool isFull;
            TryAdd(item, _data, _countLimit, out isFull);

            return (isFull == false);
        }

        /// <summary>
        /// </summary>
        /// <param name="item"></param>
        /// <exception cref="InvalidOperationException">If <c>item</c> could not be added becasue it is already in this set or becasue this set is full.</exception>
        public void Add(T item)
        {
            bool isFull;
            bool canAdd = TryAdd(item, _data, _countLimit, out isFull);

            if (! canAdd)
            {
                if (isFull)
                {
                    throw new InvalidOperationException("Cannot add item becasue this set has reached its CountLimit.");
                }
                {
                    throw new InvalidOperationException("Cannot add item becasue it already exists in this set.");
                }
            }
        }

        /// <summary>
        /// </summary>
        public void Clear()
        {
            _data = new Data();
        }

        /// <summary>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(T item)
        {
            return _data.Items.ContainsKey(item);
        }

        /// <summary>
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayIndex"></param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            _data.Items.Keys.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IEnumerator<T> GetEnumerator()
        {
            return _data.Items.Keys.GetEnumerator();
        }

        /// <summary>
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Remove(T item)
        {
            Data data = _data;

            int v;
            if (data.Items.TryRemove(item, out v))
            {
                Interlocked.Decrement(ref data.Count);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private static bool TryAdd(T item, Data data, int countLimit, out bool isFull)
        {
            // Fast check: if the set is full, then give up fast:
            if (data.Count >= countLimit)
            {
                isFull = true;
                return false;
            }

            // Try adding item to the dictionary. Each addition has a unique version token.
            // If the version token generated by this call matches what is actually in the dictionary,
            // then we can be sure that:
            //  - the item was not already in the dictionary, and
            //  - if there was a race on adding the specified item, we won the race.
            // In other words, a match means that this racer just incresed the number of items in the dictionary by 1.
            // Thus, this racer is responsible for incrementing the Count, as well as for backing out of the
            // insertion of the item, if CountLimit was reached.
            // Note: in order to provide this lock-free implementation (except the lock inside of the dictionary) we accept
            // the race limitation that an item may be in the dictionary for a short time and will be removed right after.
            // During that transient period, iterating over Items may incorrectly include that item.
            // However, this is a very rare case that occurs only diring a race for increasing Count. Once Count is at limit,
            // the check at the beginning of this method will prevent issue from happening.

            int factoryVersion = 0;
            int storedVersion = data.Items.GetOrAdd(
                                                item,
                                                (_) =>
                                                {
                                                    factoryVersion = Interlocked.Increment(ref data.Version);
                                                    return factoryVersion;
                                                });

            bool itemAdded = (factoryVersion == storedVersion);
            if (itemAdded)
            {
                int newCount = Interlocked.Increment(ref data.Count);
                if (newCount == countLimit + 1)
                {
                    int v;
                    data.Items.TryRemove(item, out v);
                    Interlocked.Decrement(ref data.Count);

                    isFull = true;
                    return false;
                }
                else
                {
                    isFull = false;
                    return true;
                }
            }
            else
            {
                isFull = (data.Count >= countLimit);
                return false;
            }
        }

        #region class Data
        private class Data
        {
            internal volatile int Count;
            internal int Version;
            internal readonly ConcurrentDictionary<T, int> Items;
            public Data()
            {
                this.Count = 0;
                this.Version = 1;
                this.Items = new ConcurrentDictionary<T, int>();
            }
        }
        #endregion class Data
    }
}
