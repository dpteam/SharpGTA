using System;
using System.Globalization;

namespace SharpGTA.Tests
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TestAttribute : Attribute
    {
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message ?? "Expected true.");
        }

        public static void True(bool condition)
        {
            True(condition, null);
        }

        public static void False(bool condition, string message)
        {
            True(!condition, message ?? "Expected false.");
        }

        public static void False(bool condition)
        {
            False(condition, null);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException((message == null ? "" : message + " ") +
                    "Expected <" + Convert.ToString(expected, CultureInfo.InvariantCulture) +
                    "> but got <" + Convert.ToString(actual, CultureInfo.InvariantCulture) + ">.");
            }
        }

        public static void Equal<T>(T expected, T actual)
        {
            Equal(expected, actual, null);
        }

        public static void NotNull(object value, string message)
        {
            True(value != null, message ?? "Expected a non-null value.");
        }

        public static void NotNull(object value)
        {
            NotNull(value, null);
        }

        public static void Near(float expected, float actual, float epsilon, string message)
        {
            if (float.IsNaN(actual) || Math.Abs(expected - actual) > epsilon)
            {
                throw new InvalidOperationException((message == null ? "" : message + " ") +
                    "Expected " + expected.ToString("R", CultureInfo.InvariantCulture) +
                    " +/- " + epsilon.ToString("R", CultureInfo.InvariantCulture) +
                    " but got " + actual.ToString("R", CultureInfo.InvariantCulture) + ".");
            }
        }

        public static void Near(float expected, float actual)
        {
            Near(expected, actual, 0.0001f, null);
        }

        public static void Near(float expected, float actual, float epsilon)
        {
            Near(expected, actual, epsilon, null);
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            if (action == null)
                throw new ArgumentNullException("action");
            try
            {
                action();
            }
            catch (T exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Expected " + typeof(T).FullName +
                    " but got " + exception.GetType().FullName + ".", exception);
            }
            throw new InvalidOperationException("Expected " + typeof(T).FullName + " but no exception was thrown.");
        }

        public static void Fail(string message)
        {
            throw new InvalidOperationException(message ?? "Test failed.");
        }
    }
}
