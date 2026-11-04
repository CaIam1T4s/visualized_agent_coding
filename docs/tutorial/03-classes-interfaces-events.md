# 类、接口、委托与事件

桌面应用通常需要把界面、数据和服务分开。C# 的类与接口用于组织职责，委托和事件用于让对象在不紧密耦合的情况下通知彼此。

## 1. 类、构造函数与属性

类描述对象的数据和行为。构造函数负责建立有效初始状态；属性提供受控的数据访问。

```csharp
public sealed class Conversation
{
    public string Id { get; }
    public string Title { get; private set; }

    public Conversation(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("标题不能为空", nameof(title));

        Title = title;
    }
}
```

`private set` 表示外部可以读取属性，但只有类自身能修改。封装能让对象保持规则一致，例如标题不能被改为空白。

## 2. 组合优先于继承

组合是让对象持有并使用另一个对象；继承表达“是一种”的关系。应用服务通常通过组合依赖接口，便于替换实现和测试。

```csharp
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

public sealed class RunLogger
{
    private readonly IClock _clock;

    public RunLogger(IClock clock) => _clock = clock;

    public void Log(string message) =>
        Console.WriteLine($"[{_clock.Now:HH:mm:ss}] {message}");
}
```

测试时可提供固定时间的 `IClock` 实现，避免测试依赖真实时钟。

## 3. 接口与依赖注入

接口定义能力契约，类负责实现。调用者依赖抽象，不必知道具体服务如何工作。

```csharp
public interface IAgentClient
{
    Task<string> ReplyAsync(string prompt, CancellationToken cancellationToken);
}

public sealed class FakeAgentClient : IAgentClient
{
    public async Task<string> ReplyAsync(string prompt, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return $"Echo: {prompt}";
    }
}
```

DI（依赖注入）容器可以在应用启动时注册实现，再把它提供给需要的对象。也可以在小程序中手动构造依赖；关键是不要让业务类自己到处创建具体客户端。

## 4. 委托与 lambda

委托是一种可保存方法引用的类型。`Action` 表示不返回值的操作，`Func<...>` 表示有返回值的函数。

```csharp
Action<string> write = text => Console.WriteLine(text);
Func<int, int, int> add = (left, right) => left + right;

write("ready");
Console.WriteLine(add(2, 3));
```

LINQ 的 lambda 以及按钮回调都建立在委托上。理解“把行为作为参数传递”即可开始使用。

## 5. 事件

事件是发布者向订阅者广播通知的机制。订阅者只需关心通知，不需要发布者持有它的具体类型。

```csharp
public sealed class ProgressSource
{
    public event EventHandler<int>? ProgressChanged;

    public void Report(int percent)
    {
        ProgressChanged?.Invoke(this, percent);
    }
}

var source = new ProgressSource();
source.ProgressChanged += (_, percent) => Console.WriteLine($"{percent}%");
source.Report(50);
```

`?.Invoke` 表示只有存在订阅者时才触发。长期存活的发布者可能通过事件引用订阅者，因此在适当时机取消订阅，避免对象无法释放或收到过期通知。

Avalonia 控件也使用事件处理鼠标、键盘和点击；复杂页面更常使用命令与 ViewModel，而不是把所有业务逻辑放在事件处理函数里。

## 6. 访问修饰符与封装

- `public`：其他代码可访问。
- `private`：仅当前类型可访问。
- `protected`：当前类型及派生类型可访问。
- `internal`：同一程序集内可访问。

默认应尽量缩小可见范围。只暴露调用方需要的操作，减少对象被随意改坏的机会。

## 7. 异常与资源释放

用 `using` 确保实现 `IDisposable` 的资源及时释放：

```csharp
using var writer = new StreamWriter("output.txt");
writer.WriteLine("saved");
```

离开作用域时会自动释放资源。对异步资源（实现 `IAsyncDisposable`）使用 `await using`。不要把所有异常都捕获后忽略；应在能恢复、记录或转换错误的边界处理。

下一篇：[异步编程与 .NET 工具链](04-async-dotnet.md)。
