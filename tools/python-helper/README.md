# Python Helper 协议 v1

## 受管理导入

- 已实现 `python.imports` 与 `python.parse.importContext`，包含项目目标校验、实际 Python 标准库目录、别名/重复/冲突检查、规则诊断和候选源码生成；不执行用户模块或安装包。
- 新角色/背景默认 v3 模板，旧 v1/v2 需明确升级。普通用户脚本使用同样的模块级受管理导入区，原手写导入保留；角色/背景手写导入可通过菜单转为受管理导入。
- 编辑器显示只读摘要；管理浮层支持添加、别名修改、移除、取消及确认，确认后支持撤销重做。完整源码继续参与引用、重命名和删除影响检查。
- 当前无可选公开运行时 API，内部基类依赖隐藏且不可移除；第三方包和实际执行运行器仍未实现。
- 详细字段、恢复与兼容规则见 [v3 导入协议](object-script-v3.md)。

## 开发、依赖与发布

- `requirements.txt` 固定 Jedi、Parso 版本及 wheel SHA256；第三方源码不提交到仓库。
- 首次开发执行 `python -B tools/python-helper/scripts/prepare_dependencies.py`，使用该 Python 的 pip 安装到仓库 `artifacts/python-helper/vendor/`，不安装到全局环境。需要 Python 3.10 或更新版本。
- 应用 build/publish 自动执行相同恢复流程；依赖清单不变且依赖已就绪时无需联网。可用 `-p:HelperPython="解释器路径"` 指定构建解释器，运行解释器仍由 `VISUALIZED_AGENT_PYTHON` 配置。
- 离线构建前，可执行 `python -m pip download --require-hashes --only-binary=:all: -r tools/python-helper/requirements.txt -d artifacts/python-helper/wheels`。存在该 wheel 目录时，恢复脚本仅使用本地包；缺包或校验失败会中止构建。
- 运行测试：`python -B -m unittest discover -s tools/python-helper/tests -p "test_*.py"`。
- 发布时将 helper 源码和生成依赖复制到 `python-helper/` 及其 `vendor/`，保留 `.dist-info` 元数据、许可证和 Jedi 随附 Typeshed。普通用户无需执行 pip；Python 解释器仍需另外提供。
- `artifacts/` 为生成目录，已加入 Git 忽略。清空后可通过恢复脚本重新生成。升级依赖时需同时更新版本、wheel 校验值并运行测试。

## 按需引用查询

- `python.findReferences` 需要先握手，能力名为 `findReferences`。参数为 `projectRoot`、`uri`、`version`、`line`、`column`；行列从 1 开始，列为 UTF-16。
- C# 应用层每次收集项目脚本、入口脚本和基础运行时脚本的源码；传入的编辑器文档覆盖磁盘内容，包括空文本。查询不会先保存原始文件。
- C# 基础设施层建立独立临时目录作为本次源码快照。Helper 使用 Jedi 0.20.1 查询作用域、跨文件导入及别名；结果路径映射回原项目。查询成功、失败、取消后，C# 均回收快照。没有写入项目清单的持久化反向引用表。
- 返回 `uri`、`version`、`references`、`issues`、`isIncomplete`。每个位置包含文件 URI、标识符 `range`、`isDefinition` 和 `name`；结果包含定义和导入别名的绑定位置。错误文件会明确标记结果可能不完整。
- 该查询是静态分析，不运行用户脚本；动态导入、运行时生成名称等可能无法完整识别，不可直接视为安全重命名证明。
- 单次最多 2000 个文件、20 MiB 源码、100 个别名查询路径；活动查询默认 30 秒超时。编辑或切换上下文会取消过期查询。Jedi 的内部缓存只位于本次临时快照内，不写用户项目或全局缓存目录。
- 编辑器使用“查找引用”按钮、右键菜单或 Shift+F12，以当前光标位置查询；位置列表可跳转已索引脚本及入口脚本。

## 多错误诊断

- 成功的源码仍由 CPython AST 解析，避免将有效的新语法误判为错误。
- CPython 报错后，通过随应用打包的 Parso 0.8.7 容错解析补充后续错误，按原始行列排序并去重。保留 CPython 的首个错误详情，所有位置均转换为 UTF-16 列。
- 对 CPython 能单独解析的源码块，抑制容错解析器产生的兼容性误报。
- 诊断增加可选 `parser`（`native`/`recovery`）来源字段；恢复解析的缺少代码块错误增加可选 `relatedLine` 指向实际块头。协议版本仍为 v1，旧客户端可忽略附加字段，旧响应缺少字段时客户端按未知来源保留诊断。
- 用户投影视图沿用现有行列转换及点击定位。已确认模板中，隐藏包装位置的恢复连带报错被过滤；缺少用户方法体时定位回定义，空启动/初始化区域定位到可编辑区域。可确定归属的独立恢复错误仍保留，无法定位的原生或未知来源错误仍显示，不直接标成“系统代码”。标题、状态栏及提示按最终展示项目计数。模板结构识别失败时仍显示只读完整源码和结构异常提示，不执行上述隐藏过滤。
- 错误恢复尽力继续解析；未闭合字符串、严重缩进损坏等仍可能遮挡后续错误，无法保证一次列出所有错误。恢复失败时至少保留原始 CPython 错误。
- 编辑器左下角显示错误数量；检查中、未检查和检查不可用分别显示状态，不以零错误代替未知结果。
- 依赖及许可证随 Helper 分发，详见 `requirements.txt`，运行时不需要 pip。

## 传输与消息

- stdin/stdout 每行一条 UTF-8 JSON（无 BOM），以 LF 结束。单条消息包含结尾换行不得超过 8 MiB；源码换行由 JSON 转义。
- stdout 只输出协议消息，stderr 输出日志。C# 持续读取 stderr，只保留末尾 4096 字符，避免管道阻塞和内存增长。
- 请求：`{"jsonrpc":"2.0","id":"1","method":"python.parse","params":{...}}`。id 为连接内递增且不重复的字符串。
- 响应保留相同 id，且恰好包含 result 或 error（整数 code、字符串 message）之一。
- 无 id 的通知不响应。v1 不处理批量请求，不支持请求级协作取消。

## 方法

- `helper.hello`：params 为 `{"protocolVersions":[1]}`；result 包含 protocolVersion、pythonVersion、capabilities（当前 `["parse", "findReferences", "findModuleDependencies", "planModuleRename", "inspectObjectScript", "blocks"]`）。无共同版本返回 -32002。每次启动进程都重新握手。hello 已承担版本和存活确认，不另外增加 ping/version 方法。
- `python.parse`：params 为 `{"uri":"file:///.../example.py","version":1,"source":"..."}`。version 为非负整数。result 原样返回 uri、version，并包含 diagnostics 与 symbols。仅解析，不 import 或执行用户源码。
- `symbols` 包含顶层 classes/functions（name、range）与 imports（module、names、range）；该结果不是完整 AST 或跨文件引用分析。
- 语法错误作为 result.diagnostics 返回（severity、message、line、column），symbols 为 null；解析成功时 diagnostics 为空数组。
- `python.blocks`：参数为 `{"source":"..."}`，source 是已经去缩进的单个脚本区域正文。result 包含 error 和只读积木节点树；未支持语句返回 `raw` 节点，注释返回 `comment` 节点。该接口不修改源码、不执行用户代码。
- `helper.shutdown`：返回空对象后退出；EOF 同样退出。

## 按需静态分析

- `python.findReferences`：参数为 `projectRoot`、`uri`、`version`、`line`、`column`，查询光标处符号。能力名为 `findReferences`。
- `python.findModuleDependencies`：参数为 `projectRoot`、`uri`、`version`，查询其他脚本中依赖目标模块的导入位置；不需要光标参数。能力名为 `findModuleDependencies`。
- 两者返回 `uri`、`version`、`references`、`issues`、`isIncomplete`；位置使用下方 UTF-16 坐标规范。模块查询每条匹配导入语句返回一处位置，并返回导入符号及模块在其他语句中的静态可解析使用位置，包括别名调用；同名局部变量、注释和字符串不计入。
- `projectRoot` 是 C# 创建并管理生命周期的临时源码快照，包含未保存编辑器覆盖内容。分析不会保存编辑器、不执行项目源码，也不保存反向引用索引。
- `references_service.py` 负责符号查询，`module_dependencies.py` 负责模块导入分析，`analysis_environment.py` 共享 Jedi/Parso 的隔离配置。
- 符号查询的语法问题提供可选 `line`、`column` 字段，`message` 不内嵌全文行号；界面按同一查询快照的用户投影显示行号并过滤隐藏区域和运行时文件的语法提示，`isIncomplete` 仍保留。
- 模块查询覆盖普通导入、别名、from 导入及相对导入；遇到语法错误或检测到动态导入时返回不完整提示。静态查询不能保证发现全部运行时构造的依赖。
- 旧 Helper 缺少对应能力时，C# 报告不支持该查询，不将其解释为没有引用。

## 用户脚本重命名预览

- `python.planModuleRename`：需要先握手且具备 `planModuleRename` 能力。参数为 `projectRoot`、`uri`、`version`、`newName`，其中根目录仍是一次性源码快照。
- 返回 `uri`、`version`、`changes`（文件 URI 和修改后的完整 `source`）、`issues`、`isIncomplete`。Helper 不写文件，C# 负责保留修改前源码、显示预览和确认应用。
- 根据静态解析的模块身份改写导入路径，以及无显式别名的模块名使用；保留 `as` 别名、导入函数/类名、同名局部变量、注释、字符串及原始换行。
- 语法错误、已识别的动态导入/动态执行、名称冲突或无法确认的相对导入会阻止应用。新名称必须为 Python 标识符且不能为关键字或 `__init__`。这不是运行时反射或任意动态 Python 程序的完整重构器。
- `Application/Projects/ScriptRenameService` 优先使用编辑器内容生成计划，预览不保存。确认时复检清单、脚本集合及磁盘源码，并由 UI 校验编辑器版本。修改成功后保留脚本稳定 ID，更新路径、引用方源码和打开的编辑器内容。
- 重命名计划模型位于 `ScriptRenamePlan.cs`，文件目标校验由 `ScriptRenameRules` 共享；`ProjectService.CommitScriptFileRename` 为内部提交步骤，App 不直接调用。界面状态位于 `MainViewModel.ScriptRename.cs`，带行号的展示模型位于 `ScriptRenamePreviewItem.cs`。
- 应用中发生可捕获的写入错误时尝试恢复源码及清单；未清理的新文件以待删除记录保护，防止下次打开误收录。持续磁盘故障导致恢复失败时明确报错并保留预览；当前不提供断电/进程崩溃后的跨文件事务恢复。
- 界面流程：输入新名称并按 Enter → 查看预览 → “应用重命名”；可选择“修改名称”返回输入，或“取消”保留原始文件及未保存编辑内容。

## 对象脚本区域识别

- `python.inspectObjectScript` 需要先握手并具备 `inspectObjectScript` 能力。请求包含 `uri`、非负 `version`、完整 `source`、`expectedClassName` 和 `kind`（`object` / `backdrop`）。预期类名和类型由应用根据项目清单提供，不从脚本自报信息推导。
- 返回 `uri`、`version`、`status`、`templateVersion`、`userRegion`、`problems`。v2 额外返回 `regions`，每项包含稳定 `id`、`kind`、`span`、`indentation` 和可选 `eventName`，`userRegion` 为 null。状态为 `recognized`、`legacyUnmarked`、`invalidStructure` 或 `unsupportedVersion`；只有 `recognized` 提供区域。
- `userRegion` 使用完整原文的 UTF-16 `offset` / `length`，半开区间，包含开始标记换行之后到结束标记行缩进之前的原文。标记本身属于系统部分。空用户区域长度为零；CRLF、Unicode 和原始缩进不被标准化。
- v1 模板首行为 `# vac:template-version=1`；类体中 `    # vac:user-code-begin` 和 `    # vac:user-code-end` 各独占一行，缩进固定为四个空格。类体保留系统 `pass`，保证用户区域为空时完整文件仍合法。
- v2 多区域模型、模板工厂、词法识别、包装校验、保存计划和隐藏包装缩进的编辑投影已实现，详见 [v2 模板协议](object-script-v2.md)。新建角色/背景脚本使用 v2；v1 继续使用保留原始缩进的旧投影，不自动迁移。多区域标题只读，诊断、引用和跳转映射完整源码位置；界面已接入基础添加功能及已确认 v1 的预览升级，Helper 只负责识别校验。两版模板共享 JSON-RPC 协议 v1，不执行用户代码。
- `object_script_service.py` 使用 `tokenize` 识别真实注释，字符串中的同样文字不算标记。检查版本、数量、顺序、独占行和缩进，再用临时占位替换区域，通过 AST 核对必要导入、类名、基类及系统占位；不执行源码。
- 用户区混用 Tab/空格或错误退格导致词法缩进异常时，使用仅供词法分析的去缩进视图重试，并把注释位置映射回原文；引号状态仍由 tokenizer 跟踪，字符串伪标记仍不成立。所有标记缩进、系统包装校验和最终 UTF-16 区间均基于未改动的原文，不格式化用户代码。
- 用户区域的语法诊断由 `python.parse` 单独处理。即使全文有语法错误，只要真实标记可确认且剥离后的包装有效，仍可识别结构；未闭合字符串吞掉结束标记等情况返回无法确认。标记内的顶格代码不影响区域归属，重新打开或加载后仍可编辑和保存；识别成功不代表这些代码具有预期的角色成员语义，当前尚未提供专门的作用域诊断。
- `Application/Editor/ObjectScripts` 保存唯一的完整源码模型，`UserSource` 只是只读切片；服务不读写磁盘，不迁移旧脚本，校验 Helper 响应的文档版本和区间边界。旧脚本保持原文并标记为 `legacyUnmarked`。
- 正常角色/背景脚本只显示用户区域，保留区域原有缩进、Tab 和字符串内容。角色/背景用户区域用于对象成员与事件逻辑；模块级变量、普通函数和独立类放在普通用户脚本。普通用户脚本和旧版未标记脚本仍全文可编辑。本轮明确语义但不增加成员类型的 AST 强制检查。
- 投影更新完整源码后再交给现有保存和 Helper 分析链路，诊断显示视图行号，引用查询及跳转转换位置；隐藏系统位置只提示而不跳到无关用户代码。空区域直接编辑即可，不再操作隐藏标记。没有末尾换行的非空视图在合并时补充同格式换行，保证结束标记独占一行；重新加载后该换行作为用户区域原文显示。
- 打开、重新加载和程序性替换源码时重新建立投影，拒绝过期结果；识别期间锁定编辑，结构不明、版本不支持或 Helper 失败时显示只读完整源码供恢复，保留重新加载入口。保存时的系统包装及磁盘冲突复检仍保留。去除包装缩进的显示转换尚未实现。详见 `src/VisualizedAgentCoding.Application/Editor/ObjectScripts/README.md`。

## 对象脚本保存合并

- `Application/Editor/ObjectScripts/ObjectScriptSaveService` 生成只含源码的保存计划，再检查版本、脚本索引和磁盘原文后应用。此功能复用既有区域识别协议，不增加 Helper 写文件能力。
- 首次保存修改时识别打开时的完整原文；对于已确认的 v1 模板，将编辑器全文与原文的系统前缀、后缀逐字符比较，只截取并合并用户区域。导入、类声明、系统 `pass` 和标记行不能通过普通保存被改变。
- 用户区可以为空，也可含语法错误；不补写或删除用户代码，不标准化缩进和换行。非空用户区必须保留末尾换行，使结束标记仍在独立行。当前会话保留已确认锚点，未闭合字符串等临时错误不需要重新猜测边界即可保存和继续修正；重新打开时仍按识别规则保守确认结构。
- 旧的未标记脚本继续以完整文件保存，但同样检查磁盘冲突；不自动迁移。损坏模板不猜测区域；用户在完整视图中明确修复后，仅在修复稿重新通过结构识别时允许保存，仍复检磁盘原文。不支持版本的模板拒绝合并，不自动降级。
- 手动保存、切换脚本/项目、关闭项目和退出自动保存共用 `SaveActiveProjectAsync`；Helper 分析异步执行。保存期间编辑版本发生变化时拒绝应用旧计划，写入失败不推进原文快照，允许重试。
- 有本地修改时，磁盘原文或脚本索引变化会阻止覆盖；无本地修改的导航/退出不写文件，保留外部版本。手动保存仍检查外部变化。当前是乐观内容检查与既有单文件原子写入，不是跨进程原子比较交换。
- 冲突时当前脚本、编辑内容及窗口保留。编辑器右键“重新加载磁盘文件…”提供明确确认：可保留编辑，或确认丢弃编辑后读入磁盘版本并更新原文快照。
- 编辑器工具栏也提供“重新加载”和“更多”按钮，恢复操作不依赖右键；“更多”、右键及 Shift+F10 使用同一菜单。右键弹出延迟到当前输入事件处理后，避免菜单与编辑器内部输入处理相互干扰。
- App 的项目创建/打开/关闭接口现在返回 `Task`；相关命令、引用导航和界面调用均等待异步保存完成。保存合并与退出自动保存策略仍相互独立。
- 重命名预览携带对象脚本的未保存内容时，也复用只读合并预检，防止绕过系统区域检查；预览仍不写盘。

## 坐标规范

- 行、列从 1 开始，列按 UTF-16 代码单元计数，与 C# 字符串/AvaloniaEdit 一致。
- range 包含 line、column、endLine、endColumn；结束位置不包含在范围内。
- SyntaxError 的位置从 Python 的字符列转换成 UTF-16；AST 的 UTF-8 字节列也转换成 UTF-16。

## 超时、取消与生命周期

- C# 使用单个懒启动进程，串行处理请求；并发调用在客户端排队，同一进程可以服务多个请求。
- 默认 5 秒请求期限包括排队、进程启动、握手及解析。排队取消不影响其他请求；活动请求超时或取消会杀掉进程及其子进程。
- 异常退出、无响应、非法响应、ID/文档版本不匹配或不兼容握手使本次请求失败；不自动重放失败请求，下一次请求重新启动并握手。
- 关闭客户端时先取消活动请求，再尝试 helper.shutdown（最多 1 秒），最后回收进程及流。
- 默认使用 `python`，可通过 `VISUALIZED_AGENT_PYTHON` 指定可执行文件路径，不接受 shell 命令。启动参数为 `-E -s -B -u`，不修改系统 PATH，不执行用户代码。
- helper 源文件随应用 build/publish 复制到 python-helper/，不依赖运行时能访问源码仓库。Python 尚需由外部环境提供；安装检测和内置环境分发属于后续工作。

## 编辑器诊断

- 输入后防抖 350 毫秒。编辑、切换文档、重命名或关闭项目均递增文档版本并取消旧任务。
- 发布诊断时再次校验请求代次、URI 和文档版本，过期结果不得更新 UI。新输入时清除旧诊断。
- 语法错误、Helper 不可用或超时均保留源码和编辑状态；诊断列表可定位到行列。

## 错误码

- -32700 JSON/UTF-8 解析错误
- -32600 无效请求
- -32601 方法不存在
- -32602 参数无效
- -32603 解析器内部错误
- -32002 协议版本不兼容
- -32004 消息超限（响应后结束进程）
- -32005 未完成握手
