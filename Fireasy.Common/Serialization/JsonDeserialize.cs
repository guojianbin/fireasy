﻿// -----------------------------------------------------------------------
// <copyright company="Fireasy"
//      email="faib920@126.com"
//      qq="55570729">
//   (c) Copyright Fireasy. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
#if !SILVERLIGHT
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Text.RegularExpressions;
#endif
using System.Linq;
using System.Globalization;
using System.Reflection;
using Fireasy.Common.Extensions;
#if !N35
using System.Dynamic;
using Fireasy.Common.Dynamic;
#endif
using Fireasy.Common.Reflection;

namespace Fireasy.Common.Serialization
{
    internal sealed class JsonDeserialize : IDisposable
    {
        private readonly JsonSerializeOption option;
        private readonly JsonSerializer serializer;
        private JsonReader jsonReader;
        private bool isDisposed;
        private SerializeContext context;
        private static MethodInfo MthToArray = typeof(Enumerable).GetMethod("ToArray", BindingFlags.Public | BindingFlags.Static);

        internal JsonDeserialize(JsonSerializer serializer, JsonReader reader, JsonSerializeOption option)
        {
            this.serializer = serializer;
            jsonReader = reader;
            this.option = option;
            context = new SerializeContext();
        }

        internal T Deserialize<T>()
        {
            return (T)Deserialize(typeof(T));
        }

        internal object Deserialize(Type type)
        {
            object value = null;
            if (WithSerializable(type, ref value))
            {
                return value;
            }

            if (WithConverter(type, ref value))
            {
                return value;
            }

            jsonReader.SkipWhiteSpaces();
#if !SILVERLIGHT
            if (type == typeof(Color))
            {
                return DeserializeColor();
            }
#endif
            if (type == typeof(Type))
            {
                return DeserializeType();
            }

#if !N35
            if (typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type) ||
                type == typeof(object))
            {
                return DeserializeIntelligently(type);
            }
#endif
            if (typeof(ArrayList).IsAssignableFrom(type))
            {
                return DeserializeSingleArray();
            }

            if (typeof(IDictionary).IsAssignableFrom(type) && type != typeof(string))
            {
                return DeserializeDictionary(type);
            }

            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            {
                return DeserializeList(type);
            }
#if !SILVERLIGHT
            if (type == typeof(DataSet))
            {
                return DeserializeDataSet();
            }

            if (type == typeof(DataTable))
            {
                return DeserializeDataTable();
            }
#endif

            return DeserializeValue(type);
        }

        private bool WithSerializable(Type type, ref object value)
        {
            if (typeof(ITextSerializable).IsAssignableFrom(type))
            {
                var obj = type.New<ITextSerializable>();
                obj.Deserialize(serializer, jsonReader.ReadRaw());
                value = obj;

                return true;
            }

            return false;
        }

        private bool WithConverter(Type type, ref object value)
        {
            var converter = option.Converters.GetConverter(type);
            if ((converter is JsonConverter  || converter is ValueConverter) && converter.CanRead)
            {
                var jsonConvert = converter as JsonConverter;
                value = jsonConvert != null && jsonConvert.Streaming ?
                    jsonConvert.ReadJson(serializer, jsonReader, type) :
                    converter.ReadObject(serializer, type, jsonReader.ReadRaw());

                return true;
            }

            return false;
        }

        private object DeserializeValue(Type type)
        {
            if (jsonReader.IsNull())
            {
                if ((type.GetNonNullableType().IsValueType && !type.IsNullableType()))
                {
                    throw new SerializationException(SR.GetString(SRKind.JsonNullableType, type));
                }

                return null;
            }

            var stype = type.GetNonNullableType();
            var typeCode = Type.GetTypeCode(stype);
            if (typeCode == TypeCode.Object)
            {
                return ParseObject(type);
            }

            var value = jsonReader.ReadValue();
            if (type.IsNullableType() && (value == null || string.IsNullOrEmpty(value.ToString())))
            {
                return null;
            }

            switch (typeCode)
            {
                case TypeCode.DateTime:
                    CheckNullString(value, type);
                    return DeserializeDateTime(value.ToString());
                case TypeCode.String:
                    return value == null ? null : DeserializeString(value.ToString());
                default:
                    CheckNullString(value, type);
                    try
                    {
                        return value.ToType(stype);
                    }
                    catch (Exception ex)
                    {
                        throw new SerializationException(SR.GetString(SRKind.DeserializeError, value, type), ex);
                    }
            }
        }

        private static void CheckNullString(object value, Type type)
        {
            if ((value == null || value.ToString().Length == 0) && !type.IsNullableType())
            {
                throw new SerializationException(SR.GetString(SRKind.JsonNullableType, type));
            }
        }

#if !SILVERLIGHT
        private Color DeserializeColor()
        {
            var str = jsonReader.ReadAsString();
            var converter = TypeDescriptor.GetConverter(typeof(int));
            if (converter == null)
            {
                return Color.Empty;
            }

            try
            {
                var val = converter.ConvertFromString("0x" + str);
                return val == null ? Color.Empty : Color.FromArgb(-16777216 | (int)val);
            }
            catch (Exception ex)
            {
                throw new SerializationException(SR.GetString(SRKind.DeserializeError, str, typeof(Color)), ex);
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000")]
        private DataSet DeserializeDataSet()
        {
            var ds = new DataSet();
            jsonReader.SkipWhiteSpaces();
            jsonReader.AssertAndConsume(JsonTokens.StartObjectLiteralCharacter);
            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                var name = jsonReader.ReadAsString();
                jsonReader.SkipWhiteSpaces();
                jsonReader.AssertAndConsume(JsonTokens.PairSeparator);
                jsonReader.SkipWhiteSpaces();
                var tb = DeserializeDataTable();
                tb.TableName = name;
                ds.Tables.Add(tb);
                jsonReader.SkipWhiteSpaces();
                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndObjectLiteralCharacter))
                {
                    break;
                }
            }

            return ds;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000")]
        private DataTable DeserializeDataTable()
        {
            var tb = new DataTable();
            jsonReader.SkipWhiteSpaces();
            jsonReader.AssertAndConsume(JsonTokens.StartArrayCharacter);
            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                DeserializeDataRow(tb);
                jsonReader.SkipWhiteSpaces();
                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndArrayCharacter))
                {
                    break;
                }
            }

            return tb;
        }

        private void DeserializeDataRow(DataTable tb)
        {
            if (jsonReader.Peek() != JsonTokens.StartObjectLiteralCharacter)
            {
                return;
            }

            var noCols = tb.Columns.Count == 0;
            jsonReader.AssertAndConsume(JsonTokens.StartObjectLiteralCharacter);
            var row = tb.NewRow();
            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                var name = DeserializeString(jsonReader.ReadKey());
                jsonReader.SkipWhiteSpaces();
                jsonReader.AssertAndConsume(JsonTokens.PairSeparator);
                jsonReader.SkipWhiteSpaces();
                var obj = ParseValue(jsonReader.ReadValue());
                jsonReader.SkipWhiteSpaces();
                if (noCols)
                {
                    tb.Columns.Add(name, obj != null ? obj.GetType() : typeof(object));
                }

                if (tb.Columns.Contains(name))
                {
                    row[name] = obj == null ? DBNull.Value : obj;
                }

                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndObjectLiteralCharacter))
                {
                    break;
                }
            }

            tb.Rows.Add(row);
        }

        private object ParseValue(object value)
        {
            var s = value as string;
            if (s != null)
            {
                if (Regex.IsMatch(s, @"Date\((\d+)\+(\d+)\)"))
                {
                    return DeserializeDateTime(s);
                }

                return DeserializeString(s);
            }

            return value;
        }
#endif

        private object DeserializeList(Type listType)
        {
            IList container = null;
            Type elementType = null;
#if !N40 && !N35
            var isReadonly = listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>);
#else
            var isReadonly = listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>);
#endif

            CreateListContainer(listType, out elementType, out container);

            jsonReader.SkipWhiteSpaces();
            if (jsonReader.IsNull())
            {
                return null;
            }

            jsonReader.AssertAndConsume(JsonTokens.StartArrayCharacter);
            while (true)
            {
                if (jsonReader.Peek() == JsonTokens.EndArrayCharacter)
                {
                    jsonReader.Read();
                    return container;
                }

                jsonReader.SkipWhiteSpaces();
                var value = DeserializeIntelligently(elementType);
                if (value != null && value.GetType() != elementType)
                {
                    value = value.ToType(elementType);
                }
                container.Add(value);
                jsonReader.SkipWhiteSpaces();

                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndArrayCharacter))
                {
                    break;
                }
            }

            if (listType.IsArray)
            {
                var invoker = ReflectionCache.GetInvoker(MthToArray.MakeGenericMethod(elementType));
                return invoker.Invoke(null, container);
            }

            if (isReadonly)
            {
                return listType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new[] { container.GetType() }, null).Invoke(new object[] { container });
            }

            return container;
        }

        private void CreateListContainer(Type listType, out Type elementType, out IList container)
        {
            if (listType.IsArray)
            {
                elementType = listType.GetElementType();
                container = typeof(List<>).MakeGenericType(elementType).New<IList>();
            }
            else if (listType.IsInterface || listType.IsArray)
            {
                elementType = listType.GetGenericArguments()[0];
                container = typeof(List<>).MakeGenericType(elementType).New<IList>();
            }
            else
            {
                elementType = listType.GetGenericImplementType(typeof(IList<>)).GetGenericArguments()[0];
                container = listType.New<IList>();
            }
        }

        private IDictionary DeserializeDictionary(Type dictType)
        {
            IDictionary container = null;
            Type[] keyValueTypes = null;

            CreateDictionaryContainer(dictType, out keyValueTypes, out container);

            jsonReader.SkipWhiteSpaces();
            jsonReader.AssertAndConsume(JsonTokens.StartObjectLiteralCharacter);
            while (true)
            {
                if (jsonReader.Peek() == JsonTokens.EndObjectLiteralCharacter)
                {
                    jsonReader.Read();
                    return container;
                }

                jsonReader.SkipWhiteSpaces();
                var key = Deserialize(keyValueTypes[0]);
                jsonReader.SkipWhiteSpaces();
                jsonReader.AssertAndConsume(JsonTokens.PairSeparator);
                jsonReader.SkipWhiteSpaces();
                container.Add(key, Deserialize(keyValueTypes[1]));
                jsonReader.SkipWhiteSpaces();

                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndObjectLiteralCharacter))
                {
                    break;
                }
            }

            return container;
        }

        private void CreateDictionaryContainer(Type dictType, out Type[] keyValueTypes, out IDictionary container)
        {
            if (dictType.IsInterface)
            {
                keyValueTypes = dictType.GetGenericArguments();
                container = typeof(Dictionary<,>).MakeGenericType(keyValueTypes).New<IDictionary>();
            }
            else
            {
                keyValueTypes = dictType.GetGenericImplementType(typeof(IDictionary<,>)).GetGenericArguments();
                container = dictType.New<IDictionary>();
            }
        }

        private object DeserializeIntelligently(Type type)
        {
            if (jsonReader.IsNull())
            {
                return null;
            }

            if (jsonReader.IsNextCharacter(JsonTokens.StartArrayCharacter))
            {
                return DeserializeSingleArray();
            }
            else if (jsonReader.IsNextCharacter(JsonTokens.StartObjectLiteralCharacter))
            {
#if !N35
                if (typeof(object) == type)
                {
                    return DeserializeDynamicObject(type);
                }
#endif

                return Deserialize(type);
            }

            return jsonReader.ReadValue();
        }

#if !N35

        private object DeserializeDynamicObject(Type type)
        {
            var dynamicObject = type == typeof(object) ? new ExpandoObject() :
                type.New<IDictionary<string, object>>();

            jsonReader.AssertAndConsume(JsonTokens.StartObjectLiteralCharacter);

            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                var name = jsonReader.ReadKey();
                jsonReader.SkipWhiteSpaces();
                jsonReader.AssertAndConsume(JsonTokens.PairSeparator);
                jsonReader.SkipWhiteSpaces();

                object value = null;
                if (jsonReader.IsNextCharacter(JsonTokens.StartArrayCharacter))
                {
                    value = DeserializeList(typeof(List<dynamic>));
                }
                else if (jsonReader.IsNextCharacter(JsonTokens.StartObjectLiteralCharacter))
                {
                    value = Deserialize<dynamic>();
                }
                else
                {
                    value = jsonReader.ReadValue();
                }

                dynamicObject.Add(name, value);
                jsonReader.SkipWhiteSpaces();
                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndObjectLiteralCharacter))
                {
                    break;
                }
            }

            return dynamicObject;
        }
#endif

        private object DeserializeSingleArray()
        {
            var array = new ArrayList();
            jsonReader.AssertAndConsume(JsonTokens.StartArrayCharacter);

            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                var value = jsonReader.ReadValue();
                array.Add(value);
                jsonReader.SkipWhiteSpaces();

                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndArrayCharacter))
                {
                    break;
                }
            }

            return array;
        }

        private static DateTime? DeserializeDateTime(string value)
        {
            if (value.Length == 0)
            {
                return null;
            }

            DateTime d;
            if (DateTime.TryParse(value, out d))
            {
                return d;
            }

           return ParseUtcDateTime(value);
        }

        private static DateTime? ParseUtcDateTime(string value)
        {
            var regex = new Regex(@"Date\((|-)(\d+)(|\+|-)(|0800)\)");
            var matches = regex.Matches(value);

            if (matches.Count == 0)
            {
                throw new SerializationException(SR.GetString(SRKind.DeserializeError, value, typeof(DateTime)));
            }

            var dkind = matches[0].Groups[3].Value == string.Empty ? DateTimeKind.Utc : DateTimeKind.Local;
            var time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var ticks = long.Parse(matches[0].Groups[1].Value + matches[0].Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            var date = new DateTime((ticks * 10000) + time.Ticks, DateTimeKind.Utc);

            if (dkind == DateTimeKind.Local)
            {
                return date.ToLocalTime();
            }

            return date;
        }

        private string DeserializeString(string value)
        {
            if (value.IndexOf("\\u") != -1)
            {
                value = value.DeUnicode();
            }

            return value;
        }

        private Type DeserializeType()
        {
            var value = jsonReader.ReadAsString();
            if (option.IgnoreType)
            {
                return null;
            }

            return value.ParseType();
        }

        private object ParseObject(Type type)
        {
            return type.IsAnonymousType() ? ParseAnonymousObject(type) : ParseGeneralObject(type);
        }

        private object ParseGeneralObject(Type type)
        {
            if (jsonReader.IsNull())
            {
                return null;
            }

            var cache = GetAccessorCache(type);

            jsonReader.AssertAndConsume(JsonTokens.StartObjectLiteralCharacter);
            var instance = type.New();

            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                var name = jsonReader.ReadKey();
                jsonReader.SkipWhiteSpaces();
                jsonReader.AssertAndConsume(JsonTokens.PairSeparator);
                jsonReader.SkipWhiteSpaces();

                PropertyAccessor accessor;
                if (!cache.TryGetValue(name, out accessor))
                {
                    jsonReader.ReadValue();
                }
                else
                {
                    var value = Deserialize(accessor.PropertyInfo.PropertyType);
                    if (value != null)
                    {
                        accessor.SetValue(instance, value);
                    }
                }

                jsonReader.SkipWhiteSpaces();
                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndObjectLiteralCharacter))
                {
                    break;
                }
            }

            return instance;
        }

        private object ParseAnonymousObject(Type type)
        {
            jsonReader.AssertAndConsume(JsonTokens.StartObjectLiteralCharacter);
            var dic = new Dictionary<string, object>();

            var constructor = type.GetConstructors()[0];
            var conInvoker = ReflectionCache.GetInvoker(constructor);

            var mapper = GenerateParameterDictionary(constructor);
            var values = GenerateParameterValues(mapper);

            while (true)
            {
                jsonReader.SkipWhiteSpaces();
                var name = jsonReader.ReadKey();

                jsonReader.SkipWhiteSpaces();
                jsonReader.AssertAndConsume(JsonTokens.PairSeparator);
                jsonReader.SkipWhiteSpaces();

                var par = mapper.FirstOrDefault(s => s.Key == name);

                if (string.IsNullOrEmpty(par.Key))
                {
                    jsonReader.ReadRaw();
                }
                else
                {
                    values[name] = Deserialize(par.Value);
                }

                jsonReader.SkipWhiteSpaces();

                if (jsonReader.AssertNextIsDelimiterOrSeparator(JsonTokens.EndObjectLiteralCharacter))
                {
                    break;
                }
            }

            return conInvoker.Invoke(values.Values.ToArray());
        }

        private static Dictionary<string, Type> GenerateParameterDictionary(ConstructorInfo constructor)
        {
            var dic = new Dictionary<string, Type>();
            foreach (var par in constructor.GetParameters())
            {
                dic.Add(par.Name, par.ParameterType);
            }

            return dic;
        }

        private static Dictionary<string, object> GenerateParameterValues(Dictionary<string, Type> mapper)
        {
            var dic = new Dictionary<string, object>();
            foreach (var par in mapper)
            {
                dic.Add(par.Key, null);
            }

            return dic;
        }
        
        /// <summary>
        /// 获取指定类型的属性访问缓存。
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private Dictionary<string, PropertyAccessor> GetAccessorCache(Type type)
        {
            return context.SetAccessors.TryGetValue(type, () =>
                {
                    return type.GetProperties()
                        .Where(s => s.CanWrite && !SerializerUtil.IsNoSerializable(option, s))
                        .Select(s => 
                            {
                                var ele = s.GetCustomAttributes<TextSerializeElementAttribute>().FirstOrDefault();
                                //如果使用Camel命名，则名称第一位小写
                                var name = ele != null ? 
                                    ele.Name : (option.CamelNaming ? 
                                        char.ToLower(s.Name[0]) + s.Name.Substring(1) : s.Name);

                                return new { name, p = s };
                            })
                        .ToDictionary(s => s.name, s => ReflectionCache.GetAccessor(s.p));
                });
        }

        /// <summary>
        /// 释放对象所占用的非托管和托管资源。
        /// </summary>
        /// <param name="disposing">为 true 则释放托管资源和非托管资源；为 false 则仅释放非托管资源。</param>
        private void Dispose(bool disposing)
        {
            if (isDisposed)
            {
                return;
            }

            if (disposing)
            {
                jsonReader.Dispose();
                jsonReader = null;
            }

            isDisposed = true;
        }

        /// <summary>
        /// 释放对象所占用的所有资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
