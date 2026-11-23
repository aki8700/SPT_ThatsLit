// https://github.com/MiraculousNightingale/spt-aki-inventory-organizing-features/blob/master/client-side/InventoryOrganizingFeatures/reflections/ReflectionHelper.cs
// University of Illinois/NCSA Open Source License

// Copyright (c) 2021 . All rights reserved.

// Developed by: SPT-Aki
              
//               https://www.offline-tarkov.com

// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation files
// (the "Software"), to deal with the Software without restriction,
// including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:

// * Redistributions of source code must retain the above copyright notice,
//   this list of conditions and the following disclaimers.

// * Redistributions in binary form must reproduce the above copyright
//   notice, this list of conditions and the following disclaimers in the
//   documentation and/or other materials provided with the distribution.

// * Neither the names of , SPT-Aki nor the names of its
//   contributors may be used to endorse or promote products derived from
//   this Software without specific prior written permission.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS WITH
// THE SOFTWARE.

using BepInEx.Logging;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ThatsLit
{
    /// <summary>
    /// Extension and helper class to simplify reflection.
    /// Also uses cache which allows using reflection in loops without performance loss.
    /// </summary>
    internal static class ReflectionHelper
    {
        public static ManualLogSource Logger { get; set; }
        private static readonly Assembly PluginAssembly; // should be removed if compiled to a separate assembly
        private static readonly Assembly TarkovAssembly;
        static ReflectionHelper()
        {
            PluginAssembly = Assembly.GetExecutingAssembly();
            TarkovAssembly = Assembly.GetAssembly(typeof(TarkovApplication));
        }
        // public static Type FindClassType()

        private static Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        private static Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();
        private static Dictionary<string, PropertyInfo> PropertyCache = new Dictionary<string, PropertyInfo>();
        private static Dictionary<string, MethodInfo> MethodCache = new Dictionary<string, MethodInfo>();

        public static string GenerateCacheKey(params object[] parameters)
        {
            string key = "";
            foreach (var param in parameters)
            {
                if (param is Array)
                {
                    foreach (var element in (Array)param)
                    {
                        key += $"{element}-";
                    }
                    continue;
                }
                key += $"{param}-";
            }
            return key.TrimEnd('-');
        }

        private static bool TryGetFromCache<T>(string key, out T cachedOutput) where T : MemberInfo
        {
            var cacheType = typeof(T);
            if (cacheType == typeof(Type))
            {
                if (TypeCache.TryGetValue(key, out Type value))
                {
                    cachedOutput = (T)(value as object);
                    return true;
                };
                cachedOutput = default;
                return false;
            }
            if (cacheType == typeof(FieldInfo))
            {
                if (FieldCache.TryGetValue(key, out FieldInfo value))
                {
                    cachedOutput = (T)(value as object);
                    return true;
                };
                cachedOutput = default;
                return false;
            }
            if (cacheType == typeof(PropertyInfo))
            {
                if (PropertyCache.TryGetValue(key, out PropertyInfo value))
                {
                    cachedOutput = (T)(value as object);
                    return true;
                };
                cachedOutput = default;
                return false;
            }
            if (cacheType == typeof(MethodInfo))
            {
                if (MethodCache.TryGetValue(key, out MethodInfo value))
                {
                    cachedOutput = (T)(value as object);
                    return true;
                };
                cachedOutput = default;
                return false;
            }
            throw new Exception($"ReflectionHelper.TryGetFromCache<{typeof(T)}> can't be used with type {typeof(T)}.");
        }

        private static void AddToCache<T>(string key, T objectToCache)
        {
            var cacheType = typeof(T);
            if (cacheType == typeof(Type))
            {
                TypeCache.Add(key, objectToCache as Type);
                return;
            }
            if (cacheType == typeof(FieldInfo))
            {
                FieldCache.Add(key, objectToCache as FieldInfo);
                return;
            }
            if (cacheType == typeof(PropertyInfo))
            {
                PropertyCache.Add(key, objectToCache as PropertyInfo);
                return;
            }
            if (cacheType == typeof(MethodInfo))
            {
                MethodCache.Add(key, objectToCache as MethodInfo);
                return;
            }
            throw new Exception($"ReflectionHelper.AddToCache<{typeof(T)}> can't be used with type {typeof(T)}.");
        }

        public static Type FindClassTypeByMethodNames(string[] names, Assembly targetAssembly = null, bool searchInAllTypes = false)
        {
            var key = GenerateCacheKey("method", targetAssembly?.FullName ?? TarkovAssembly?.FullName, searchInAllTypes, names);
            // Take from cache if present
            if (TryGetFromCache(key, out Type cached))
            {
                return cached;
            }

            targetAssembly ??= TarkovAssembly;
            var types = searchInAllTypes ? AccessTools.AllTypes() : AccessTools.GetTypesFromAssembly(targetAssembly);
            Type result = null;
            Type secondMatch = null;
            foreach (var type in types)
            {
                if (type.Assembly.Equals(PluginAssembly) || !type.IsClass) continue;
                var methods = AccessTools.GetMethodNames(type);
                if (!names.All(methods.Contains)) continue;
                if (result == null) result = type;
                else { secondMatch = type; break; }
            }

            if (secondMatch != null)
            {
                Logger.LogWarning($"ReflectionHelper.FindClassTypeByMethodNames [AmbiguousMatch-Key]: {key}");
                Logger.LogWarning($"ReflectionHelper.FindClassTypeByMethodNames [AmbiguousMatch]: {result.AssemblyQualifiedName}");
                Logger.LogWarning($"ReflectionHelper.FindClassTypeByMethodNames [AmbiguousMatch]: {secondMatch.AssemblyQualifiedName}");
                throw new AmbiguousMatchException();
            }

            if (result == null) throw GetNotFoundException(key);

            // Cache if found
            if (result != null)
            {
                AddToCache(key, result);
            }

            return result;
        }

        public static Type FindClassTypeByFieldNames(string[] names, Assembly targetAssembly = null, bool searchInAllTypes = false)
        {
            var key = GenerateCacheKey("field", targetAssembly?.FullName ?? TarkovAssembly?.FullName, searchInAllTypes, names);
            // Take from cache if present
            if (TryGetFromCache(key, out Type cached))
            {
                return cached;
            }

            targetAssembly ??= TarkovAssembly;
            var types = searchInAllTypes ? AccessTools.AllTypes() : AccessTools.GetTypesFromAssembly(targetAssembly);
            Type result = null;
            Type secondMatch = null;
            foreach (var type in types)
            {
                if (type.Assembly.Equals(PluginAssembly) || !type.IsClass) continue;
                var fields = AccessTools.GetFieldNames(type);
                if (!names.All(fields.Contains)) continue;
                if (result == null) result = type;
                else { secondMatch = type; break; }
            }

            if (secondMatch != null)
            {
                Logger.LogWarning($"ReflectionHelper.FindClassTypeByFieldNames [AmbiguousMatch-Key]: {key}");
                Logger.LogWarning($"ReflectionHelper.FindClassTypeByFieldNames [AmbiguousMatch]: {result.AssemblyQualifiedName}");
                Logger.LogWarning($"ReflectionHelper.FindClassTypeByFieldNames [AmbiguousMatch]: {secondMatch.AssemblyQualifiedName}");
                throw new AmbiguousMatchException();
            }

            if (result == null) throw GetNotFoundException(key);

            // Cache if found
            if (result != null)
            {
                AddToCache(key, result);
            }

            return result;
        }

        // public static Type FindClassTypeByPropertyNames

        public static MethodInfo FindMethodByArgTypes(this object instance, Type[] methodArgTypes, BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            return FindMethodByArgTypes(instance.GetType(), methodArgTypes, bindingAttr);
        }

        public static MethodInfo FindMethodByArgTypes(Type type, Type[] methodArgTypes, BindingFlags bindingAttr = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            // Take from cache if present
            var key = GenerateCacheKey(type, methodArgTypes, bindingAttr);
            if (TryGetFromCache(key, out MethodInfo cached))
            {
                return cached;
            }

            MethodInfo result = null;
            MethodInfo secondMatch = null;
            foreach (var method in type.GetMethods(bindingAttr))
            {
                var parameters = method.GetParameters();
                if (parameters.Length != methodArgTypes.Length) continue;
                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                    if (parameters[i].ParameterType != methodArgTypes[i]) { matches = false; break; }
                if (!matches) continue;
                if (result == null) result = method;
                else { secondMatch = method; break; }
            }

            if (secondMatch != null)
            {
                Logger.LogWarning($"ReflectionHelper.FindMethodByArgTypes [AmbiguousMatch-Key]: {key}");
                Logger.LogWarning($"ReflectionHelper.FindMethodByArgTypes [AmbiguousMatch-Type-AssemblyQualifiedName]: {type.AssemblyQualifiedName}");
                Logger.LogWarning($"ReflectionHelper.FindMethodByArgTypes [AmbiguousMatch]: {result.Name}");
                Logger.LogWarning($"ReflectionHelper.FindMethodByArgTypes [AmbiguousMatch]: {secondMatch.Name}");
                throw new AmbiguousMatchException();
            }

            if (result == null) throw GetNotFoundException(key);

            // Cache if found
            if (result != null)
            {
                AddToCache(key, result);
            }

            return result;
        }

        private static FieldInfo GetFieldWithCache(Type type, string name)
        {
            var key = GenerateCacheKey(type, name);
            // try to get from cache
            if (TryGetFromCache(key, out FieldInfo cached))
            {
                return cached;
            }

            var field = AccessTools.Field(type, name);
            //var field = type.GetField(name, AccessTools.allDeclared);
            if (field == null) throw GetNotFoundException(key);

            // cache if found
            AddToCache(key, field);
            return field;
        }

        private static PropertyInfo GetPropertyWithCache(Type type, string name)
        {
            var key = GenerateCacheKey(type, name);
            // try to get from cache
            if (TryGetFromCache(key, out PropertyInfo cached))
            {
                return cached;
            }

            var property = AccessTools.Property(type, name);
            //var property = type.GetProperty(name, AccessTools.allDeclared);
            if (property == null) throw GetNotFoundException(key);

            // cache if found
            AddToCache(key, property);
            return property;
        }

        private static MethodInfo GetMethodWithCache(Type type, string name, Type[] methodArgTypes = null)
        {
            var key = GenerateCacheKey(type, name, methodArgTypes);
            // try to get from cache
            if (TryGetFromCache(key, out MethodInfo cached))
            {
                return cached;
            }
            var method = AccessTools.Method(type, name, methodArgTypes);
            //var method = type.GetMethod(name, AccessTools.allDeclared, null, methodArgTypes, null);
            if (method == null) throw GetNotFoundException(key);

            // cache if found
            AddToCache(key, method);
            return method;
        }

        private static Exception GetNotFoundException(string searchParamsKey)
        {
            return new Exception($"ReflectionHelper | Couldn't find member with parameters key {searchParamsKey}.");
        }

        public static T InvokeMethod<T>(this Type staticType, string name, object[] args = null, Type[] methodArgTypes = null)
        {
            return (T)staticType.InvokeMethod(name, args, methodArgTypes);
        }

        public static object InvokeMethod(this Type staticType, string name, object[] args = null, Type[] methodArgTypes = null)
        {
            var method = GetMethodWithCache(staticType, name, methodArgTypes);

            args ??= Array.Empty<object>();
            var parameters = method.GetParameters();
            if (args.Length < parameters.Length)
            {
                int suppliedLength = args.Length;
                Array.Resize(ref args, parameters.Length);
                for (int i = suppliedLength; i < parameters.Length; i++)
                    args[i] = Type.Missing;
            }

            return method.Invoke(null, args);
        }

        public static object GetFieldValue(this Type staticType, string name)
        {
            return GetField(staticType, name).GetValue(null);
        }

        public static bool TryGetFieldValue(this Type staticType, string name, out object value)
        {
            try
            {
                value = staticType.GetFieldValue(name);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static object GetFieldValueOrDefault(this Type staticType, string name)
        {
            if (staticType.TryGetFieldValue(name, out object value))
            {
                return value;
            }
            return default;
        }

        public static T GetFieldValue<T>(this Type staticType, string name)
        {
            return (T)staticType.GetFieldValue(name);
        }

        public static bool TryGetFieldValue<T>(this Type staticType, string name, out T value)
        {
            try
            {
                value = staticType.GetFieldValue<T>(name);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static T GetFieldValueOrDefault<T>(this Type staticType, string name)
        {
            if (staticType.TryGetFieldValue(name, out T value))
            {
                return value;
            }
            return default;
        }

        public static T InvokeMethod<T>(this object targetObj, string name, object[] args = null, Type[] methodArgTypes = null)
        {
            return (T)targetObj.InvokeMethod(name, args, methodArgTypes);
        }

        public static object InvokeMethod(this object targetObj, string name, object[] args = null, Type[] methodArgTypes = null)
        {
            var method = GetMethodWithCache(targetObj.GetType(), name, methodArgTypes);

            args ??= Array.Empty<object>();
            var parameters = method.GetParameters();
            if (args.Length < parameters.Length)
            {
                int suppliedLength = args.Length;
                Array.Resize(ref args, parameters.Length);
                for (int i = suppliedLength; i < parameters.Length; i++)
                    args[i] = Type.Missing;
            }

            return method.Invoke(targetObj, args);
        }

        public static T GetFieldValue<T>(this object targetObj, string name)
        {
            return (T)targetObj.GetFieldValue(name);
        }

        public static bool TryGetFieldValue<T>(this object targetObj, string name, out T value)
        {
            try
            {
                value = targetObj.GetFieldValue<T>(name);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static T GetFieldValueOrDefault<T>(this object targetObj, string name)
        {
            if (targetObj.TryGetFieldValue(name, out T value))
            {
                return value;
            }
            return default;
        }

        public static object GetFieldValue(this object targetObj, string name)
        {
            return targetObj.GetField(name).GetValue(targetObj);
        }

        public static bool TryGetFieldValue(this object targetObj, string name, out object value)
        {
            try
            {
                value = targetObj.GetFieldValue(name);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static object GetFieldValueOrDefault(this object targetObj, string name)
        {
            if (targetObj.TryGetFieldValue(name, out object value))
            {
                return value;
            }
            return default;
        }

        public static T GetPropertyValue<T>(this object targetObj, string name)
        {
            return (T)targetObj.GetPropertyValue(name);
        }

        public static bool TryGetPropertyValue<T>(this object targetObj, string name, out T value)
        {
            try
            {
                value = targetObj.GetPropertyValue<T>(name);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static T GetPropertyValueOrDefault<T>(this object targetObj, string name)
        {
            if (targetObj.TryGetPropertyValue(name, out T value))
            {
                return value;
            }
            return default;
        }

        public static object GetPropertyValue(this object targetObj, string name)
        {
            return targetObj.GetProperty(name).GetValue(targetObj);
        }

        public static bool TryGetPropertyValue(this object targetObj, string name, out object value)
        {
            try
            {
                value = targetObj.GetPropertyValue(name);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static object GetPropertyValueOrDefault(this object targetObj, string name)
        {
            if (targetObj.TryGetPropertyValue(name, out object value))
            {
                return value;
            }
            return default;
        }

        public static FieldInfo GetField(this object targetObj, string name)
        {
            return GetFieldWithCache(targetObj.GetType(), name);
        }

        public static PropertyInfo GetProperty(this object targetObj, string name)
        {
            return GetPropertyWithCache(targetObj.GetType(), name);
        }

        public static MethodInfo GetMethod(this object targetObj, string name, Type[] methodArgTypes = null)
        {
            return GetMethodWithCache(targetObj.GetType(), name, methodArgTypes);
        }

        public static FieldInfo GetField(this Type staticType, string name)
        {
            return GetFieldWithCache(staticType, name);
        }

        public static PropertyInfo GetProperty(this Type staticType, string name)
        {
            return GetPropertyWithCache(staticType, name);
        }

        public static MethodInfo GetMethod(this Type staticType, string name, Type[] methodArgTypes = null)
        {
            return GetMethodWithCache(staticType, name, methodArgTypes);
        }
    }
}
