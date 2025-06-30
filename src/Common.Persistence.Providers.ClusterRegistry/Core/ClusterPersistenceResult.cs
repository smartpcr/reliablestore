//-------------------------------------------------------------------------------
// <copyright file="ClusterPersistenceResult.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Core
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Represents the result of a cluster persistence operation.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    public readonly struct ClusterPersistenceResult<T>
    {
        private readonly T? value;
        private readonly bool hasValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterPersistenceResult{T}"/> struct with a value.
        /// </summary>
        /// <param name="value">The value.</param>
        public ClusterPersistenceResult(T value)
        {
            this.value = value;
            this.hasValue = true;
        }

        /// <summary>
        /// Gets a value indicating whether this result contains a value.
        /// </summary>
#if NET6_0_OR_GREATER
        [MemberNotNullWhen(true, nameof(ClusterPersistenceResult<T>.Value))]
#endif
        public bool HasValue => this.hasValue;

        /// <summary>
        /// Gets the value if it exists.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when HasValue is false.</exception>
        public T Value
        {
            get
            {
                if (!this.hasValue)
                {
                    throw new InvalidOperationException("Result does not contain a value.");
                }
                return this.value!;
            }
        }

        /// <summary>
        /// Gets the value if it exists, or the default value for the type.
        /// </summary>
        /// <returns>The value or default.</returns>
        public T? GetValueOrDefault() => this.hasValue ? this.value : default;

        /// <summary>
        /// Gets the value if it exists, or the specified default value.
        /// </summary>
        /// <param name="defaultValue">The default value to return if no value exists.</param>
        /// <returns>The value or the specified default.</returns>
        public T GetValueOrDefault(T defaultValue) => this.hasValue ? this.value! : defaultValue;

        /// <summary>
        /// Creates a result with no value.
        /// </summary>
        /// <returns>A result indicating no value was found.</returns>
        public static ClusterPersistenceResult<T> NoValue() => new();

        /// <summary>
        /// Creates a result with the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>A result containing the value.</returns>
        public static ClusterPersistenceResult<T> WithValue(T value) => new(value);

        /// <summary>
        /// Implicitly converts a value to a result.
        /// </summary>
        /// <param name="value">The value.</param>
        public static implicit operator ClusterPersistenceResult<T>(T value) => new(value);

        /// <summary>
        /// Returns a string representation of this result.
        /// </summary>
        /// <returns>A string representation.</returns>
        public override string ToString()
        {
            return this.hasValue ? $"Value: {this.value}" : "No Value";
        }
    }
}