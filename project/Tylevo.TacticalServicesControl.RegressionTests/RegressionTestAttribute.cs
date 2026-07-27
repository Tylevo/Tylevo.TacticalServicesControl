using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class RegressionTestAttribute : Attribute
{
}
