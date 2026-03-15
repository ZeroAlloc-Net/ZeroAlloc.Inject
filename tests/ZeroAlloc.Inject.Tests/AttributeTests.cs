using System.Reflection;

namespace ZeroAlloc.Inject.Tests;

public class AttributeTests
{
    [Fact]
    public void TransientAttribute_IsServiceAttribute()
    {
        var attr = new TransientAttribute();
        Assert.IsAssignableFrom<ServiceAttribute>(attr);
    }

    [Fact]
    public void ScopedAttribute_IsServiceAttribute()
    {
        var attr = new ScopedAttribute();
        Assert.IsAssignableFrom<ServiceAttribute>(attr);
    }

    [Fact]
    public void SingletonAttribute_IsServiceAttribute()
    {
        var attr = new SingletonAttribute();
        Assert.IsAssignableFrom<ServiceAttribute>(attr);
    }

    [Fact]
    public void ServiceAttribute_DefaultValues()
    {
        var attr = new TransientAttribute();
        Assert.Null(attr.As);
        Assert.Null(attr.Key);
        Assert.False(attr.AllowMultiple);
    }

    [Fact]
    public void ServiceAttribute_SetProperties()
    {
        var attr = new TransientAttribute
        {
            As = typeof(IDisposable),
            Key = "mykey",
            AllowMultiple = true
        };
        Assert.Equal(typeof(IDisposable), attr.As);
        Assert.Equal("mykey", attr.Key);
        Assert.True(attr.AllowMultiple);
    }

    [Fact]
    public void ZeroAllocInjectAttribute_StoresMethodName()
    {
        var attr = new ZeroAllocInjectAttribute("AddMyServices");
        Assert.Equal("AddMyServices", attr.MethodName);
    }

    [Fact]
    public void TransientAttribute_TargetsClassOnly()
    {
        var usage = typeof(TransientAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .First();

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void DecoratorAttribute_CanBeInstantiated()
    {
        var attr = new DecoratorAttribute();
        Assert.IsType<DecoratorAttribute>(attr);
    }

    [Fact]
    public void DecoratorAttribute_TargetsClassOnly()
    {
        var usage = typeof(DecoratorAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .First();

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
