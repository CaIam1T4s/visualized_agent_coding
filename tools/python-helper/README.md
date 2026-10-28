# Python Helper 协议 v1

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

- `helper.hello`：params 为 `{"protocolVersions":[1]}`；result 包含 protocolVersion、pythonVersion、capabilities（当前 `["parse", "findReferences", "findModuleDependencies", "planModuleRename", "inspectObjectScript"]`）。无共同版本返回 -32002。每次启动进程都重新握手。hello 已承担版本和存活确认，不另外增加 ping/version 方法。
- `python.parse`：params 为 `{"uri":"file:///.../example.py","version":1,"source":"..."}`。version 为非负整数。result 原样返回 uri、version，并包含 diagnostics 与 symbols。仅解析，不 import 或执行用户源码。
- `symbols` 包含顶层 classes/functions（name、range）与 imports（module、names、range）；该结果不是完整 AST 或跨文件引用分析。
- 语法错误作为 result.diagnostics 返回（severity、message、line、column），symbols 为 null；解析成功时 diagnostics 为空数组。
- `helper.shutdown`：返回空对象后退出；EOF 同样退出。

## 按需静态分析

- `python.findReferences`：参数为 `projectRoot`、`uri`、`version`、`line`、`column`，查询光标处符号。能力名为 `findReferences`。
- `python.findModuleDependencies`：参数为 `projectRoot`、`uri`、`version`，查询其他脚本中依赖目标模块的导入位置；不需要光标参数。能力名为 `findModuleDependencies`。
- 两者返回 `uri`、`version`、`references`、`issues`、`isIncomplete`；位置使用下方 UTF-16 坐标规范。模块查询每条匹配导入语句返回一处位置，并返回导入符号及模块在其他语句中的静态可解析使用位置，包括别名调用；同名局部变量、注释和字符串不计入。
- `projectRoot` 是 C# 创建并管理生命周期的临时源码快照，包含未保存编辑器覆盖内容。分析不会保存编辑器、不执行项目源码，也不保存反向引用索引。
- `references_service.py` 负责符号查询，`module_dependencies.py` 负责模块导入分析，`analysis_environment.py` 共享 Jedi/Parso 的隔离配置。
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
- 返回 `uri`、`version`、`status`、`templateVersion`、`userRegion`、`problems`。状态为 `recognized`、`legacyUnmarked`、`invalidStructure` 或 `unsupportedVersion`；只有 `recognized` 提供区域。
- `userRegion` 使用完整原文的 UTF-16 `offset` / `length`，半开区间，包含开始标记换行之后到结束标记行缩进之前的原文。标记本身属于系统部分。空用户区域长度为零；CRLF、Unicode 和原始缩进不被标准化。
- v1 模板首行为 `# vac:template-version=1`；类体中 `    # vac:user-code-begin` 和 `    # vac:user-code-end` 各独占一行，缩进固定为四个空格。类体保留系统 `pass`，保证用户区域为空时完整文件仍合法。
- `object_script_service.py` 使用 `tokenize` 识别真实注释，字符串中的同样文字不算标记。检查版本、数量、顺序、独占行和缩进，再用临时占位替换区域，通过 AST 核对必要导入、类名、基类及系统占位；不执行源码。
- 用户区混用 Tab/空格或错误退格导致词法缩进异常时，使用仅供词法分析的去缩进视图重试，并把注释位置映射回原文；引号状态仍由 tokenizer 跟踪，字符串伪标记仍不成立。所有标记缩进、系统包装校验和最终 UTF-16 区间均基于未改动的原文，不格式化用户代码。
- 用户区域的语法诊断由 `python.parse` 单独处理。即使全文有语法错误，只要真实标记可确认且剥离后的包装有效，仍可识别结构；未闭合字符串吞掉结束标记等情况返回无法确认。标记内的顶格代码不影响区域归属，重新打开或加载后仍可编辑和保存；识别成功不代表这些代码具有预期的角色成员语义，当前尚未提供专门的作用域诊断。
- `Application/Editor/ObjectScripts` 保存唯一的完整源码模型，`UserSource` 只是只读切片；服务不读写磁盘，不迁移旧脚本，校验 Helper 响应的文档版本和区间边界。旧脚本保持原文并标记为 `legacyUnmarked`。
- 区域识别本身仍为只读；应用已接入用户区域保存合并及输入级保护，界面仍显示完整源码。App 使用 AvaloniaEdit 的只读区间接口和动态锚点，限制输入及删除在用户区域内；跨系统边界的删除整体拒绝。起点位于插入前、终点位于插入后，支持空区域、粘贴、替换及撤销重做，不随用户输入重新解析边界。普通用户脚本及旧版未标记脚本仍全文可编辑。
- 打开、重新加载和程序性替换源码时重新建立边界；识别期间及结构不明、版本不支持、Helper 失败时全文只读，保留查看、复制与重新加载入口。已打开文档以确认的系统前后缀恢复用户边界，即使用户代码暂时包含未闭合字符串也不丢失锚点。结构损坏的磁盘文件需在外部修复后重新加载。保存时的系统包装及磁盘冲突复检仍保留。隐藏视图及视图坐标映射尚未接入。
- 在开始标记注释行末按 Enter，会在用户区域内创建带四空格缩进的空行并移动光标；空用户区域也支持在结束标记的缩进处按 Enter。原始标记及换行格式保持不变，操作可撤销，其他系统区域仍禁止输入。
- 在用户区域首行的第 1 列按 Backspace，如果该行仅含空格或 Tab，会删除用户区域内的整条空白行（含其换行），可撤销；首行含代码时仍禁止向前合并到系统注释。

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
