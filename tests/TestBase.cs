using Saas.Identity.AspNetCore.Controllers.Implementation;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 所有 controller test 类继承此基类。
/// 每个测试构造时调 InMemoryStore.Reset() 重置 fixture，
/// 避免 xUnit 不确定执行顺序时测试间状态污染。
/// </summary>
public abstract class TestBase
{
    protected TestBase()
    {
        InMemoryStore.Reset();
    }
}