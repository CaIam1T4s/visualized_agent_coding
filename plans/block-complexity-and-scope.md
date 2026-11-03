# 蓝图式节点栈编辑方案（Python 唯一真源）

## Context

当前节点编辑器只有 `start` 区域的只读投影：现有 AST 投影服务把 AST 映射成节点树，现有编辑器模型压平成带缩进的行，只有移动/旋转用了自绘节点控件。复杂条件、自定义函数和注释在嵌套式节点里成本过高。

讨论后的方向不是完整 Blueprint 自由图，而是：

> **Python 仍是唯一真源；图形侧采用「执行链 + 类型化节点 + 自由坐标」的节点栈。**
> 图只是 Python 的结构化视图，不是第二套运行时，也不把坐标变成语义。

## 核心决策

1. Python 源码决定语句顺序、作用域、节点存在性和代码生成结果。
2. 每个事件/方法有独立画布；类定义页负责导航、类属性和方法/事件入口。
3. 控制流节点接受一个或多个布尔子输入；每个布尔输入对应一个 `exec` 输出，表示该表达式为真时执行的语句。循环只有一个子输入和一个 `exec` 输出（`while` 为布尔条件，`for` 为范围/列表）；分支底部有默认 `exec` 输出，表示所有条件都不满足时执行，留空表示不生成语句。
4. 节点允许自由 x/y，但拖拽首版只改坐标，不改顺序；顺序由 Python 决定。
5. Python 编辑后不在输入时实时解析，只在切换到图形化视图时重新投影。
6. 表达式由类型化值节点连接构成；只有基础表达式输入口允许文本编辑，不做“展开成另一张图”的独立模式。
7. 不支持的结构保留为代码块，不静默丢弃源码或注释。

## 1. 总体模型

### 1.1 节点与源码

- 每个**语句节点**保存区域 ID + UTF-16 源码区间，不只保存行号；编辑后行号会漂移。
- 每个**表达式节点**保存父语句内部的源码子区间，它通常和父语句共享同一行。
- 节点不要求 AST 有稳定 ID；跨刷新用「区域 + 节点类型 + 结构路径 + 规范化源码片段」做序列 diff。
- 代码生成和运行时分离：代码生成按节点结构输出 Python；运行时仍由 CPython 执行，不从节点图另写解释器。

### 1.2 四种导线关系

不要把所有子连线混成一种。节点端口至少分四类：

| 关系 | 方向 | 用途 |
|---|---|---|
| `Next` | 一进一出 | 同一语句链中的下一条语句 |
| `Value` | 多个、有类型 | 参数、条件、操作数、返回值 |
| `Exec` | 一个或多个输出 | 控制流节点的条件分支体；每个输出与一个布尔子输入配对，循环只有一个 |
| `Annotation` | 无输入、只绑定 | 注释节点 |

- `break` / `return` / `pass` 是语句节点，不是参数；`break/continue` 只能出现在循环体，`return` 只能出现在方法/函数体。
- 注释节点只走 `Annotation`，绑定到具体语句或表达式节点，不占 `Next`，也不作为 `Value` 输入。
- `def` / `class` 的体在定义时不会执行，只被编译；执行链只描述代码生成和语句顺序，不替代运行时。

#### 端口类型

| 节点 | 输入 | 输出 |
|---|---|---|
| 语句节点 | `<exec>` | `<exec>` |
| 控制流节点 | `<exec>`；一个或多个布尔子输入 | 每个布尔输入对应一个 `<exec>` 输出；分支底部默认 `<exec>` |
| 循环节点 | `<exec>`；一个循环子输入（while 为 bool，for 为范围/列表值） | 一个 `<exec>` 输出 |
| `return` | `<exec>`、可选值输入 | 无 |
| `break` / `continue` | `<exec>` | 无 |
| 数据/算术/布尔/调用表达式 | 对应值输入 | `number`、`text`、`boolean`、`none`、`list`、`object`、`any` |
| 注释节点 | 无 | 无端口，仅 `Annotation` 绑定 |

规则：

- 语句节点只输出 `<exec>`，不直接输出值。需要返回值时用“调用表达式”节点，而不是“调用语句”节点。
- 值节点没有 `<exec>` 端口，它的求值发生在被父表达式引用时；不要让值节点单独挂在执行链上。
- `<exec>` 只能接 `<exec>`；值只能接兼容值类型。类型未知时用 `any`，允许连接但提示。
- 函数调用分「调用语句」（有 `<exec>` 进出，丢弃返回值）和「调用表达式」（只有值输入/输出）。
- 控制流节点的每个布尔子输入都对应一个 `exec` 输出；循环节点只有一个循环子输入和一个 `exec` 输出；分支底部默认 `exec` 表示所有条件都不满足时执行，留空则不生成语句。

### 1.3 节点模板

- **起始节点**：事件（启动、点击、消息、按键、计时器、克隆启动）、函数/类定义入口、`作为克隆体启动`。
- **数据节点**：数字、布尔值、`None`、字符串。
- **变量节点**：为每个变量创建一个模板，按其作用域分类（参数、局部、实例属性、类属性、项目变量、模块全局）；分类可自由展开/收起，支持搜索。
- **算术表达式**：四则运算、取模、幂。
- **布尔表达式**：比较（`==`、`!=`、`<`、`<=`、`>`、`>=`、`in`、`not in`、`is`、`is not`）、`and`、`or`、`not`、`bool()`。比较归入布尔表达式，不归入算术。
- **控制流**：`if/elif/else`、`while`、`for range`、`for each`、`match/case`、克隆相关控制。
- **控制流子语句**：`return`、`break`、`continue`、`pass`。
- **其他表达式**：函数调用、方法调用、属性读取。
- **运动/外观**：移动、旋转、滑行、造型、显示/隐藏等。

### 1.4 条件、参数与表达式

- 条件/参数是类型化 `Value` 输入点：条件接受返回布尔值的表达式，参数接受对应类型的值节点。
- 只有以**基础表达式**作为输入的参数可以使用可编辑表达式文本；基础表达式指字面量、变量和无需再拆分的简单表达式。条件口遵循同一规则：输入口一旦接收到输入导线，文本输入框自动禁用，改为显示已连接的值。
- 复杂表达式通过布尔/算术/调用等表达式节点和值导线组合。
- 值线必须无环；求值顺序固定为 Python 的从左到右和短路顺序。
- `and` / `or` 返回操作数本身，输出类型按 `any` 处理，不强行标成 `boolean`。

### 1.5 match

Python 使用 `match` 语句，不提供与源码无法对应的 `switch` 节点：

- `if/elif/else` 是分支节点：每个分支一个布尔子输入和对应 `exec` 输出，底部默认 `exec` 表示都不满足时执行。
- `match/case` 使用 `match` 节点：一个值输入，多个 `case` 段（模式、可选 guard、对应 `exec` 输出），底部默认 `_` 段。
- Python 没有 fall-through，每个 case 的输出独立，不需要 `break`。
- 首版支持字面量、`or` 模式和简单 guard；序列/映射/类模式等复杂模式回退代码块。

## 2. 画布与布局

### 2.1 每个起始节点一个画布

- 每个事件/方法/函数入口是一张独立画布，一条 `Next` 执行链。
- 类定义页作为导航：列出类属性、方法、事件；点击方法/事件进入对应画布。
- 方法/事件画布顶部显示所属类、方法名、参数和返回类型提示。
- 属性不单独做设置界面；属性列表在类定义页只读展示，通过变量模板选择。

### 2.2 控制流节点

控制流节点用布尔子输入和对应的 `exec` 输出表示分支：

```text
┌ IF · 如果 ─────────────────────────────────────────────┐
│ ▷ exec in                              exec out (next) ▷│
├────────────────────────────────────────────────────────┤
│ ▷ bool  条件 1  health > 0              exec body ▷     │
│ ▷ bool  条件 2  health == 0             exec body ▷     │
│         否则 / elif (bool 可空)          exec default ▷  │
└────────────────────────────────────────────────────────┘
```

- 这里的 Body 指某个 `exec` 输出接出的语句链，不是嵌套容器。
- `if/elif/else`：一个或多个布尔子输入，每个对应一个 `exec` 输出；底部默认 `exec` 表示所有条件都不满足时执行。
- 底部默认项的布尔输入为空时映射为 `else`；该项 Body 也为空时不生成语句。
- 循环节点（`while`、`for range`、`for each`）只有一个循环子输入和一个 `exec` 输出；`while` 的子输入是布尔条件，`for` 的子输入是范围/列表值，其他循环参数是普通值输入。Body 从该输出继续。
- `match/case` 使用独立 `match` 节点：值输入 + 多个 case 段 + 底部默认 `_` 段。
- `break/continue` 默认绑定最近循环，节点上显示目标循环摘要。
- `for/while ... else` 首版回退代码块，不复用分支默认项。

### 2.3 自由坐标

- 节点可拖到任意 x/y，吸附 8px 网格，提供对齐参考线和「自动排列」。
- 执行口之间的导线支持手动重连，重连后改变执行顺序并写回 Python；首版节点拖拽只改坐标，规则见 §2.6。
- 节点显示顺序编号；拖到前驱上方仍按编号执行，提示「视觉位置与执行顺序不一致」。
- 控制流节点的分支体从对应 `exec` 输出继续；节点高度由标题栏和条件行决定。

### 2.4 新节点默认填充

- 新节点默认接在前驱输出方向的前方；同一语句链排在前驱右侧，分支体从对应 `exec` 输出向右下延伸。
- 与后继重叠时，后继及后续节点整体让位，保留相对偏移。
- 没有前驱时放在画布或对应语句链的起点。
- 目标控制流节点折叠时，自动展开对应条件行并高亮新节点。
- 控制流节点高度由标题栏和条件行决定；Body 链独立布局，不改变父节点高度。

### 2.5 注释

- 独占行注释：注释节点通过 `Annotation` 绑定到下一条语句；没有下一条时绑定上一条。
- 行内注释：保留在所属语句的源码片段中，随语句移动。
- docstring：作为字符串表达式语句或专用 docstring 节点处理，不当作普通注释。
- 注释节点不参与执行链，不接收输入；删除/移动目标节点时，注释按绑定关系跟随。
- 首版不做自由漂浮贴纸。

### 2.6 拖拽重排与端点重连

节点拖拽首版只改坐标；执行顺序通过手动重连导线或显式重排改变。两种入口都必须写回 Python 源码，不能只改图上的线：

**A. 显式重排模式**

- 用插入槽或「目标节点左半/右半」决定 before/after，不按自由坐标就近推断。
- 用源码区间剪切/插入，不能用 `ast.unparse`，否则丢失注释、空行和格式。
- 跨作用域要重新缩进并校验 `break/return` 位置。
- 剪切 + 插入合并为一次撤销单元。

**B. 端点重连**

- `<exec>` 输出只能接 `<exec>` 输入；布尔条件输入只能接 `boolean` 或 `any`；其他值输入只能接兼容值类型。类型不匹配直接拒绝，并高亮冲突端口。
- 控制流节点的每个布尔子输入对应一个 `exec` 输出；循环只有一个循环子输入和一个 `exec` 输出；分支底部默认 `exec` 必须保留。
- 重连是语义重排，不是纯视图操作：新顺序必须写回 Python，不能只保存导线。
- 写回使用源码区间移动，保留注释、空行和原格式；跨方法/函数体/循环体重新缩进。
- 校验 `break/continue` 仍在循环内、`return` 仍在函数内、没有跨区域。
- 值线必须无环；执行线只允许结构化控制流。任意回边、不可归约控制流、从循环中间跳入都应拒绝。
- 首版重连只允许同方法、同层语句链；跨循环/分支边界后续配合显式插入槽再做。
- 重连完成后重新解析校验；导线仍由 Python 顺序推导，不作为语义真源持久化。

## 3. 类、方法与属性

### 3.1 类定义页

对象脚本本身是一个类，图形视图以对象/类为根：

```text
对象脚本「角色 A」  class Actor(Base)
├─ 类属性
│   └─ health = 100
├─ 方法
│   ├─ def initialize(self)      → 方法图
│   ├─ def heal(self, amount)    → 方法图
│   └─ def on_start(self)        → 事件图
└─ 事件
    ├─ on_click / on_message / on_key / on_timer / on_clone
```

普通用户脚本以「模块」为根，模块链上可有受管理导入、模块级变量、函数定义节点、类定义节点和模块级语句。

### 3.2 方法

- 方法节点属于类声明，拥有参数值口和方法体执行链。
- `self` 是实例方法隐式参数，不出现在参数列表；普通函数没有 `self`。
- `return` 是方法体终止节点，可有多个返回点。
- 方法调用从方法列表/搜索中选择，自身方法隐式传 `self`；不为每个方法创建左侧模板。

### 3.3 属性

- 类属性：类定义页中的声明节点，写回类体 `x = ...`。
- 实例属性：自动扫描整个类的 `self.xxx = ...`，不要求显式声明。
- 每个属性生成一个变量模板，按「类属性 / 实例属性」分组；分类可展开/收起，支持搜索。
- 「设置属性」节点可新建属性，写回 `self.x = ...`；不单独做属性设置界面。
- 在实例方法里写 `self.x` 会创建实例属性并遮蔽类属性，UI 明确提示；修改共享值必须显式 `Class.x = ...`。
- 属性列表区分声明类，继承来的属性显示声明类而非当前类。

### 3.4 导入模板

- 只对静态可解析签名生成模板：函数名、参数、类名和构造参数。
- 项目内用户脚本函数/类可生成可跳转定义节点；标准库/第三方库只生成调用节点，不生成可编辑函数体，也不执行导入反射。
- 导入类生成构造调用节点；方法和属性访问用通用节点加搜索选择，无法解析时回退通用调用节点 + 源码片段。
- 模板按模块分组、可搜索、按需生成，避免塞满调色板。

## 4. 变量

### 4.1 作用域

| 作用域 | 定义位置 | 读取生成 | 修改生成 | 生命周期 |
|---|---|---|---|---|
| 参数 | `def` 头部 | `name` | `name = ...` / `+=` | 一次调用 |
| 局部变量 | 方法体内首次赋值 | `name` | `name = ...` / `+=` | 一次调用 |
| 循环变量 | `for` 头部 | `name` | 默认只读，写入回退 | 循环后仍存在 |
| 实例属性 | 方法中 `self.x = ...` | `self.x` | `self.x = ...` / `+=` | 实例生命周期 |
| 类属性 | 类体中 `x = ...` | `Class.x` 或 `self.x` | `Class.x = ...` / `+=` | 类/进程 |
| 项目变量/列表 | 数据管理器，`project.json` | `get_variable("x")` | `set_variable` / `add_to_variable` / 列表 API | 一次运行 |
| 模块全局 | 普通脚本模块链赋值 | `x` | 函数内需 `global`，首版回退 | 进程 |

对象脚本不提供模块级变量；普通用户脚本才有模块全局。

### 4.2 定义、获取、修改

- **定义**：局部变量首次赋值即定义，不生成 `x = None`；项目变量由现有数据管理器定义；实例属性首次 `self.x = ...` 定义；类属性在类定义页定义。
- **新建入口**：变量模板分类中提供新建项目变量、新建局部变量、新建实例属性、新建类属性。
- **获取**：每个变量一个模板，按作用域分组；分类可展开/收起、支持搜索；拖入画布创建读取节点，模板菜单可创建设置/增量节点。
- **变量面板**：按参数、局部、实例属性、类属性、项目变量、模块全局分组；只显示当前位置可见的变量，但允许展开其他分组查看。
- **修改**：
  - 赋值：`x = value`、`self.x = value`、`Class.x = value`、`set_variable(...)`。
  - 增量：`+=`、`-=`、`*=`、`/=`；项目变量用 `add_to_variable`，列表用现有 API。
  - 结构性修改：首版只开放项目列表 API；`del`、`setattr`、Python 列表操作回退代码块。

### 4.3 显示与悬停

变量模板和节点标签都采用窄间距：

```text
amount   参数 · heal
health   实例 · Actor
health   类 · Base
total    局部 · on_start
i        循环 · on_start
score    项目
settings 模块
```

- 变量名正常字重，作用域淡色小字，窄间距，不用冒号拼成伪标识符。
- 对象名只放在画布标题/面包屑，不进入变量身份。
- 悬停格式：`<具体作用域>：<变量名> = <具体值>`。
  - 例：`项目变量：score = 0`
  - 例：`实例属性：health = 100`
  - 例：`局部变量：total = /`（编辑态无法静态确定时）
  - 运行预览中如能取得当前值，显示当前值；不把运行时值写回初始值。
- 实例属性/类属性同名且代码写 `self.x` 时，标为 `self · 动态` 并提示运行期先实例后类。
- 悬停第二行可显示生成代码，如 `self.health`、`Actor.health`、`get_variable("score")`。

### 4.4 遮蔽、重命名、删除

- 遮蔽警告必须保留：`self.x` 读实例属性遮蔽类属性、局部变量与项目变量同名等。
- 项目变量重命名复用现有 `ProjectDataReferenceService`。
- 实例/类属性重命名：首版做「当前类扫描 + 预览」，不跨文件。
- 删除项目变量走现有引用检查；删除属性首版只移除当前节点，发现其他方法引用时提示。
- 局部变量无持久身份，重命名只影响当前方法源码区间。

## 5. Python 同步

### 5.1 进入图形化时刷新

不在 Python 输入时实时解析。触发点：

- 切换到图形化模式
- 打开脚本/项目
- 显式「重新加载」
- 外部文件冲突确认后重载

流程：

1. 读取当前未保存的 `PythonCode`，调用 `python.nodes`；过渡期兼容 `python.blocks`。
2. 与上一次节点快照做同级序列 diff（LCS/Myers），匹配节点保留 x/y、折叠、选中、条件行状态。
3. 新增节点按默认位置规则插入；删除节点移除。
4. 语法错误时保留上一次有效树，显示「源码有语法错误，节点暂未更新」；首次进入无法解析时显示错误状态。
5. 图形化视图内拖动只改布局；参数编辑只局部生成源码片段，不触发全量重新解析。
6. 切回 Python 时先把待写回片段写入编辑器缓冲，再切换。

### 5.2 写回

- 节点编辑生成该语句/表达式的新源码片段，走现有 `TextDocument.Replace`，保留原生撤销重做。
- 保存继续复用 `ObjectScriptSaveService` / `MultiRegionProjection` 的区域保存与冲突检查。
- 不整体重写未修改区域；不能用 `ast.unparse` 重建整个文件。
- 跨作用域修改需要重新缩进；首版插入/删除限定在已有作用域内，跨区域移动后续再做。

## 6. 布局持久化

- 布局保存到项目内 `blueprint/layout.json`，按脚本 ID + 区域 ID + 作用域路径记录节点指纹、局部坐标、折叠状态。
- 启动时若新路径不存在，读取旧 `blocks/layout.json` 并迁移；保留一个版本的回退读取。
- 布局是可丢弃的视图状态，不参与运行、诊断、源码合并；缺失/损坏按默认布局重建。
- 布局与源码分开写入；源码保存失败不回滚布局，布局也不覆盖源码。
- 节点指纹变化时用同级序列 diff 迁移旧坐标；匹配失败回到默认位置。

## 7. 首版范围与非目标

### 首版支持

- 事件/方法独立画布、类定义页导航、类属性/实例属性/实例方法/事件。
- 命令栈、`if/elif/else`、`while`、`for range`、`for each` 控制流节点。
- （第二阶段）简单 `match/case`：使用 `match` 语句，支持字面量、`or`、guard；复杂模式回退代码块。
- 数字/布尔/None/字符串、算术表达式、布尔表达式（含比较）、变量读取/赋值/增量修改。
- 变量模板按作用域分组，分类可展开/收起并支持搜索。
- `break`/`continue`/`return`/`pass`。
- 注释节点与行内注释保真。
- 项目变量/列表现有 API、类属性 `Class.x` 修改、实例属性自动扫描。
- 导入模板只生成构造/方法调用节点。
- 自由坐标、网格吸附、自动排列、布局持久化。
- Python → 图形化在切换时增量同步；图形化 → Python 局部写回。

### 非目标 / 回退代码块

- 图作为唯一真源、任意自由接线、不可归约控制流、显式回边导线。
- `try/except`、复杂 `match` 模式、`with`、推导式、`lambda`、生成器、`async`。
- `property`、装饰器、继承修改、元类、描述符。
- 模块全局写入、`global/nonlocal`、闭包变量专用节点。
- `del`、`setattr`、动态属性。
- 首版节点拖拽只改坐标；执行顺序通过手动重连导线改变，显式重排和跨边界重连按阶段实现。
- 自由漂浮注释贴纸。
- 像素级碰撞等无关功能。

## 8. 文件迁移/更名计划

旧文件最终迁移到新文件名；名称中的 `Block` 相关字样统一改为 `Node` 或 `Blueprint`，不再使用 `Scratch` 命名。

### 迁移原则

- **分阶段**：新增新名 → 迁移实现 → 更新引用 → 数据迁移 → 删除旧名。
- **过渡期保留转发层**：旧文件/旧类型只做转发或别名，不保留第二套逻辑。
- **不丢功能**：XAML `x:Class`、DI、`.sln`、测试、RPC、项目内布局文件都要同步迁移。
- **删除条件**：全量构建和测试通过、无旧引用、布局与协议兼容验证通过后，才删除旧文件。

### 迁移映射

| 旧文件/名称 | 新文件/名称 | 迁移方式 |
|---|---|---|
| `tools/python-helper/blocks_service.py` | `tools/python-helper/nodes_service.py` | 移动实现；旧模块临时 re-export |
| `tools/python-helper/tests/test_blocks.py` | `tools/python-helper/tests/test_nodes.py` | 重命名并更新测试命令 |
| `src/VisualizedAgentCoding.Application/Python/BlockProtocol.cs` | `.../Python/NodeProtocol.cs` | `Block*` 类型迁移为 `Node*`，过渡别名 |
| `src/VisualizedAgentCoding.Domain/Blocks/`（规划中） | `src/VisualizedAgentCoding.Domain/Blueprint/` | `BlockDocument`→`BlueprintDocument`、`BlockNode`→`BlueprintNode`、`BlockScope`→`NodeScope` |
| `src/VisualizedAgentCoding.Blocks.Core/`（规划中） | `src/VisualizedAgentCoding.Blueprint.Core/` | 项目重命名；更新 `.sln` 和项目引用 |
| `src/.../ViewModels/MainViewModel.Blocks.cs` | `.../MainViewModel.Blueprint.cs` | partial class 文件迁移，功能不变 |
| `src/.../ViewModels/BlockEditorModel.cs` | `.../BlueprintEditorModel.cs` | 类名/文件名迁移 |
| `src/.../Views/BlockEditorView.axaml(.cs)` | `.../BlueprintEditorView.axaml(.cs)` | 更新 `x:Class` 与所有引用 |
| `src/.../Controls/ScratchBlockControl.cs` | `.../Controls/BlueprintNodeControl.cs` | 类名/文件名迁移，去掉 Scratch 命名 |
| `src/.../Services/BlockLayoutStore.cs`（新增） | `.../Services/BlueprintLayoutStore.cs` | 新文件直接使用新名 |
| `tests/.../BlockProjectionTests.cs` | `.../BlueprintProjectionTests.cs` | 重命名并更新测试引用 |
| `python.blocks` RPC | `python.nodes` | 过渡期双名；新版优先 `nodes`，旧 Helper 回退 `blocks` |
| `blocks/layout.json` | `blueprint/layout.json` | 读取旧路径并迁移，写新路径；保留一个版本的回退读取 |
| `Block*` / `Scratch*` 类型 | `Node*` / `Blueprint*` | 过渡别名 + `Obsolete` 标记，最后删除 |

### 迁移阶段

1. **新增新名**：创建新文件/新类型，旧文件改为转发或包装，行为不变。
2. **迁移实现**：把逻辑移入新文件，更新 DI、XAML、`.sln`、测试和文档引用。
3. **数据迁移**：`blocks/layout.json` 读取后写入 `blueprint/layout.json`；RPC 同时接受 `python.blocks` 和 `python.nodes`。
4. **清理旧名**：确认无旧引用后删除旧文件、旧别名和旧测试；保留迁移说明和回滚方式。

### 迁移后的目标文件

- `tools/python-helper/nodes_service.py`：节点类型、源码范围、类/模块根、def/event、属性扫描、注释锚点、表达式子树。
- `tools/python-helper/tests/test_nodes.py`：复杂条件、循环、def/class、注释、属性、源码范围。
- `src/VisualizedAgentCoding.Application/Python/NodeProtocol.cs`：`SourceSpan`、节点种类、作用域、类成员、注释绑定。
- `src/VisualizedAgentCoding.Domain/Blueprint/`（新增）：`BlueprintDocument`、`BlueprintNode`、`NodePort`、`VariableRef`、`NodeScope`。
- `src/VisualizedAgentCoding.Blueprint.Core/`（新增）：布局、端口规则、序列 diff、源码片段替换计划、布局序列化。
- `src/VisualizedAgentCoding.App/Controls/BlueprintWorkspace.cs`（新增）：单控件绘制节点、控制流分支行、可重连导线、命中测试、拖拽。
- `src/VisualizedAgentCoding.App/Controls/BlueprintNodeControl.cs`（由旧自绘控件迁移）：绘制节点、参数槽和颜色风格。
- `src/VisualizedAgentCoding.App/ViewModels/MainViewModel.Blueprint.cs`：节点文档、画布导航、切换时刷新。
- `src/VisualizedAgentCoding.App/ViewModels/BlueprintEditorModel.cs`：节点集合、类定义页、选中/折叠/画布状态。
- `src/VisualizedAgentCoding.App/Views/BlueprintEditorView.axaml`：类定义页导航、每事件画布、变量模板分组/搜索/悬停。
- `src/VisualizedAgentCoding.App/Services/BlueprintLayoutStore.cs`（新增）：`blueprint/layout.json` 读写与旧路径迁移。
- `src/VisualizedAgentCoding.Application/Editor/ObjectScripts/`：类成员/属性索引、区域写回复用。
- `src/VisualizedAgentCoding.App/Composition/AnalysisComposition.cs`：注入 blueprint projection/layout 服务。
- `tests/VisualizedAgentCoding.App.Tests/BlueprintProjectionTests.cs`：投影、布局、同步、迁移、变量显示测试。
- 文档：`docs/project-plans.md`、`docs/python-visual-editor-plan.md` 更新命名和迁移进度。

## 9. Reuse

- `MultiRegionProjection.ViewRegionSources()`、稳定区域 ID、`IEditorProjection` 行列映射。
- `ObjectScriptSaveService` / `ObjectScriptSavePlan` 的可信区域保存与冲突检查。
- `IPythonBridge` 的 `python.nodes` JSON-RPC 通道（过渡期兼容 `python.blocks`）。
- `ProjectDataReferenceService` 的项目变量重命名与引用检查。
- 现有 v3 方法/事件区域管理（自定义方法、消息、点击、按键、计时器）。
- AvaloniaEdit `TextDocument.Replace` 的撤销重做。
- `ProjectWorkspace` / `ProjectService` 获取项目根目录以保存布局文件。
- 迁移后的 `BlueprintNodeControl` 的 `DrawingContext` 绘制、参数槽和颜色风格。

## 10. Steps

- [x] 创建新命名文件/类型（`nodes_service.py`、`NodeProtocol.cs`、`BlueprintEditorModel`、`BlueprintNodeControl` 等），旧文件保留转发层。
- [x] 迁移实现并更新 DI、XAML `x:Class`、`.sln`、测试引用；旧入口在过渡期仍可用。
- [x] 数据迁移：读取 `blocks/layout.json` 写入 `blueprint/layout.json`；RPC 同时支持 `python.blocks` / `python.nodes`。
- [x] 确认无旧引用后删除旧文件/别名，保留回滚说明。
- [x] 扩展 `nodes_service.py`：输出源码区间、节点种类、类/模块根、def/event、属性、注释、表达式子树；补测试。
- [x] 扩展 `NodeProtocol.cs` 与 Domain `Blueprint` 模型：`SourceSpan`、`NodeKind`、`PortKind`、`VariableRef`、`NodeScope`。
- [x] 实现 `Blueprint.Core` 布局：执行链、控制流分支行、自由坐标、新节点默认位置与后继让位。
- [x] 实现 `BlueprintWorkspace` 渲染：节点卡片、控制流分支行、端口、可重连导线、注释锚点、缩放平移。
- [x] 在迁移后的 `BlueprintEditorModel`、`MainViewModel.Blueprint.cs`、`BlueprintEditorView.axaml` 中接入新画布，保留旧入口和现有功能。
- [x] 实现控制流节点的布尔子输入与 `exec` 输出、分支默认项、循环单输入/输出。
- [x] 实现变量模板按作用域分组、展开/收起、搜索。
- [x] 实现类定义页导航 + 每事件/方法独立画布；接入现有方法/事件列表。
- [x] 实现 Python → 图形化切换时刷新、序列 diff、布局保留、语法错误回退。
- [x] 实现参数编辑、变量模板分组/搜索、变量悬停、项目变量管理器入口。
- [x] 实现节点插入/删除、赋值/增量修改、`break/return/pass`，走局部源码写回与撤销。
- [x] 实现注释节点、行内注释保真、docstring 处理。
- [x] 实现类属性/实例属性扫描、属性重命名预览、导入调用模板。
- [x] 实现 `blueprint/layout.json` 持久化、旧路径迁移、缺失/损坏重建、版本兼容。
- [x] 后续阶段：实现简单 `match/case` 节点（使用 `match` 语句）。
- [x] 实现同方法同层端点重连，改变 Python 顺序并保留注释/缩进。
- [x] 后续阶段：实现跨方法/跨循环边界的重连与显式重排，带类型校验、源码保真移动和结构化控制流校验。
- [x] 补单元测试与手动验收，更新文档。

## 11. Verification

### 自动化

- Helper：复杂条件、嵌套循环、`for/while else`、`def/class`、`self.x` 属性、注释、源码区间、语法错误。
- Blueprint.Core：新节点默认位置、后继让位、控制流行高与分支布局、布局持久化往返、序列 diff 保留坐标。
- App：切换模式刷新、语法错误保留旧树、变量显示与悬停格式、节点写回撤销。
- 端口/重连：`<exec>` 接值、值接 `<exec>` 均拒绝；同方法同层重连后 Python 顺序改变且注释保留；跨循环边界的非法重连被拒绝。
- 控制流：if/elif/else 的每个布尔输入对应 exec 输出，底部默认项在空输入时映射 else；循环只有一个循环子输入和一个 exec 输出。
- 参数文本：基础表达式口可编辑，接入导线后自动禁用；复杂表达式只通过节点连接。
- 变量模板：按作用域分组、分类可展开/收起、搜索能匹配具体变量。
- `match`：简单 case/default 正常；复杂模式回退代码块。
- 迁移：旧 `blocks/layout.json` 能读取并写入 `blueprint/layout.json`；`python.blocks` 在旧 Helper 下可回退；删除旧文件后无旧引用、构建和测试通过。
- 回归：现有 `BlueprintProjectionTests`、`ManagedProjectEntryTests`、`PythonRuntimeTests`、`MessageEventsTests`。

### 手动验收

1. 新建角色，类定义页列出类属性、`initialize`、`start`、点击等事件；点击进入独立画布。
2. 在 `initialize` 设置 `self.health = 100`，变量模板的「实例属性」分组出现 `health`，悬停显示 `实例属性：health = 100`。
3. 在 `start` 写复杂条件 `if a and (b or not c):`，条件由布尔表达式节点连接；基础参数口可文本编辑，接入导线后文本输入框自动禁用。
4. 写 `while` 和 `for range`，循环节点只有一个循环子输入和一个 exec 输出，`break` 绑定最近循环。
5. 在 Python 编辑器新增一条语句，切回图形化后新节点出现在前驱输出方向，后续节点让位。
6. 拖动节点改坐标，切到 Python 再切回，坐标和折叠状态保留；再拖执行导线端点改顺序，确认 Python 顺序同步改变且注释保留。
7. 添加独占行注释和行内注释，移动/保存/重开不丢失。
8. 导入一个含类定义的项目脚本，生成构造/方法调用模板，不生成可编辑类体。
9. 项目变量 `score` 与局部变量同名，变量模板按作用域分组，搜索可区分；重命名项目变量后引用同步。
10. 语法错误时切回图形化，显示提示且保留上次有效树，不覆盖 Python 源码。
11. 如果存在 `match` 分支，确认使用 `match` 语句映射，而不是 `switch`；底部默认 `_` 段留空时不生成语句。
12. 打开旧项目，确认旧布局路径、旧 RPC、导入和变量仍可用；新路径写入成功，功能无丢失。

## 12. 待确认

1. 悬停“具体值”在编辑态对局部/属性无法静态确定时，用 `/` 表示是否可接受？
2. 首版确认：节点拖拽只改坐标；执行顺序通过手动重连导线改变，显式重排入口按阶段实现。

## 13. 评审反馈处理

- “11.3 2:42” 未包含可执行的修改内容，暂按无具体变更处理；如指向某段文字、截图或录屏时间点，请补充位置后再更新对应章节。
