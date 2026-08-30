namespace RecompOne.Runtime.Modding;

public abstract class FunctionHookAttribute : Attribute
{
    public string Overlay { get; }
    public string? Function { get; }
    public uint Address { get; set; }

    protected FunctionHookAttribute(string overlay, string? function)
    {
        Overlay = overlay;
        Function = function;
    }
}

/// <summary>Replaces a game function e add a leading orig parameter to be able to call the original.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ReplaceAttribute : FunctionHookAttribute
{
    public ReplaceAttribute(string overlay, string? function = null) : base(overlay, function)
    {
    }
}

/// <summary>runs before a game function, return flase to skip the original, void methods default to true</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PreHookAttribute : FunctionHookAttribute
{
    public PreHookAttribute(string overlay, string? function = null) : base(overlay, function)
    {
    }
}

/// <summary>Runs after a game function</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PostHookAttribute : FunctionHookAttribute
{
    public PostHookAttribute(string overlay, string? function = null) : base(overlay, function)
    {
    }
}