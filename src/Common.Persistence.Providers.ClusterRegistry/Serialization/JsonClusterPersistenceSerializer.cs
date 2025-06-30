//-------------------------------------------------------------------------------
// <copyright file="JsonClusterPersistenceSerializer.cs" company="Microsoft Corp.">
//     Copyright (c) Microsoft Corp. All rights reserved.
// </copyright>
//-------------------------------------------------------------------------------

namespace Common.Persistence.Providers.ClusterRegistry.Serialization
{
    using System;
    using System.IO.Compression;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON-based implementation of cluster persistence serializer.
    /// </summary>
    public sealed class JsonClusterPersistenceSerializer : IClusterPersistenceSerializer
    {
        private readonly JsonSerializerOptions jsonOptions;
        private readonly bool enableCompression;

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonClusterPersistenceSerializer"/> class.
        /// </summary>
        /// <param name="enableCompression">Whether to enable compression for large values.</param>
        /// <param name="jsonOptions">Custom JSON serializer options (optional).</param>
        public JsonClusterPersistenceSerializer(bool enableCompression = false, JsonSerializerOptions? jsonOptions = null)
        {
            this.enableCompression = enableCompression;
            this.jsonOptions = jsonOptions ?? JsonClusterPersistenceSerializer.CreateDefaultJsonOptions();
        }

        /// <inheritdoc />
        public string Serialize<T>(T value)
        {
            try
            {
                // Handle null values
                if (value == null)
                {
                    return this.enableCompression ? JsonClusterPersistenceSerializer.CompressString("null") : "null";
                }

                // Special handling for strings to avoid double-encoding
                if (typeof(T) == typeof(string))
                {
                    var stringValue = value.ToString()!;
                    var jsonString = JsonSerializer.Serialize(stringValue, this.jsonOptions);
                    return this.enableCompression ? JsonClusterPersistenceSerializer.CompressString(jsonString) : jsonString;
                }

                // Handle primitive types efficiently
                if (JsonClusterPersistenceSerializer.IsPrimitiveType(typeof(T)))
                {
                    var primitiveJson = JsonSerializer.Serialize(value, this.jsonOptions);
                    return this.enableCompression ? JsonClusterPersistenceSerializer.CompressString(primitiveJson) : primitiveJson;
                }

                // Serialize complex objects
                var json = JsonSerializer.Serialize(value, this.jsonOptions);
                return this.enableCompression ? JsonClusterPersistenceSerializer.CompressString(json) : json;
            }
            catch (Exception ex)
            {
                throw new ClusterPersistenceSerializationException($"Failed to serialize object of type {typeof(T).Name}.", ex);
            }
        }

        /// <inheritdoc />
        public T Deserialize<T>(string serializedValue)
        {
            if (string.IsNullOrEmpty(serializedValue))
            {
                throw new ClusterPersistenceSerializationException("Cannot deserialize null or empty string.");
            }

            try
            {
                // Decompress if needed
                var json = this.enableCompression ? JsonClusterPersistenceSerializer.DecompressString(serializedValue) : serializedValue;

                // Handle null JSON
                if (json == "null")
                {
                    return default(T)!;
                }

                // Deserialize using the configured options
                var result = JsonSerializer.Deserialize<T>(json, this.jsonOptions);
                return result!;
            }
            catch (Exception ex)
            {
                throw new ClusterPersistenceSerializationException($"Failed to deserialize string to type {typeof(T).Name}.", ex);
            }
        }

        /// <inheritdoc />
        public bool TryDeserialize<T>(string serializedValue, out T? value)
        {
            value = default;

            if (string.IsNullOrEmpty(serializedValue))
            {
                return false;
            }

            try
            {
                value = this.Deserialize<T>(serializedValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static JsonSerializerOptions CreateDefaultJsonOptions()
        {
            return new JsonSerializerOptions
            {
                // Include fields to handle simple data structures
                IncludeFields = true,

                // Handle case-insensitive property names for flexibility
                PropertyNameCaseInsensitive = true,

                // Use camel case for consistency
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

                // Allow reading numbers as strings for flexibility
                NumberHandling = JsonNumberHandling.AllowReadingFromString,

                // Handle unknown properties gracefully
                ReadCommentHandling = JsonCommentHandling.Skip,

                // Compact output to save space
                WriteIndented = false,

                // Allow trailing commas for flexibility
                AllowTrailingCommas = true,

                // Handle enums as strings for readability
                Converters = { new JsonStringEnumConverter() }
            };
        }

        private static bool IsPrimitiveType(Type type)
        {
            return type.IsPrimitive ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(TimeSpan) ||
                   type == typeof(Guid) ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && JsonClusterPersistenceSerializer.IsPrimitiveType(Nullable.GetUnderlyingType(type)!));
        }

        private static string CompressString(string input)
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);

            using var outputStream = new System.IO.MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
            {
                gzipStream.Write(inputBytes, 0, inputBytes.Length);
            }

            var compressedBytes = outputStream.ToArray();
            return Convert.ToBase64String(compressedBytes);
        }

        private static string DecompressString(string compressedInput)
        {
            var compressedBytes = Convert.FromBase64String(compressedInput);

            using var inputStream = new System.IO.MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new System.IO.MemoryStream();

            gzipStream.CopyTo(outputStream);
            var decompressedBytes = outputStream.ToArray();

            return Encoding.UTF8.GetString(decompressedBytes);
        }
    }
}