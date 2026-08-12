// xUnit 序列化所有 controller 测试，避免 InMemoryStore 并发修改。
// xUnit 默认 different test classes run in parallel.
// 用 [CollectionDefinition] + [Collection("Sequential")] 把所有测试类串行。
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]