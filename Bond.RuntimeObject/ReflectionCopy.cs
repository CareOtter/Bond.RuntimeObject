// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;

    internal static class ReflectionCopy
    {

        #region Public APIs

        /// <summary>
        /// Get a value indicating whether the Type is a bonded&lt;T>
        /// </summary>
        internal static bool IsRuntimeBonded(this Type type)
        {
            if (type.IsGenericType())
            {
                var definition = type.GetGenericTypeDefinition();
                return typeof(IRuntimeBonded<>).IsAssignableFrom(definition);
            }

            return false;
        }

        /// <summary>
        /// Gets the generic type arguments of a type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool ContainsRuntimeBonded(this Type type)
        {
            return type.GetGenericArguments().Any(x => x == typeof(IRuntimeBonded));
        }

        #endregion

        #region PCL compatibility

        internal static bool IsClass(this Type type)
        {
            return type.GetTypeInfo().IsClass;
        }

        static bool IsInterface(this Type type)
        {
            return type.GetTypeInfo().IsInterface;
        }

        internal static bool IsGenericType(this Type type)
        {
            return type.GetTypeInfo().IsGenericType;
        }

        static Type GetBaseType(this Type type)
        {
            if (type.IsInterface())
            {
                throw new ArgumentException("GetBaseType cannot be called on an interface, as there may be multiple base interfaces", "type");
            }

            return type.GetTypeInfo().BaseType;
        }

        internal static bool IsAssignableFrom(this Type type, Type that)
        {
            return type.GetTypeInfo().IsAssignableFrom(that.GetTypeInfo());
        }

        internal static MethodInfo FindMethod(this Type type, string name, params Type[] paramTypes)
        {
            var methods = type.GetDeclaredMethods(name);

            var result = (
                from method in methods
                let parameters = method.GetParameters()
                where parameters != null
                where parameters.Select(p => p.ParameterType).Where(t => !t.IsGenericParameter).SequenceEqual(paramTypes)
                select method).FirstOrDefault();
            
            if (result == null)
            {
                if (type.IsInterface())
                {
                    var interfaces = type.GetInterfaces();
                    var matchedMethods = interfaces.Select(x => x.FindMethod(name, paramTypes)).Where(x => x != null).ToList();

                    if (matchedMethods.Count > 1)
                    {
                        throw new AmbiguousMatchException("FindMethod found more than one matching method");
                    }
                    else
                    {
                        result = matchedMethods.FirstOrDefault();
                    }
                }
                else
                {
                    var baseType = type.GetBaseType();
                    if (baseType != null)
                        result = baseType.FindMethod(name, paramTypes);
                }
            }

            return result;
        }

        internal static MethodInfo GetMethod(this Type type, Type declaringType, string name, params Type[] paramTypes)
        {
            return declaringType.MakeGenericTypeFrom(type).FindMethod(name, paramTypes);
        }
        
        internal static ConstructorInfo GetConstructor(this Type type, params Type[] paramTypes)
        {
            var methods = type.GetDeclaredConstructors();

            return (
                from method in methods
                let parameters = method.GetParameters()
                where parameters != null
                where method.IsStatic == false
                where parameters.Select(p => p.ParameterType).SequenceEqual(paramTypes)
                select method).FirstOrDefault();
        }

        internal static PropertyInfo GetDeclaredProperty(this Type type, string name, Type returnType)
        {
            var property = type.GetDeclaredProperty(name);
            return (property != null && property.PropertyType == returnType) ? property : null;
        }

        internal static PropertyInfo GetDeclaredProperty(this Type type, Type declaringType, string name, Type returnType)
        {
            return declaringType.MakeGenericTypeFrom(type).GetDeclaredProperty(name, returnType);
        }

        static Type MakeGenericTypeFrom(this Type genericType, Type concreteType)
        {
            var typeArguments = concreteType.GetGenericArguments();
            if (concreteType.IsArray)
            {
                typeArguments = new[] { concreteType.GetElementType() };
            }

            var typeParameters = genericType.GetGenericParameters();

            if (typeArguments.Length == 2 && typeParameters.Length == 1)
                typeArguments = new[] { typeof(KeyValuePair<,>).MakeGenericType(typeArguments) };

            return genericType.MakeGenericType(typeArguments);
        }

        #endregion

        #region Internal

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="T">Input type of the lambda.</typeparam>
        /// <typeparam name="TResult">Return type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the MethodInfo for the "int Math::Abs(int)" overload, you can write:
        /// <code>(MethodInfo)Reflection.InfoOf((int x) => Math.Abs(x))</code>
        /// </example>
        static MemberInfo InfoOf<T, TResult>(Expression<Func<T, TResult>> expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            return InfoOf(expression.Body);
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="T">Input type of the lambda.</typeparam>
        /// <typeparam name="TResult">Return type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. Return null if that member is not a method. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the MethodInfo for the "int Math::Abs(int)" overload, you can write:
        /// <code>Reflection.MethodInfoOf((int x) => Math.Abs(x))</code>
        /// </example>
        internal static MethodInfo MethodInfoOf<T, TResult>(Expression<Func<T, TResult>> expression)
        {
            return InfoOf(expression) as MethodInfo;
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="T">Input type of the lambda.</typeparam>
        /// <typeparam name="TResult">Return type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. Return null if the member is not a FieldInfo. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the FieldInfo for the "int Foo::someField" field, you can write:
        /// <code>Reflection.FieldInfoOf((Foo f) => f.someField)</code>
        /// </example>
        internal static FieldInfo FieldInfoOf<T, TResult>(Expression<Func<T, TResult>> expression)
        {
            return InfoOf(expression) as FieldInfo;
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="TResult">Return type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the PropertyInfo of "DateTime DateTime::Now { get; }", you can write:
        /// <code>(PropertyInfo)Reflection.InfoOf(() => DateTime.Now)</code>
        /// </example>
        static MemberInfo InfoOf<TResult>(Expression<Func<TResult>> expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            return InfoOf(expression.Body);
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="TResult">Return type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. Return null if that member is not a method. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the MethodInfo for the "int Math::Abs(int)" overload, you can write:
        /// <code>Reflection.MethodInfoOf(() => Math.Abs(default(int)))</code>
        /// </example>
        internal static MethodInfo MethodInfoOf<TResult>(Expression<Func<TResult>> expression)
        {
            return InfoOf(expression) as MethodInfo;
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="T">Input type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the MethodInfo for the "void Console::WriteLine(string)" overload, you can write:
        /// <code>(MethodInfo)Reflection.InfoOf((string s) => Console.WriteLine(s))</code>
        /// </example>
        static MemberInfo InfoOf<T>(Expression<Action<T>> expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            return InfoOf(expression.Body);
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="T">Input type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. Return null if that member is not a method. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the MethodInfo for the "void Foo::DoThing(int)" overload, you can write:
        /// <code>Reflection.MethodInfoOf(() => Foo.DoThing(default(int)))</code>
        /// </example>
        internal static MethodInfo MethodInfoOf<T>(Expression<Action<T>> expression)
        {
            return InfoOf(expression) as MethodInfo;
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <typeparam name="T">Input type of the lambda.</typeparam>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. Return null if the member is not a generic method definition. An exception occurs if this node does not contain member information.</returns>
        /// <example>
        /// To obtain the generic method definition for some "void Foo::Bar&lt;T>(T)" overload, you can write:
        /// <code>Reflection.GenericMethodInfoOf(() => Foo.Bar(default(int)))</code>, which returns the definition Foo.Bar&lt;>
        /// </example>
        internal static MethodInfo GenericMethodInfoOf<T>(Expression<Action<T>> expression)
        {
            var methodInfo = MethodInfoOf(expression);
            return methodInfo == null ? null : methodInfo.GetGenericMethodDefinition();
        }

        /// <summary>
        /// Gets the reflection member information from the top-level node in the body of the given lambda expression.
        /// </summary>
        /// <param name="expression">Lambda expression to extract reflection information from</param>
        /// <returns>Member information of the top-level node in the body of the lambda expression. An exception occurs if this node does not contain member information.</returns>
        static MemberInfo InfoOf(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            MethodCallExpression mce;
            MemberExpression me;
            NewExpression ne;
            UnaryExpression ue;
            BinaryExpression be;

            if ((mce = expression as MethodCallExpression) != null)
            {
                return mce.Method;
            }
            else if ((me = expression as MemberExpression) != null)
            {
                return me.Member;
            }
            else if ((ne = expression as NewExpression) != null)
            {
                return ne.Constructor;
            }
            else if ((ue = expression as UnaryExpression) != null)
            {
                if (ue.Method != null)
                {
                    return ue.Method;
                }
            }
            else if ((be = expression as BinaryExpression) != null)
            {
                if (be.Method != null)
                {
                    return be.Method;
                }
            }

            throw new NotSupportedException("Expression tree type doesn't have an extractable MemberInfo object.");
        }


        static T GetAttribute<T>(this MemberInfo type)
            where T : class
        {
            return type.GetCustomAttributes(typeof(T), false).FirstOrDefault() as T;
        }

        internal static T GetAttribute<T>(this Type type)
            where T : class
        {
            // ReSharper disable once RedundantCast
            // This explicit cast is needed because when targeting non-portable runtime,
            // type.GetTypeInfo returns an object which is also a Type, causing wrong call.
            return GetAttribute<T>(type.GetTypeInfo() as MemberInfo);
        }

        #endregion
    }
}
