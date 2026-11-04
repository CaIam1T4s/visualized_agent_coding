# 异步编程与 .NET 工具链

桌面界面必须保持响应。网络请求、文件操作和长时间任务不应阻塞 UI 线程。C# 使用 `Task`、`async`/`await` 和取消令牌表达这类工作。

## 1. Task 与 async/await

`Task` 表示一个未来完成的异步操作；`Task<T>` 还会产生类型为 `T` 的结果。`await` 等待操作完成，但不会像同步等待那样占住当前线程。

```csharp
static async Task<string> LoadMessageAsync()
{
    await Task.Delay(500); // 示例中模拟耗时操作
    return "loaded";
}

string message = await LoadMessageAsync();
Console.WriteLine(message);
```

方法只要包含 `await`，通常就声明为 `async`，并返回 `Task` 或 `Task<T>`。事件处理器是常见例外，可写成 `async void`；普通业务方法不要用 `async void`，否则调用方无法等待它或正常观察异常。

## 2. 异步 I/O 与错误处理

真实文件和网络操作应优先使用异步 API，而不是用 `Task.Run` 包装同步 I/O。

```csharp
static async Task<string> ReadTextAsync(string path)
{
    try
    {
        return await File.ReadAllTextAsync(path);
    }
    catch (IOException ex)
    {
        Console.WriteLine($"读取失败：{ex.Message}");
        throw;
    }
}
```

`await` 时发生的异常会在等待位置重新抛出，因此可以用常规 `try/catch` 处理。需要向上层报告失败时，处理后重新抛出，不要返回看似成功的空结果。

## 3. 取消操作

取消通常是协作式的：调用方发出取消请求，被调用方定期检查并停止工作。

```csharp
static async Task CountAsync(CancellationToken cancellationToken)
{
    for (int i = 1; i <= 10; i++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine(i);
        await Task.Delay(300, cancellationToken);
    }
}

using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(1));

try
{
    await CountAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("操作已取消");
}
```

UI 中开始任务时创建 `CancellationTokenSource`，把 `Token` 传给下层；用户按“停止”时调用 `Cancel()`，任务完成后释放 source。取消不是强制杀线程，代码必须主动配合。

## 4. 并发与 UI 线程

`Task.WhenAll` 可等待多个独立任务：

```csharp
Task<string> first = FetchAsync("one");
Task<string> second = FetchAsync("two");
string[] results = await Task.WhenAll(first, second);
```

不要随意对 UI 绑定集合进行后台并发修改。桌面框架通常要求 UI 状态更新回到 UI 线程；Avalonia 提供 Dispatcher 相关 API。优先使用框架的异步 I/O，只有 CPU 密集计算确实需要并行时，才考虑 `Task.Run`，并通过性能测量确认收益。

## 5. 可枚举的异步事件流

`IAsyncEnumerable<T>` 适合逐步产出结果，例如模型逐字回复或工具进度：

```csharp
static async IAsyncEnumerable<string> StreamAsync(
    [System.Runtime.CompilerServices.EnumeratorCancellation]
    CancellationToken cancellationToken = default)
{
    foreach (string part in new[] { "Hello", " ", "world" })
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(100, cancellationToken);
        yield return part;
    }
}

await foreach (string part in StreamAsync())
{
    Console.Write(part);
}
```

这能让界面逐步显示内容，而不必等整段结果完成。

## 6. dotnet 命令行

.NET SDK 附带项目管理、编译和测试工具。常用命令：

```powershell
dotnet --info
dotnet new console -n LearningApp
cd LearningApp
dotnet run
dotnet build
dotnet test
dotnet format
```

`dotnet new` 创建项目，`dotnet run` 编译并运行，`dotnet build` 编译，`dotnet test` 执行测试，`dotnet format` 按项目格式规则格式化代码。包通常通过 NuGet 管理，项目依赖会记录在 `.csproj` 中。

## 7. VS Code 调试与格式化

安装 Microsoft 的 C# Dev Kit 和兼容项目目标框架的 .NET SDK。打开含 `.csproj` 的文件夹后，可在运行与调试视图启动程序、设置断点、查看变量。`.editorconfig` 用于共享格式与代码风格；可用 **Format Document** 格式化当前文件，也可在设置中启用保存时格式化。

## 8. 适用于当前桌面项目的练习

1. 写一个异步方法，模拟等待后返回结果。
2. 增加 `CancellationToken`，验证停止后不再产出结果。
3. 定义 `IAgentClient` 接口和 `FakeAgentClient` 实现。
4. 用 `IAsyncEnumerable<AgentEvent>` 逐步产生文本和状态事件。
5. 在 ViewModel 中消费事件，并确保 UI 更新通过 Avalonia Dispatcher 调度。
6. 为成功、取消和失败路径编写单元测试。

掌握这些内容后，再进入 Avalonia 的 XAML、数据绑定、ViewModel 和自定义绘制学习。
