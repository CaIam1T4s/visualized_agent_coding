# C# 常用语法：从 C++ 快速上手

本文面向有 C++ 基础的读者，重点介绍日常 C# 代码中常见的语法差异。示例可放入控制台项目的 `Program.cs` 运行。

## 1. 程序入口与命名空间

现代 .NET 控制台项目通常使用顶级语句，不必手动声明 `Main`：

```csharp
using System;

Console.WriteLine("Hello, C#");
```

这相当于编译器帮你生成了程序入口。大型程序仍可显式写出 `Program` 类和 `Main` 方法。命名空间用于组织类型，常见写法是 `namespace MyApp;`。

## 2. 变量、类型推断与常量

C# 是静态类型语言。`var` 只让编译器从初始化表达式推断类型，并不是动态类型；变量的类型确定后不能改变。

```csharp
int count = 3;
var message = "ready"; // 推断为 string
const double Pi = 3.14159;

count += 1;
Console.WriteLine($"{message}: {count}, pi={Pi}");
```

常见类型包括 `int`、`long`、`double`、`decimal`、`bool`、`char` 和 `string`。`decimal` 常用于需要十进制精确表示的金额计算。

## 3. 字符串插值

在字符串前加 `$`，即可在 `{}` 中嵌入表达式：

```csharp
string name = "Ada";
int a = 1;
int b = 1;
Console.WriteLine($"{name}: {a + b}"); // Ada: 2
```

字符串拼接仍可使用 `+`，但格式化输出通常优先使用插值字符串。花括号中的表达式先求值，再转换成文本。

## 4. 条件、循环与模式匹配

```csharp
int score = 82;
string grade = score >= 60 ? "pass" : "fail";

if (score >= 90)
{
    Console.WriteLine("excellent");
}
else if (score >= 60)
{
    Console.WriteLine("passed");
}
else
{
    Console.WriteLine("try again");
}

for (int i = 0; i < 3; i++)
{
    Console.WriteLine(i);
}

foreach (string item in new[] { "one", "two" })
{
    Console.WriteLine(item);
}
```

条件表达式 `condition ? a : b` 适合简单二选一。`foreach` 用于遍历集合，不需要自行管理迭代器索引。

模式匹配可以简洁检查类型和值：

```csharp
object value = 42;
if (value is int number && number > 0)
{
    Console.WriteLine($"positive integer: {number}");
}
```

## 5. 方法与参数

方法声明需要写明返回类型和参数类型。`void` 表示不返回值。

```csharp
static int Add(int left, int right)
{
    return left + right;
}

static void MainExample()
{
    int total = Add(2, 3);
    Console.WriteLine(total);
}
```

表达式体适合短方法：

```csharp
static int Double(int value) => value * 2;
```

可选参数需要给出默认值；命名参数可以让调用处更清楚：

```csharp
static string Greet(string name, string greeting = "Hello") => $"{greeting}, {name}!";
Console.WriteLine(Greet(name: "Lin", greeting: "Welcome"));
```

## 6. 空值与可空类型

引用类型默认不应被赋为 `null`（启用可空引用类型时，编译器会检查）；用 `?` 明确表示可能为空。值类型如 `int` 需要写成 `int?` 才能容纳 `null`。

```csharp
string? nickname = null;
string displayName = nickname ?? "Guest";
int? selectedCount = null;

Console.WriteLine(displayName);
Console.WriteLine(selectedCount.HasValue ? selectedCount.Value : 0);
```

`??` 是空合并运算符：左侧非空时使用左侧，否则使用右侧。不要仅为了消除警告而到处添加 `!`；应确认值确实非空。

## 7. 异常基础

异常用于表示无法按正常流程完成的操作。捕获具体异常，并在能处理或补充上下文的地方捕获：

```csharp
try
{
    int value = int.Parse("123");
    Console.WriteLine(value);
}
catch (FormatException ex)
{
    Console.WriteLine($"输入不是有效整数：{ex.Message}");
}
```

如果当前方法无法恢复，通常让异常继续向上抛出，比吞掉异常或只打印后继续执行更可靠。

## 8. 与 C++ 的重要区别

- 普通对象通常由 .NET 垃圾回收器管理；不要手动 `delete`。
- `class` 是引用类型，赋值会复制对象引用；`struct` 是值类型，赋值通常复制值。
- 字符串 `string` 不可变；修改操作会产生新字符串。
- 属性、事件、LINQ 和异步任务是 C# 常见惯用方式。
- 资源（文件、流、锁等）需确定性释放，通常用 `using`，而不是依赖垃圾回收时机。

下一篇：[类型、集合与 LINQ](02-types-collections-linq.md)。
