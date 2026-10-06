// Copyright (c) NeeLaboratory. MIT; port of the baseline default comparison converter.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeeView
{
    /// <summary>
    /// 初期値と同じ値のプロパティを出力しない JsonConverter
    /// </summary>
    /// <remarks>
    /// 読み書き可能なプロパティのみ対応。フィールドはサポートしません。
    /// 以下の属性やオプションには対応していません。
    /// - JsonPropertyOrder
    /// - PropertyNamingPolicy
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public class DiffJsonConverter<T> : JsonConverter<T>, IDiffJsonConverter<T>, IDiffJsonConverter where T : new()
    {
        private sealed record PropMeta(string JsonName, Type PropertyType, Func<T, object?> Getter, Action<T, object?> Setter, IDefaultable? Defaultable);

        private static readonly PropMeta[] _props = BuildProps();
        private static readonly Dictionary<string, PropMeta> _map = _props.ToDictionary(p => p.JsonName, StringComparer.Ordinal);

        /// <summary>Cache original writable-property getters/setters, excluding extension-data ownership.</summary>
        /// <returns>Immutable metadata in the original reflection order.</returns>
        private static PropMeta[] BuildProps()
        {
            TestFieldMembers();

            return typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0 &&
                            p.GetMethod is { IsPublic: true } &&
                            p.SetMethod is { IsPublic: true } &&
                            p.GetCustomAttribute<JsonIgnoreAttribute>(false)?.Condition is not (JsonIgnoreCondition.Always or JsonIgnoreCondition.WhenWriting) &&
                            p.GetCustomAttribute<JsonExtensionDataAttribute>(false) is null)
                .Select(p =>
                {
                    var nameAttr = p.GetCustomAttribute<JsonPropertyNameAttribute>(false);
                    var jsonName = nameAttr?.Name ?? p.Name;

                    // --- Getter (Expression) ---
                    var inst = Expression.Parameter(typeof(T), "x");
                    var getExp = Expression.Property(inst, p);
                    var getObj = Expression.Convert(getExp, typeof(object));
                    var getter = Expression.Lambda<Func<T, object?>>(getObj, inst).Compile();

                    // --- Setter (Expression) ---
                    var inst2 = Expression.Parameter(typeof(T), "x");
                    var val = Expression.Parameter(typeof(object), "v");
                    var valCast = Expression.Convert(val, p.PropertyType);
                    var setExp = Expression.Call(inst2, p.SetMethod!, valCast);
                    var setter = Expression.Lambda<Action<T, object?>>(setExp, inst2, val).Compile();

                    // defaultable
                    var defAttr = p.GetCustomAttribute<DiffJsonDefaultAttribute>(false) ?? p.PropertyType.GetCustomAttribute<DiffJsonDefaultAttribute>(false);
                    var defaultable = defAttr?.CreateDefaultable();

                    return new PropMeta(jsonName, p.PropertyType, getter, setter, defaultable);
                })
                .ToArray();
        }

        [Conditional("DEBUG")]
        private static void TestFieldMembers()
        {
            // Field members are not supported.
            foreach (var f in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var nameAttr = f.GetCustomAttribute<JsonPropertyNameAttribute>(false);
                var jsonName = nameAttr?.Name ?? f.Name;

                if (f.IsPublic &&
                    f.GetCustomAttribute<JsonIncludeAttribute>(false) is not null &&
                    f.GetCustomAttribute<JsonIgnoreAttribute>(false)?.Condition is not (JsonIgnoreCondition.Always or JsonIgnoreCondition.WhenWriting))
                {
                    throw new InvalidOperationException("Field members are not supported.");
                }
            }
        }

        /// <summary>Read a default-initialized value using the original exact property names.</summary>
        /// <param name="reader">Reader at StartObject.</param><param name="typeToConvert">Requested type.</param>
        /// <param name="options">Nested serializer options.</param><returns>The restored typed value.</returns>
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject.");
            }

            reader.Read();

            return ReadInner(ref reader, typeToConvert, options);
        }

        /// <summary>Restore the body after StartObject; the raw SaveData node retains unknown keys separately.</summary>
        /// <param name="reader">Reader at the first property or EndObject.</param><param name="typeToConvert">Requested type.</param>
        /// <param name="options">Nested serializer options.</param><returns>The default-initialized restored value.</returns>
        public T ReadInner(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new T();

            while (reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName.");
                }

                string propName = reader.GetString()!;
                reader.Read();

                if (_map.TryGetValue(propName, out var meta))
                {
                    object? value = JsonSerializer.Deserialize(ref reader, meta.PropertyType, options);

                    meta.Setter(result, value);
                }
                else
                {
                    reader.Skip();
                }

                reader.Read();
            }

            return result;
        }

        /// <summary>Write original nondefault properties inside one object.</summary>
        /// <param name="writer">Destination writer.</param><param name="value">Typed value.</param><param name="options">Nested serializer options.</param>
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            WriteInner(writer, value, options);

            writer.WriteEndObject();
        }

        /// <summary>Compare against new T with the original Equals/IDefaultable rules.</summary>
        /// <param name="writer">Writer inside an object.</param><param name="value">Current value.</param><param name="options">Nested serializer options.</param>
        public void WriteInner(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var defaultValue = new T();

            foreach (var prop in _props)
            {
                var current = prop.Getter(value!);
                var def = prop.Getter(defaultValue);

                if (prop.Defaultable is null ? !Equals(current, def) : !prop.Defaultable.IsDefault(current))
                {
                    writer.WritePropertyName(prop.JsonName);
                    JsonSerializer.Serialize(writer, current, prop.PropertyType, options);
                }
            }
        }

        /// <summary>Write all known writable properties for original untrimmed mementos.</summary>
        /// <param name="writer">Writer inside an object.</param><param name="value">Current value.</param><param name="options">Nested serializer options.</param>
        public void WriteInnerNoDiff(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            foreach (var prop in _props)
            {
                var current = prop.Getter(value!);
                writer.WritePropertyName(prop.JsonName);
                JsonSerializer.Serialize(writer, current, prop.PropertyType, options);
            }
        }

        /// <summary>Expose the original property/default comparison for a write-only JSON copy.</summary>
        /// <param name="value">The typed current value.</param>
        /// <param name="defaults">The containing configuration's defaults, including profile/command instance defaults.</param>
        /// <returns>Known properties only; extension keys remain owned by the raw JSON.</returns>
        public IEnumerable<DiffJsonProperty> CompareProperties(object value, object defaults)
        {
            foreach (var prop in _props)
            {
                var current = prop.Getter((T)value);
                var baseline = prop.Getter((T)defaults);
                yield return new(prop.JsonName, prop.PropertyType, current, baseline,
                    prop.Defaultable is null ? Equals(current, baseline) : prop.Defaultable.IsDefault(current));
            }
        }

        #region IDiffJsonConverter

        object? IDiffJsonConverter.Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Read(ref reader, typeToConvert, options);
        }

        object? IDiffJsonConverter.ReadInner(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ReadInner(ref reader, typeToConvert, options);
        }

        void IDiffJsonConverter.Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            Write(writer, (T)value, options);
        }

        void IDiffJsonConverter.WriteInner(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            WriteInner(writer, (T)value, options);
        }

        void IDiffJsonConverter.WriteInnerNoDiff(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            WriteInnerNoDiff(writer, (T)value, options);
        }

        #endregion IDiffJsonConverter
    }


    public interface IDiffJsonConverter<T> where T : new()
    {
        T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
        T ReadInner(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
        void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options);
        void WriteInner(Utf8JsonWriter writer, T value, JsonSerializerOptions options);
        void WriteInnerNoDiff(Utf8JsonWriter writer, T value, JsonSerializerOptions options);
    }

    /// <summary>Original writable-property comparison, without flattening extension data.</summary>
    public sealed record DiffJsonProperty(string Name, Type Type, object? Value, object? Default, bool IsDefault);

    public interface IDiffJsonConverter
    {
        IEnumerable<DiffJsonProperty> CompareProperties(object value, object defaults);
        object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
        object? ReadInner(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);
        void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options);
        void WriteInner(Utf8JsonWriter writer, object value, JsonSerializerOptions options);
        void WriteInnerNoDiff(Utf8JsonWriter writer, object value, JsonSerializerOptions options);
    }


    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
    public class DiffJsonDefaultAttribute : Attribute
    {
        public DiffJsonDefaultAttribute(Type defaultableType)
        {
            Debug.Assert(defaultableType.IsAssignableTo(typeof(IDefaultable)));

            DefaultableType = defaultableType;
        }

        public Type DefaultableType { get; }

        public IDefaultable CreateDefaultable()
        {
            return (IDefaultable)Activator.CreateInstance(DefaultableType)!;
        }
    }


    public interface IDefaultable
    {
        bool IsDefault(object? obj);
    }

    public class FalseDefaultable : IDefaultable
    {
        public bool IsDefault(object? obj)
        {
            return false;
        }
    }
}
