﻿using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Arch.LowLevel.Jagged;

/// <summary>
///     The <see cref="Bucket{T}"/> struct
///     represents a bucket of the <see cref="JaggedArray{T}"/> where items are stored
/// </summary>
/// <typeparam name="T"></typeparam>
internal record struct UnsafeBucket<T> : IDisposable where T : unmanaged
{
    /// <summary>
    ///     The items array.
    /// </summary>
    internal UnsafeArray<T> Array = UnsafeArray.Empty<T>();
    
    /// <summary>
    ///     Creates an instance of the <see cref="Bucket{T}"/>.
    /// </summary>
    /// <param name="capacity">The capacity</param>
    public UnsafeBucket(int capacity)
    {
        Array = new UnsafeArray<T>(capacity);
    }
    
    /// <summary>
    ///     The amount of items in this <see cref="Bucket{T}"/>.
    /// </summary>
    internal int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set;
    }

    /// <summary>
    ///     If this <see cref="Bucket{T}"/> is empty.
    /// </summary>
    internal bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Count <= 0;
    }
    
    /// <summary>
    ///     Returns a reference to an item at the given index.
    /// </summary>
    /// <param name="i">The index.</param>
    public ref T this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Array[i];
    }

    /// <summary>
    ///     Disposes this <see cref="UnsafeBucket{T}"/>. 
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        Array.Dispose();
    }
}

/// <summary>
///     The <see cref="UnsafeJaggedArray{T}"/> class,
///     represents a jagged array with <see cref="UnsafeBucket{T}"/>s storing the items.
/// </summary>
/// <typeparam name="T"></typeparam>
public struct UnsafeJaggedArray<T> : IDisposable where T : unmanaged
{
    /// <summary>
    ///     The <see cref="Bucket{T}"/> size in items.
    /// </summary>
    private readonly int _bucketSize;

    /// <summary>
    ///     The <see cref="Bucket{T}"/> size in items - 1.
    /// </summary>
    private readonly int _bucketSizeMinusOne;

    /// <summary>
    ///     The allocated <see cref="Bucket{T}"/>s.
    /// </summary>
    private UnsafeArray<UnsafeBucket<T>> _bucketArray;

    /// <summary>
    ///     The filler, the default value.
    /// </summary>
    private readonly T _filler;

    /// <summary>
    ///     Creates an instance of the <see cref="JaggedArray{T}"/>.
    /// </summary>
    /// <param name="bucketSize">The <see cref="Bucket{T}"/> size in bytes.</param>
    /// <param name="capacity">The total initial capacity, how many items should fit in.</param>
    public UnsafeJaggedArray(int bucketSize, int capacity = 64)
    {
        _bucketSize = MathExtensions.RoundToPowerOfTwo(bucketSize);
        _bucketSizeMinusOne = _bucketSize - 1;
        _bucketArray = new UnsafeArray<UnsafeBucket<T>>(capacity / bucketSize + 1);

        _filler = default!;
        
        // Fill buckets
        for (var i = 0; i < _bucketArray.Length; i++)
        {
            var bucket = new UnsafeBucket<T>(_bucketSize);
            _bucketArray[i] = bucket;
            UnsafeArray.Fill(ref bucket.Array, _filler);
        }
    }

    /// <summary>
    ///     Creates an instance of the <see cref="JaggedArray{T}"/>.
    /// </summary>
    /// <param name="bucketSize">The <see cref="Bucket{T}"/> size in bytes.</param>
    /// <param name="filler">The filler value for all slots, basically a custom default-value.</param>
    /// <param name="capacity">The total initial capacity, how many items should fit in.</param>
    public UnsafeJaggedArray(int bucketSize, T filler, int capacity = 64)
    {
        _bucketSize = MathExtensions.RoundToPowerOfTwo(bucketSize);
        _bucketSizeMinusOne = _bucketSize - 1;
        _bucketArray = new UnsafeArray<UnsafeBucket<T>>(capacity / bucketSize + 1);

        _filler = filler;
        
        // Fill buckets
        for (var i = 0; i < _bucketArray.Length; i++)
        {
            var bucket = new UnsafeBucket<T>(_bucketSize);
            _bucketArray[i] = bucket;
            UnsafeArray.Fill(ref bucket.Array, _filler);
        }
    }

    /// <summary>
    ///     The capacity, the total amount of items. 
    /// </summary>
    public int Capacity => _bucketArray.Length * _bucketSize;

    /// <summary>
    ///     The length, the buckets inside the <see cref="_bucketArray"/>.
    /// </summary>
    public int Buckets => _bucketArray.Length;

    /// <summary>
    ///     Adds an item to the <see cref="JaggedArray{T}"/>.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="item">The item.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int index, in T item)
    {
        IdToSlot(index, out var outerIndex, out var innerIndex);

        ref var bucket = ref _bucketArray[outerIndex];
        bucket[innerIndex] = item;
        bucket.Count++;
    }

    /// <summary>
    ///     Removes an item from the <see cref="JaggedArray{T}"/>.
    /// </summary>
    /// <param name="index">The index.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(int index)
    {
        IdToSlot(index, out var outerIndex, out var innerIndex);

        ref var bucket = ref _bucketArray[outerIndex];
        bucket[innerIndex] = _filler;
        bucket.Count--;
    }

    /// <summary>
    ///     Trys to get an item from its index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="value">The returned value.</param>
    /// <returns>True if sucessfull, otherwhise false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(int index, out T value)
    {
        // If the id is negative
        if (index < 0)
        {
            value = _filler;
            return false;
        }

        IdToSlot(index, out var outerIndex, out var innerIndex);

        // If the item is outside the array. Then it definetly doesn't exist
        if (outerIndex > _bucketArray.Length)
        {
            value = _filler;
            return false;
        }

        ref var item = ref _bucketArray[outerIndex][innerIndex];

        // If the item is the default then the nobody set its value.
        if (EqualityComparer<T>.Default.Equals(item, _filler))
        {
            value = _filler;
            return false;
        }

        value = item;
        return true;
    }

    /// <summary>
    ///     Ensures the capacity and increases it if necessary.
    /// </summary>
    /// <param name="newCapacity">The new capcity.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCapacity(int newCapacity)
    {
        if (newCapacity < Capacity)
        {
            return;
        }

        var length = Buckets;
        var buckets = newCapacity / _bucketSize + 1;
        _bucketArray = UnsafeArray.Resize(ref _bucketArray, buckets);

        for (var i = length; i < _bucketArray.Length; i++)
        {
            var bucket = new UnsafeBucket<T>(_bucketSize);
            _bucketArray[i] = bucket;
            UnsafeArray.Fill(ref bucket.Array, _filler);
        }
    }

    /// <summary>
    ///     Trims the last few empty buckets to release memory.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TrimExcess()
    {
        // Count how many of the last buckets are empty, to trim them
        var count = 0;
        for (var i = _bucketArray.Length - 1; i >= 0; i--)
        {
            ref var bucket = ref _bucketArray[i];
            if (!bucket.IsEmpty)
            {
                break;
            }

            count++;
        }

        var buckets = _bucketArray.Length - count;
        _bucketArray = UnsafeArray.Resize(ref _bucketArray, buckets);
    }

    /// <summary>
    ///     Converts the passed id to its inner and outer index ( or slot ) inside the <see cref="_items"/> array.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="outerIndex">The outer index.</param>
    /// <param name="innerIndex">The inner index.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IdToSlot(int id, out int outerIndex, out int innerIndex)
    {
        Debug.Assert(id >= 0, "Id cannot be negative.");

        /* Instead of the '%' operator we can use logical '&' operator which is faster. But it requires the bucket size to be a power of 2. */
        outerIndex = id / _bucketSize;
        innerIndex = id & _bucketSizeMinusOne;
    }

    /// <summary>
    ///     Returns a reference to an item at the given index.
    /// </summary>
    /// <param name="i">The index.</param>
    public ref T this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            IdToSlot(i, out var outerIndex, out var innerIndex);
            return ref _bucketArray[outerIndex][innerIndex];
        }
    }

    /// <summary>
    ///     Disposes this <see cref="UnsafeJaggedArray{T}"/>.
    /// </summary>
    public void Dispose()
    {
        foreach (ref var bucket in _bucketArray)
        {
            bucket.Dispose();
        }
        _bucketArray.Dispose();
    }
}
