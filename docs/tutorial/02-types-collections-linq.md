# C# 类型、集合与 LINQ

C# 的类型系统帮助编译器提前发现错误。写桌面应用时，集合用于保存消息、会话和积木节点；LINQ 则用于筛选和转换集合。

## 1. 引用类型与值类型

`class` 定义引用类型，变量保存对象引用；`struct` 定义值类型，适合小型、通常不可变的数据值。初学阶段优先使用 `class`，只有明确需要值语义时再选 `struct`。

```csharp
public sealed class Agent
{
    public string Name { get; set; } = "";
}

var first = new Agent { Name = "Helper" };
var second = first;
second.Name = "Editor";
Console.WriteLine(first.Name); // Editor：两者引用同一个对象
```

不可变数据可以使用 `record`：

```csharp
public record Point(int X, int Y);

var p1 = new Point(2, 4);
var p2 = p1 with { X = 5 };
Console.WriteLine(p2); // Point { X = 5, Y = 4 }
```

`with` 会创建一个修改后副本，不会改变 `p1`。

## 2. 数组与常用泛型集合

数组长度固定；`List<T>` 可动态增删；`Dictionary<TKey, TValue>` 按键查找；`HashSet<T>` 保存不重复元素。

```csharp
using System.Collections.Generic;

var messages = new List<string> { "hello", "working" };
messages.Add("done");

var statusById = new Dictionary<int, string>
{
    [101] = "running",
    [102] = "complete"
};

var tags = new HashSet<string> { "C#", "Avalonia", "C#" };
Console.WriteLine(messages.Count);       // 3
Console.WriteLine(statusById[101]);      // running
Console.WriteLine(tags.Count);           // 2
```

泛型 `<T>` 让同一集合实现可用于不同类型，同时保留编译期类型检查。尽量声明需要的接口类型，例如 `IReadOnlyList<string>`，以限制调用方只能读取。

## 3. 集合初始化与对象初始化

集合初始化器适合小型固定数据；对象初始化器可在创建对象时设置属性：

```csharp
var names = new List<string> { "Ada", "Lin" };

var task = new TaskInfo
{
    Id = "task-1",
    Title = "Build UI",
    IsRunning = true
};

public sealed class TaskInfo
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsRunning { get; set; }
}
```

`init` 属性只能在初始化阶段设置，适合创建后不应随意更改的数据；`set` 则允许后续赋值。

## 4. LINQ 查询

LINQ 可筛选、排序、投影集合。常见方法包括 `Where`、`Select`、`OrderBy`、`Any`、`FirstOrDefault` 和 `ToList`。

```csharp
var scores = new[] { 72, 95, 58, 88 };

var passing = scores
    .Where(score => score >= 60)
    .OrderByDescending(score => score)
    .Select(score => $"Score: {score}")
    .ToList();

foreach (var item in passing)
{
    Console.WriteLine(item);
}
```

`score => ...` 是 lambda 表达式，可理解为一个简短的匿名函数。LINQ 查询常是延迟执行的：在枚举结果时才真正执行；若需固定结果或多次遍历，调用 `ToList()`。

## 5. 枚举与模式匹配

枚举适合表示有限状态，避免散落的数字或拼写不一致的字符串：

```csharp
public enum RunStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

RunStatus status = RunStatus.Running;
string label = status switch
{
    RunStatus.Pending => "等待中",
    RunStatus.Running => "运行中",
    RunStatus.Completed => "已完成",
    RunStatus.Failed => "失败",
    _ => "未知"
};
Console.WriteLine(label);
```

`switch` 表达式会根据值选择结果。`_` 是兜底分支；对枚举来说保留兜底也能应对未来新增状态。

## 6. 泛型方法与约束

泛型方法可以针对多种类型复用逻辑：

```csharp
static T FirstOr<T>(IReadOnlyList<T> values, T fallback)
{
    return values.Count > 0 ? values[0] : fallback;
}

var firstName = FirstOr(new[] { "Ada", "Lin" }, "Guest");
Console.WriteLine(firstName);
```

方法可用 `where T : ...` 添加约束，例如要求类型实现某个接口。不要为了“泛型化”而过早增加复杂约束；先确认逻辑确实需要复用于多种类型。

## 7. 何时避免 LINQ

简单集合查询适合 LINQ。若查询中有副作用、复杂分支、需要逐步调试，普通 `foreach` 往往更清楚。避免在性能敏感的热路径中反复创建大量临时集合，先测量再优化。

下一篇：[类、接口、委托与事件](03-classes-interfaces-events.md)。
