# Object script template v2

模板版本与 JSON-RPC 协议版本独立；继续调用 `python.inspectObjectScript`。新建角色/背景脚本使用 v2，并通过多区域视图隐藏类/方法包装缩进；v1 和无标记旧文件仍按原有模式打开，不自动升级。界面已接入基础添加功能和已确认 v1 模板的显式预览升级。

## 默认文件

```python
# vac:template-version=2
from runtime.element_base import ElementBase


class Hero(ElementBase):
    pass

    # vac:region-begin {"id":"members","kind":"members"}
    # vac:region-end members

    def _vac_on_start(self):
        # vac:region-begin {"id":"start","kind":"start"}
        pass  # 请删除此行并编写代码
        # vac:region-end start
```

背景对应 `runtime.backdrop_base.BackdropBase`。类级系统 `pass` 保障空类定义区；启动、初始化和事件方法体的用户 `pass` 带有可删除提示注释，用户可以整行删除并编写代码，也可以保留这个无害的空操作。该提示只用于新生成模板，不重写已有用户脚本。未来添加功能按钮再接入自动选中；当前不隐藏或自动补回 pass。空方法体可原样保存，由语法诊断报告问题。

## 区域约定

- 首行必须精确为 `# vac:template-version=2`。每个区域由真实的独占行注释成对包围，不支持嵌套；标记文字在字符串内不算标记。未知或多余的 `# vac:` 指令拒绝识别。
- 开始标记为 `# vac:region-begin ` 加 JSON 对象，允许键为 `id`、`kind`、事件区域专用的 `eventName`；拒绝重复 JSON 键、未知键、类型不符以及重复区域 ID。
- ID 匹配 `[a-z][a-z0-9_]{0,63}`，在单个脚本内稳定且唯一。结束标记为 `# vac:region-end <id>`，必须与开始匹配。
- `members` 必须首位且恰好一个，标记缩进四空格，区域内容属于类定义。
- `start` 恰好一个，包装为 `_vac_on_start(self)`；`initialize` 最多一个，包装为 `initialize(self)`；`event` 包装为 `_vac_event_<id>(self)`。这些标记缩进八空格，区域内容属于方法体。非 members 区域的物理顺序按文档保留，不表示执行顺序。
- `eventName` 是事件身份字符串，例如 `clicked` 或 `message:你好`，长度 1–256 字符且非全空白，单个脚本内唯一。JSON 转义保留空格和 Unicode；同一事件不同参数可编码为不同身份。具体事件注册和调度契约后续定义，此字段不触发执行。
- 区域从开始标记下一行的第一个字符起，到结束标记行的缩进之前止。返回 UTF-16 半开范围，保留 LF/CRLF、Tab、字符串和原文缩进。所有系统片段在剥离用户内容后通过 AST 校验，不执行用户代码。
- 用户区域中的错误缩进、顶格语句及可恢复语法错误不决定区域归属。未闭合字符串吞掉真实结束标记时返回 `invalidStructure`，不做裸文本搜索恢复。已有可信模型的内容修改不要求重新词法分析。

## 返回模型

v2 成功结果为 `status: recognized`、`templateVersion: 2`、`userRegion: null`、`problems: []`，另含按源码顺序排列的 `regions`：

```json
{"id":"start","kind":"start","span":{"offset":0,"length":0},"indentation":8,"eventName":null}
```

上例仅展示字段，offset/length 由实际源码计算。失败结果不提供可编辑区域。C# 端额外校验版本、URI、身份、类型、事件唯一性、缩进、范围顺序、不重叠、边界及 UTF-16 代理对。v1 保留旧 `userRegion` 契约，Application 将其额外表示为 ID `user` 的单个 members 区域；v1 的 `HasUserRegion` 与单区域投影继续可用，v2 使用 `HasRegions`，不能冒充单个大切片。

## 保存与兼容

`MultiRegionProjection` 为每个区域生成只读标题和独立可编辑切片，按区域 ID 管理内容；用户输入同名标题注释不会改变区域身份。`PythonWrapperIndentation` 只在词法字符串外去除/恢复包装缩进，跟踪多行字符串、转义换行以及 f/t-string 中的表达式和嵌套字符串，未修改行复用原始源码。完整源码与视图分别维护行列映射，代码中的诊断和引用转换位置；隐藏包装的位置不会跳到无关用户代码。

区域之间有只读的分隔换行。区域内可编辑、粘贴及撤销重做，跨越标题或分隔符的删除整体拒绝；选择和复制不受限制。动态锚点随编辑和原生撤销重做移动，不通过中文标题解析用户缓冲区。标题行末 Enter 可在对应区域内创建空行，首空行最左侧 Backspace 删除空行后回到标题末尾，不修改标题。

当前编辑投影的已确认文档可直接生成区域保存计划，不因用户临时未闭合字符串而重新识别边界。非空区域最后没有换行时补充同格式换行，保护结束标记；不补回用户删除的 `pass`。保存后更新已确认快照，外部磁盘变化仍拒绝覆盖。

`ObjectScriptRegionSaveService.Prepare` 接受已确认 v2 文档、编辑版本及按区域 ID 提供的内容替换。省略的区域原样保留；未知 ID 拒绝。替换内容必须为空或以 LF 结束（CRLF 也满足），不补 `pass` 或格式化源码。合并保留所有系统片段，重新计算每个区域偏移及长度，返回新的完整文档和既有 `ObjectScriptSavePlan`。

计划仍通过 `ObjectScriptSaveService.Apply` 检查当前编辑版本、清单身份和路径、磁盘原文，再执行已有单文件写入。无修改合并应逐字符一致，普通 I/O 失败不清空编辑内容。不宣称跨文件或跨进程原子事务。全文保存入口也支持 v2：复检区域元数据和所有系统片段，拒绝通过全文保存修改隐藏包装或区域身份；词法损坏的编辑应使用可信区域模型合并入口。

已确认 v1 文件可通过“更多 → 升级脚本模板…”预览并确认迁移；v1 用户内容原文保留在 members 区域，新增默认 start，不猜测哪些语句应移动到启动方法。应用到缓冲区时支持整次撤销/重做，保存复检磁盘冲突；Helper 仍仅识别和校验源码，不实施迁移或写盘。无标记或结构无法确认的文件不迁移。普通用户脚本不增加类型注释，身份仍来自项目清单。

## 验证

```powershell
python -B -m unittest discover -s tools/python-helper/tests -p 'test_object_script*.py'
dotnet test tests/VisualizedAgentCoding.App.Tests/VisualizedAgentCoding.App.Tests.csproj --no-restore --filter 'FullyQualifiedName~ObjectScript'
```

覆盖两种对象模板、可选初始化/多事件、无修改往返、单区域修改与位置更新、可见 pass/空方法、Unicode/CRLF/字符串保真、错误用户源码、重复/损坏元数据与包装、过期版本和磁盘冲突，以及 v1 兼容。
