using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SharpGTA;

namespace SharpGTA.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string filter = args.Length == 0 ? null : args[0];
            List<MethodInfo> tests = new List<MethodInfo>();
            Type[] types;
            try
            {
                types = typeof(Program).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types;
            }

            Array.Sort(types, delegate(Type left, Type right)
            {
                return string.CompareOrdinal(left == null ? "" : left.FullName, right == null ? "" : right.FullName);
            });

            foreach (Type type in types)
            {
                if (type == null)
                    continue;
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                Array.Sort(methods, delegate(MethodInfo left, MethodInfo right)
                {
                    return string.CompareOrdinal(left.Name, right.Name);
                });
                foreach (MethodInfo method in methods)
                {
                    if (method.GetCustomAttributes(typeof(TestAttribute), false).Length == 0)
                        continue;
                    string name = type.FullName + "." + method.Name;
                    if (filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        tests.Add(method);
                }
            }

            int failed = 0;
            StopwatchTimer timer = new StopwatchTimer();
            foreach (MethodInfo test in tests)
            {
                string name = test.DeclaringType.FullName + "." + test.Name;
                timer.Restart();
                try
                {
                    object instance = test.IsStatic ? null : Activator.CreateInstance(test.DeclaringType);
                    test.Invoke(instance, null);
                    Console.WriteLine("PASS " + name + " (" + timer.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms)");
                }
                catch (Exception exception)
                {
                    failed++;
                    TargetInvocationException invocation = exception as TargetInvocationException;
                    if (invocation != null && invocation.InnerException != null)
                        exception = invocation.InnerException;
                    Console.WriteLine("FAIL " + name);
                    Console.WriteLine(exception.ToString());
                }
            }

            Console.WriteLine(Branding.Name + " tests: " + (tests.Count - failed).ToString(CultureInfo.InvariantCulture) +
                " passed, " + failed.ToString(CultureInfo.InvariantCulture) + " failed, " +
                tests.Count.ToString(CultureInfo.InvariantCulture) + " total.");
            return failed == 0 && tests.Count != 0 ? 0 : 1;
        }

        private sealed class StopwatchTimer
        {
            private System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

            public long ElapsedMilliseconds
            {
                get { return watch.ElapsedMilliseconds; }
            }

            public void Restart()
            {
                watch.Reset();
                watch.Start();
            }
        }
    }
}
