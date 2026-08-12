namespace Saas.Identity.AspNetCore.Tests.Harness;

/// <summary>
/// Attribute form for xUnit — equivalent to java @Fn.
/// Read by HarnessTraceListener to write .state/trace.json.
/// Note: xUnit uses [Trait("Fn","M01.F01.I01")] by convention;
/// this attribute is also accepted.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class FnAttribute : Attribute
{
    public string[] Fns { get; }
    public FnAttribute(params string[] fns) { Fns = fns; }
}