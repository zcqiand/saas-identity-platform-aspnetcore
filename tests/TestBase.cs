using Saas.Identity.AspNetCore.Controllers.Implementation;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 所有 controller test 类继承此基类。
/// 每个测试构造时调 InMemoryStore.Reset() 重置 fixture，
/// 避免 xUnit 不确定执行顺序时测试间状态污染。
///
/// M10.Database — controller 改走 DbContext 后，in-memory fixture 仍可工作（仍走 InMemoryStore
/// fixture 调用），但实际 controller 走 DbContext 路径需要真 PG。Phase 5 的 @SpringBootTest +
/// Testcontainers 替换（M10.F01 / M10.F02 / M10.F03 共享 plan §D 风险 #10）。
/// </summary>
public abstract class TestBase
{
    protected TestBase()
    {
        InMemoryStore.Reset();
    }
}